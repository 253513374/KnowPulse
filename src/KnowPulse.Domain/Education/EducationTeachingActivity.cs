using System;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

public class EducationTeachingActivity : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid PlanId { get; private set; }
    public Guid PlanRevisionId { get; private set; }
    public Guid StepId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public EducationTeachingPlanStepKind StepKind { get; private set; }
    public int StepOrder { get; private set; }
    public int EstimatedMinutes { get; private set; }
    public DateTime StartsAtUtc { get; private set; }
    public DateTime DueAtUtc { get; private set; }
    public DateTime AssignedAtUtc { get; private set; }
    public DateTime? FirstProvidedAtUtc { get; private set; }
    public int RevisionNumber { get; private set; }
    public Guid? CurrentRevisionId { get; private set; }
    public int StudentRevisionNumber { get; private set; }
    public Guid? CurrentStudentRevisionId { get; private set; }
    public Guid? CurrentFollowUpRevisionId { get; private set; }
    protected EducationTeachingActivity() { }
    public EducationTeachingActivity(EducationTeachingPlanRevision published, Guid stepId, DateTime now)
        : base(Identity(published.Id, stepId, published.StudentUserId))
    {
        var content = published.Content();
        var step = content?.Steps.SingleOrDefault(x => x.Id == stepId);
        if (published.Kind != EducationTeachingPlanRevisionKind.Published || step == null || now < published.RecordedAtUtc) throw Invalid();
        TenantId = published.TenantId; OwnerUserId = published.OwnerUserId; StudentUserId = published.StudentUserId; PlanId = published.PlanId;
        PlanRevisionId = published.Id; StepId = step.Id; StepKind = step.Kind; StepOrder = content!.Steps.FindIndex(x => x.Id == step.Id) + 1; EstimatedMinutes = step.EstimatedMinutes;
        StartsAtUtc = content!.StartsAtUtc; DueAtUtc = step.DueAtUtc; AssignedAtUtc = EducationRules.RequireUtc(now);
    }
    public static Guid Identity(Guid versionId, Guid stepId, Guid studentId)
    {
        EducationRules.RequireId(versionId); EducationRules.RequireId(stepId); EducationRules.RequireId(studentId);
        return EducationStudentGrowthSnapshot.StableId($"education-plan-activity-v1|{versionId:D}|{stepId:D}|{studentId:D}");
    }
    public void MarkProvided(DateTime now)
    {
        EducationRules.RequireUtc(now); if (now < AssignedAtUtc) throw Invalid(); FirstProvidedAtUtc ??= now;
    }
    public void Advance(EducationTeachingActivityRevision revision)
    {
        if (revision.ActivityId != Id || revision.TenantId != TenantId || revision.StudentUserId != StudentUserId || revision.OwnerUserId != OwnerUserId ||
            revision.StepKind != StepKind || revision.RevisionNumber != RevisionNumber + 1 || revision.BasedOnRevisionId != CurrentRevisionId ||
            revision.ExpectedConcurrencyStamp != ConcurrencyStamp) throw Invalid();
        if (revision.Action == EducationTeachingActivityAction.TeacherFollowUp) {
            if (revision.ReplyToStudentRevisionId != CurrentStudentRevisionId || revision.StudentRevisionNumber != null) throw Invalid();
            CurrentFollowUpRevisionId = revision.Id;
        } else {
            if (revision.StudentRevisionNumber != StudentRevisionNumber + 1) throw Invalid();
            StudentRevisionNumber = revision.StudentRevisionNumber.Value; CurrentStudentRevisionId = revision.Id;
        }
        RevisionNumber = revision.RevisionNumber; CurrentRevisionId = revision.Id;
    }
    private static BusinessException Invalid() => new(EducationTeachingPlanErrors.Invalid);
}

public class EducationTeachingActivityRevision : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid ActivityId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public EducationTeachingPlanStepKind StepKind { get; private set; }
    public int RevisionNumber { get; private set; }
    public int? StudentRevisionNumber { get; private set; }
    public Guid? BasedOnRevisionId { get; private set; }
    public Guid? ReplyToStudentRevisionId { get; private set; }
    public EducationTeachingActivityAction Action { get; private set; }
    public EducationTeachingActivityDifficulty? Difficulty { get; private set; }
    public string? SharedText { get; private set; }
    public string? InternalTeacherNote { get; private set; }
    public int? ReportedMinutes { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public string ExpectedConcurrencyStamp { get; private set; } = "";
    public string RequestFingerprint { get; private set; } = "";
    protected EducationTeachingActivityRevision() { }
    public EducationTeachingActivityRevision(Guid id, EducationTeachingActivity activity, EducationTeachingActivityRevision? currentStudent,
        EducationTeachingActivityAction action, EducationTeachingActivityDifficulty? difficulty, string? sharedText, int? reportedMinutes,
        bool confirmSharing, bool confirmActivityOnly, string? teacherNote, Guid? replyToStudentId, string fingerprint, DateTime now)
        : base(EducationRules.RequireId(id))
    {
        if (!Enum.IsDefined(action) || activity.CurrentStudentRevisionId != currentStudent?.Id ||
            currentStudent != null && (currentStudent.ActivityId != activity.Id || currentStudent.StudentUserId != activity.StudentUserId ||
                currentStudent.OwnerUserId != activity.OwnerUserId || currentStudent.TenantId != activity.TenantId || currentStudent.Action == EducationTeachingActivityAction.TeacherFollowUp) ||
            now < activity.AssignedAtUtc) throw Invalid();
        SharedText = string.IsNullOrWhiteSpace(sharedText) ? null : Check.Length(sharedText, nameof(sharedText), 2000);
        if (action == EducationTeachingActivityAction.TeacherFollowUp) {
            if (currentStudent?.Action != EducationTeachingActivityAction.Difficulty || replyToStudentId != currentStudent.Id || !confirmActivityOnly ||
                SharedText != null || difficulty != null || reportedMinutes != null) throw Invalid();
            InternalTeacherNote = Check.NotNullOrWhiteSpace(teacherNote, nameof(teacherNote), 2000); ActorUserId = activity.OwnerUserId;
            ReplyToStudentRevisionId = currentStudent.Id;
        } else {
            if (activity.FirstProvidedAtUtc == null || teacherNote != null || replyToStudentId != null || SharedText != null && !confirmSharing ||
                action != EducationTeachingActivityAction.Completed && reportedMinutes != null) throw Invalid();
            if (action != EducationTeachingActivityAction.Difficulty && (now < activity.StartsAtUtc || difficulty != null)) throw Invalid();
            if (action == EducationTeachingActivityAction.Started && currentStudent?.Action == EducationTeachingActivityAction.Completed && SharedText == null) throw Invalid();
            if (action == EducationTeachingActivityAction.Difficulty && (!difficulty.HasValue || !Enum.IsDefined(difficulty.Value) || SharedText == null)) throw Invalid();
            if (action == EducationTeachingActivityAction.Completed) {
                // Formal completion is derived from its bound submission, never a student self-report.
                if (activity.StepKind is EducationTeachingPlanStepKind.Diagnostic or EducationTeachingPlanStepKind.Reassessment ||
                    currentStudent?.Action != EducationTeachingActivityAction.Started || !confirmActivityOnly || reportedMinutes is < 1 or > 1440) throw Invalid();
            }
            ActorUserId = activity.StudentUserId; StudentRevisionNumber = checked(activity.StudentRevisionNumber + 1);
        }
        TenantId = activity.TenantId; ActivityId = activity.Id; StudentUserId = activity.StudentUserId; OwnerUserId = activity.OwnerUserId; StepKind = activity.StepKind;
        RevisionNumber = checked(activity.RevisionNumber + 1); BasedOnRevisionId = activity.CurrentRevisionId;
        Action = action; Difficulty = difficulty; ReportedMinutes = reportedMinutes; RecordedAtUtc = EducationRules.RequireUtc(now);
        ExpectedConcurrencyStamp = activity.ConcurrencyStamp; RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint);
    }
    private static BusinessException Invalid() => new(EducationTeachingPlanErrors.Invalid);
}
