using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using KnowPulse.QuestionBank;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace KnowPulse.Education;

public interface IEducationQuestionPracticeAppService : IApplicationService
{
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate)] Task<EducationPracticeOperationDto> StartMyPracticeAsync(Guid recipientId, StartEducationPracticeDto input);
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate)] Task<EducationPracticeOperationDto> StartMyPracticeForSchoolAsync(Guid schoolId, Guid recipientId, StartEducationPracticeDto input);
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate)] Task<EducationPracticeDto> GetMyPracticeAsync(Guid id);
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate)] Task<EducationPracticeDto> GetMyPracticeForSchoolAsync(Guid practiceId, Guid schoolId);
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate)] Task<EducationPracticeOperationDto?> GetMyPracticeStartOperationAsync(Guid schoolId, Guid recipientId, Guid operationId);
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate)] Task<PagedResultDto<EducationPracticeListDto>> GetMyPracticeListAsync(GetEducationPracticeListDto input);
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate)] Task<EducationPracticePreparationDto> PreviewMyPracticeAsync(Guid id, PreviewEducationPracticeDto input);
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate)] Task<EducationPracticeOperationDto> SubmitMyPracticeAsync(Guid id, SubmitEducationPracticeDto input);
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate)] Task<EducationPracticeOperationDto?> GetMyPracticeOperationAsync(Guid id, Guid operationId);
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate)] Task<EducationPracticeImageDto> UploadMyPracticeImageAsync(Guid id, Guid operationId, IRemoteStreamContent file);
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate)] Task<EducationPracticeImageDto?> GetMyPracticeImageOperationAsync(Guid id, Guid operationId);
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate)] Task<IRemoteStreamContent> DownloadMyPracticeImageAsync(Guid id, Guid attachmentId);
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage)] Task<PagedResultDto<EducationPracticeListDto>> GetPracticeSubmissionsAsync(Guid submissionId, Guid? questionId, PagedResultRequestDto input);
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage)] Task<EducationPracticeDto> GetPracticeSubmissionAsync(Guid id);
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.LearningManage)] Task<IRemoteStreamContent> DownloadPracticeImageAsync(Guid id, Guid attachmentId);
}
public class StartEducationPracticeDto
{
    public Guid OperationId { get; set; }
    public Guid PaperQuestionId { get; set; }
    public Guid ExpectedResultReleaseId { get; set; }
}
public class GetEducationPracticeListDto : PagedResultRequestDto
{
    public Guid? SchoolId { get; set; }
    public Guid? RecipientId { get; set; }
    public Guid? PaperQuestionId { get; set; }
}
public class PreviewEducationPracticeDto
{
    [Required] public EducationAssessmentResponseValue Response { get; set; } = new();
}
public class SubmitEducationPracticeDto : PreviewEducationPracticeDto
{
    public Guid OperationId { get; set; }
    [Required, StringLength(64, MinimumLength = 64)] public string PreparationFingerprint { get; set; } = "";
    public bool ConfirmSharingWithTeacher { get; set; }
    public bool ConfirmPracticeOnly { get; set; }
}
public class EducationPracticeOperationDto
{
    public Guid PracticeId { get; set; }
    public Guid OperationId { get; set; }
    public bool IsSubmission { get; set; }
    public string RequestFingerprint { get; set; } = "";
    public DateTime RecordedAtUtc { get; set; }
}
public class EducationPracticeListDto : EntityDto<Guid>
{
    public Guid SchoolId { get; set; }
    public Guid RecipientId { get; set; }
    public Guid PaperQuestionId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
}
public class EducationPracticeDto : EducationPracticeListDto
{
    public bool SourceAvailable { get; set; }
    public Guid? SubmissionOperationId { get; set; }
    public EducationStudentAssessmentQuestionDto? Question { get; set; }
    public EducationAssessmentResponseValue? SubmittedResponse { get; set; }
    public EducationPracticeFeedbackDto? Feedback { get; set; }
    public List<EducationPracticeImageDto> OwnImages { get; set; } = [];
}
public class EducationPracticeFeedbackDto
{
    public List<string> CorrectAnswers { get; set; } = [];
    public string? Analysis { get; set; }
    public QuestionScoringDefinition? ScoringDefinition { get; set; }
    public EducationPracticeEvaluation Evaluation { get; set; } = new(0, null, []);
}
public class EducationPracticePreparationDto
{
    public string Fingerprint { get; set; } = "";
    public DateTime ServerNowUtc { get; set; }
}
public class EducationPracticeImageDto : EntityDto<Guid>
{
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
    public string ContentHash { get; set; } = "";
    public bool IsUploaded { get; set; }
}
