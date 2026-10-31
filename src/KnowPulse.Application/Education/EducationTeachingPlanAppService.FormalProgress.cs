using System;
using System.Threading.Tasks;
using Volo.Abp;

namespace KnowPulse.Education;

public partial class EducationTeachingPlanAppService
{
    private async Task<EducationActivityFormalAssessmentDto?> FormalProgressAsync(EducationTeachingActivity activity, bool checkStart = true)
    {
        if (activity.StepKind is not (EducationTeachingPlanStepKind.Diagnostic or EducationTeachingPlanStepKind.Reassessment)) return null;
        var binding = await FormalBindingAsync(activity.Id); if (binding == null) return null;
        var student = CurrentUser.Id == activity.StudentUserId;
        if (!await IsMvpPermissionGrantedAsync(student ? EducationPermissions.AssessmentParticipate : EducationPermissions.AssessmentManage))
            return new() { RequiresAssessmentPermission = true };
        var plan = await RefAsync<EducationTeachingPlan>(binding.PlanId); var task = await RefAsync<EducationAssessmentTask>(binding.TaskId);
        var recipient = await RefAsync<EducationAssessmentRecipient>(binding.RecipientId); var review = await FormalReviewAsync(binding);
        var stopped = binding.IsDeleted || plan?.PublishedRevisionId != binding.PlanRevisionId || plan.ExecutionState != EducationPlanExecutionState.Running ||
            review != null && (!review.IsEnabled || review.PlanContextUse != EducationLearningExposureUse.ConfirmedNoHints);
        var dto = new EducationActivityFormalAssessmentDto { IsStopped = stopped };
        if (task == null || task.TenantId != binding.TenantId || task.OwnerUserId != binding.OwnerUserId ||
            task.PublishedRevisionId != binding.TaskRevisionId || recipient == null || recipient.TaskId != binding.TaskId ||
            recipient.TaskRevisionId != binding.TaskRevisionId || recipient.StudentUserId != activity.StudentUserId) { dto.IsStopped = true; return dto; }
        dto.CanOpen = true; dto.TaskId = task.Id; dto.RecipientId = recipient.Id; dto.Title = binding.TaskTitle;
        if (recipient.CurrentAttemptId is Guid id) {
            var attempt = await RefAsync<EducationAssessmentAttempt>(id) ?? throw Invalid();
            if (attempt.TenantId != binding.TenantId || attempt.RecipientId != recipient.Id || attempt.StudentUserId != binding.StudentUserId ||
                attempt.TaskRevisionId != binding.TaskRevisionId || attempt.ExamPaperId != binding.ExamPaperId) throw Invalid();
            dto.AttemptId = attempt.Id;
            if (attempt.SubmissionId is Guid submissionId) {
                var submission = await RefAsync<EducationAssessmentSubmission>(submissionId) ?? throw Invalid();
                if (submission.AttemptId != attempt.Id || submission.StudentUserId != binding.StudentUserId || submission.ExamPaperId != binding.ExamPaperId ||
                    submission.PaperSnapshotVersion != binding.PaperSnapshotVersion) throw Invalid();
                dto.SealedAtUtc = submission.SealedAtUtc;
            } else if (student) {
                var revision = await RefAsync<EducationAssessmentTaskRevision>(binding.TaskRevisionId) ?? throw Invalid(); var now = DateTime.UtcNow;
                dto.CanStart = task.ClosureId == null && now >= revision.StartsAtUtc && now < revision.EndsAtUtc && now < attempt.ExpiresAtUtc;
            }
        } else if (student && checkStart && !stopped) {
            var revision = await RefAsync<EducationAssessmentTaskRevision>(binding.TaskRevisionId) ?? throw Invalid(); var now = DateTime.UtcNow;
            dto.CanStart = task.ClosureId == null && now >= revision.StartsAtUtc && now < revision.EndsAtUtc &&
                await LazyServiceProvider.LazyGetRequiredService<EducationTeachingFormalAssessmentGuard>().CanStartAsync(recipient.Id);
        }
        return dto;
    }
}
