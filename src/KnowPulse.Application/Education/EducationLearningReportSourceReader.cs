using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.Exam;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

/// <summary>Captures scoring facts for a recorded teacher; never grants a public reading permission.</summary>
[RemoteService(false)]
public partial class EducationLearningReportSourceReader : KnowPulseAppService
{
    private const int MaxRows = 50000;
    private const int MaxHistory = 200000;
    private const int MaxTasks = 500;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private IRepository<T, Guid> Repo<T>() where T : class, IEntity<Guid> => LazyServiceProvider.LazyGetRequiredService<IRepository<T, Guid>>();
    private async Task<T?> RefAsync<T>(Guid id) where T : class, IEntity<Guid>
    { using var tracking = Repo<T>().DisableTracking(); return await Repo<T>().FindAsync(id); }
    private async Task<IQueryable<T>> QueryAsync<T>() where T : class, IEntity<Guid>
    { using var tracking = Repo<T>().DisableTracking(); return await Repo<T>().GetQueryableAsync(); }
    private static BusinessException Invalid() => new(EducationLearningReportErrors.Invalid);
    private static BusinessException Changed() => new(EducationLearningReportErrors.SourceChanged);
    private static void Capacity(int count, int maximum) { if (count > maximum) throw new BusinessException(EducationLearningReportErrors.Capacity); }
    private static T Parse<T>(string json) => EducationLearningReportJson.Read<T>(json, EducationLearningReportJson.MaxSourceBytes);
    private async Task<List<T>> BoundedAsync<T>(IQueryable<T> query, int limit, CancellationToken ct)
    { var rows = await AsyncExecuter.ToListAsync(query.Take(limit + 1), ct); Capacity(rows.Count, limit); return rows; }

    public virtual async Task<EducationLearningReportSource> CaptureAsync(EducationCurriculumSnapshot scope, DateTime start, DateTime end,
        IReadOnlyCollection<Guid> selectedTaskIds, bool includeScores = true, CancellationToken ct = default, bool acquireAuthorityBarrier = true)
    {
        if (LazyServiceProvider.LazyGetRequiredService<IUnitOfWorkManager>().Current == null) throw new InvalidOperationException("Report capture requires a relational unit of work.");
        var now = DateTime.UtcNow;
        if (scope == null || scope.TenantId != CurrentTenant.Id || start.Kind != DateTimeKind.Utc || end.Kind != DateTimeKind.Utc ||
            start >= end || end > now || end - start > TimeSpan.FromDays(730) || selectedTaskIds == null || selectedTaskIds.Count > MaxTasks ||
            selectedTaskIds.Contains(Guid.Empty) || selectedTaskIds.Distinct().Count() != selectedTaskIds.Count) throw Invalid();
        var teaching = await RefAsync<TeachingAssignment>(scope.TeachingAssignmentId) ?? throw new AbpAuthorizationException();
        // Student plan reads already hold School/student barriers; avoid reversing student/teacher account lock order.
        await LazyServiceProvider.LazyGetRequiredService<EducationAssessmentAccessGuard>().EnsureRecordedTeacherRelationAsync(scope, teaching.AcademicTermId, teaching.SchoolClassId, forUpdate: acquireAuthorityBarrier);
        await ValidateCurriculumAsync(scope);
        var schoolClass = await RefAsync<SchoolClass>(teaching.SchoolClassId) ?? throw Invalid();
        var roster = await RosterAsync(teaching, ct);
        var studentIds = roster.Select(x => x.StudentUserId).ToList();
        var tasks = await QueryAsync<EducationAssessmentTask>(); var revisions = await QueryAsync<EducationAssessmentTaskRevision>();
        var candidates = await BoundedAsync(from task in tasks join revision in revisions on task.PublishedRevisionId equals (Guid?)revision.Id
            where task.TenantId == scope.TenantId && task.OwnerUserId == scope.TeacherUserId && revision.TenantId == scope.TenantId &&
                revision.TaskId == task.Id && revision.TeachingAssignmentId == scope.TeachingAssignmentId && revision.SchoolId == scope.SchoolId &&
                revision.AcademicTermId == teaching.AcademicTermId && revision.SchoolClassId == teaching.SchoolClassId &&
                revision.StartsAtUtc < end && start < revision.EndsAtUtc && (selectedTaskIds.Count == 0 || selectedTaskIds.Contains(task.Id))
            orderby task.Id select revision, MaxTasks, ct);
        candidates = candidates.Where(x => Parse<EducationCurriculumSnapshot>(x.CurriculumSnapshotJson).TextbookVersionId == scope.TextbookVersionId).ToList();
        if (selectedTaskIds.Count > 0 && candidates.Count != selectedTaskIds.Count) throw new AbpAuthorizationException();
        // The cutoff worker uses Task without School, so capture also takes every relevant Task barrier.
        using (var tracking = Repo<EducationAssessmentTask>().EnableTracking())
        {
            foreach (var candidate in candidates.OrderBy(x => x.TaskId))
            {
                var task = await Repo<EducationAssessmentTask>().GetAsync(candidate.TaskId, cancellationToken: ct);
                if (task.OwnerUserId != scope.TeacherUserId || task.TenantId != scope.TenantId || task.PublishedRevisionId != candidate.Id) throw Changed();
                await Repo<EducationAssessmentTask>().UpdateManyAsync([task], autoSave: true, cancellationToken: ct);
            }
        }
        var taskIds = candidates.Select(x => x.TaskId).ToList(); var paperIds = candidates.Select(x => x.ExamPaperId).Distinct().ToList();
        var currentTasks = await BoundedAsync((await QueryAsync<EducationAssessmentTask>()).Where(x => taskIds.Contains(x.Id) && x.TenantId == scope.TenantId).OrderBy(x => x.Id), MaxTasks, ct);
        var recipients = await BoundedAsync((await QueryAsync<EducationAssessmentRecipient>()).Where(x => taskIds.Contains(x.TaskId) && x.TenantId == scope.TenantId).OrderBy(x => x.Id), MaxRows, ct);
        var visibleRecipients = recipients.Where(x => studentIds.Contains(x.StudentUserId)).ToList();
        var attempts = await BoundedAsync((await QueryAsync<EducationAssessmentAttempt>()).Where(x => taskIds.Contains(x.TaskId) && studentIds.Contains(x.StudentUserId) && x.TenantId == scope.TenantId).OrderBy(x => x.Id), MaxRows, ct);
        var submissionIds = attempts.Where(x => x.SubmissionId.HasValue).Select(x => x.SubmissionId!.Value).ToList();
        var submissions = await BoundedAsync((await QueryAsync<EducationAssessmentSubmission>()).Where(x => submissionIds.Contains(x.Id) && x.TenantId == scope.TenantId).OrderBy(x => x.Id), MaxRows, ct);
        var gradeRoots = await BoundedAsync((await QueryAsync<EducationAssessmentQuestionGrade>()).Where(x => submissionIds.Contains(x.SubmissionId) && x.TenantId == scope.TenantId && x.OwnerUserId == scope.TeacherUserId).OrderBy(x => x.Id), MaxRows, ct);
        var gradeIds = gradeRoots.Select(x => x.Id).ToList();
        var qualifications = await BoundedAsync((await QueryAsync<EducationEvidenceQualification>()).Where(x => gradeIds.Contains(x.Id) && x.TenantId == scope.TenantId && x.OwnerUserId == scope.TeacherUserId).OrderBy(x => x.Id), MaxRows, ct);
        var questions = await BoundedAsync((await QueryAsync<ExamPaperQuestion>()).Where(x => paperIds.Contains(x.PaperId) && x.TenantId == scope.TenantId)
            .OrderBy(x => x.Id).Select(x => new FrozenQuestion(x.Id, x.PaperId, x.OriginalQuestionId, x.QuestionDefinitionRevisionId, x.DisplayOrder, x.Score, x.CurriculumSnapshotJson, x.CurriculumPointWeightsJson)), MaxRows, ct);
        var papers = await BoundedAsync((await QueryAsync<ExamPaper>()).Where(x => paperIds.Contains(x.Id) && x.TenantId == scope.TenantId).OrderBy(x => x.Id)
            .Select(x => new PaperHeader(x.Id, x.SnapshotVersion, x.QuestionCount, x.TotalScore, x.PersonalOwnerUserId)), MaxTasks, ct);
        foreach (var paper in papers)
            if (paper.OwnerUserId != scope.TeacherUserId || questions.Count(x => x.PaperId == paper.Id) != paper.QuestionCount || paper.QuestionCount == 0 || questions.Where(x => x.PaperId == paper.Id).Sum(x => x.Score) != paper.TotalScore) throw Invalid();
        if (papers.Count != paperIds.Count || attempts.Count != visibleRecipients.Count(x => x.CurrentAttemptId.HasValue)) throw Invalid();
        var links = await RelationsAsync(scope, ct);
        var originals = ConnectedOriginals(questions.Select(x => x.OriginalId), links.Values);
        var history = await HistoryAsync(scope, studentIds, originals, now, ct);
        var exposures = await ExposureAsync(scope, studentIds, originals, end, ct, questions);
        var headers = candidates.Select(x => new EducationLearningReportAssessment { TaskId = x.TaskId, TaskRevisionId = x.Id, ExamPaperId = x.ExamPaperId,
            PaperSnapshotVersion = papers.Single(p => p.Id == x.ExamPaperId).Version, Title = x.Title, StartsAtUtc = x.StartsAtUtc, EndsAtUtc = x.EndsAtUtc,
            AssignedCount = recipients.Count(r => r.TaskId == x.TaskId), VisibleAssignedCount = visibleRecipients.Count(r => r.TaskId == x.TaskId) }).OrderBy(x => x.TaskId).ToList();
        var policy = await RefAsync<EducationMasteryRule>(EducationMasteryRule.IdFor(scope.TenantId, scope.SchoolId, scope.EducationSubjectId));
        var policyVersion = policy?.CurrentRevisionId is Guid policyId ? await RefAsync<EducationMasteryRuleRevision>(policyId) ?? throw Invalid() : null;
        if (policyVersion != null && (policyVersion.RuleId != policy!.Id || policyVersion.TenantId != scope.TenantId || policyVersion.SchoolId != scope.SchoolId || policyVersion.EducationSubjectId != scope.EducationSubjectId)) throw Invalid();
        var rule = policyVersion?.CalculationRule();
        if (rule != null && end - start > TimeSpan.FromDays(rule.WindowDays)) throw new BusinessException(EducationLearningErrorCodes.InvalidRule);
        var output = new List<EducationLearningReportStudentSource>();
        var confirmed = includeScores ? await ReadConfirmedAsync(scope, gradeRoots, qualifications, submissions, attempts, questions, ct) : [];
        foreach (var student in roster)
        {
            var rows = new List<EducationLearningReportAssessmentResult>();
            foreach (var recipient in visibleRecipients.Where(x => x.StudentUserId == student.StudentUserId).OrderBy(x => x.TaskId))
            {
                var task = currentTasks.Single(x => x.Id == recipient.TaskId); var revision = candidates.Single(x => x.Id == recipient.TaskRevisionId && x.TaskId == task.Id);
                var paper = papers.Single(x => x.Id == revision.ExamPaperId);
                var attempt = recipient.CurrentAttemptId is Guid aid ? attempts.SingleOrDefault(x => x.Id == aid && x.RecipientId == recipient.Id && x.StudentUserId == student.StudentUserId && x.TaskRevisionId == revision.Id) ?? throw Invalid() : null;
                var submission = attempt?.SubmissionId is Guid sid ? submissions.SingleOrDefault(x => x.Id == sid && x.AttemptId == attempt.Id && x.StudentUserId == student.StudentUserId && x.ExamPaperId == paper.Id) ?? throw Invalid() : null;
                var grades = gradeRoots.Where(x => x.SubmissionId == submission?.Id).ToList();
                if (grades.Any(x => x.AttemptId != attempt!.Id || x.ExamPaperId != paper.Id || x.TaskId != task.Id || x.TaskRevisionId != revision.Id || !questions.Any(q => q.Id == x.PaperQuestionId && q.PaperId == paper.Id)) ||
                    grades.Select(x => x.PaperQuestionId).Distinct().Count() != grades.Count) throw Invalid();
                var count = grades.Count(x => x.CurrentConfirmedRevisionId.HasValue);
                var state = submission != null ? submission.EffectiveAtUtc < start || submission.EffectiveAtUtc >= end ? EducationLearningAssessmentState.OutsideWindow :
                    count == paper.QuestionCount ? EducationLearningAssessmentState.Scored : EducationLearningAssessmentState.PendingGrading :
                    attempt != null ? now >= attempt.ExpiresAtUtc ? EducationLearningAssessmentState.AwaitingSeal : EducationLearningAssessmentState.InProgress :
                    task.ClosureId.HasValue ? EducationLearningAssessmentState.Absent : EducationLearningAssessmentState.NotStarted;
                rows.Add(new() { TaskId = task.Id, RecipientId = recipient.Id, AttemptId = attempt?.Id, SubmissionId = submission?.Id, State = state,
                    QuestionCount = paper.QuestionCount, ConfirmedQuestionCount = count, MaxScore = paper.TotalScore, EffectiveAtUtc = submission?.EffectiveAtUtc,
                    TotalScore = includeScores && state == EducationLearningAssessmentState.Scored ? confirmed.Where(x => x.SubmissionId == submission!.Id).Sum(x => x.ObtainedScore) : null,
                    ResultReleaseId = task.CurrentResultReleaseId });
            }
            output.Add(new() { Student = student, Assessments = rows, Evidence = confirmed.Where(x => x.StudentUserId == student.StudentUserId && x.EffectiveAtUtc >= start && x.EffectiveAtUtc < end).ToList(),
                Attempts = history.Where(x => x.StudentUserId == student.StudentUserId).ToList(), Exposures = exposures.Values.Where(x => x.StudentUserId == student.StudentUserId).ToList() });
        }
        var manifest = new
        {
            Scope = new { scope.TenantId, scope.TeacherUserId, scope.SchoolId, scope.TeachingAssignmentId, scope.TeacherTextbookSelectionId, scope.TextbookVersionId, scope.EducationSubjectId,
                scope.CatalogRevision, Mappings = scope.Mappings.OrderBy(x => x.MappingId).ToList() }, start, end, ClassName = schoolClass.Name, Roster = roster,
            Tasks = currentTasks.Select(x => new { x.Id, x.PublishedRevisionId, x.ClosureId, x.CurrentResultReleaseId, x.ConfirmedGradeSetVersion }), Headers = headers,
            Recipients = recipients.Select(x => new { x.Id, x.StudentUserId, x.TaskRevisionId, x.CurrentAttemptId }),
            Attempts = attempts.Select(x => new { x.Id, x.RecipientId, x.StartedAtUtc, x.SubmissionId }), Submissions = submissions.Select(x => new { x.Id, x.EffectiveAtUtc }),
            Grades = gradeRoots.Select(x => new { x.Id, x.CurrentConfirmedRevisionId }), Qualifications = qualifications.Select(x => new { x.Id, x.CurrentRevisionId }),
            States = output.SelectMany(x => x.Assessments).Select(x => new { x.RecipientId, x.State }), History = history, Links = links.VersionIds, Exposures = exposures.VersionIds, RuleRevisionId = policyVersion?.Id
        };
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(EducationLearningReportJson.Write(manifest, EducationLearningReportJson.MaxSourceBytes))));
        return new() { TenantId = scope.TenantId, OwnerUserId = scope.TeacherUserId, AcademicTermId = teaching.AcademicTermId, SchoolClassId = teaching.SchoolClassId,
            SchoolClassName = schoolClass.Name, Curriculum = scope, WindowStartsAtUtc = start, WindowEndsAtUtc = end, CapturedAtUtc = now,
            Fingerprint = fingerprint, IncludesScoringData = includeScores, Rule = rule, RuleDecisionRevisionId = policyVersion?.Id, RuleDecisionRevisionNumber = policyVersion?.RevisionNumber,
            Students = output, Assessments = headers, Relationships = links.Values };
    }

    private async Task ValidateCurriculumAsync(EducationCurriculumSnapshot scope)
    {
        var book = await RefAsync<TextbookVersion>(scope.TextbookVersionId); var selection = await RefAsync<TeacherTextbookSelection>(scope.TeacherTextbookSelectionId);
        if (book == null || selection == null || book.TenantId != scope.TenantId || selection.TenantId != scope.TenantId || book.SchoolId != scope.SchoolId || selection.SchoolId != scope.SchoolId ||
            book.EducationSubjectId != scope.EducationSubjectId || book.CatalogRevision != scope.CatalogRevision || book.Status != TextbookVersionStatus.Published ||
            selection.TeacherUserId != scope.TeacherUserId || selection.TeachingAssignmentId != scope.TeachingAssignmentId || selection.TextbookVersionId != book.Id || selection.EndedAtUtc.HasValue ||
            scope.Mappings == null || scope.Mappings.Count is < 1 or > 3000 || scope.Mappings.Select(x => x.MappingId).Distinct().Count() != scope.Mappings.Count) throw Changed();
        var ids = scope.Mappings.Select(x => x.MappingId).ToList();
        var mappings = await AsyncExecuter.ToListAsync((await QueryAsync<TextbookKnowledgePointMapping>()).Where(x => ids.Contains(x.Id) && x.TenantId == scope.TenantId && x.TextbookVersionId == book.Id));
        if (mappings.Count != ids.Count || mappings.Any(x => !scope.Mappings.Any(m => m.MappingId == x.Id && m.ChapterId == x.ChapterId && m.KnowledgePointId == x.KnowledgePointId && m.KnowledgePointRevisionId == x.KnowledgePointRevisionId))) throw Changed();
    }

    private async Task<List<EducationLearningReportStudent>> RosterAsync(TeachingAssignment teaching, CancellationToken ct)
    {
        var now = DateTime.UtcNow; var enrollments = await QueryAsync<StudentEnrollment>(); var members = await QueryAsync<SchoolMembership>(); var accounts = await QueryAsync<IdentityUser>();
        var rows = await BoundedAsync(from enrollment in enrollments join member in members on enrollment.StudentUserId equals member.UserId join account in accounts on member.UserId equals account.Id
            where enrollment.TenantId == teaching.TenantId && enrollment.SchoolId == teaching.SchoolId && enrollment.SchoolClassId == teaching.SchoolClassId &&
                enrollment.EffectiveFromUtc <= now && (!enrollment.EffectiveToUtc.HasValue || now < enrollment.EffectiveToUtc.Value) && member.TenantId == teaching.TenantId &&
                member.SchoolId == teaching.SchoolId && member.Role == SchoolMemberRole.Student && member.EffectiveFromUtc <= now && (!member.EffectiveToUtc.HasValue || now < member.EffectiveToUtc.Value) &&
                account.TenantId == teaching.TenantId && account.IsActive orderby account.Id
            select new { Enrollment = enrollment.Id, Membership = member.Id, Account = account.Id, account.Name, account.Surname, account.UserName }, EducationAssessmentTaskRevision.MaxRecipients, ct);
        if (rows.Select(x => x.Account).Distinct().Count() != rows.Count) throw Invalid();
        return rows.Select(x => new EducationLearningReportStudent { StudentUserId = x.Account, StudentEnrollmentId = x.Enrollment, SchoolMembershipId = x.Membership,
            StudentName = string.Join(" ", new[] { x.Name, x.Surname }.Where(n => !string.IsNullOrWhiteSpace(n))) is { Length: > 0 } name ? name : x.UserName, AccountName = x.UserName }).ToList();
    }

    private sealed record PaperHeader(Guid Id, int Version, int QuestionCount, decimal TotalScore, Guid? OwnerUserId);
    private sealed record FrozenQuestion(Guid Id, Guid PaperId, Guid OriginalId, Guid? DefinitionId, int DisplayOrder, decimal Score, string? CurriculumJson, string? WeightsJson);
}
