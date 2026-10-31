using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using KnowPulse.Exam;
using Volo.Abp.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public partial class EducationAssessmentAppService
{
    [Authorize(EducationPermissions.AssessmentParticipate), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationAssessmentScoreReviewRequestDto> RequestScoreReviewAsync(Guid recipientId, RequestEducationAssessmentScoreReviewDto input)
    {
        await StudentAsync(); EducationRules.RequireId(input.OperationId);
        var fp = Hash(new { Command = "score-review", recipientId, Input = input });
        try
        {
            using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
            var recipient = await StudentRecipientAsync(recipientId); await StudentRevisionAsync(recipient);
            var prior = await ReferenceAsync<EducationAssessmentScoreReviewRequest>(input.OperationId);
            if (prior != null)
            {
                if (prior.TaskId != recipient.TaskId || prior.StudentUserId != CurrentUser.Id || prior.TenantId != CurrentTenant.Id || prior.RequestFingerprint != fp) throw Conflict();
                return await CompleteAsync(uow, () => StudentReviewDtoAsync(prior));
            }
            await StudentRevisionAsync(recipient, forUpdate: true);
            var task = await ReferenceAsync<EducationAssessmentTask>(recipient.TaskId) ?? throw new AbpAuthorizationException();
            var release = await ReferenceAsync<EducationAssessmentResultRelease>(input.ResultReleaseId) ?? throw new AbpAuthorizationException();
            if (task.CurrentResultReleaseId != release.Id || release.TaskId != task.Id || release.TenantId != CurrentTenant.Id ||
                release.VisibilityAt(DateTime.UtcNow) == ExamResultVisibility.Hidden) throw new AbpAuthorizationException();
            var item = (await ReferencesAsync<EducationAssessmentResultReleaseItem>(x => x.ResultReleaseId == release.Id && x.RecipientId == recipientId && x.StudentUserId == CurrentUser.Id && x.TenantId == CurrentTenant.Id)).SingleOrDefault() ?? throw new AbpAuthorizationException();
            var review = new EducationAssessmentScoreReviewRequest(input.OperationId, item, input.PaperQuestionId, input.Reason, fp, DateTime.UtcNow);
            await Repo<EducationAssessmentScoreReviewRequest>().InsertAsync(review, autoSave: true);
            return await CompleteAsync(uow, () => StudentReviewDtoAsync(review));
        }
        catch (Exception error) when (Recoverable(error))
        {
            using var recovery = Uow.Begin(requiresNew: true, isTransactional: false);
            var recipient = await StudentRecipientAsync(recipientId); await StudentRevisionAsync(recipient);
            var receipt = await ReferenceAsync<EducationAssessmentScoreReviewRequest>(input.OperationId); if (receipt == null) throw;
            if (receipt.TaskId != recipient.TaskId || receipt.StudentUserId != CurrentUser.Id || receipt.TenantId != CurrentTenant.Id || receipt.RequestFingerprint != fp) throw Conflict();
            return await CompleteAsync(recovery, () => StudentReviewDtoAsync(receipt));
        }
    }

    private async Task<EducationAssessmentScoreReviewRequestDto> StudentReviewDtoAsync(EducationAssessmentScoreReviewRequest review)
    {
        var dto = GradeDto<EducationAssessmentScoreReviewRequestDto>(review);
        if (await ResultVisibilityAsync(review.TaskId) != ExamResultVisibility.FullReview) dto.ResolutionReason = null;
        return dto;
    }

    [Authorize(EducationPermissions.AssessmentParticipate)]
    public virtual async Task<EducationAssessmentScoreReviewRequestDto> GetMyScoreReviewAsync(Guid requestId)
    {
        await StudentAsync();
        var review = await ReferenceAsync<EducationAssessmentScoreReviewRequest>(requestId) ?? throw new AbpAuthorizationException();
        if (review.TenantId != CurrentTenant.Id || review.StudentUserId != CurrentUser.Id) throw new AbpAuthorizationException();
        var recipient = (await ReferencesAsync<EducationAssessmentRecipient>(x => x.TaskId == review.TaskId && x.StudentUserId == CurrentUser.Id)).SingleOrDefault() ?? throw new AbpAuthorizationException();
        await StudentRevisionAsync(recipient); return await StudentReviewDtoAsync(review);
    }

    [Authorize(EducationPermissions.AssessmentManage)]
    public virtual async Task<PagedResultDto<EducationAssessmentScoreReviewRequestDto>> GetScoreReviewsAsync(Guid taskId, PagedResultRequestDto input)
    {
        await GradingTaskAsync(taskId); var take = Page(input);
        using var tracking = Repo<EducationAssessmentScoreReviewRequest>().DisableTracking();
        var query = (await Repo<EducationAssessmentScoreReviewRequest>().GetQueryableAsync()).Where(x => x.TaskId == taskId && x.TenantId == CurrentTenant.Id);
        return new(await AsyncExecuter.CountAsync(query), (await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.RequestedAtUtc).ThenBy(x => x.Id)
            .Skip(input.SkipCount).Take(take))).Select(GradeDto<EducationAssessmentScoreReviewRequestDto>).ToList());
    }

    [Authorize(EducationPermissions.AssessmentManage), UnitOfWork(IsDisabled = true)]
    public virtual async Task<EducationAssessmentScoreReviewRequestDto> ResolveScoreReviewAsync(Guid requestId, ResolveEducationAssessmentScoreReviewDto input)
    {
        await TeacherAsync(); EducationRules.RequireId(input.OperationId);
        var fp = Hash(new { Command = "resolve-review", requestId, Input = input });
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var existing = await ReferenceAsync<EducationAssessmentScoreReviewRequest>(requestId) ?? throw new AbpAuthorizationException();
        var (task, _) = await GradingTaskAsync(existing.TaskId, write: true);
        using var tracking = Repo<EducationAssessmentScoreReviewRequest>().EnableTracking();
        var review = await Repo<EducationAssessmentScoreReviewRequest>().GetAsync(requestId);
        if (review.ResolutionOperationId == input.OperationId)
        {
            if (review.ResolutionRequestFingerprint != fp) throw Conflict();
            return await CompleteAsync(uow, () => Task.FromResult(GradeDto<EducationAssessmentScoreReviewRequestDto>(review)));
        }
        if (review.ConcurrencyStamp != input.ConcurrencyStamp) throw new AbpDbConcurrencyException();
        if (input.Decision == EducationAssessmentScoreReviewDecision.Corrected)
        {
            var corrected = input.CorrectedGradeRevisionId.HasValue ? await ReferenceAsync<EducationAssessmentGradeRevision>(input.CorrectedGradeRevisionId.Value) : null;
            var item = (await ReferencesAsync<EducationAssessmentResultReleaseItem>(x => x.ResultReleaseId == input.ResultingReleaseId && x.SubmissionId == review.SubmissionId && x.TenantId == CurrentTenant.Id)).SingleOrDefault();
            if (corrected == null || corrected.Kind != EducationAssessmentGradeRevisionKind.Confirmed || corrected.SubmissionId != review.SubmissionId ||
                corrected.TenantId != CurrentTenant.Id || corrected.RecordedAtUtc < review.RequestedAtUtc || item == null ||
                task.CurrentResultReleaseId != item.ResultReleaseId || item.ResultReleaseId == review.ResultReleaseId ||
                !item.Grades.Grades.Any(x => x.GradeRevisionId == corrected.Id) || review.PaperQuestionId.HasValue && review.PaperQuestionId != corrected.PaperQuestionId) throw Invalid();
        }
        else if (input.CorrectedGradeRevisionId.HasValue || input.ResultingReleaseId.HasValue) throw Invalid();
        review.Resolve(input.OperationId, fp, input.ConcurrencyStamp, input.Decision, input.Reason, input.CorrectedGradeRevisionId, input.ResultingReleaseId, DateTime.UtcNow);
        await Repo<EducationAssessmentScoreReviewRequest>().UpdateManyAsync([review], autoSave: true);
        return await CompleteAsync(uow, () => Task.FromResult(GradeDto<EducationAssessmentScoreReviewRequestDto>(review)));
    }

    [Authorize(EducationPermissions.AssessmentManage)]
    public virtual async Task<EducationAssessmentScoreReviewRequestDto?> GetScoreReviewResolutionAsync(Guid requestId, Guid operationId)
    {
        await TeacherAsync();
        var review = await ReferenceAsync<EducationAssessmentScoreReviewRequest>(requestId) ?? throw new AbpAuthorizationException();
        await GradingTaskAsync(review.TaskId);
        return review.ResolutionOperationId == operationId ? GradeDto<EducationAssessmentScoreReviewRequestDto>(review) : null;
    }
}
