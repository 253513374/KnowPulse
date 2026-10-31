using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.Education;

public class EducationFeedbackTeachingDto : EntityDto<Guid>
{
    public string SchoolName { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string SubjectName { get; set; } = string.Empty;
}

public class EducationFeedbackTargetDto
{
    public Guid StudentEnrollmentId { get; set; }
    public Guid SchoolMembershipId { get; set; }
    public Guid StudentUserId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
}

public class EducationFeedbackRecipientInput
{
    public Guid TeachingAssignmentId { get; set; }
    public Guid StudentEnrollmentId { get; set; }
    public Guid SchoolMembershipId { get; set; }
}

public class EducationFeedbackTargetContextDto
{
    public Guid SchoolId { get; set; }
    public EducationFeedbackTeachingDto Teaching { get; set; } = new();
    public EducationFeedbackTargetDto Student { get; set; } = new();
}

public class CreateEducationTeacherObservationDto : EducationFeedbackRecipientInput
{
    public Guid OperationId { get; set; }
    [Required, StringLength(4000)] public string Text { get; set; } = string.Empty;
    public DateTime ObservedAtUtc { get; set; }
}

public class EducationTeacherObservationDto : EntityDto<Guid>
{
    public Guid SchoolId { get; set; }
    public Guid TeachingAssignmentId { get; set; }
    public Guid StudentEnrollmentId { get; set; }
    public Guid SchoolMembershipId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime ObservedAtUtc { get; set; }
    public string RequestFingerprint { get; set; } = string.Empty;
}

public class EducationFeedbackPhraseScopeDto
{
    public Guid SchoolId { get; set; }
    public Guid TeachingAssignmentId { get; set; }
}

public class SaveEducationFeedbackPhraseDto : EducationFeedbackPhraseScopeDto
{
    public Guid OperationId { get; set; }
    [StringLength(40)] public string? ConcurrencyStamp { get; set; }
    public Guid? ExpectedRevisionId { get; set; }
    [Required, StringLength(4000)] public string Text { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
}

public class EducationFeedbackPhraseRevisionDto : EntityDto<Guid>
{
    public Guid PhraseId { get; set; }
    public int RevisionNumber { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}

public class EducationFeedbackPhraseDto : EntityDto<Guid>
{
    public string ConcurrencyStamp { get; set; } = string.Empty;
    public EducationFeedbackPhraseRevisionDto CurrentRevision { get; set; } = new();
}

public class SaveEducationFeedbackDraftDto : EducationFeedbackRecipientInput
{
    public Guid OperationId { get; set; }
    [StringLength(40)] public string? ConcurrencyStamp { get; set; }
    public Guid? ExpectedRevisionId { get; set; }
    [Required] public EducationFeedbackContent Content { get; set; } = new();
}

public class EducationFeedbackRevisionDto : EntityDto<Guid>
{
    public Guid FeedbackId { get; set; }
    public int RevisionNumber { get; set; }
    public Guid? BasedOnRevisionId { get; set; }
    public Guid? DraftRevisionId { get; set; }
    public Guid? PreviousPublishedRevisionId { get; set; }
    public EducationFeedbackRevisionKind Kind { get; set; }
    public EducationFeedbackContent? Content { get; set; }
    public string? Reason { get; set; }
    public string RequestFingerprint { get; set; } = string.Empty;
    public DateTime RecordedAtUtc { get; set; }
}

public class EducationFeedbackDto : EntityDto<Guid>
{
    public Guid TeachingAssignmentId { get; set; }
    public Guid StudentEnrollmentId { get; set; }
    public Guid SchoolMembershipId { get; set; }
    public Guid StudentUserId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string SubjectName { get; set; } = string.Empty;
    public string ConcurrencyStamp { get; set; } = string.Empty;
    public EducationFeedbackRevisionDto CurrentRevision { get; set; } = new();
    public EducationFeedbackRevisionDto? Draft { get; set; }
    public EducationFeedbackRevisionDto? Published { get; set; }
    public EducationFeedbackVisibility Visibility { get; set; }
    public DateTime? PublishedVersionOpenedAtUtc { get; set; }
}

public class EducationFeedbackAssessmentGateDto
{
    public Guid TaskId { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid? ResultReleaseId { get; set; }
    public bool IsReviewOpen { get; set; }
}

public class EducationFeedbackPublishPreviewDto
{
    public EducationFeedbackDto Feedback { get; set; } = new();
    public string PreviewFingerprint { get; set; } = string.Empty;
    public List<EducationFeedbackAssessmentGateDto> Assessments { get; set; } = [];
}

public class PublishEducationFeedbackDto
{
    public Guid OperationId { get; set; }
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = string.Empty;
    public Guid ExpectedDraftRevisionId { get; set; }
    [Required, StringLength(64, MinimumLength = 64)] public string PreviewFingerprint { get; set; } = string.Empty;
    public bool ConfirmRecipientAndContent { get; set; }
    [StringLength(2048)] public string? Reason { get; set; }
}

public class WithdrawEducationFeedbackDto
{
    public Guid OperationId { get; set; }
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = string.Empty;
    public Guid ExpectedPublishedRevisionId { get; set; }
    [Required, StringLength(2048)] public string Reason { get; set; } = string.Empty;
}

/// <summary>Student metadata has no draft, internal observation, reason, or private source references.</summary>
public class EducationStudentFeedbackDto : EntityDto<Guid>
{
    public Guid SchoolId { get; set; }
    public string SchoolName { get; set; } = string.Empty;
    public Guid VersionId { get; set; }
    public int VersionNumber { get; set; }
    public string TeacherName { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string SubjectName { get; set; } = string.Empty;
    public EducationFeedbackVisibility Visibility { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public DateTime? OpenedAtUtc { get; set; }
    public EducationFeedbackKind? Kind { get; set; }
    public string? Context { get; set; }
    public DateTime? OccurredAtUtc { get; set; }
    public DateTime? PeriodEndsAtUtc { get; set; }
    public string? Text { get; set; }
    public List<Guid> AssessmentRecipientIds { get; set; } = [];
}

public class EducationFeedbackNoticeDto : EntityDto<Guid>
{
    public Guid FeedbackId { get; set; }
    public EducationFeedbackRevisionKind Kind { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public bool IsCurrentVersion { get; set; }
    public bool IsRead { get; set; }
    public DateTime? MessageReadAtUtc { get; set; }
    public bool CanOpen { get; set; }
}
