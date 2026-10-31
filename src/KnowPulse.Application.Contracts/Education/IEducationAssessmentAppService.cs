using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace KnowPulse.Education;

public interface IEducationAssessmentAppService : IApplicationService
{
    [Authorize(EducationPermissions.AssessmentManage)]
    Task<EducationAssessmentTaskDto> CreateDraftAsync(CreateEducationAssessmentDraftDto input);
    [Authorize(EducationPermissions.AssessmentManage)]
    Task<EducationAssessmentDraftOptionsDto> GetDraftOptionsAsync(GetEducationAssessmentDraftOptionsInput input);
    [Authorize(EducationPermissions.AssessmentManage)]
    Task<EducationAssessmentTaskDto> SaveDraftAsync(Guid id, SaveEducationAssessmentDraftDto input);
    [Authorize(EducationPermissions.AssessmentManage)]
    Task<EducationAssessmentTaskDto> GetAsync(Guid id);
    [Authorize(EducationPermissions.AssessmentManage)]
    Task<PagedResultDto<EducationAssessmentTaskDto>> GetMyListAsync(GetEducationAssessmentTaskListInput input);
    [Authorize(EducationPermissions.AssessmentManage)]
    Task<EducationAssessmentRosterPreviewDto> PreviewRecipientsAsync(Guid id);
    [Authorize(EducationPermissions.AssessmentManage)]
    Task<EducationAssessmentTaskRevisionDto> PublishAsync(Guid id, PublishEducationAssessmentDto input);
    [Authorize(EducationPermissions.AssessmentManage)]
    Task<EducationAssessmentTaskRevisionDto?> GetOperationAsync(Guid id, Guid operationId);
    [Authorize(EducationPermissions.AssessmentParticipate)]
    Task<PagedResultDto<EducationStudentAssessmentTaskDto>> GetMyStudentListAsync(GetEducationStudentAssessmentListInput input);
    [Authorize(EducationPermissions.AssessmentParticipate)]
    Task<EducationStudentAssessmentTaskDto> GetMyStudentTaskAsync(Guid recipientId);
    [Authorize(EducationPermissions.AssessmentParticipate)]
    Task<EducationStudentAssessmentAttemptDto> StartOrResumeAsync(Guid recipientId, StartEducationAssessmentDto input);
    [Authorize(EducationPermissions.AssessmentParticipate)]
    Task<EducationStudentAssessmentAttemptDto> GetAttemptAsync(Guid attemptId);
    [Authorize(EducationPermissions.AssessmentParticipate)]
    Task<EducationAssessmentResponseRevisionDto> SaveResponseAsync(Guid attemptId, SaveEducationAssessmentResponseDto input);
    [Authorize(EducationPermissions.AssessmentParticipate)]
    Task<EducationAssessmentResponseRevisionDto?> GetResponseRevisionAsync(Guid attemptId, Guid operationId);
    [Authorize(EducationPermissions.AssessmentParticipate)]
    Task<EducationAssessmentSubmissionDto> SubmitAsync(Guid attemptId, SubmitEducationAssessmentDto input);
    [Authorize(EducationPermissions.AssessmentParticipate)]
    Task<EducationAssessmentSubmissionDto?> GetSubmissionAsync(Guid attemptId);
    [Authorize(EducationPermissions.AssessmentParticipate)]
    Task<EducationAssessmentSubmissionPreviewDto> PreviewSubmissionAsync(Guid attemptId);
    [Authorize(EducationPermissions.AssessmentParticipate)]
    Task<EducationAssessmentAnswerImagePolicyDto> GetAnswerImagePolicyAsync(Guid attemptId);
    [Authorize(EducationPermissions.AssessmentParticipate)]
    Task<EducationAssessmentAnswerImageDto> UploadAnswerImageAsync(Guid attemptId, UploadEducationAssessmentAnswerImageDto input, IRemoteStreamContent file);
    [Authorize(EducationPermissions.AssessmentParticipate)]
    Task<EducationAssessmentAnswerImageDto?> GetAnswerImageAsync(Guid attemptId, Guid operationId);
    [Authorize(EducationPermissions.AssessmentParticipate)]
    Task<IRemoteStreamContent> DownloadAnswerImageAsync(Guid attemptId, Guid attachmentId);
}
