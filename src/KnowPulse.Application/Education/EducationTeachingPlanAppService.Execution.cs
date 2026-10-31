using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public partial class EducationTeachingPlanAppService : IEducationTeachingPlanExecutionAppService
{
    private static EducationPlanDecisionDto DecisionDto(EducationTeachingPlanDecisionRevision row) => new() {
        Id = row.Id, PlanId = row.PlanId, PublishedRevisionId = row.PublishedRevisionId, BasedOnDecisionId = row.BasedOnDecisionId,
        RevisionNumber = row.RevisionNumber, Decision = row.Decision, Before = row.Before, After = row.After, Reason = row.Reason,
        Evidence = EducationLearningReportJson.Read<EducationPlanExecutionEvidence>(row.EvidenceJson, EducationPlanExecutionEvidence.MaxBytes), RequestFingerprint = row.RequestFingerprint, RecordedAtUtc = row.RecordedAtUtc };
    private async Task<EducationTeachingPlanDecisionRevision?> ExecutionReceiptAsync(Guid planId, Guid operationId, string? fingerprint = null)
    {
        await OwnAsync(planId); var row = await RefAsync<EducationTeachingPlanDecisionRevision>(operationId); if (row == null) return null;
        if (row.PlanId != planId || row.OwnerUserId != CurrentUser.Id || row.TenantId != CurrentTenant.Id || fingerprint != null && row.RequestFingerprint != fingerprint)
            throw new BusinessException(EducationTeachingPlanErrors.OperationConflict);
        return row;
    }
    private async Task<EducationPlanExecutionEvidence> ExecutionEvidenceAsync(EducationTeachingPlan plan)
    {
        var version = await RefAsync<EducationTeachingPlanRevision>(plan.PublishedRevisionId ?? plan.LastPublishedRevisionId ?? throw Invalid()) ?? throw Invalid();
        if (version.PlanId != plan.Id || version.OwnerUserId != plan.OwnerUserId || version.StudentUserId != plan.StudentUserId || version.TenantId != plan.TenantId || version.Kind != EducationTeachingPlanRevisionKind.Published) throw Invalid();
        var fresh = false; try { fresh = await FreshAsync(version.Basis()!); } catch (BusinessException) { } catch (AbpAuthorizationException) { }
        var assessments = await IsMvpPermissionGrantedAsync(EducationPermissions.AssessmentManage);
        var evidence = new EducationPlanExecutionEvidence { PublishedRevisionId = version.Id, SourceIsCurrent = fresh, RequiresAssessmentPermission = !assessments };
        var activities = plan.PublishedRevisionId == version.Id ? await EnsureActivitiesAsync(version) : await RowsAsync<EducationTeachingActivity>(x => x.PlanRevisionId == version.Id && x.TenantId == plan.TenantId);
        foreach (var row in activities.OrderBy(x => x.StepOrder)) {
            var (student, follow) = await ActivityHeadsAsync(row); var formal = await FormalProgressAsync(row, checkStart: false);
            var binding = assessments ? await FormalBindingAsync(row.Id) : null;
            int? confirmed = null; string? gradeHash = null; var gradeIds = new System.Collections.Generic.List<Guid>();
            if (formal?.AttemptId is Guid attemptId && assessments) {
                using var tracking = Repo<EducationAssessmentQuestionGrade>().DisableTracking();
                var grades = await AsyncExecuter.ToListAsync((await Repo<EducationAssessmentQuestionGrade>().GetQueryableAsync())
                    .Where(x => x.TenantId == plan.TenantId && x.OwnerUserId == plan.OwnerUserId && x.StudentUserId == plan.StudentUserId && x.AttemptId == attemptId)
                    .OrderBy(x => x.PaperQuestionId).Select(x => new { x.PaperQuestionId, x.CurrentConfirmedRevisionId }).Take(501));
                if (grades.Count > 500) throw new BusinessException(EducationLearningReportErrors.Capacity);
                confirmed = grades.Count(x => x.CurrentConfirmedRevisionId != null); gradeHash = Hash(grades);
                gradeIds = grades.Where(x => x.CurrentConfirmedRevisionId.HasValue).Select(x => x.CurrentConfirmedRevisionId!.Value).ToList();
            }
            evidence.Activities.Add(new() { ActivityId = row.Id, StepId = row.StepId, Kind = row.StepKind, StudentEventId = student?.Id, StudentAction = student?.Action,
                FollowUpEventId = follow?.Id, MaterialsAvailable = await ActivityMaterialsAvailableAsync(row), AssessmentRecipientId = formal?.RecipientId,
                AssessmentTaskId = formal?.TaskId, AttemptId = formal?.AttemptId, SealedAtUtc = formal?.SealedAtUtc, AssessmentStopped = formal?.IsStopped == true,
                BindingReviewId = binding?.CurrentReviewId, ConfirmedQuestionCount = confirmed, ConfirmedGradeSetHash = gradeHash, ConfirmedGradeRevisionIds = gradeIds });
        }
        return evidence;
    }
    private static string ExecutionFingerprint(EducationTeachingPlan plan, EducationPlanExecutionEvidence evidence) => Hash(new { plan.Id, plan.CurrentDecisionId, plan.ExecutionState, evidence });

    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationPlanExecutionReviewDto> GetExecutionReviewAsync(Guid planId)
    {
        await TeacherAsync(); using var uow = Uow.Begin(requiresNew: true, isTransactional: true); await OwnAsync(planId, write: true);
        var plan = await RefAsync<EducationTeachingPlan>(planId) ?? throw Invalid(); var evidence = await ExecutionEvidenceAsync(plan);
        var version = await RefAsync<EducationTeachingPlanRevision>(evidence.PublishedRevisionId) ?? throw Invalid();
        var last = plan.CurrentDecisionId is Guid decisionId ? await RefAsync<EducationTeachingPlanDecisionRevision>(decisionId) ?? throw Invalid() : null;
        var result = new EducationPlanExecutionReviewDto { PlanId = plan.Id, StudentName = plan.StudentName, SubjectName = plan.SubjectName, Title = version.Content()!.Title,
            State = plan.ExecutionState, CurrentDecisionId = plan.CurrentDecisionId, AdjustmentNeedsPublication = last?.Decision == EducationPlanDecision.Adjust && last.PublishedRevisionId == plan.PublishedRevisionId,
            IsCurrentPublication = plan.PublishedRevisionId == version.Id, ConcurrencyStamp = plan.ConcurrencyStamp, Evidence = evidence, EvidenceFingerprint = ExecutionFingerprint(plan, evidence) };
        await uow.CompleteAsync(); return result;
    }
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationPlanDecisionDto> DecideExecutionAsync(Guid planId, DecideEducationPlanExecutionDto input)
    {
        await TeacherAsync(); EducationRules.RequireId(input.OperationId); var fp = Hash(new { Command = "plan-execution", planId, input });
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var receipt = await ExecutionReceiptAsync(planId, input.OperationId, fp); if (receipt != null) { await uow.CompleteAsync(); return DecisionDto(receipt); }
        await OwnAsync(planId, write: true); using var tracking = Repo<EducationTeachingPlan>().EnableTracking(); var plan = await Repo<EducationTeachingPlan>().GetAsync(planId);
        if (plan.ConcurrencyStamp != input.ConcurrencyStamp || plan.CurrentDecisionId != input.ExpectedDecisionId || plan.PublishedRevisionId != input.ExpectedPublishedRevisionId) throw new AbpDbConcurrencyException();
        var evidence = await ExecutionEvidenceAsync(plan); var fingerprint = ExecutionFingerprint(plan, evidence);
        if (fingerprint != input.EvidenceFingerprint) throw new AbpDbConcurrencyException();
        if (input.Decision is EducationPlanDecision.Resume or EducationPlanDecision.Continue) {
            var version = await RefAsync<EducationTeachingPlanRevision>(evidence.PublishedRevisionId) ?? throw Invalid(); var content = version.Content()!;
            await ValidatePeriodAsync(plan, content.StartsAtUtc, content.EndsAtUtc); await ValidateMaterialsAsync(plan, content);
        }
        var previous = plan.CurrentDecisionId is Guid previousId ? await RefAsync<EducationTeachingPlanDecisionRevision>(previousId) ?? throw Invalid() : null;
        var revision = new EducationTeachingPlanDecisionRevision(input.OperationId, plan, previous, input.Decision, input.Reason, evidence, fingerprint,
            input.ConfirmReviewedEvidenceAndUncertainty, fp, input.ConcurrencyStamp, DateTime.UtcNow);
        plan.AdvanceDecision(revision); await Repo<EducationTeachingPlanDecisionRevision>().InsertAsync(revision, autoSave: true);
        await Repo<EducationTeachingPlan>().UpdateAsync(plan, autoSave: true);
        if (revision.Before != revision.After) await Repo<EducationTeachingPlanNotice>().InsertAsync(EducationTeachingPlanNotice.Execution(GuidGenerator.Create(), plan, revision), autoSave: true);
        await uow.CompleteAsync(); return DecisionDto(revision);
    }
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationPlanDecisionDto?> GetExecutionOperationAsync(Guid planId, Guid operationId)
    {
        await TeacherAsync(); using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var receipt = await ExecutionReceiptAsync(planId, operationId);
        await uow.CompleteAsync(); return receipt == null ? null : DecisionDto(receipt);
    }
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<PagedResultDto<EducationPlanDecisionDto>> GetExecutionHistoryAsync(Guid planId, Guid? expectedDecisionId, PagedResultRequestDto input)
    {
        await TeacherAsync(); var take = Page(input); using var uow = Uow.Begin(requiresNew: true, isTransactional: true); var plan = await OwnAsync(planId, write: true);
        if (plan.CurrentDecisionId != expectedDecisionId) throw new AbpDbConcurrencyException();
        using var tracking = Repo<EducationTeachingPlanDecisionRevision>().DisableTracking();
        var query = (await Repo<EducationTeachingPlanDecisionRevision>().GetQueryableAsync()).Where(x => x.PlanId == planId && x.TenantId == CurrentTenant.Id && x.OwnerUserId == CurrentUser.Id);
        var total = await AsyncExecuter.LongCountAsync(query); var rows = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.RevisionNumber).Skip(input.SkipCount).Take(take));
        await uow.CompleteAsync(); return new(total, rows.Select(DecisionDto).ToList());
    }
}
