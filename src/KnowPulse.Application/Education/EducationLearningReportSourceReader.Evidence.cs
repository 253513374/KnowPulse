using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;

namespace KnowPulse.Education;

public partial class EducationLearningReportSourceReader
{
    private sealed record VersionReference(Guid Id, Guid? RevisionId);
    private sealed record OriginDecision(Guid Id, Guid RelationId, Guid First, Guid Second, bool Enabled);
    private sealed record HintDecision(Guid Id, Guid ExposureId, Guid StudentId, Guid FeedbackRevisionId, EducationLearningExposureUse Use);
    private sealed record PlanHintDecision(Guid Id, Guid ExposureId, Guid StudentId, Guid OwnerId, Guid PlanId, Guid PlanRevisionId, EducationLearningExposureUse Use);
    private sealed record ExposureVersion(Guid Id, Guid StudentId, Guid OriginalId, DateTime ExposedAtUtc, Guid? ReviewRevisionId);

    private async Task<(List<EducationLearningQuestionRelationship> Values, List<VersionReference> VersionIds)> RelationsAsync(EducationCurriculumSnapshot scope, CancellationToken ct)
    {
        var roots = await BoundedAsync((await QueryAsync<EducationQuestionOriginRelation>()).Where(x => x.TenantId == scope.TenantId && x.OwnerUserId == scope.TeacherUserId &&
            x.SchoolId == scope.SchoolId && x.EducationSubjectId == scope.EducationSubjectId).OrderBy(x => x.Id), MaxRows, ct);
        var ids = roots.Where(x => x.CurrentRevisionId.HasValue).Select(x => x.CurrentRevisionId!.Value).ToList();
        var decisions = await BoundedAsync((await QueryAsync<EducationQuestionOriginRelationRevision>()).Where(x => ids.Contains(x.Id) && x.TenantId == scope.TenantId && x.OwnerUserId == scope.TeacherUserId)
            .OrderBy(x => x.Id).Select(x => new OriginDecision(x.Id, x.RelationId, x.FirstOriginalQuestionId, x.SecondOriginalQuestionId, x.IsEnabled)), MaxRows, ct);
        if (decisions.Count != ids.Count || decisions.Any(x => !roots.Any(r => r.Id == x.RelationId && r.CurrentRevisionId == x.Id && r.FirstOriginalQuestionId == x.First && r.SecondOriginalQuestionId == x.Second))) throw Invalid();
        return (decisions.Where(x => x.Enabled).Select(x => new EducationLearningQuestionRelationship(x.Id, x.First, x.Second)).ToList(),
            roots.Select(x => new VersionReference(x.Id, x.CurrentRevisionId)).ToList());
    }

    private static List<Guid> ConnectedOriginals(IEnumerable<Guid> originals, List<EducationLearningQuestionRelationship> links)
    {
        var connected = new HashSet<Guid>(originals); var pending = new Queue<Guid>(connected);
        var edges = links.SelectMany(x => new[] { (From: x.FirstQuestionId, To: x.SecondQuestionId), (From: x.SecondQuestionId, To: x.FirstQuestionId) })
            .GroupBy(x => x.From).ToDictionary(x => x.Key, x => x.Select(e => e.To).ToList());
        while (pending.TryDequeue(out var current))
            if (edges.TryGetValue(current, out var next)) foreach (var target in next) if (connected.Add(target)) pending.Enqueue(target);
        Capacity(connected.Count, MaxRows); return connected.Order().ToList();
    }

    private async Task<List<EducationLearningQuestionAttempt>> HistoryAsync(EducationCurriculumSnapshot scope, List<Guid> students, List<Guid> originals, DateTime now, CancellationToken ct)
    {
        // Removing a paper/attempt from current UI queries must not erase an earlier encounter.
        using var deleted = LazyServiceProvider.LazyGetRequiredService<IDataFilter>().Disable<ISoftDelete>();
        var attempts = await QueryAsync<EducationAssessmentAttempt>(); var revisions = await QueryAsync<EducationAssessmentTaskRevision>();
        var assignments = await QueryAsync<TeachingAssignment>(); var questions = await QueryAsync<ExamPaperQuestion>();
        return await BoundedAsync(from attempt in attempts join revision in revisions on attempt.TaskRevisionId equals revision.Id
            join assignment in assignments on revision.TeachingAssignmentId equals assignment.Id join question in questions on attempt.ExamPaperId equals question.PaperId
            where attempt.TenantId == scope.TenantId && students.Contains(attempt.StudentUserId) && attempt.StartedAtUtc <= now && revision.TenantId == scope.TenantId &&
                revision.SchoolId == scope.SchoolId && assignment.TenantId == scope.TenantId && assignment.SchoolId == scope.SchoolId &&
                assignment.EducationSubjectId == scope.EducationSubjectId && question.TenantId == scope.TenantId && originals.Contains(question.OriginalQuestionId)
            orderby attempt.StartedAtUtc, attempt.TaskId, question.DisplayOrder, question.Id
            select new EducationLearningQuestionAttempt(scope.TenantId, scope.SchoolId, scope.EducationSubjectId, attempt.StudentUserId,
                attempt.TaskId, attempt.Id, question.Id, question.OriginalQuestionId, question.DisplayOrder, attempt.StartedAtUtc), MaxHistory, ct);
    }

    private async Task<(List<EducationLearningQuestionExposure> Values, List<ExposureVersion> VersionIds)> ExposureAsync(EducationCurriculumSnapshot scope, List<Guid> students,
        List<Guid> originals, DateTime end, CancellationToken ct, List<FrozenQuestion>? questions = null)
    {
        var rows = await BoundedAsync((await QueryAsync<EducationQuestionExposure>()).Where(x => x.TenantId == scope.TenantId && x.SchoolId == scope.SchoolId &&
            x.EducationSubjectId == scope.EducationSubjectId && students.Contains(x.StudentUserId) && originals.Contains(x.OriginalQuestionId) && x.ExposedAtUtc <= end)
            .OrderBy(x => x.Id), MaxHistory, ct);
        var exposureIds = rows.Select(x => x.Id).ToList();
        var roots = await BoundedAsync((await QueryAsync<EducationExposureHintReview>()).Where(x => x.TenantId == scope.TenantId && exposureIds.Contains(x.Id)).OrderBy(x => x.Id), MaxHistory, ct);
        var ids = roots.Where(x => x.CurrentRevisionId.HasValue).Select(x => x.CurrentRevisionId!.Value).ToList();
        var decisions = await BoundedAsync((await QueryAsync<EducationExposureHintReviewRevision>()).Where(x => ids.Contains(x.Id) && x.TenantId == scope.TenantId).OrderBy(x => x.Id)
            .Select(x => new HintDecision(x.Id, x.ExposureId, x.StudentUserId, x.FeedbackRevisionId, x.Use)), MaxHistory, ct);
        if (decisions.Count != ids.Count) throw Invalid();
        var values = new List<EducationLearningQuestionExposure>(); var versions = new List<ExposureVersion>();
        foreach (var row in rows)
        {
            var root = roots.SingleOrDefault(x => x.Id == row.Id); var review = root?.CurrentRevisionId is Guid id ? decisions.SingleOrDefault(x => x.Id == id) ?? throw Invalid() : null;
            if (root != null && (row.Source != EducationQuestionExposureSource.StudentFeedback || root.StudentUserId != row.StudentUserId || root.FeedbackRevisionId != row.FeedbackRevisionId) ||
                review != null && (review.ExposureId != row.Id || review.StudentId != row.StudentUserId || review.FeedbackRevisionId != row.FeedbackRevisionId)) throw Invalid();
            var use = row.Source == EducationQuestionExposureSource.AssessmentReview ? EducationLearningExposureUse.Confirmed : review?.Use ?? EducationLearningExposureUse.Unverified;
            values.Add(new(row.Id, row.StudentUserId, row.OriginalQuestionId, row.ExposedAtUtc, use, review?.Id));
            versions.Add(new(row.Id, row.StudentUserId, row.OriginalQuestionId, row.ExposedAtUtc, review?.Id));
        }
        var plans = await BoundedAsync((await QueryAsync<EducationTeachingPlanQuestionExposure>()).Where(x => x.TenantId == scope.TenantId && x.SchoolId == scope.SchoolId &&
            x.EducationSubjectId == scope.EducationSubjectId && students.Contains(x.StudentUserId) && originals.Contains(x.OriginalQuestionId) && x.ExposedAtUtc <= end)
            .OrderBy(x => x.Id), MaxHistory, ct);
        Capacity(rows.Count + plans.Count, MaxHistory);
        var planIds = plans.Select(x => x.Id).ToList();
        var planRoots = await BoundedAsync((await QueryAsync<EducationTeachingPlanHintReview>()).Where(x => x.TenantId == scope.TenantId && planIds.Contains(x.Id)).OrderBy(x => x.Id), MaxHistory, ct);
        var planReviewIds = planRoots.Where(x => x.CurrentRevisionId.HasValue).Select(x => x.CurrentRevisionId!.Value).ToList();
        var planReviews = await BoundedAsync((await QueryAsync<EducationTeachingPlanHintReviewRevision>()).Where(x => x.TenantId == scope.TenantId && planReviewIds.Contains(x.Id)).OrderBy(x => x.Id)
            .Select(x => new PlanHintDecision(x.Id, x.ExposureId, x.StudentUserId, x.OwnerUserId, x.PlanId, x.PlanRevisionId, x.Use)), MaxHistory, ct);
        if (planReviews.Count != planReviewIds.Count) throw Invalid();
        foreach (var plan in plans) {
            var root = planRoots.SingleOrDefault(x => x.Id == plan.Id);
            var review = root?.CurrentRevisionId is Guid id ? planReviews.Single(x => x.Id == id) : null;
            if (root != null && (root.StudentUserId != plan.StudentUserId || root.PlanId != plan.PlanId || root.PlanRevisionId != plan.PlanRevisionId) ||
                review != null && (review.OwnerId != root!.OwnerUserId || review.ExposureId != plan.Id || review.StudentId != plan.StudentUserId ||
                    review.PlanId != plan.PlanId || review.PlanRevisionId != plan.PlanRevisionId)) throw Invalid();
            values.Add(new(plan.Id, plan.StudentUserId, plan.OriginalQuestionId, plan.ExposedAtUtc, review?.Use ?? EducationLearningExposureUse.Unverified, review?.Id));
            versions.Add(new(plan.Id, plan.StudentUserId, plan.OriginalQuestionId, plan.ExposedAtUtc, review?.Id));
        }
        var formal = await FormalExposuresAsync(scope, students, originals, end, ct);
        Capacity(values.Count + formal.Values.Count, MaxHistory); values.AddRange(formal.Values); versions.AddRange(formal.Versions);
        var study = await StudyExposuresAsync(scope, students, originals, questions, end, ct);
        Capacity(values.Count + study.Values.Count, MaxHistory); values.AddRange(study.Values); versions.AddRange(study.Versions);
        return (values, versions);
    }

    private sealed record ConfirmedRow(Guid Id, Guid TaskId, Guid QuestionGradeId, Guid SubmissionId, Guid PaperQuestionId, Guid AttemptId, Guid StudentUserId,
        Guid ExamPaperId, Guid DefinitionId, int PaperVersion, EducationAssessmentGradeRevisionKind Kind, decimal MaxScore, decimal? ObtainedScore,
        DateTime? ConfirmedAtUtc, string ContentJson, string CalculationVersion);
    private sealed record QualificationDecision(Guid Id, Guid GradeId, Guid StudentId, Guid DefinitionId, EducationLearningEvidenceUse Use, bool NoHints, bool NewRequirements, bool OriginsChecked);

    private async Task<List<EducationLearningQuestionEvidence>> ReadConfirmedAsync(EducationCurriculumSnapshot scope, List<EducationAssessmentQuestionGrade> roots,
        List<EducationEvidenceQualification> qualifications, List<EducationAssessmentSubmission> submissions, List<EducationAssessmentAttempt> attempts,
        List<FrozenQuestion> questions, CancellationToken ct)
    {
        var qualificationIds = qualifications.Where(x => x.CurrentRevisionId.HasValue).Select(x => x.CurrentRevisionId!.Value).ToList();
        var qualified = await BoundedAsync((await QueryAsync<EducationEvidenceQualificationRevision>()).Where(x => qualificationIds.Contains(x.Id) && x.TenantId == scope.TenantId && x.OwnerUserId == scope.TeacherUserId)
            .OrderBy(x => x.Id).Select(x => new QualificationDecision(x.Id, x.QuestionGradeId, x.StudentUserId, x.QuestionDefinitionRevisionId, x.Use,
                x.NoHintsOrAnswersConfirmed, x.NewRequirementsConfirmed, x.OriginalRelationshipsChecked)), MaxRows, ct);
        if (qualified.Count != qualificationIds.Count) throw Invalid();
        var result = new List<EducationLearningQuestionEvidence>();
        var bindingCache = new Dictionary<Guid, (EducationCurriculumSnapshot Scope, List<QuestionCurriculumPointWeight> Weights)>();
        var confirmedIds = roots.Where(x => x.CurrentConfirmedRevisionId.HasValue).Select(x => x.CurrentConfirmedRevisionId!.Value).ToList();
        if (confirmedIds.Distinct().Count() != confirmedIds.Count) throw Invalid();
        foreach (var batch in confirmedIds.Chunk(32))
        {
            var rows = await AsyncExecuter.ToListAsync((await QueryAsync<EducationAssessmentGradeRevision>()).Where(x => batch.Contains(x.Id) && x.TenantId == scope.TenantId).OrderBy(x => x.Id)
                .Select(x => new ConfirmedRow(x.Id, x.TaskId, x.QuestionGradeId, x.SubmissionId, x.PaperQuestionId, x.AttemptId, x.StudentUserId, x.ExamPaperId,
                    x.QuestionDefinitionRevisionId, x.PaperSnapshotVersion, x.Kind, x.MaxScore, x.ObtainedScore, x.ConfirmedAtUtc, x.ContentJson, x.CalculationVersion)), ct);
            if (rows.Count != batch.Length) throw Invalid();
            foreach (var row in rows)
            {
                var root = roots.Single(x => x.CurrentConfirmedRevisionId == row.Id);
                var submission = submissions.SingleOrDefault(x => x.Id == row.SubmissionId) ?? throw Invalid();
                var attempt = attempts.SingleOrDefault(x => x.Id == row.AttemptId) ?? throw Invalid();
                var question = questions.SingleOrDefault(x => x.Id == row.PaperQuestionId) ?? throw Invalid();
                if (row.QuestionGradeId != root.Id || row.TaskId != root.TaskId || row.SubmissionId != root.SubmissionId || row.PaperQuestionId != root.PaperQuestionId ||
                    row.StudentUserId != root.StudentUserId || row.AttemptId != root.AttemptId || row.ExamPaperId != root.ExamPaperId ||
                    question.PaperId != row.ExamPaperId || question.DefinitionId != row.DefinitionId || submission.AttemptId != row.AttemptId || submission.StudentUserId != row.StudentUserId ||
                    submission.PaperSnapshotVersion != row.PaperVersion || row.Kind != EducationAssessmentGradeRevisionKind.Confirmed || !row.ObtainedScore.HasValue ||
                    !row.ConfirmedAtUtc.HasValue || row.MaxScore != question.Score || row.CalculationVersion != EducationAssessmentGradeEvidence.AlgorithmVersion) throw Invalid();
                var content = EducationAssessmentGradeContent.Parse(row.ContentJson, confirmed: true);
                if (content.MaxScore != row.MaxScore || content.ObtainedScore != row.ObtainedScore || question.CurriculumJson == null || question.WeightsJson == null) throw Invalid();
                if (!bindingCache.TryGetValue(question.Id, out var binding))
                { binding = (Parse<EducationCurriculumSnapshot>(question.CurriculumJson), Parse<List<QuestionCurriculumPointWeight>>(question.WeightsJson)); bindingCache.Add(question.Id, binding); }
                if (binding.Scope.TenantId != scope.TenantId || binding.Scope.SchoolId != scope.SchoolId || binding.Scope.TeacherUserId != scope.TeacherUserId ||
                    binding.Scope.TeachingAssignmentId != scope.TeachingAssignmentId || binding.Scope.EducationSubjectId != scope.EducationSubjectId || binding.Scope.TextbookVersionId != scope.TextbookVersionId ||
                    binding.Weights.Count != content.MappingEvidence.Count || content.MappingEvidence.Any(p => !binding.Weights.Any(w => w.TextbookKnowledgePointMappingId == p.TextbookKnowledgePointMappingId && w.Score == p.MaxScore) ||
                        !binding.Scope.Mappings.Any(m => m.MappingId == p.TextbookKnowledgePointMappingId && m.KnowledgePointId == p.KnowledgePointId && m.KnowledgePointRevisionId == p.KnowledgePointRevisionId))) throw Invalid();
                var qr = qualifications.SingleOrDefault(x => x.Id == root.Id);
                var decision = qr?.CurrentRevisionId is Guid qid ? qualified.SingleOrDefault(x => x.Id == qid) ?? throw Invalid() : null;
                if (decision != null && (decision.GradeId != root.Id || decision.StudentId != root.StudentUserId || decision.DefinitionId != row.DefinitionId)) throw Invalid();
                result.Add(new() { QuestionGradeId = root.Id, GradeRevisionId = row.Id, TenantId = scope.TenantId, SchoolId = scope.SchoolId,
                    TeachingAssignmentId = scope.TeachingAssignmentId, StudentUserId = row.StudentUserId, EducationSubjectId = scope.EducationSubjectId, TextbookVersionId = scope.TextbookVersionId,
                    TaskId = row.TaskId, SubmissionId = row.SubmissionId, AttemptId = row.AttemptId, PaperQuestionId = row.PaperQuestionId, OriginalQuestionId = question.OriginalId,
                    QuestionDefinitionRevisionId = row.DefinitionId, DisplayOrder = question.DisplayOrder, AttemptStartedAtUtc = attempt.StartedAtUtc, EffectiveAtUtc = submission.EffectiveAtUtc,
                    GradeConfirmedAtUtc = row.ConfirmedAtUtc.Value, MaxScore = row.MaxScore, ObtainedScore = row.ObtainedScore.Value, Use = decision?.Use ?? EducationLearningEvidenceUse.Unverified,
                    QualificationRevisionId = decision?.Id, NoHintsOrAnswersConfirmed = decision?.NoHints ?? false, NewRequirementsConfirmed = decision?.NewRequirements ?? false,
                    OriginalRelationshipsChecked = decision?.OriginsChecked ?? false, Points = content.MappingEvidence.Select(p => new EducationLearningPointScore(p.TextbookKnowledgePointMappingId,
                        p.KnowledgePointId, p.KnowledgePointRevisionId, p.MaxScore, p.ObtainedScore)).OrderBy(p => p.MappingId).ToList() });
            }
        }
        return result;
    }
}
