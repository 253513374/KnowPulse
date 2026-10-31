using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.Exam;
using Volo.Abp.Identity;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public sealed record EducationStudentGrowthSource(EducationCurriculumSnapshot Curriculum, EducationAssessmentRecipient Recipient,
    Guid AcademicTermId, EducationStudentGrowthContext Context, EducationLearningAnalysisScope Scope, List<EducationLearningQuestionEvidence> Evidence,
    List<EducationLearningQuestionAttempt> Attempts, List<EducationLearningQuestionRelationship> Relationships,
    List<EducationLearningQuestionExposure> Exposures, List<EducationStudentGrowthAssessment> Assessments,
    EducationMasteryCalculationRule? Rule, int? RuleDecisionRevisionNumber, Dictionary<Guid, int> PointRevisionNumbers);

public partial class EducationLearningReportSourceReader
{
    private sealed record StudentCandidate(EducationAssessmentRecipient Recipient, EducationAssessmentTaskRevision Revision,
        EducationAssessmentResultRelease Release, string SchoolName, string ClassName, string TermName, Guid OwnerUserId);
    private async Task StudentCallerAsync()
    {
        await CheckMvpPermissionAsync(EducationPermissions.AssessmentParticipate);
        await LazyServiceProvider.LazyGetRequiredService<EducationAssessmentAccessGuard>().CurrentAccountAsync();
    }
    private async Task<List<StudentCandidate>> StudentCandidatesAsync(CancellationToken ct, Guid? schoolId = null)
    {
        await StudentCallerAsync(); var now = DateTime.UtcNow;
        var recipients = await QueryAsync<EducationAssessmentRecipient>(); var tasks = await QueryAsync<EducationAssessmentTask>();
        var releases = await QueryAsync<EducationAssessmentResultRelease>(); var revisions = await QueryAsync<EducationAssessmentTaskRevision>();
        var schools = await QueryAsync<School>(); var classes = await QueryAsync<SchoolClass>(); var terms = await QueryAsync<AcademicTerm>();
        var members = await QueryAsync<SchoolMembership>(); var enrollments = await QueryAsync<StudentEnrollment>();
        return await BoundedAsync(from recipient in recipients join task in tasks on recipient.TaskId equals task.Id
            join revision in revisions on recipient.TaskRevisionId equals revision.Id join release in releases on task.CurrentResultReleaseId equals (Guid?)release.Id
            join school in schools on recipient.SchoolId equals school.Id join schoolClass in classes on recipient.SchoolClassId equals schoolClass.Id
            join term in terms on revision.AcademicTermId equals term.Id join member in members on recipient.SchoolMembershipId equals member.Id
            join enrollment in enrollments on recipient.StudentEnrollmentId equals enrollment.Id
            where recipient.TenantId == CurrentTenant.Id && recipient.StudentUserId == CurrentUser.Id && (!schoolId.HasValue || recipient.SchoolId == schoolId.Value) && task.TenantId == recipient.TenantId &&
                task.PublishedRevisionId == revision.Id && revision.TaskId == task.Id && revision.TenantId == recipient.TenantId &&
                revision.SchoolId == school.Id && revision.SchoolClassId == schoolClass.Id && revision.AcademicTermId == term.Id &&
                release.TenantId == recipient.TenantId && release.TaskId == task.Id && release.TaskRevisionId == revision.Id && release.ClosureId == task.ClosureId &&
                release.ConfiguredVisibility != ExamResultVisibility.Hidden && release.ScoreOpensAtUtc <= now && release.ReleasedAtUtc <= now &&
                school.TenantId == recipient.TenantId && schoolClass.TenantId == recipient.TenantId && schoolClass.SchoolId == school.Id && schoolClass.AcademicTermId == term.Id &&
                term.TenantId == recipient.TenantId && term.SchoolId == school.Id && term.StartsAtUtc <= now && now < term.EndsAtUtc &&
                member.TenantId == recipient.TenantId && member.SchoolId == school.Id && member.UserId == recipient.StudentUserId && member.Role == SchoolMemberRole.Student &&
                member.EffectiveFromUtc <= now && (member.EffectiveToUtc == null || now < member.EffectiveToUtc) &&
                enrollment.TenantId == recipient.TenantId && enrollment.SchoolId == school.Id && enrollment.SchoolClassId == schoolClass.Id &&
                enrollment.StudentUserId == recipient.StudentUserId && enrollment.EffectiveFromUtc <= now && (enrollment.EffectiveToUtc == null || now < enrollment.EffectiveToUtc)
            orderby task.Id select new StudentCandidate(recipient, revision, release, school.Name, schoolClass.Name, term.Name, task.OwnerUserId), MaxTasks, ct);
    }
    private EducationCurriculumSnapshot StudentScope(StudentCandidate candidate)
    {
        var scope = Parse<EducationCurriculumSnapshot>(candidate.Revision.CurriculumSnapshotJson);
        if (scope.TenantId != CurrentTenant.Id || scope.SchoolId != candidate.Recipient.SchoolId || scope.TeacherUserId != candidate.OwnerUserId ||
            scope.TeachingAssignmentId != candidate.Revision.TeachingAssignmentId || scope.EducationSubjectId == Guid.Empty || scope.TextbookVersionId == Guid.Empty)
            throw Invalid();
        return scope;
    }
    private Guid ContextId(StudentCandidate candidate)
    {
        var scope = StudentScope(candidate);
        return EducationStudentGrowthSnapshot.ContextFor(CurrentTenant.Id, candidate.Recipient.StudentUserId, candidate.Recipient.StudentEnrollmentId, scope.TeachingAssignmentId, scope.TextbookVersionId);
    }
    private async Task<EducationMasteryRuleRevision?> StudentRuleAsync(EducationCurriculumSnapshot scope)
    {
        var root = await RefAsync<EducationMasteryRule>(EducationMasteryRule.IdFor(CurrentTenant.Id, scope.SchoolId, scope.EducationSubjectId));
        if (root == null) return null;
        var revision = root.CurrentRevisionId is Guid id ? await RefAsync<EducationMasteryRuleRevision>(id) : null;
        if (root.TenantId != CurrentTenant.Id || revision == null || revision.RuleId != root.Id || revision.TenantId != CurrentTenant.Id ||
            revision.SchoolId != scope.SchoolId || revision.EducationSubjectId != scope.EducationSubjectId || revision.RevisionNumber != root.RevisionNumber) throw Invalid();
        return revision;
    }
    private async Task<EducationStudentGrowthContext> StudentContextAsync(StudentCandidate candidate)
    {
        var scope = StudentScope(candidate); var rule = await StudentRuleAsync(scope);
        var teacher = await RefAsync<IdentityUser>(candidate.OwnerUserId);
        if (teacher != null && teacher.TenantId != CurrentTenant.Id) throw Invalid();
        return new() { Id = ContextId(candidate), SchoolName = candidate.SchoolName, ClassName = candidate.ClassName,
            TermName = candidate.TermName, TeacherName = teacher == null ? "" : string.Join(" ", new[] { teacher.Name, teacher.Surname }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } name ? name : teacher.UserName,
            SubjectName = scope.SubjectName, TextbookTitle = scope.TextbookTitle, MaximumWindowDays = rule is { IsEnabled: true } ? rule.WindowDays : 730 };
    }
    public virtual async Task<List<EducationStudentGrowthContext>> GetStudentContextsAsync(CancellationToken ct = default, Guid? schoolId = null)
    {
        var candidates = await StudentCandidatesAsync(ct, schoolId); var result = new List<EducationStudentGrowthContext>();
        foreach (var candidate in candidates.DistinctBy(ContextId)) result.Add(await StudentContextAsync(candidate));
        return result.OrderBy(x => x.SchoolName).ThenBy(x => x.SubjectName).ThenBy(x => x.TextbookTitle).ThenBy(x => x.Id).ToList();
    }

    public virtual async Task<EducationStudentGrowthSource> CaptureStudentAsync(Guid contextId, DateTime start, DateTime end, CancellationToken ct = default, Guid? schoolId = null)
    {
        if (LazyServiceProvider.LazyGetRequiredService<IUnitOfWorkManager>().Current == null) throw new InvalidOperationException("Student growth capture requires a relational unit of work.");
        await StudentCallerAsync(); var now = DateTime.UtcNow;
        if (contextId == Guid.Empty || start.Kind != DateTimeKind.Utc || end.Kind != DateTimeKind.Utc || start >= end || end > now || end - start > TimeSpan.FromDays(730)) throw Invalid();
        var candidates = await StudentCandidatesAsync(ct, schoolId); var first = candidates.FirstOrDefault(x => ContextId(x) == contextId) ?? throw new AbpAuthorizationException();
        var recipient = first.Recipient;
        await LazyServiceProvider.LazyGetRequiredService<EducationAssessmentAccessGuard>().EnsureStudentAsync(recipient.SchoolId, first.Revision.AcademicTermId, recipient.SchoolClassId,
            recipient.SchoolMembershipId, recipient.StudentEnrollmentId, recipient.StudentUserId, forUpdate: true);
        candidates = (await StudentCandidatesAsync(ct, schoolId)).Where(x => ContextId(x) == contextId).ToList();
        if (candidates.Count == 0) throw new AbpAuthorizationException(); first = candidates[0]; var scope = StudentScope(first);
        var ruleDecision = await StudentRuleAsync(scope); var rule = ruleDecision?.CalculationRule();
        if (rule != null && end - start > TimeSpan.FromDays(rule.WindowDays)) throw new BusinessException(EducationLearningErrorCodes.InvalidRule);
        // Cutoff grading also writes Task, so the School/student barrier alone is insufficient.
        using (var tracking = Repo<EducationAssessmentTask>().EnableTracking())
            foreach (var candidate in candidates.OrderBy(x => x.Revision.TaskId))
            {
                var task = await Repo<EducationAssessmentTask>().GetAsync(candidate.Revision.TaskId, cancellationToken: ct);
                if (task.CurrentResultReleaseId != candidate.Release.Id || task.PublishedRevisionId != candidate.Revision.Id || task.TenantId != CurrentTenant.Id) throw Changed();
                await Repo<EducationAssessmentTask>().UpdateManyAsync([task], autoSave: true, cancellationToken: ct);
            }
        var releaseIds = candidates.Select(x => x.Release.Id).ToList(); var recipientIds = candidates.Select(x => x.Recipient.Id).ToList();
        var items = await BoundedAsync((await QueryAsync<EducationAssessmentResultReleaseItem>()).Where(x => releaseIds.Contains(x.ResultReleaseId) &&
            recipientIds.Contains(x.RecipientId) && x.TenantId == CurrentTenant.Id && x.StudentUserId == CurrentUser.Id).OrderBy(x => x.Id), MaxTasks, ct);
        var submissionIds = items.Select(x => x.SubmissionId).ToList();
        var submissions = await BoundedAsync((await QueryAsync<EducationAssessmentSubmission>()).Where(x => submissionIds.Contains(x.Id) && x.TenantId == CurrentTenant.Id && x.StudentUserId == CurrentUser.Id).OrderBy(x => x.Id), MaxTasks, ct);
        var attemptIds = items.Select(x => x.AttemptId).ToList();
        var attempts = await BoundedAsync((await QueryAsync<EducationAssessmentAttempt>()).Where(x => attemptIds.Contains(x.Id) && x.TenantId == CurrentTenant.Id && x.StudentUserId == CurrentUser.Id).OrderBy(x => x.Id), MaxTasks, ct);
        var paperIds = candidates.Select(x => x.Revision.ExamPaperId).Distinct().ToList();
        var questions = await BoundedAsync((await QueryAsync<ExamPaperQuestion>()).Where(x => paperIds.Contains(x.PaperId) && x.TenantId == CurrentTenant.Id).OrderBy(x => x.Id)
            .Select(x => new FrozenQuestion(x.Id, x.PaperId, x.OriginalQuestionId, x.QuestionDefinitionRevisionId, x.DisplayOrder, x.Score, x.CurriculumSnapshotJson, x.CurriculumPointWeightsJson)), MaxRows, ct);
        var metadata = await BoundedAsync((await QueryAsync<ExamPaperQuestion>()).Where(x => paperIds.Contains(x.PaperId) && x.TenantId == CurrentTenant.Id).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.PaperId, x.Type, x.Difficulty, x.MultipleChoiceScoringMode }), MaxRows, ct);
        var frozenScopes = questions.Select(x => Parse<EducationCurriculumSnapshot>(x.CurriculumJson ?? throw Invalid())).ToList();
        if (frozenScopes.Any(x => x.TenantId != CurrentTenant.Id || x.SchoolId != scope.SchoolId || x.EducationSubjectId != scope.EducationSubjectId || x.TextbookVersionId != scope.TextbookVersionId)) throw Invalid();
        var mappings = frozenScopes.SelectMany(x => x.Mappings).DistinctBy(x => x.MappingId).OrderBy(x => x.MappingId).ToList();
        Capacity(mappings.Select(x => x.KnowledgePointRevisionId).Distinct().Count(), 3000);
        var assessmentValues = new List<(EducationStudentGrowthAssessment Value, string Coverage, string Difficulty, string Scoring)>();
        foreach (var candidate in candidates)
        {
            var item = items.SingleOrDefault(x => x.ResultReleaseId == candidate.Release.Id && x.RecipientId == candidate.Recipient.Id);
            var closure = await RefAsync<EducationAssessmentClosure>(candidate.Release.ClosureId) ?? throw Invalid();
            var entry = closure.Manifest.Entries.SingleOrDefault(x => x.RecipientId == candidate.Recipient.Id && x.StudentUserId == CurrentUser.Id) ?? throw Invalid();
            if (closure.TaskId != candidate.Revision.TaskId || closure.TaskRevisionId != candidate.Revision.Id || closure.TenantId != CurrentTenant.Id) throw Invalid();
            var at = item == null ? closure.EffectiveAtUtc : submissions.SingleOrDefault(x => x.Id == item.SubmissionId)?.EffectiveAtUtc ?? throw Invalid();
            if (item == null && entry.Outcome != EducationAssessmentRecipientOutcome.Absent || item != null && (entry.SubmissionId != item.SubmissionId || entry.AttemptId != item.AttemptId || item.TaskId != candidate.Revision.TaskId)) throw Invalid();
            if (at < start || at >= end) continue;
            var paperQuestions = questions.Where(x => x.PaperId == candidate.Revision.ExamPaperId).ToList();
            var paperMetadata = metadata.Where(x => x.PaperId == candidate.Revision.ExamPaperId).ToList();
            assessmentValues.Add((new() { TaskId = candidate.Revision.TaskId, RecipientId = candidate.Recipient.Id, ResultReleaseId = candidate.Release.Id,
                Title = candidate.Revision.Title, EffectiveAtUtc = at, IsAbsent = item == null, TotalScore = item?.TotalScore, MaxScore = item?.MaxScore,
                Visibility = candidate.Release.VisibilityAt(now) },
                string.Join('|', paperQuestions.SelectMany(x => Parse<EducationCurriculumSnapshot>(x.CurriculumJson!).Mappings).Select(x => x.KnowledgePointRevisionId).Distinct().Order()),
                string.Join('|', paperMetadata.GroupBy(x => x.Difficulty).OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Count()}")),
                string.Join('|', paperQuestions.OrderBy(x => x.DisplayOrder).Select(x => $"{x.DefinitionId}:{x.Score:G29}:" + paperMetadata.Single(m => m.Id == x.Id).MultipleChoiceScoringMode))));
        }
        var eligibleTasks = assessmentValues.Where(x => !x.Value.IsAbsent).Select(x => x.Value.TaskId).ToList();
        var eligibleItems = items.Where(x => eligibleTasks.Contains(x.TaskId)).ToList(); var gradeReferences = eligibleItems.SelectMany(x => x.Grades.Grades).ToList();
        var gradeIds = gradeReferences.Select(x => x.QuestionGradeId).ToList();
        var roots = await BoundedAsync((await QueryAsync<EducationAssessmentQuestionGrade>()).Where(x => gradeIds.Contains(x.Id) && x.TenantId == CurrentTenant.Id && x.StudentUserId == CurrentUser.Id && x.OwnerUserId == scope.TeacherUserId).OrderBy(x => x.Id), MaxRows, ct);
        if (roots.Count != gradeReferences.Count || roots.Any(x => !gradeReferences.Any(r => r.QuestionGradeId == x.Id && r.GradeRevisionId == x.CurrentConfirmedRevisionId && r.PaperQuestionId == x.PaperQuestionId))) throw Invalid();
        var qualifications = await BoundedAsync((await QueryAsync<EducationEvidenceQualification>()).Where(x => gradeIds.Contains(x.Id) && x.TenantId == CurrentTenant.Id && x.OwnerUserId == scope.TeacherUserId).OrderBy(x => x.Id), MaxRows, ct);
        var evidence = await ReadConfirmedAsync(scope, roots, qualifications, submissions, attempts, questions, ct);
        foreach (var item in eligibleItems)
            if (evidence.Where(x => x.SubmissionId == item.SubmissionId).Sum(x => x.ObtainedScore) != item.TotalScore || evidence.Where(x => x.SubmissionId == item.SubmissionId).Sum(x => x.MaxScore) != item.MaxScore) throw Invalid();
        var relationships = await RelationsAsync(scope, ct); var originals = ConnectedOriginals(questions.Select(x => x.OriginalId), relationships.Values);
        var history = await HistoryAsync(scope, [recipient.StudentUserId], originals, now, ct);
        var exposures = await ExposureAsync(scope, [recipient.StudentUserId], originals, end, ct, questions);
        var ordered = assessmentValues.OrderBy(x => x.Value.EffectiveAtUtc).ThenBy(x => x.Value.TaskId).ToList(); var assessments = new List<EducationStudentGrowthAssessment>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var current = ordered[i]; var limit = i == 0 ? EducationGrowthComparisonLimit.First : EducationGrowthComparisonLimit.None;
            if (current.Value.IsAbsent || i > 0 && ordered[i - 1].Value.IsAbsent) limit |= EducationGrowthComparisonLimit.MissingScore;
            if (i > 0) { var previous = ordered[i - 1];
                if (current.Coverage != previous.Coverage) limit |= EducationGrowthComparisonLimit.Coverage;
                if (current.Value.MaxScore != previous.Value.MaxScore) limit |= EducationGrowthComparisonLimit.Maximum;
                if (current.Difficulty != previous.Difficulty) limit |= EducationGrowthComparisonLimit.Difficulty;
                if (current.Scoring != previous.Scoring) limit |= EducationGrowthComparisonLimit.Scoring;
            }
            assessments.Add(current.Value with { ComparisonLimit = limit });
        }
        return new(scope, recipient, first.Revision.AcademicTermId, await StudentContextAsync(first), new() { TenantId = CurrentTenant.Id, SchoolId = scope.SchoolId,
            TeachingAssignmentId = scope.TeachingAssignmentId, StudentUserId = recipient.StudentUserId, EducationSubjectId = scope.EducationSubjectId,
            TextbookVersionId = scope.TextbookVersionId, WindowStartsAtUtc = start, WindowEndsAtUtc = end,
            Points = mappings.DistinctBy(x => x.KnowledgePointRevisionId).Select(x => new EducationLearningPoint(x.KnowledgePointId, x.KnowledgePointRevisionId, x.KnowledgePointCode, x.KnowledgePointName)).ToList() },
            evidence, history, relationships.Values, exposures.Values, assessments, rule, ruleDecision?.RevisionNumber,
            mappings.GroupBy(x => x.KnowledgePointRevisionId).ToDictionary(x => x.Key, x => x.First().RevisionNumber));
    }
}
