using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace KnowPulse.Education;

public interface IEducationStudyTaskAppService : IApplicationService
{
    [Authorize(EducationPermissions.LearningManage)] Task<EducationStudyRevisionDto> SaveDraftAsync(Guid taskId, SaveEducationStudyTaskDto input);
    [Authorize(EducationPermissions.LearningManage)] Task<EducationStudyTaskDto> GetAsync(Guid id);
    [Authorize(EducationPermissions.LearningManage)] Task<PagedResultDto<EducationStudyTaskListDto>> GetListAsync(PagedResultRequestDto input);
    [Authorize(EducationPermissions.LearningManage)] Task<EducationStudyRevisionDto?> GetOperationAsync(Guid taskId, Guid operationId);
    [Authorize(EducationPermissions.LearningManage)] Task<EducationStudyRosterDto> PreviewRecipientsAsync(Guid taskId);
    [Authorize(EducationPermissions.LearningManage)] Task<EducationStudyRevisionDto> PublishAsync(Guid taskId, PublishEducationStudyTaskDto input);
    [Authorize(EducationPermissions.LearningManage)] Task<EducationStudyRevisionDto> WithdrawAsync(Guid taskId, WithdrawEducationStudyTaskDto input);
    [Authorize(EducationPermissions.LearningManage)] Task<EducationStudyProgressDto> GetProgressAsync(Guid taskId, Guid? publicationId, PagedResultRequestDto input);
    [Authorize(EducationPermissions.LearningManage)] Task<EducationTeacherStudyEventDto> SaveFollowUpAsync(Guid recipientId, SaveEducationStudyFollowUpDto input);
    [Authorize(EducationPermissions.LearningManage)] Task<EducationTeacherStudyEventDto?> GetFollowUpOperationAsync(Guid recipientId, Guid operationId);
    [Authorize(EducationPermissions.LearningManage), Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)] Task<PagedResultDto<EducationMaterialSourceDto>> GetMaterialSourcesAsync(Guid taskId, GetEducationMaterialSourcesDto input);
    [Authorize(EducationPermissions.LearningManage), Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)] Task<EducationMaterialSourcePreviewDto> PreviewMaterialSourceAsync(Guid taskId, EducationMaterialSourceDto input);
    [Authorize(EducationPermissions.LearningManage), Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)] Task<EducationMaterialContentDto> GetTeacherMaterialAsync(Guid taskId, Guid revisionId, Guid materialId);
    [Authorize(EducationPermissions.LearningManage), Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)] Task<IRemoteStreamContent> DownloadTeacherMaterialAsync(Guid taskId, Guid revisionId, Guid materialId, Guid attachmentId);
    [Authorize(EducationPermissions.LearningParticipate)] Task<PagedResultDto<EducationMyStudyTaskDto>> GetMyListAsync(PagedResultRequestDto input, Guid? schoolId = null);
    [Authorize(EducationPermissions.LearningParticipate)] Task<EducationMyStudyTaskDto> OpenMyAsync(Guid recipientId);
    [Authorize(EducationPermissions.LearningParticipate)] Task<EducationMyStudyTaskDto> OpenMyForSchoolAsync(Guid recipientId, Guid schoolId, Guid? expectedPublicationId = null);
    [Authorize(EducationPermissions.LearningParticipate)] Task<EducationStudentStudyEventDto> SaveMyProgressAsync(Guid recipientId, SaveEducationMyActivityDto input);
    [Authorize(EducationPermissions.LearningParticipate)] Task<EducationStudentStudyEventDto?> GetMyOperationAsync(Guid recipientId, Guid operationId);
    [Authorize(EducationPermissions.LearningParticipate)] Task<PagedResultDto<EducationStudentStudyEventDto>> GetMyHistoryAsync(Guid recipientId, Guid? expectedStudentRevisionId, PagedResultRequestDto input);
    [Authorize(EducationPermissions.LearningParticipate)] Task<EducationMaterialContentDto> GetMyMaterialAsync(Guid recipientId, Guid materialId);
    [Authorize(EducationPermissions.LearningParticipate)] Task<IRemoteStreamContent> DownloadMyMaterialAsync(Guid recipientId, Guid materialId, Guid attachmentId);
}
public class SaveEducationStudyTaskDto
{
    public Guid OperationId { get; set; }
    public Guid? ExpectedRevisionId { get; set; }
    [StringLength(40)] public string? ConcurrencyStamp { get; set; }
    [Required] public EducationCurriculumScopeDto Scope { get; set; } = new();
    [Required] public EducationStudyTaskContent Content { get; set; } = new();
}
public class PublishEducationStudyTaskDto
{
    public Guid OperationId { get; set; }
    public Guid ExpectedRevisionId { get; set; }
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = "";
    [Required, StringLength(64, MinimumLength = 64)] public string RosterFingerprint { get; set; } = "";
    [StringLength(2000)] public string? PublicReason { get; set; }
    public bool ConfirmRecipientsMaterialsAndSafeText { get; set; }
}
public class WithdrawEducationStudyTaskDto
{
    public Guid OperationId { get; set; }
    public Guid ExpectedRevisionId { get; set; }
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = "";
    [Required, StringLength(2000)] public string PublicReason { get; set; } = "";
    public bool ConfirmStudentVisibleReason { get; set; }
}
public class EducationStudyRevisionDto : EntityDto<Guid>
{
    public Guid TaskId { get; set; }
    public int RevisionNumber { get; set; }
    public EducationStudyTaskRevisionKind Kind { get; set; }
    public EducationCurriculumSnapshot Scope { get; set; } = new();
    public EducationStudyTaskContent? Content { get; set; }
    public string? PublicReason { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public string RequestFingerprint { get; set; } = "";
}
public class EducationStudyTaskDto : EntityDto<Guid>
{
    public string ConcurrencyStamp { get; set; } = "";
    public Guid? PublishedRevisionId { get; set; }
    public Guid? LastPublishedRevisionId { get; set; }
    public EducationStudyRevisionDto Current { get; set; } = new();
}
public class EducationStudyTaskListDto : EntityDto<Guid>
{
    public string Title { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public DateTime EndsAtUtc { get; set; }
    public Guid? PublishedRevisionId { get; set; }
}
public class EducationStudyRosterDto
{
    public Guid DraftRevisionId { get; set; }
    public string ConcurrencyStamp { get; set; } = "";
    public string RosterFingerprint { get; set; } = "";
    public List<EducationAssessmentRosterStudentDto> Students { get; set; } = [];
}
public class EducationStudentStudyEventDto : EntityDto<Guid>
{
    public string RequestFingerprint { get; set; } = "";
    public Guid RecipientId { get; set; }
    public int StudentRevisionNumber { get; set; }
    public EducationTeachingActivityAction Action { get; set; }
    public EducationTeachingActivityDifficulty? Difficulty { get; set; }
    public string? SharedText { get; set; }
    public int? ReportedMinutes { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}
public class EducationTeacherStudyEventDto : EntityDto<Guid>
{
    public Guid RecipientId { get; set; }
    public Guid? ReplyToStudentRevisionId { get; set; }
    public string InternalTeacherNote { get; set; } = "";
    public DateTime RecordedAtUtc { get; set; }
    public string RequestFingerprint { get; set; } = "";
}
public class SaveEducationStudyFollowUpDto
{
    public Guid OperationId { get; set; }
    public Guid? ExpectedRevisionId { get; set; }
    public Guid ExpectedStudentRevisionId { get; set; }
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = "";
    [Required, StringLength(2000)] public string InternalTeacherNote { get; set; } = "";
    public bool ConfirmPrivateHandling { get; set; }
}
public sealed record EducationStudyMaterialDisplay(Guid Id, string Title) : IEducationMaterialDisplay;
public class EducationMyStudyTaskDto : EntityDto<Guid>
{
    public Guid SchoolId { get; set; }
    public Guid TaskId { get; set; }
    public Guid PublicationId { get; set; }
    public int VersionNumber { get; set; }
    public string Title { get; set; } = "";
    public string TeacherName { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public string TextbookTitle { get; set; } = "";
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public DateTime ServerNowUtc { get; set; }
    public int EstimatedMinutes { get; set; }
    public EducationStudyTaskAvailability Availability { get; set; }
    public EducationTeachingActivityState State { get; set; }
    public bool MaterialsAvailable { get; set; }
    public string? Instructions { get; set; }
    public string? PublicReason { get; set; }
    public List<EducationStudyMaterialDisplay> Materials { get; set; } = [];
    public Guid? CurrentStudentRevisionId { get; set; }
    public EducationStudentStudyEventDto? CurrentStudent { get; set; }
    public DateTime? FirstProvidedAtUtc { get; set; }
    public DateTime? TeacherHandledAtUtc { get; set; }
}
public class EducationTeacherStudyRecipientDto : EducationMyStudyTaskDto
{
    public string StudentName { get; set; } = "";
    public Guid? CurrentRevisionId { get; set; }
    public string ConcurrencyStamp { get; set; } = "";
    public EducationTeacherStudyEventDto? CurrentFollowUp { get; set; }
}
public class EducationStudyProgressDto
{
    public Guid PublicationId { get; set; }
    public bool IsCurrent { get; set; }
    public int AssignedCount { get; set; }
    public long VisibleCount { get; set; }
    public List<EducationActivityVersionDto> Publications { get; set; } = [];
    public List<EducationTeacherStudyRecipientDto> Students { get; set; } = [];
}
