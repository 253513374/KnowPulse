using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using KnowPulse.Exam;

namespace KnowPulse.Education;

public partial class EducationLearningReportSourceReader
{
    public virtual async Task<string> CheckFormalNoveltyAsync(EducationCurriculumSnapshot scope, Guid studentId, List<Guid> originals,
        List<Guid> planOriginals, Guid? currentBindingId = null, CancellationToken ct = default, IReadOnlyCollection<Guid>? paperQuestionIds = null)
    {
        if (scope.TenantId != CurrentTenant.Id || studentId == Guid.Empty || originals.Count is < 1 or > 500 || originals.Contains(Guid.Empty)) throw Invalid();
        var relations = await RelationsAsync(scope, ct); var connected = ConnectedOriginals(originals, relations.Values); var now = DateTime.UtcNow;
        if (planOriginals.Any(connected.Contains)) throw new BusinessException(EducationTeachingPlanErrors.AssessmentNotNew);
        var history = await HistoryAsync(scope, [studentId], connected, now, ct);
        if (history.Count != 0) throw new BusinessException(EducationTeachingPlanErrors.AssessmentNotNew);
        using var deleted = LazyServiceProvider.LazyGetRequiredService<IDataFilter>().Disable<ISoftDelete>();
        var bindings = await QueryAsync<EducationTeachingFormalAssessment>(); var questions = await QueryAsync<EducationTeachingFormalQuestion>();
        var assigned = await BoundedAsync(from question in questions join binding in bindings on question.BindingId equals binding.Id
            where question.TenantId == scope.TenantId && binding.TenantId == scope.TenantId && binding.StudentUserId == studentId &&
                binding.SchoolId == scope.SchoolId && binding.EducationSubjectId == scope.EducationSubjectId &&
                (!currentBindingId.HasValue || binding.Id != currentBindingId) && connected.Contains(question.OriginalQuestionId)
            orderby question.Id select question.Id, MaxHistory, ct);
        if (assigned.Count != 0) throw new BusinessException(EducationTeachingPlanErrors.AssessmentNotNew);
        List<FrozenQuestion>? frozen = null;
        if (paperQuestionIds != null) {
            if (paperQuestionIds.Count is < 1 or > 500 || paperQuestionIds.Distinct().Count() != paperQuestionIds.Count) throw Invalid();
            frozen = await BoundedAsync((await QueryAsync<ExamPaperQuestion>()).Where(x => x.TenantId == scope.TenantId && paperQuestionIds.Contains(x.Id)).OrderBy(x => x.Id)
                .Select(x => new FrozenQuestion(x.Id, x.PaperId, x.OriginalQuestionId, x.QuestionDefinitionRevisionId, x.DisplayOrder, x.Score, x.CurriculumSnapshotJson, x.CurriculumPointWeightsJson)), 500, ct);
            if (frozen.Count != paperQuestionIds.Count || !frozen.Select(x => x.OriginalId).ToHashSet().SetEquals(originals)) throw Invalid();
        }
        var exposures = await ExposureAsync(scope, [studentId], connected, now, ct, frozen);
        if (exposures.Values.Any(x => x.Use != EducationLearningExposureUse.ConfirmedNoHints)) throw new BusinessException(EducationTeachingPlanErrors.AssessmentNotNew);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            Originals = connected, Relationships = relations.VersionIds, Exposures = exposures.VersionIds }, Json))));
    }

    private sealed record FormalContext(Guid QuestionRowId, Guid StudentId, Guid OriginalId, DateTime ProvidedAtUtc,
        Guid? ExpectedReviewId, Guid? ActualReviewId, EducationLearningExposureUse? Use);

    private async Task<(List<EducationLearningQuestionExposure> Values, List<ExposureVersion> Versions)> FormalExposuresAsync(
        EducationCurriculumSnapshot scope, List<Guid> students, List<Guid> originals, DateTime end, CancellationToken ct)
    {
        using var deleted = LazyServiceProvider.LazyGetRequiredService<IDataFilter>().Disable<ISoftDelete>();
        var questions = await QueryAsync<EducationTeachingFormalQuestion>(); var bindings = await QueryAsync<EducationTeachingFormalAssessment>();
        var activities = await QueryAsync<EducationTeachingActivity>(); var reviews = await QueryAsync<EducationTeachingFormalReview>();
        var rows = await BoundedAsync(from question in questions join binding in bindings on question.BindingId equals binding.Id
            join activity in activities on binding.ActivityId equals activity.Id
            from review in reviews.Where(x => x.Id == binding.CurrentReviewId && x.TenantId == scope.TenantId).DefaultIfEmpty()
            where binding.TenantId == scope.TenantId && question.TenantId == scope.TenantId && activity.TenantId == scope.TenantId &&
                binding.StudentUserId == activity.StudentUserId && binding.PlanId == activity.PlanId && binding.PlanRevisionId == activity.PlanRevisionId &&
                binding.SchoolId == scope.SchoolId && binding.EducationSubjectId == scope.EducationSubjectId &&
                students.Contains(binding.StudentUserId) && originals.Contains(question.OriginalQuestionId) &&
                activity.FirstProvidedAtUtc.HasValue && activity.FirstProvidedAtUtc.Value <= end
            orderby question.Id
            select new FormalContext(question.Id, binding.StudentUserId, question.OriginalQuestionId, activity.FirstProvidedAtUtc!.Value,
                binding.CurrentReviewId, review == null ? null : review.Id, review == null ? null : review.PlanContextUse), MaxHistory, ct);
        var values = new List<EducationLearningQuestionExposure>(); var versions = new List<ExposureVersion>();
        foreach (var row in rows) {
            if (row.ExpectedReviewId != row.ActualReviewId) throw Invalid();
            var id = EducationTeachingFormalQuestion.ExposureIdFor(row.QuestionRowId);
            values.Add(new(id, row.StudentId, row.OriginalId, row.ProvidedAtUtc, row.Use ?? EducationLearningExposureUse.ConfirmedNoHints, row.ActualReviewId));
            versions.Add(new(id, row.StudentId, row.OriginalId, row.ProvidedAtUtc, row.ActualReviewId));
        }
        return (values, versions);
    }
}
