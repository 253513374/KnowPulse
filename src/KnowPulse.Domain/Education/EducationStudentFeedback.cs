using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

public sealed record EducationFeedbackRecipient(TeachingAssignment Teaching, StudentEnrollment Enrollment, SchoolMembership Membership,
    string TeacherName, string StudentName, string AccountName, string ClassName, string SubjectName)
{
    public void Validate()
    {
        if (Teaching.TenantId != Enrollment.TenantId || Teaching.TenantId != Membership.TenantId ||
            Teaching.SchoolId != Enrollment.SchoolId || Teaching.SchoolId != Membership.SchoolId ||
            Teaching.SchoolClassId != Enrollment.SchoolClassId || Enrollment.StudentUserId != Membership.UserId || Membership.Role != SchoolMemberRole.Student)
            throw new BusinessException(EducationFeedbackErrorCodes.Invalid);
    }
}

public class EducationTeacherObservation : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid TeachingAssignmentId { get; private set; }
    public Guid StudentEnrollmentId { get; private set; }
    public Guid SchoolMembershipId { get; private set; }
    public Guid SchoolId { get; private set; }
    public Guid AcademicTermId { get; private set; }
    public Guid SchoolClassId { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public DateTime ObservedAtUtc { get; private set; }
    public string RequestFingerprint { get; private set; } = string.Empty;
    protected EducationTeacherObservation() { }
    public EducationTeacherObservation(Guid id, EducationFeedbackRecipient target, string text, DateTime observedAt, string fingerprint) : base(EducationRules.RequireId(id))
    {
        target.Validate(); var teaching = target.Teaching; var enrollment = target.Enrollment;
        TenantId = teaching.TenantId; OwnerUserId = teaching.TeacherUserId; StudentUserId = enrollment.StudentUserId;
        TeachingAssignmentId = teaching.Id; StudentEnrollmentId = enrollment.Id; SchoolMembershipId = target.Membership.Id;
        SchoolId = teaching.SchoolId; AcademicTermId = teaching.AcademicTermId; SchoolClassId = teaching.SchoolClassId;
        Text = Check.NotNullOrWhiteSpace(text, nameof(text), 4000); ObservedAtUtc = EducationRules.RequireUtc(observedAt);
        RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint);
    }
}

public class EducationStudentFeedback : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid TeachingAssignmentId { get; private set; }
    public Guid StudentEnrollmentId { get; private set; }
    public Guid SchoolMembershipId { get; private set; }
    public Guid SchoolId { get; private set; }
    public Guid AcademicTermId { get; private set; }
    public Guid SchoolClassId { get; private set; }
    public Guid EducationSubjectId { get; private set; }
    public string TeacherName { get; private set; } = string.Empty;
    public string StudentName { get; private set; } = string.Empty;
    public string AccountName { get; private set; } = string.Empty;
    public string ClassName { get; private set; } = string.Empty;
    public string SubjectName { get; private set; } = string.Empty;
    public int RevisionNumber { get; private set; }
    public Guid? CurrentRevisionId { get; private set; }
    public Guid? CurrentDraftRevisionId { get; private set; }
    public Guid? PublishedRevisionId { get; private set; }
    public Guid? LastPublishedRevisionId { get; private set; }
    protected EducationStudentFeedback() { }
    public EducationStudentFeedback(Guid id, EducationFeedbackRecipient target) : base(EducationRules.RequireId(id))
    {
        target.Validate(); var teaching = target.Teaching; var enrollment = target.Enrollment;
        TenantId = teaching.TenantId; OwnerUserId = teaching.TeacherUserId; StudentUserId = enrollment.StudentUserId;
        TeachingAssignmentId = teaching.Id; StudentEnrollmentId = enrollment.Id; SchoolMembershipId = target.Membership.Id;
        SchoolId = teaching.SchoolId; AcademicTermId = teaching.AcademicTermId; SchoolClassId = teaching.SchoolClassId; EducationSubjectId = teaching.EducationSubjectId;
        TeacherName = Check.NotNullOrWhiteSpace(target.TeacherName, nameof(target.TeacherName), 256);
        StudentName = Check.NotNullOrWhiteSpace(target.StudentName, nameof(target.StudentName), 256);
        AccountName = Check.NotNullOrWhiteSpace(target.AccountName, nameof(target.AccountName), 256);
        ClassName = Check.NotNullOrWhiteSpace(target.ClassName, nameof(target.ClassName), 128);
        SubjectName = Check.NotNullOrWhiteSpace(target.SubjectName, nameof(target.SubjectName), 128);
    }

    public void Advance(EducationStudentFeedbackRevision revision)
    {
        if (revision.FeedbackId != Id || revision.TenantId != TenantId || revision.StudentUserId != StudentUserId || revision.OwnerUserId != OwnerUserId ||
            revision.RevisionNumber != RevisionNumber + 1 || revision.BasedOnRevisionId != CurrentRevisionId || revision.ExpectedConcurrencyStamp != ConcurrencyStamp)
            throw new BusinessException(EducationFeedbackErrorCodes.Invalid);
        RevisionNumber = revision.RevisionNumber; CurrentRevisionId = revision.Id;
        if (revision.Kind == EducationFeedbackRevisionKind.Draft) CurrentDraftRevisionId = revision.Id;
        else if (revision.Kind == EducationFeedbackRevisionKind.Published) { PublishedRevisionId = revision.Id; LastPublishedRevisionId = revision.Id; }
        else PublishedRevisionId = null;
    }
}

public class EducationStudentFeedbackRevision : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid FeedbackId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public int RevisionNumber { get; private set; }
    public Guid? BasedOnRevisionId { get; private set; }
    public Guid? DraftRevisionId { get; private set; }
    public Guid? PreviousPublishedRevisionId { get; private set; }
    public EducationFeedbackRevisionKind Kind { get; private set; }
    public string? ContentJson { get; private set; }
    public EducationFeedbackContent? Content => ContentJson == null ? null : EducationFeedbackContent.Parse(ContentJson);
    public string? Reason { get; private set; }
    public string RequestFingerprint { get; private set; } = string.Empty;
    public string ExpectedConcurrencyStamp { get; private set; } = string.Empty;
    public DateTime RecordedAtUtc { get; private set; }
    protected EducationStudentFeedbackRevision() { }
    public EducationStudentFeedbackRevision(Guid id, EducationStudentFeedback feedback, EducationFeedbackRevisionKind kind,
        EducationFeedbackContent? content, Guid? draftRevisionId, string? reason, string expectedStamp, string fingerprint, DateTime now) : base(EducationRules.RequireId(id))
    {
        if (!Enum.IsDefined(kind) || feedback.ConcurrencyStamp != expectedStamp ||
            (kind == EducationFeedbackRevisionKind.Withdrawn ? content != null || !feedback.PublishedRevisionId.HasValue : content == null) ||
            (kind == EducationFeedbackRevisionKind.Published ? !draftRevisionId.HasValue || draftRevisionId != feedback.CurrentDraftRevisionId ||
                feedback.CurrentRevisionId != draftRevisionId : draftRevisionId.HasValue) ||
            (kind == EducationFeedbackRevisionKind.Withdrawn || kind == EducationFeedbackRevisionKind.Published && feedback.LastPublishedRevisionId.HasValue) && string.IsNullOrWhiteSpace(reason))
            throw new BusinessException(EducationFeedbackErrorCodes.Invalid);
        TenantId = feedback.TenantId; FeedbackId = feedback.Id; StudentUserId = feedback.StudentUserId; OwnerUserId = feedback.OwnerUserId;
        RevisionNumber = checked(feedback.RevisionNumber + 1); BasedOnRevisionId = feedback.CurrentRevisionId; DraftRevisionId = draftRevisionId;
        PreviousPublishedRevisionId = kind == EducationFeedbackRevisionKind.Draft ? null : feedback.LastPublishedRevisionId;
        Kind = kind; ContentJson = content?.ToCanonicalJson(); Reason = Check.Length(reason, nameof(reason), 2048);
        ExpectedConcurrencyStamp = Check.NotNullOrWhiteSpace(expectedStamp, nameof(expectedStamp), 40);
        RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint); RecordedAtUtc = EducationRules.RequireUtc(now);
    }
}

public class EducationFeedbackAssessment : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid FeedbackId { get; private set; }
    public Guid FeedbackRevisionId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid TaskRevisionId { get; private set; }
    public Guid RecipientId { get; private set; }
    public Guid StudentUserId { get; private set; }
    protected EducationFeedbackAssessment() { }
    public EducationFeedbackAssessment(Guid id, EducationStudentFeedbackRevision feedback, EducationAssessmentRecipient recipient) : base(EducationRules.RequireId(id))
    {
        if (feedback.TenantId != recipient.TenantId || feedback.StudentUserId != recipient.StudentUserId || feedback.Content == null ||
            !feedback.Content.AssessmentTaskIds.Contains(recipient.TaskId)) throw new BusinessException(EducationFeedbackErrorCodes.Invalid);
        TenantId = feedback.TenantId; FeedbackId = feedback.FeedbackId; FeedbackRevisionId = feedback.Id; StudentUserId = recipient.StudentUserId;
        TaskId = recipient.TaskId; TaskRevisionId = recipient.TaskRevisionId; RecipientId = recipient.Id;
    }
}

public class EducationFeedbackRead : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid FeedbackId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public DateTime OpenedAtUtc { get; private set; }
    protected EducationFeedbackRead() { }
    public EducationFeedbackRead(EducationStudentFeedbackRevision revision, DateTime now) : base(revision.Id)
    {
        if (revision.Kind == EducationFeedbackRevisionKind.Draft || now < revision.RecordedAtUtc) throw new BusinessException(EducationFeedbackErrorCodes.Invalid);
        TenantId = revision.TenantId; FeedbackId = revision.FeedbackId; StudentUserId = revision.StudentUserId; OpenedAtUtc = EducationRules.RequireUtc(now);
    }
}

public class EducationFeedbackNotice : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid FeedbackId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public EducationFeedbackRevisionKind Kind { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public DateTime? MessageReadAtUtc { get; private set; }
    protected EducationFeedbackNotice() { }
    public EducationFeedbackNotice(EducationStudentFeedbackRevision revision) : base(revision.Id)
    {
        if (revision.Kind == EducationFeedbackRevisionKind.Draft) throw new BusinessException(EducationFeedbackErrorCodes.Invalid);
        TenantId = revision.TenantId; FeedbackId = revision.FeedbackId; StudentUserId = revision.StudentUserId; Kind = revision.Kind; RecordedAtUtc = revision.RecordedAtUtc;
    }
    public void MarkMessageRead(DateTime now)
    {
        EducationRules.RequireUtc(now);
        if (now < RecordedAtUtc) throw new BusinessException(EducationFeedbackErrorCodes.Invalid);
        MessageReadAtUtc ??= now;
    }
}
