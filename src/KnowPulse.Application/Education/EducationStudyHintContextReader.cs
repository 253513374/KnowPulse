using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace KnowPulse.Education;

internal sealed record EducationStudyContact(Guid RecipientId, Guid StudentId, Guid OwnerId, DateTime ProvidedAtUtc, string ScopeJson);
internal sealed record EducationStudyHintQuestion(Guid Id, Guid OriginalId, string? ScopeJson, string? WeightsJson);
internal sealed record EducationStudyExactHintContext(Guid Id, Guid RecipientId, Guid OwnerId, Guid StudentId, Guid PaperQuestionId, Guid OriginalId,
    DateTime ExposedAtUtc, EducationLearningExposureUse Use, Guid? DecisionId);

[RemoteService(false)]
public class EducationStudyHintContextReader : KnowPulseAppService
{
    private const int Limit = 200000;
    private async Task<IQueryable<T>> QueryAsync<T>() where T : class, IEntity<Guid>
    { var repo = LazyServiceProvider.LazyGetRequiredService<IRepository<T, Guid>>(); using var tracking = repo.DisableTracking(); return await repo.GetQueryableAsync(); }
    private static void Capacity(int count) { if (count > Limit) throw new BusinessException(EducationLearningReportErrors.Capacity); }
    internal async Task<List<EducationStudyContact>> ContactsAsync(EducationCurriculumSnapshot scope, IReadOnlyCollection<Guid> students, DateTime end, CancellationToken ct = default)
    {
        if (scope.TenantId != CurrentTenant.Id) throw new BusinessException(EducationLearningReportErrors.Invalid);
        using var deleted = LazyServiceProvider.LazyGetRequiredService<IDataFilter>().Disable<ISoftDelete>();
        var recipients = await QueryAsync<EducationStudyRecipient>(); var publications = await QueryAsync<EducationStudyTaskRevision>(); var tasks = await QueryAsync<EducationStudyTask>();
        var rows = await AsyncExecuter.ToListAsync((from recipient in recipients join publication in publications on recipient.TaskRevisionId equals publication.Id join task in tasks on recipient.TaskId equals task.Id
            where recipient.TenantId == scope.TenantId && publication.TenantId == scope.TenantId && task.TenantId == scope.TenantId && task.SchoolId == scope.SchoolId &&
                task.EducationSubjectId == scope.EducationSubjectId && publication.Kind == EducationStudyTaskRevisionKind.Published && publication.TaskId == task.Id &&
                publication.OwnerUserId == task.OwnerUserId && recipient.OwnerUserId == task.OwnerUserId && recipient.SchoolId == task.SchoolId && students.Contains(recipient.StudentUserId) &&
                recipient.FirstProvidedAtUtc.HasValue && recipient.FirstProvidedAtUtc.Value <= end
            orderby recipient.Id select new EducationStudyContact(recipient.Id, recipient.StudentUserId, recipient.OwnerUserId, recipient.FirstProvidedAtUtc!.Value, publication.ScopeJson)).Take(Limit + 1), ct);
        Capacity(rows.Count); return rows;
    }
    internal async Task<List<EducationStudyExactHintContext>> ExactAsync(EducationCurriculumSnapshot scope, IReadOnlyCollection<Guid> students,
        IReadOnlyCollection<EducationStudyHintQuestion> questions, DateTime end, CancellationToken ct = default)
    {
        var contacts = await ContactsAsync(scope, students, end, ct); var values = new List<EducationStudyExactHintContext>();
        foreach (var contact in contacts) {
            var assigned = EducationLearningReportJson.Read<EducationCurriculumSnapshot>(contact.ScopeJson, EducationLearningReportJson.MaxSourceBytes);
            if (assigned.TenantId != scope.TenantId || assigned.SchoolId != scope.SchoolId || assigned.EducationSubjectId != scope.EducationSubjectId) throw Invalid();
            foreach (var question in questions.DistinctBy(x => x.Id).Where(x => EducationStudyHintScope.Overlaps(assigned, x.ScopeJson, x.WeightsJson))) {
                values.Add(new(EducationStudyHintReview.Identity(contact.RecipientId, question.Id), contact.RecipientId, contact.OwnerId, contact.StudentId,
                    question.Id, question.OriginalId, contact.ProvidedAtUtc, EducationLearningExposureUse.Unverified, null)); Capacity(values.Count);
            }
        }
        if (values.Count == 0) return values;
        using var deleted = LazyServiceProvider.LazyGetRequiredService<IDataFilter>().Disable<ISoftDelete>(); var ids = values.Select(x => x.Id).ToList();
        var roots = await AsyncExecuter.ToListAsync((await QueryAsync<EducationStudyHintReview>()).Where(x => x.TenantId == scope.TenantId && ids.Contains(x.Id)), ct);
        var revisionIds = roots.Where(x => x.CurrentRevisionId.HasValue).Select(x => x.CurrentRevisionId!.Value).ToList();
        var revisions = await AsyncExecuter.ToListAsync((await QueryAsync<EducationStudyHintReviewRevision>()).Where(x => x.TenantId == scope.TenantId && revisionIds.Contains(x.Id))
            .Select(x => new { x.Id, x.ReviewId, x.RecipientId, x.PaperQuestionId, x.StudentUserId, x.OwnerUserId, x.Use }), ct);
        if (revisions.Count != revisionIds.Count) throw Invalid();
        var byRoot = roots.ToDictionary(x => x.Id); var byRevision = revisions.ToDictionary(x => x.Id);
        for (var i = 0; i < values.Count; i++) {
            var value = values[i]; if (!byRoot.TryGetValue(value.Id, out var root)) continue;
            if (root.RecipientId != value.RecipientId || root.PaperQuestionId != value.PaperQuestionId || root.StudentUserId != value.StudentId || root.OwnerUserId != value.OwnerId || root.ExposedAtUtc != value.ExposedAtUtc) throw Invalid();
            if (root.CurrentRevisionId is not Guid id) continue; var revision = byRevision[id];
            if (revision.ReviewId != root.Id || revision.RecipientId != root.RecipientId || revision.PaperQuestionId != root.PaperQuestionId || revision.StudentUserId != root.StudentUserId || revision.OwnerUserId != root.OwnerUserId) throw Invalid();
            values[i] = value with { Use = revision.Use, DecisionId = revision.Id };
        }
        return values;
    }
    private static BusinessException Invalid() => new(EducationLearningReportErrors.Invalid);
    internal async Task<List<EducationStudyExactHintContext>> ConfirmedAsync(EducationCurriculumSnapshot scope, IReadOnlyCollection<Guid> students,
        IReadOnlyCollection<Guid> originals, DateTime end, CancellationToken ct = default)
    {
        if (originals.Count == 0) return [];
        var contacts = (await ContactsAsync(scope, students, end, ct)).ToDictionary(x => x.RecipientId); var recipients = contacts.Keys.ToList();
        if (recipients.Count == 0) return [];
        using var deleted = LazyServiceProvider.LazyGetRequiredService<IDataFilter>().Disable<ISoftDelete>();
        var roots = await QueryAsync<EducationStudyHintReview>(); var revisions = await QueryAsync<EducationStudyHintReviewRevision>(); var questions = await QueryAsync<KnowPulse.Exam.ExamPaperQuestion>();
        var rows = await AsyncExecuter.ToListAsync((from root in roots join revision in revisions on root.CurrentRevisionId equals (Guid?)revision.Id join question in questions on root.PaperQuestionId equals question.Id
            where root.TenantId == scope.TenantId && revision.TenantId == scope.TenantId && question.TenantId == scope.TenantId && recipients.Contains(root.RecipientId) &&
                originals.Contains(question.OriginalQuestionId) && revision.Use == EducationLearningExposureUse.Confirmed
            orderby root.Id select new { root.Id, root.RecipientId, root.StudentUserId, root.OwnerUserId, root.PaperQuestionId, root.ExposedAtUtc, question.OriginalQuestionId,
                RevisionId = revision.Id, revision.ReviewId, RevisionRecipient = revision.RecipientId, RevisionQuestion = revision.PaperQuestionId, RevisionStudent = revision.StudentUserId, RevisionOwner = revision.OwnerUserId }).Take(Limit + 1), ct);
        Capacity(rows.Count); var result = new List<EducationStudyExactHintContext>();
        foreach (var row in rows) {
            var contact = contacts[row.RecipientId];
            if (row.StudentUserId != contact.StudentId || row.OwnerUserId != contact.OwnerId || row.ExposedAtUtc != contact.ProvidedAtUtc || row.ReviewId != row.Id ||
                row.RevisionRecipient != row.RecipientId || row.RevisionQuestion != row.PaperQuestionId || row.RevisionStudent != row.StudentUserId || row.RevisionOwner != row.OwnerUserId) throw Invalid();
            result.Add(new(EducationStudentGrowthSnapshot.StableId($"education-study-known-hint-v1|{row.Id:D}"), row.RecipientId, row.OwnerUserId, row.StudentUserId,
                row.PaperQuestionId, row.OriginalQuestionId, row.ExposedAtUtc, EducationLearningExposureUse.Confirmed, row.RevisionId));
        }
        return result;
    }
}
