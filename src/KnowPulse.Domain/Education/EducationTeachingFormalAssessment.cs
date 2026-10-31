using System;
using KnowPulse.Exam;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

public class EducationTeachingFormalAssessment : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid ActivityId { get; private set; }
    public Guid PlanId { get; private set; }
    public Guid PlanRevisionId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid SchoolId { get; private set; }
    public Guid EducationSubjectId { get; private set; }
    public Guid TeachingAssignmentId { get; private set; }
    public EducationTeachingPlanStepKind StepKind { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid TaskRevisionId { get; private set; }
    public Guid RecipientId { get; private set; }
    public Guid ExamPaperId { get; private set; }
    public int PaperSnapshotVersion { get; private set; }
    public string TaskTitle { get; private set; } = "";
    public string CurriculumJson { get; private set; } = "";
    public string RequestFingerprint { get; private set; } = "";
    public string PreviewFingerprint { get; private set; } = "";
    public DateTime RecordedAtUtc { get; private set; }
    public int ReviewRevisionNumber { get; private set; }
    public Guid? CurrentReviewId { get; private set; }
    protected EducationTeachingFormalAssessment() { }
    public EducationTeachingFormalAssessment(Guid id, EducationTeachingActivity activity, EducationTeachingPlan plan, EducationAssessmentTaskRevision task,
        EducationAssessmentRecipient recipient, int paperVersion, bool originsChecked, bool newRequirementsChecked, bool exactPlanHasNoHints,
        string previewFingerprint, string requestFingerprint, DateTime now) : base(EducationRules.RequireId(id))
    {
        if (activity.TenantId != plan.TenantId || activity.PlanId != plan.Id || activity.PlanRevisionId != plan.PublishedRevisionId ||
            activity.OwnerUserId != plan.OwnerUserId || activity.StudentUserId != plan.StudentUserId ||
            activity.StepKind is not (EducationTeachingPlanStepKind.Diagnostic or EducationTeachingPlanStepKind.Reassessment) ||
            task.TenantId != plan.TenantId || task.OwnerUserId != plan.OwnerUserId || task.SchoolId != plan.SchoolId ||
            task.TeachingAssignmentId != plan.TeachingAssignmentId || task.AcademicTermId != plan.AcademicTermId || task.SchoolClassId != plan.SchoolClassId ||
            task.PublishedAtUtc == null || task.StartsAtUtc < activity.StartsAtUtc || task.EndsAtUtc > activity.DueAtUtc || task.EndsAtUtc <= now ||
            task.DurationMinutes > activity.EstimatedMinutes || task.MaxAttempts != 1 || paperVersion < 1 ||
            recipient.TenantId != plan.TenantId || recipient.TaskId != task.TaskId || recipient.TaskRevisionId != task.Id ||
            recipient.StudentUserId != plan.StudentUserId || recipient.StudentEnrollmentId != plan.StudentEnrollmentId ||
            recipient.SchoolMembershipId != plan.SchoolMembershipId || recipient.CurrentAttemptId != null || recipient.StartedAttemptCount != 0 ||
            !originsChecked || !newRequirementsChecked || !exactPlanHasNoHints)
            throw Invalid();
        TenantId = plan.TenantId; ActivityId = activity.Id; PlanId = plan.Id; PlanRevisionId = activity.PlanRevisionId;
        StudentUserId = plan.StudentUserId; OwnerUserId = plan.OwnerUserId; SchoolId = plan.SchoolId; EducationSubjectId = plan.EducationSubjectId;
        TeachingAssignmentId = plan.TeachingAssignmentId; StepKind = activity.StepKind; TaskId = task.TaskId; TaskRevisionId = task.Id;
        RecipientId = recipient.Id; ExamPaperId = task.ExamPaperId; PaperSnapshotVersion = paperVersion; TaskTitle = task.Title;
        CurriculumJson = task.CurriculumSnapshotJson;
        PreviewFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(previewFingerprint);
        RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(requestFingerprint); RecordedAtUtc = EducationRules.RequireUtc(now);
    }
    public void Advance(EducationTeachingFormalReview review)
    {
        if (review.BindingId != Id || review.OwnerUserId != OwnerUserId || review.StudentUserId != StudentUserId || review.TenantId != TenantId ||
            review.RevisionNumber != ReviewRevisionNumber + 1 || review.BasedOnReviewId != CurrentReviewId || review.ExpectedConcurrencyStamp != ConcurrencyStamp) throw Invalid();
        CurrentReviewId = review.Id; ReviewRevisionNumber = review.RevisionNumber;
    }
    private static BusinessException Invalid() => new(EducationTeachingPlanErrors.Invalid);
}

public class EducationTeachingFormalQuestion : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid BindingId { get; private set; }
    public Guid ExamPaperId { get; private set; }
    public Guid PaperQuestionId { get; private set; }
    public Guid OriginalQuestionId { get; private set; }
    public Guid DefinitionRevisionId { get; private set; }
    protected EducationTeachingFormalQuestion() { }
    public EducationTeachingFormalQuestion(EducationTeachingFormalAssessment binding, ExamPaperQuestion question)
        : base(EducationStudentGrowthSnapshot.StableId($"education-formal-question-v1|{binding.Id:D}|{question.Id:D}"))
    {
        if (question.TenantId != binding.TenantId || question.PaperId != binding.ExamPaperId || !question.QuestionDefinitionRevisionId.HasValue)
            throw new BusinessException(EducationTeachingPlanErrors.Invalid);
        TenantId = binding.TenantId; BindingId = binding.Id; ExamPaperId = question.PaperId; PaperQuestionId = question.Id;
        OriginalQuestionId = question.OriginalQuestionId; DefinitionRevisionId = question.QuestionDefinitionRevisionId.Value;
    }
    public Guid ExposureId => ExposureIdFor(Id);
    public static Guid ExposureIdFor(Guid questionId) => EducationStudentGrowthSnapshot.StableId($"education-formal-plan-context-v1|{questionId:D}");
}

public class EducationTeachingFormalReview : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid BindingId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public int RevisionNumber { get; private set; }
    public Guid? BasedOnReviewId { get; private set; }
    public bool IsEnabled { get; private set; }
    public EducationLearningExposureUse PlanContextUse { get; private set; }
    public string Reason { get; private set; } = "";
    public string ExpectedConcurrencyStamp { get; private set; } = "";
    public string RequestFingerprint { get; private set; } = "";
    public DateTime RecordedAtUtc { get; private set; }
    protected EducationTeachingFormalReview() { }
    public EducationTeachingFormalReview(Guid id, EducationTeachingFormalAssessment binding, bool enabled, EducationLearningExposureUse use,
        bool confirmedExactContext, string reason, string stamp, string fingerprint, DateTime now) : base(EducationRules.RequireId(id))
    {
        if (!Enum.IsDefined(use) || !confirmedExactContext || stamp != binding.ConcurrencyStamp || enabled && use != EducationLearningExposureUse.ConfirmedNoHints ||
            now < binding.RecordedAtUtc) throw new BusinessException(EducationTeachingPlanErrors.Invalid);
        TenantId = binding.TenantId; BindingId = binding.Id; OwnerUserId = binding.OwnerUserId; StudentUserId = binding.StudentUserId;
        RevisionNumber = checked(binding.ReviewRevisionNumber + 1); BasedOnReviewId = binding.CurrentReviewId;
        IsEnabled = enabled; PlanContextUse = use; Reason = Check.NotNullOrWhiteSpace(reason, nameof(reason), 2048);
        ExpectedConcurrencyStamp = Check.NotNullOrWhiteSpace(stamp, nameof(stamp), 40);
        RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint); RecordedAtUtc = EducationRules.RequireUtc(now);
    }
}
