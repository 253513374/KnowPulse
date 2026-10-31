using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public partial class EducationTeachingPlanAppService
{
    private static readonly JsonSerializerOptions FormalJson = new(JsonSerializerDefaults.Web);
    private sealed record FormalCandidate(EducationTeachingActivity Activity, EducationTeachingPlan Plan, EducationAssessmentTaskRevision Task,
        EducationAssessmentRecipient Recipient, ExamPaper Paper, List<ExamPaperQuestion> Questions, EducationFormalAssessmentPreviewDto Preview);
    private async Task<EducationTeachingFormalAssessment?> FormalBindingAsync(Guid activityId)
    {
        using var deleted = LazyServiceProvider.LazyGetRequiredService<IDataFilter>().Disable<ISoftDelete>();
        return (await RowsAsync<EducationTeachingFormalAssessment>(x => x.ActivityId == activityId && x.TenantId == CurrentTenant.Id)).SingleOrDefault();
    }
    private async Task<EducationTeachingFormalReview?> FormalReviewAsync(EducationTeachingFormalAssessment binding)
    {
        var review = binding.CurrentReviewId is Guid id ? await RefAsync<EducationTeachingFormalReview>(id) ?? throw Invalid() : null;
        if (review != null && (review.BindingId != binding.Id || review.TenantId != binding.TenantId || review.OwnerUserId != binding.OwnerUserId ||
            review.StudentUserId != binding.StudentUserId)) throw Invalid();
        return review;
    }
    private static EducationFormalBindingReceiptDto FormalReceipt(EducationTeachingFormalAssessment row) => new() { Id = row.Id, ActivityId = row.ActivityId,
        TaskId = row.TaskId, TaskRevisionId = row.TaskRevisionId, RecipientId = row.RecipientId, PaperSnapshotVersion = row.PaperSnapshotVersion,
        RecordedAtUtc = row.RecordedAtUtc, RequestFingerprint = row.RequestFingerprint };
    private static EducationFormalReviewDto FormalReviewDto(EducationTeachingFormalReview row) => new() { Id = row.Id, BindingId = row.BindingId,
        RevisionNumber = row.RevisionNumber, BasedOnReviewId = row.BasedOnReviewId, IsEnabled = row.IsEnabled, PlanContextUse = row.PlanContextUse,
        Reason = row.Reason, RecordedAtUtc = row.RecordedAtUtc, RequestFingerprint = row.RequestFingerprint };
    private async Task<FormalCandidate> FormalCandidateAsync(Guid activityId, Guid taskId, EducationTeachingFormalAssessment? existing = null)
    {
        await HintTeacherAsync(); var (activity, plan) = await ActivityAccessAsync(activityId, false);
        if (activity.StepKind is not (EducationTeachingPlanStepKind.Diagnostic or EducationTeachingPlanStepKind.Reassessment)) throw Invalid();
        var planVersion = await RefAsync<EducationTeachingPlanRevision>(activity.PlanRevisionId) ?? throw Invalid(); var basis = planVersion.Basis() ?? throw Invalid();
        var task = await RefAsync<EducationAssessmentTask>(taskId) ?? throw new AbpAuthorizationException();
        if (task.TenantId != CurrentTenant.Id || task.OwnerUserId != CurrentUser.Id) throw new AbpAuthorizationException();
        var revision = await RefAsync<EducationAssessmentTaskRevision>(existing?.TaskRevisionId ?? task.PublishedRevisionId ?? throw Invalid()) ?? throw Invalid();
        if (revision.TaskId != task.Id || revision.TenantId != CurrentTenant.Id || revision.OwnerUserId != CurrentUser.Id ||
            revision.TeachingAssignmentId != plan.TeachingAssignmentId || revision.SchoolId != plan.SchoolId ||
            revision.AcademicTermId != plan.AcademicTermId || revision.SchoolClassId != plan.SchoolClassId || revision.PublishedAtUtc == null) throw Invalid();
        var recipient = (await RowsAsync<EducationAssessmentRecipient>(x => x.TaskRevisionId == revision.Id && x.StudentUserId == plan.StudentUserId && x.TenantId == CurrentTenant.Id)).SingleOrDefault()
            ?? throw new AbpAuthorizationException();
        if (recipient.StudentEnrollmentId != plan.StudentEnrollmentId || recipient.SchoolMembershipId != plan.SchoolMembershipId) throw new AbpAuthorizationException();
        var paper = await RefAsync<ExamPaper>(revision.ExamPaperId) ?? throw Invalid();
        if (paper.TenantId != CurrentTenant.Id || paper.PersonalOwnerUserId != CurrentUser.Id || paper.QuestionCount is < 1 or > 500) throw Invalid();
        var questions = (await RowsAsync<ExamPaperQuestion>(x => x.PaperId == paper.Id && x.TenantId == CurrentTenant.Id)).OrderBy(x => x.DisplayOrder).ToList();
        if (questions.Count != paper.QuestionCount || questions.Sum(x => x.Score) != paper.TotalScore) throw Invalid();
        var scope = JsonSerializer.Deserialize<EducationCurriculumSnapshot>(revision.CurriculumSnapshotJson, FormalJson) ?? throw Invalid();
        if (scope.TenantId != plan.TenantId || scope.SchoolId != plan.SchoolId || scope.TeacherUserId != plan.OwnerUserId ||
            scope.EducationSubjectId != plan.EducationSubjectId || scope.TeachingAssignmentId != plan.TeachingAssignmentId ||
            scope.TextbookVersionId != plan.TextbookVersionId) throw Invalid();
        var covered = new HashSet<Guid>(); var questionDtos = new List<EducationFormalQuestionPreviewDto>();
        foreach (var question in questions) {
            if (!question.QuestionDefinitionRevisionId.HasValue || question.CurriculumSnapshotJson == null || question.CurriculumPointWeightsJson == null) throw Invalid();
            var questionScope = JsonSerializer.Deserialize<EducationCurriculumSnapshot>(question.CurriculumSnapshotJson, FormalJson) ?? throw Invalid();
            var weights = JsonSerializer.Deserialize<List<QuestionCurriculumPointWeight>>(question.CurriculumPointWeightsJson, FormalJson) ?? throw Invalid();
            if (questionScope.TenantId != plan.TenantId || questionScope.SchoolId != plan.SchoolId || questionScope.EducationSubjectId != plan.EducationSubjectId ||
                questionScope.TeachingAssignmentId != plan.TeachingAssignmentId || questionScope.TextbookVersionId != plan.TextbookVersionId ||
                weights.Count == 0 || weights.Any(x => x.Score <= 0 || x.Weight <= 0) || weights.Sum(x => x.Score) != question.Score ||
                weights.Select(x => x.TextbookKnowledgePointMappingId).Distinct().Count() != weights.Count) throw Invalid();
            foreach (var weight in weights) {
                var point = questionScope.Mappings.SingleOrDefault(x => x.MappingId == weight.TextbookKnowledgePointMappingId) ?? throw Invalid();
                covered.Add(point.KnowledgePointRevisionId);
            }
            var scoring = question.ScoringDefinitionJson == null ? null : QuestionScoringDefinition.Parse(question.ScoringDefinitionJson);
            questionDtos.Add(new() { PaperQuestionId = question.Id, Title = question.Title, Stem = question.Stem, ReferenceAnswer = scoring?.ReferenceAnswer,
                Options = JsonSerializer.Deserialize<List<ExamPaperQuestionOptionDto>>(question.OptionsJson, FormalJson) ?? [], Analysis = question.Analysis,
                ScoringPoints = scoring?.ScoringPoints.Select(x => x.Description).ToList() ?? [],
                CorrectAnswers = JsonSerializer.Deserialize<List<string>>(question.CorrectAnswersJson) ?? [],
                BlankAnswers = scoring?.Blanks.Select(x => x.StemMarker + ": " + string.Join(" / ", x.AllowedAnswers)).ToList() ?? [] });
        }
        if (!covered.SetEquals(planVersion.Content()!.TargetPointRevisionIds)) throw new BusinessException(EducationTeachingPlanErrors.AssessmentNotNew);
        string? novelty = null;
        if (existing == null) {
            if (plan.PublishedRevisionId != activity.PlanRevisionId || !await FreshAsync(basis) || task.ClosureId != null || task.PublishedRevisionId != revision.Id ||
                recipient.StartedAttemptCount != 0 || recipient.CurrentAttemptId != null || revision.MaxAttempts != 1 || revision.EndsAtUtc <= DateTime.UtcNow ||
                revision.StartsAtUtc < activity.StartsAtUtc || revision.EndsAtUtc > activity.DueAtUtc || revision.DurationMinutes > activity.EstimatedMinutes) throw Changed();
            novelty = await Reader.CheckFormalNoveltyAsync(scope, plan.StudentUserId, questions.Select(x => x.OriginalQuestionId).ToList(),
                basis.Questions.Select(x => x.OriginalQuestionId).Distinct().ToList(), paperQuestionIds: questions.Select(x => x.Id).ToList());
        } else if (existing.ActivityId != activity.Id || existing.PlanRevisionId != activity.PlanRevisionId || existing.RecipientId != recipient.Id ||
            existing.ExamPaperId != paper.Id || existing.PaperSnapshotVersion != paper.SnapshotVersion) throw Changed();
        var fingerprint = Hash(new { ActivityId = activity.Id, activity.CurrentRevisionId, activity.ConcurrencyStamp, PlanRevisionId = activity.PlanRevisionId,
            TaskRevisionId = revision.Id, paper.SnapshotVersion, Novelty = novelty,
            Questions = questions.Select(x => new { x.Id, x.OriginalQuestionId, x.QuestionDefinitionRevisionId, x.Score, x.Stem, x.Title,
                x.CorrectAnswersJson, x.ScoringDefinitionJson, x.CurriculumPointWeightsJson, x.CurriculumSnapshotJson }) });
        var preview = new EducationFormalAssessmentPreviewDto { ActivityId = activity.Id, TaskId = task.Id, TaskRevisionId = revision.Id, RecipientId = recipient.Id,
            ExamPaperId = paper.Id, PaperSnapshotVersion = paper.SnapshotVersion, TaskTitle = revision.Title, ExpectedActivityRevisionId = activity.CurrentRevisionId,
            ActivityConcurrencyStamp = activity.ConcurrencyStamp, PreviewFingerprint = fingerprint, PlanId = plan.Id, PlanRevisionId = planVersion.Id, PlanContent = PublicContent(planVersion), Questions = questionDtos };
        return new(activity, plan, revision, recipient, paper, questions, preview);
    }
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), Authorize(EducationPermissions.AssessmentManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<PagedResultDto<EducationFormalCandidateDto>> GetFormalAssessmentCandidatesAsync(Guid activityId, PagedResultRequestDto input)
    {
        await HintTeacherAsync(); var take = Page(input); using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var (activity, plan) = await ActivityAccessAsync(activityId, false);
        if (plan.PublishedRevisionId != activity.PlanRevisionId || activity.StepKind is not (EducationTeachingPlanStepKind.Diagnostic or EducationTeachingPlanStepKind.Reassessment)) throw Changed();
        using var rt = Repo<EducationAssessmentRecipient>().DisableTracking(); using var tt = Repo<EducationAssessmentTask>().DisableTracking(); using var vt = Repo<EducationAssessmentTaskRevision>().DisableTracking();
        var recipients = await Repo<EducationAssessmentRecipient>().GetQueryableAsync(); var tasks = await Repo<EducationAssessmentTask>().GetQueryableAsync();
        var versions = await Repo<EducationAssessmentTaskRevision>().GetQueryableAsync(); var now = DateTime.UtcNow;
        var query = from recipient in recipients join task in tasks on recipient.TaskId equals task.Id join version in versions on recipient.TaskRevisionId equals version.Id
            where recipient.TenantId == CurrentTenant.Id && recipient.StudentUserId == plan.StudentUserId && recipient.StudentEnrollmentId == plan.StudentEnrollmentId &&
                recipient.SchoolMembershipId == plan.SchoolMembershipId && recipient.CurrentAttemptId == null && recipient.StartedAttemptCount == 0 &&
                task.TenantId == CurrentTenant.Id && task.OwnerUserId == CurrentUser.Id && task.PublishedRevisionId == version.Id && task.ClosureId == null &&
                version.TenantId == CurrentTenant.Id && version.TeachingAssignmentId == plan.TeachingAssignmentId &&
                version.StartsAtUtc >= activity.StartsAtUtc && version.EndsAtUtc <= activity.DueAtUtc && version.EndsAtUtc > now &&
                version.DurationMinutes <= activity.EstimatedMinutes && version.MaxAttempts == 1
            select new EducationFormalCandidateDto { Id = task.Id, Title = version.Title, StartsAtUtc = version.StartsAtUtc, EndsAtUtc = version.EndsAtUtc, DurationMinutes = version.DurationMinutes };
        var count = await AsyncExecuter.CountAsync(query); var rows = await AsyncExecuter.ToListAsync(query.OrderBy(x => x.EndsAtUtc).ThenBy(x => x.Id).Skip(input.SkipCount).Take(take));
        await uow.CompleteAsync(); return new(count, rows);
    }
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), Authorize(EducationPermissions.AssessmentManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationFormalAssessmentPreviewDto> PreviewFormalAssessmentAsync(Guid activityId, Guid taskId)
    {
        await HintTeacherAsync(); using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var candidate = await FormalCandidateAsync(activityId, taskId); await uow.CompleteAsync(); return candidate.Preview;
    }
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), Authorize(EducationPermissions.AssessmentManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationFormalBindingReceiptDto> BindFormalAssessmentAsync(Guid activityId, BindEducationFormalAssessmentDto input)
    {
        await HintTeacherAsync(); EducationRules.RequireId(input.OperationId); var fingerprint = Hash(new { Command = "bind-formal-assessment", activityId, input });
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); await ActivityAccessAsync(activityId, false);
        var prior = await RefAsync<EducationTeachingFormalAssessment>(input.OperationId);
        if (prior != null) {
            if (prior.ActivityId != activityId || prior.OwnerUserId != CurrentUser.Id || prior.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
            if (prior.RequestFingerprint != fingerprint) throw new BusinessException(EducationTeachingPlanErrors.OperationConflict);
            await uow.CompleteAsync(); return FormalReceipt(prior);
        }
        if (await FormalBindingAsync(activityId) != null) throw new BusinessException(EducationTeachingPlanErrors.OperationConflict);
        var candidate = await FormalCandidateAsync(activityId, input.TaskId);
        await ValidateMaterialsAsync(candidate.Plan, (await RefAsync<EducationTeachingPlanRevision>(candidate.Activity.PlanRevisionId) ?? throw Invalid()).Content()!);
        if (candidate.Task.Id != input.ExpectedTaskRevisionId || candidate.Paper.SnapshotVersion != input.ExpectedPaperSnapshotVersion ||
            candidate.Activity.CurrentRevisionId != input.ExpectedActivityRevisionId || candidate.Activity.ConcurrencyStamp != input.ActivityConcurrencyStamp ||
            candidate.Preview.PreviewFingerprint != input.PreviewFingerprint) throw new AbpDbConcurrencyException();
        var binding = new EducationTeachingFormalAssessment(input.OperationId, candidate.Activity, candidate.Plan, candidate.Task, candidate.Recipient, candidate.Paper.SnapshotVersion,
            input.ConfirmOriginalRelationships, input.ConfirmNewAssessmentRequirements, input.ConfirmExactPlanContainsNoHints, input.PreviewFingerprint, fingerprint, DateTime.UtcNow);
        await Repo<EducationTeachingFormalAssessment>().InsertAsync(binding, autoSave: true);
        await Repo<EducationTeachingFormalQuestion>().InsertManyAsync(candidate.Questions.Select(x => new EducationTeachingFormalQuestion(binding, x)).ToList(), autoSave: true);
        await uow.CompleteAsync(); return FormalReceipt(binding);
    }
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), Authorize(EducationPermissions.AssessmentManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationFormalAssessmentDetailsDto?> GetFormalAssessmentAsync(Guid activityId)
    {
        await HintTeacherAsync(); using var uow = Uow.Begin(requiresNew: true, isTransactional: true); await ActivityAccessAsync(activityId, false);
        var binding = await FormalBindingAsync(activityId); if (binding == null) { await uow.CompleteAsync(); return null; }
        var candidate = await FormalCandidateAsync(activityId, binding.TaskId, binding); var review = await FormalReviewAsync(binding);
        var result = new EducationFormalAssessmentDetailsDto { Binding = FormalReceipt(binding), Preview = candidate.Preview, ConcurrencyStamp = binding.ConcurrencyStamp,
            CurrentReview = review == null ? null : FormalReviewDto(review) }; await uow.CompleteAsync(); return result;
    }
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), Authorize(EducationPermissions.AssessmentManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationFormalReviewDto> ReviewFormalAssessmentAsync(Guid activityId, ReviewEducationFormalAssessmentDto input)
    {
        await HintTeacherAsync(); EducationRules.RequireId(input.OperationId); var fingerprint = Hash(new { Command = "formal-plan-context-review", activityId, input });
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true); await ActivityAccessAsync(activityId, false);
        var binding = await FormalBindingAsync(activityId) ?? throw Invalid();
        var prior = await RefAsync<EducationTeachingFormalReview>(input.OperationId);
        if (prior != null) {
            if (prior.BindingId != binding.Id || prior.OwnerUserId != CurrentUser.Id || prior.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
            if (prior.RequestFingerprint != fingerprint) throw new BusinessException(EducationTeachingPlanErrors.OperationConflict);
            await uow.CompleteAsync(); return FormalReviewDto(prior);
        }
        using var tracking = Repo<EducationTeachingFormalAssessment>().EnableTracking();
        binding = await Repo<EducationTeachingFormalAssessment>().GetAsync(binding.Id);
        if (binding.IsDeleted || binding.CurrentReviewId != input.ExpectedReviewId || binding.ConcurrencyStamp != input.ConcurrencyStamp) throw new AbpDbConcurrencyException();
        if (input.IsEnabled && input.PlanContextUse == EducationLearningExposureUse.ConfirmedNoHints) {
            var context = await ActivityAccessAsync(activityId, false);
            await ValidateMaterialsAsync(context.Plan, (await RefAsync<EducationTeachingPlanRevision>(context.Root.PlanRevisionId) ?? throw Invalid()).Content()!);
        }
        var review = new EducationTeachingFormalReview(input.OperationId, binding, input.IsEnabled, input.PlanContextUse, input.ConfirmExactContext, input.Reason,
            input.ConcurrencyStamp, fingerprint, DateTime.UtcNow);
        binding.Advance(review); await Repo<EducationTeachingFormalReview>().InsertAsync(review, autoSave: true);
        await Repo<EducationTeachingFormalAssessment>().UpdateManyAsync([binding], autoSave: true); await uow.CompleteAsync(); return FormalReviewDto(review);
    }
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), Authorize(EducationPermissions.AssessmentManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationFormalBindingReceiptDto?> GetFormalBindingOperationAsync(Guid activityId, Guid operationId)
    {
        await HintTeacherAsync(); using var uow = Uow.Begin(requiresNew: true, isTransactional: true); await ActivityAccessAsync(activityId, false);
        var binding = await RefAsync<EducationTeachingFormalAssessment>(operationId);
        if (binding != null && (binding.ActivityId != activityId || binding.OwnerUserId != CurrentUser.Id || binding.TenantId != CurrentTenant.Id)) throw new AbpAuthorizationException();
        await uow.CompleteAsync(); return binding == null ? null : FormalReceipt(binding);
    }
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), Authorize(EducationPermissions.AssessmentManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationFormalReviewDto?> GetFormalReviewOperationAsync(Guid activityId, Guid operationId)
    {
        await HintTeacherAsync(); using var uow = Uow.Begin(requiresNew: true, isTransactional: true); await ActivityAccessAsync(activityId, false);
        var binding = await FormalBindingAsync(activityId) ?? throw Invalid(); var review = await RefAsync<EducationTeachingFormalReview>(operationId);
        if (review != null && (review.BindingId != binding.Id || review.OwnerUserId != CurrentUser.Id || review.TenantId != CurrentTenant.Id)) throw new AbpAuthorizationException();
        await uow.CompleteAsync(); return review == null ? null : FormalReviewDto(review);
    }
    internal async Task<EducationLearningExposureUse> FormalContextUseForReviewAsync(Guid activityId)
    {
        await HintTeacherAsync(); var activity = await RefAsync<EducationTeachingActivity>(activityId) ?? throw new AbpAuthorizationException();
        var plan = await OwnAsync(activity.PlanId); var binding = await FormalBindingAsync(activityId) ?? throw Invalid();
        if (activity.StudentUserId != plan.StudentUserId || binding.OwnerUserId != plan.OwnerUserId || binding.StudentUserId != plan.StudentUserId) throw Invalid();
        return (await FormalReviewAsync(binding))?.PlanContextUse ?? EducationLearningExposureUse.ConfirmedNoHints;
    }
}
