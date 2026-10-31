using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.Education;

public interface IEducationAssessmentReminderAppService : IApplicationService
{
    [Authorize(EducationPermissions.AssessmentManage)] Task<PagedResultDto<EducationAssessmentReminderPreviewDto>> GetReminderTargetsAsync(GetEducationReminderTargetsInput input);
    [Authorize(EducationPermissions.AssessmentManage)] Task<EducationAssessmentReminderPreviewDto> GetReminderPreviewAsync(Guid recipientId, Guid schoolId);
    [Authorize(EducationPermissions.AssessmentManage)] Task<EducationAssessmentReminderDto> SendReminderAsync(Guid recipientId, Guid schoolId, SendEducationAssessmentReminderDto input);
    [Authorize(EducationPermissions.AssessmentManage)] Task<EducationAssessmentReminderDto?> GetReminderOperationAsync(Guid recipientId, Guid schoolId, Guid operationId);
    [Authorize(EducationPermissions.AssessmentParticipate)] Task<PagedResultDto<EducationAssessmentReminderDto>> GetMyRemindersAsync(GetEducationMyRemindersInput input);
    [Authorize(EducationPermissions.AssessmentParticipate)] Task<PagedResultDto<EducationAssessmentReminderDto>> GetMyNoticesAsync(GetEducationMyRemindersInput input);
    [Authorize(EducationPermissions.AssessmentParticipate)] Task<EducationAssessmentReminderDto> ReadMyReminderAsync(Guid id, Guid schoolId);
}

public class GetEducationMyRemindersInput : PagedResultRequestDto
{
    public Guid SchoolId { get; set; }
}
public class GetEducationReminderTargetsInput : GetEducationMyRemindersInput
{
    public Guid TaskId { get; set; }
}
public class SendEducationAssessmentReminderDto
{
    public Guid OperationId { get; set; }
    [Required, StringLength(64, MinimumLength = 64)] public string PreviewFingerprint { get; set; } = string.Empty;
    public bool ConfirmRecipient { get; set; }
}
public class EducationAssessmentReminderPreviewDto : EntityDto<Guid>
{
    public Guid SchoolId { get; set; }
    public Guid TaskId { get; set; }
    public Guid PublicationId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string TaskTitle { get; set; } = string.Empty;
    public DateTime EndsAtUtc { get; set; }
    public DateTime ServerNowUtc { get; set; }
    public DateTime? NextAllowedAtUtc { get; set; }
    public bool HasStarted { get; set; }
    public bool CanSend { get; set; }
    public string BlockReason { get; set; } = string.Empty;
    public string PreviewFingerprint { get; set; } = string.Empty;
}
public class EducationAssessmentReminderDto : EntityDto<Guid>
{
    public EducationAssessmentReminderKind Kind { get; set; }
    public Guid? ResultReleaseId { get; set; }
    public bool CanOpen { get; set; } = true;
    public Guid SchoolId { get; set; }
    public Guid TaskId { get; set; }
    public Guid PublicationId { get; set; }
    public Guid RecipientId { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public DateTime AvailableAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public string WeChatDelivery { get; set; } = "NotConfigured";
}
