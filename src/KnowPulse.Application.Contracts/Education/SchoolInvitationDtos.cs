using System;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Auditing;

namespace KnowPulse.Education;

public class CreateSchoolInvitationDto
{
    public Guid OperationId { get; set; }
    public Guid TargetUserId { get; set; }
    public SchoolMemberRole Role { get; set; }
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public bool ConfirmVerifiedAccount { get; set; }
}
public class RespondSchoolInvitationDto
{
    public Guid OperationId { get; set; }
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = "";
    public SchoolInvitationResponse Response { get; set; }
    public SchoolInvitationDeclineReason? DeclineReason { get; set; }
    public bool ConfirmIdentityAndSchool { get; set; }
}
public class RevokeSchoolInvitationDto
{
    public Guid OperationId { get; set; }
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = "";
    [Required, StringLength(1000), DisableAuditing] public string Reason { get; set; } = "";
}
public class GetMySchoolInvitationsInput : PagedResultRequestDto
{
    public bool IncludeInactive { get; set; }
}
public class MySchoolInvitationDto : EntityDto<Guid>
{
    public Guid SchoolId { get; set; }
    public string SchoolName { get; set; } = "";
    public string SchoolCode { get; set; } = "";
    public Guid TargetUserId { get; set; }
    public SchoolMemberRole Role { get; set; }
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public SchoolInvitationStatus Status { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public Guid? MembershipId { get; set; }
    public Guid? ResponseOperationId { get; set; }
    public SchoolInvitationDeclineReason? DeclineReason { get; set; }
    public bool CanRespond { get; set; }
    public string ConcurrencyStamp { get; set; } = "";
}
public class SchoolInvitationDto : MySchoolInvitationDto
{
    public bool CanRevoke { get; set; }
    public Guid InvitedByUserId { get; set; }
    public string TargetDisplayName { get; set; } = "";
    public Guid? RevokedByUserId { get; set; }
    public Guid? RevocationOperationId { get; set; }
    public string? RevocationReason { get; set; }
}
