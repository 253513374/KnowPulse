using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Exam;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace KnowPulse.Education;

[RemoteService(false)]
public class EducationTeachingFormalAssessmentGuard : KnowPulseAppService
{
    private IRepository<T, Guid> Repo<T>() where T : class, IEntity<Guid> => LazyServiceProvider.LazyGetRequiredService<IRepository<T, Guid>>();
    private async Task<T?> RefAsync<T>(Guid id) where T : class, IEntity<Guid> { using var tracking = Repo<T>().DisableTracking(); return await Repo<T>().FindAsync(id); }
    public virtual async Task<bool> CanRemindAsync(Guid recipientId)
    {
        using var deleted = LazyServiceProvider.LazyGetRequiredService<IDataFilter>().Disable<ISoftDelete>();
        using var tracking = Repo<EducationTeachingFormalAssessment>().DisableTracking();
        var binding = await Repo<EducationTeachingFormalAssessment>().FindAsync(x => x.RecipientId == recipientId && x.TenantId == CurrentTenant.Id);
        if (binding == null) return true;
        if (binding.IsDeleted) return false;
        var plan = await RefAsync<EducationTeachingPlan>(binding.PlanId); var activity = await RefAsync<EducationTeachingActivity>(binding.ActivityId);
        var review = binding.CurrentReviewId is Guid reviewId ? await RefAsync<EducationTeachingFormalReview>(reviewId) : null;
        return plan != null && !plan.IsDeleted && plan.ExecutionState == EducationPlanExecutionState.Running && plan.PublishedRevisionId == binding.PlanRevisionId &&
            activity != null && !activity.IsDeleted && activity.PlanRevisionId == binding.PlanRevisionId &&
            (!binding.CurrentReviewId.HasValue || review != null && review.IsEnabled && review.PlanContextUse == EducationLearningExposureUse.ConfirmedNoHints);
    }
    public virtual async Task<bool> IsPlanExecutionStoppedAsync(Guid recipientId)
    {
        using var deleted = LazyServiceProvider.LazyGetRequiredService<IDataFilter>().Disable<ISoftDelete>();
        using var tracking = Repo<EducationTeachingFormalAssessment>().DisableTracking();
        var binding = await Repo<EducationTeachingFormalAssessment>().FindAsync(x => x.RecipientId == recipientId && x.TenantId == CurrentTenant.Id);
        if (binding == null) return false;
        var plan = await RefAsync<EducationTeachingPlan>(binding.PlanId);
        return plan == null || plan.ExecutionState != EducationPlanExecutionState.Running;
    }
    public virtual async Task<bool> CanStartAsync(Guid recipientId)
    {
        using var deleted = LazyServiceProvider.LazyGetRequiredService<IDataFilter>().Disable<ISoftDelete>();
        using var tracking = Repo<EducationTeachingFormalAssessment>().DisableTracking();
        var binding = await Repo<EducationTeachingFormalAssessment>().FindAsync(x => x.RecipientId == recipientId && x.TenantId == CurrentTenant.Id);
        if (binding == null) return true;
        if (binding.StudentUserId != CurrentUser.Id || binding.IsDeleted) return false;
        var plan = await RefAsync<EducationTeachingPlan>(binding.PlanId); var activity = await RefAsync<EducationTeachingActivity>(binding.ActivityId);
        var task = await RefAsync<EducationAssessmentTask>(binding.TaskId); var paper = await RefAsync<ExamPaper>(binding.ExamPaperId);
        var recipient = await RefAsync<EducationAssessmentRecipient>(binding.RecipientId);
        if (plan == null || plan.IsDeleted || activity == null || activity.IsDeleted || task == null || task.IsDeleted || paper == null || paper.IsDeleted ||
            plan.TenantId != CurrentTenant.Id || plan.ExecutionState != EducationPlanExecutionState.Running || activity.TenantId != CurrentTenant.Id || task.TenantId != CurrentTenant.Id || paper.TenantId != CurrentTenant.Id ||
            plan.PublishedRevisionId != binding.PlanRevisionId || activity.PlanRevisionId != binding.PlanRevisionId || activity.PlanId != plan.Id ||
            plan.OwnerUserId != binding.OwnerUserId || plan.StudentUserId != binding.StudentUserId || activity.StudentUserId != binding.StudentUserId ||
            task.PublishedRevisionId != binding.TaskRevisionId || task.OwnerUserId != binding.OwnerUserId || paper.SnapshotVersion != binding.PaperSnapshotVersion ||
            recipient == null || recipient.StudentUserId != CurrentUser.Id || recipient.TaskRevisionId != binding.TaskRevisionId ||
            recipient.StudentEnrollmentId != plan.StudentEnrollmentId || recipient.SchoolMembershipId != plan.SchoolMembershipId) return false;
        var review = binding.CurrentReviewId is Guid id ? await RefAsync<EducationTeachingFormalReview>(id) : null;
        if (binding.CurrentReviewId.HasValue && review == null) return false;
        if (review != null && (review.BindingId != binding.Id || review.TenantId != binding.TenantId || review.OwnerUserId != binding.OwnerUserId || review.StudentUserId != binding.StudentUserId ||
            !review.IsEnabled || review.PlanContextUse != EducationLearningExposureUse.ConfirmedNoHints)) return false;
        try {
            var scope = JsonSerializer.Deserialize<EducationCurriculumSnapshot>(binding.CurriculumJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new BusinessException(EducationTeachingPlanErrors.Invalid);
            if (scope.TenantId != binding.TenantId || scope.SchoolId != binding.SchoolId || scope.TeacherUserId != binding.OwnerUserId ||
                scope.TeachingAssignmentId != binding.TeachingAssignmentId || scope.EducationSubjectId != binding.EducationSubjectId) return false;
            await LazyServiceProvider.LazyGetRequiredService<EducationAssessmentAccessGuard>().EnsureRecordedTeacherRelationAsync(scope, plan.AcademicTermId, plan.SchoolClassId, false);
            var frozen = await Repo<EducationTeachingFormalQuestion>().GetListAsync(x => x.BindingId == binding.Id && x.TenantId == binding.TenantId);
            var originals = frozen.Select(x => x.OriginalQuestionId).ToList();
            var version = await RefAsync<EducationTeachingPlanRevision>(binding.PlanRevisionId);
            var basis = version?.Basis(); if (basis == null || originals.Count is < 1 or > 500) return false;
            await LazyServiceProvider.LazyGetRequiredService<EducationLearningReportSourceReader>().CheckFormalNoveltyAsync(scope, binding.StudentUserId, originals,
                basis.Questions.Select(x => x.OriginalQuestionId).Distinct().ToList(), binding.Id, paperQuestionIds: frozen.Select(x => x.PaperQuestionId).ToList());
            return true;
        } catch (BusinessException) { return false; } catch (Volo.Abp.Authorization.AbpAuthorizationException) { return false; }
    }
    public virtual async Task EnsureCanStartAsync(Guid recipientId)
    {
        if (!await CanStartAsync(recipientId)) throw new BusinessException(EducationTeachingPlanErrors.AssessmentStopped);
    }
}
