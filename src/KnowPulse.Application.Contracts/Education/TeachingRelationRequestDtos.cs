using System;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Auditing;

namespace KnowPulse.Education;

public class SubmitTeachingRelationRequestDto
{
    public Guid OperationId { get; set; }
    public Guid? OriginalAssignmentId { get; set; }
    [Required, StringLength(2000), DisableAuditing] public string Text { get; set; } = "";
    public bool ConfirmSharingWithSchool { get; set; }
}
public class WithdrawTeachingRelationRequestDto
{
    public Guid OperationId { get; set; }
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = "";
}
public class ResolveTeachingRelationRequestDto : WithdrawTeachingRelationRequestDto
{
    public long SchoolConfigurationRevision { get; set; }
    public TeachingRelationResolution Resolution { get; set; }
    [StringLength(40)] public string? OriginalAssignmentStamp { get; set; }
    public Guid? AcademicTermId { get; set; }
    public Guid? SchoolClassId { get; set; }
    public Guid? EducationSubjectId { get; set; }
    public DateTime? EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    [Required, StringLength(2000), DisableAuditing] public string Text { get; set; } = "";
    public bool ConfirmReviewedAccountAndChange { get; set; }
}
public class MyTeachingRelationDto : EntityDto<Guid>
{
    public string AcademicTermName { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
}
public class TeachingRelationRequestDto : EntityDto<Guid>
{
    public Guid SchoolId { get; set; }
    public string SchoolName { get; set; } = "";
    public string SchoolCode { get; set; } = "";
    public Guid TeacherUserId { get; set; }
    public string TeacherDisplayName { get; set; } = "";
    public Guid? OriginalAssignmentId { get; set; }
    public MyTeachingRelationDto? Original { get; set; }
    public DateTime? OriginalEndsAtRequestUtc { get; set; }
    public string? OriginalAssignmentStamp { get; set; }
    public bool OriginalChanged { get; set; }
    public string Text { get; set; } = "";
    public DateTime RecordedAtUtc { get; set; }
    public TeachingRelationRequestStatus Status { get; set; }
    public TeachingRelationResolution? Resolution { get; set; }
    public string? ResolutionText { get; set; }
    public Guid? ResultingAssignmentId { get; set; }
    public MyTeachingRelationDto? Resulting { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public Guid? CompletedByUserId { get; set; }
    public Guid? CompletionOperationId { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public long SchoolConfigurationRevision { get; set; }
    public bool CanWithdraw { get; set; }
    public string ConcurrencyStamp { get; set; } = "";
}
