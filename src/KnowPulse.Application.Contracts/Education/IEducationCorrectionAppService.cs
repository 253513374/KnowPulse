using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace KnowPulse.Education;

public interface IEducationCorrectionAppService : IApplicationService
{
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage)] Task<EducationCorrectionTargetDto> GetCorrectionTargetAsync(Guid questionGradeId);
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage)] Task<EducationCorrectionRevisionDto> SaveCorrectionDraftAsync(Guid taskId, SaveEducationCorrectionDraftDto input);
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage)] Task<EducationCorrectionTaskDto> GetCorrectionAsync(Guid taskId);
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage)] Task<PagedResultDto<EducationCorrectionTaskDto>> GetCorrectionListAsync(PagedResultRequestDto input);
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage)] Task<EducationCorrectionPublishPreviewDto> PreviewCorrectionPublishAsync(Guid taskId);
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage)] Task<EducationCorrectionRevisionDto> PublishCorrectionAsync(Guid taskId, PublishEducationCorrectionDto input);
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage)] Task<EducationCorrectionRevisionDto> WithdrawCorrectionAsync(Guid taskId, WithdrawEducationCorrectionDto input);
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage)] Task<EducationCorrectionRevisionDto?> GetCorrectionOperationAsync(Guid taskId, Guid operationId);
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage)] Task<EducationCorrectionProgressDto> GetCorrectionProgressAsync(Guid taskId, Guid? publicationId);
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage)] Task<PagedResultDto<EducationCorrectionHistoryDto>> GetCorrectionSubmissionsAsync(Guid deliveryId, PagedResultRequestDto input);
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage)] Task<EducationCorrectionReviewDto> PublishCorrectionReviewAsync(Guid deliveryId, PublishEducationCorrectionReviewDto input);
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage)] Task<EducationCorrectionReviewDto?> GetCorrectionReviewOperationAsync(Guid deliveryId, Guid operationId);
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage)] Task<IRemoteStreamContent> DownloadCorrectionImageAsync(Guid submissionId, Guid attachmentId);
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate)] Task<PagedResultDto<EducationMyCorrectionDto>> GetMyCorrectionListAsync(PagedResultRequestDto input, Guid? schoolId = null);
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate)] Task<EducationMyCorrectionDto> GetMyCorrectionAsync(Guid deliveryId);
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate)] Task<EducationMyCorrectionDto> GetMyCorrectionForSchoolAsync(Guid deliveryId, Guid schoolId);
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate)] Task<EducationCorrectionSubmissionPreviewDto> PreviewMyCorrectionAsync(Guid deliveryId, PreviewEducationCorrectionDto input);
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate)] Task<EducationCorrectionSubmissionDto> SubmitMyCorrectionAsync(Guid deliveryId, SubmitEducationCorrectionDto input);
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate)] Task<EducationCorrectionSubmissionDto?> GetMyCorrectionOperationAsync(Guid deliveryId, Guid operationId);
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate)] Task<PagedResultDto<EducationCorrectionHistoryDto>> GetMyCorrectionHistoryAsync(Guid deliveryId, Guid? expectedSubmissionId, PagedResultRequestDto input);
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate)] Task<EducationCorrectionImageDto> UploadMyCorrectionImageAsync(Guid deliveryId, Guid operationId, IRemoteStreamContent file);
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate)] Task<EducationCorrectionImageDto?> GetMyCorrectionImageOperationAsync(Guid deliveryId, Guid operationId);
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate)] Task<IRemoteStreamContent> DownloadMyCorrectionImageAsync(Guid deliveryId, Guid attachmentId);
}
public class SaveEducationCorrectionDraftDto
{
    public Guid OperationId { get; set; }
    public Guid QuestionGradeId { get; set; }
    public Guid? ExpectedRevisionId { get; set; }
    [StringLength(40)] public string? ConcurrencyStamp { get; set; }
    [Required] public EducationCorrectionInstructions Content { get; set; } = new();
}
public class PublishEducationCorrectionDto
{
    public Guid OperationId { get; set; }
    public Guid ExpectedRevisionId { get; set; }
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = "";
    [Required, StringLength(64)] public string PreviewFingerprint { get; set; } = "";
    [StringLength(2000)] public string? PublicReason { get; set; }
    public bool ConfirmRecipientSourceAndSafeText { get; set; }
}
public class WithdrawEducationCorrectionDto
{
    public Guid OperationId { get; set; }
    public Guid ExpectedRevisionId { get; set; }
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = "";
    [Required, StringLength(2000)] public string PublicReason { get; set; } = "";
    public bool ConfirmStudentVisibleWithdrawal { get; set; }
}
public class EducationCorrectionTargetDto
{
    public Guid SourceTaskId { get; set; }
    public Guid QuestionGradeId { get; set; }
    public Guid GradeRevisionId { get; set; }
    public Guid? ResultReleaseId { get; set; }
    public bool FullReviewAvailable { get; set; }
    public string StudentName { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public string QuestionTitle { get; set; } = "";
    public string QuestionStem { get; set; } = "";
    public decimal MaxScore { get; set; }
    public decimal ObtainedScore { get; set; }
    public DateTime AvailableFromUtc { get; set; }
    public DateTime AvailableToUtc { get; set; }
}
public class EducationCorrectionRevisionDto : EntityDto<Guid>
{
    public Guid TaskId { get; set; }
    public Guid? BasedOnRevisionId { get; set; }
    public int RevisionNumber { get; set; }
    public EducationCorrectionRevisionKind Kind { get; set; }
    public EducationCorrectionInstructions? Content { get; set; }
    public Guid GradeRevisionId { get; set; }
    public Guid ResultReleaseId { get; set; }
    public string? PublicReason { get; set; }
    public string StudentName { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public string RequestFingerprint { get; set; } = "";
    public DateTime RecordedAtUtc { get; set; }
}
public class EducationCorrectionTaskDto : EntityDto<Guid>
{
    public Guid QuestionGradeId { get; set; }
    public string ConcurrencyStamp { get; set; } = "";
    public Guid? PublishedRevisionId { get; set; }
    public Guid? LastPublishedRevisionId { get; set; }
    public EducationCorrectionRevisionDto Current { get; set; } = new();
}
public class EducationCorrectionPublishPreviewDto
{
    public Guid DraftRevisionId { get; set; }
    public string ConcurrencyStamp { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public EducationCorrectionTargetDto Target { get; set; } = new();
}
public class PreviewEducationCorrectionDto
{
    public Guid? ExpectedSubmissionId { get; set; }
    [Required] public EducationCorrectionContent Content { get; set; } = new();
}
public class SubmitEducationCorrectionDto : PreviewEducationCorrectionDto
{
    public Guid OperationId { get; set; }
    [Required, StringLength(64)] public string PreviewFingerprint { get; set; } = "";
    public bool ConfirmSharingWithTeacher { get; set; }
}
public class EducationCorrectionSubmissionPreviewDto
{
    public Guid DeliveryId { get; set; }
    public Guid? ExpectedSubmissionId { get; set; }
    public string Fingerprint { get; set; } = "";
    public EducationCorrectionContent Content { get; set; } = new();
    public List<EducationCorrectionImageDto> Images { get; set; } = [];
    public DateTime DueAtUtc { get; set; }
    public DateTime ServerNowUtc { get; set; }
}
public class EducationCorrectionSubmissionDto : EntityDto<Guid>
{
    public Guid DeliveryId { get; set; }
    public int SubmissionNumber { get; set; }
    public Guid? BasedOnSubmissionId { get; set; }
    public EducationCorrectionContent Content { get; set; } = new();
    public DateTime SubmittedAtUtc { get; set; }
    public string RequestFingerprint { get; set; } = "";
}
public class PublishEducationCorrectionReviewDto
{
    public Guid OperationId { get; set; }
    public Guid ExpectedSubmissionId { get; set; }
    public Guid? ExpectedReviewId { get; set; }
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = "";
    public EducationCorrectionReviewDecision Decision { get; set; }
    [Required, StringLength(2000)] public string StudentFeedback { get; set; } = "";
    public bool ConfirmStudentVisibleFeedback { get; set; }
}
public class EducationCorrectionReviewDto : EntityDto<Guid>
{
    public Guid DeliveryId { get; set; }
    public Guid SubmissionId { get; set; }
    public Guid? BasedOnReviewId { get; set; }
    public int ReviewNumber { get; set; }
    public EducationCorrectionReviewDecision Decision { get; set; }
    public string StudentFeedback { get; set; } = "";
    public DateTime PublishedAtUtc { get; set; }
    public string RequestFingerprint { get; set; } = "";
}
public class EducationCorrectionImageDto : EntityDto<Guid>
{
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
    public bool IsUploaded { get; set; }
    public string ContentHash { get; set; } = "";
}
public class EducationMyCorrectionDto : EntityDto<Guid>
{
    public Guid SchoolId { get; set; }
    public EducationCorrectionWorkState? WorkState { get; set; }
    public Guid TaskId { get; set; }
    public Guid PublicationId { get; set; }
    public int VersionNumber { get; set; }
    public Guid AssessmentRecipientId { get; set; }
    public string? Title { get; set; }
    public string TeacherName { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public string? Instructions { get; set; }
    public string? PublicReason { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime DueAtUtc { get; set; }
    public DateTime ServerNowUtc { get; set; }
    public int EstimatedMinutes { get; set; }
    public EducationCorrectionAvailability Availability { get; set; }
    public Guid? CurrentSubmissionId { get; set; }
    public EducationCorrectionSubmissionDto? CurrentSubmission { get; set; }
    public EducationCorrectionReviewDto? CurrentReview { get; set; }
    public EducationAssessmentStudentQuestionResultDto? OriginalQuestion { get; set; }
}
public class EducationCorrectionProgressDto
{
    public Guid? ReviewHeadId { get; set; }
    public Guid TaskId { get; set; }
    public Guid PublicationId { get; set; }
    public string StudentName { get; set; } = "";
    public string ConcurrencyStamp { get; set; } = "";
    public bool IsCurrent { get; set; }
    public EducationMyCorrectionDto Delivery { get; set; } = new();
    public List<EducationActivityVersionDto> Publications { get; set; } = [];
}
public class EducationCorrectionHistoryDto
{
    public EducationCorrectionSubmissionDto Submission { get; set; } = new();
    public List<EducationCorrectionReviewDto> Reviews { get; set; } = [];
}
