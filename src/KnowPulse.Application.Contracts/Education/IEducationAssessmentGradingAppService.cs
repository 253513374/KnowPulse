using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace KnowPulse.Education;

public interface IEducationAssessmentGradingAppService : IApplicationService
{
    [Authorize(EducationPermissions.AssessmentManage)] Task<EducationAssessmentGradingOverviewDto> GetGradingOverviewAsync(Guid taskId);
    [Authorize(EducationPermissions.AssessmentManage)] Task<EducationAssessmentSubmissionForGradingDto> GetSubmissionForGradingAsync(Guid submissionId);
    [Authorize(EducationPermissions.AssessmentManage)] Task<IRemoteStreamContent> DownloadSubmissionImageAsync(Guid submissionId, Guid attachmentId);
    [Authorize(EducationPermissions.AssessmentManage)] Task<EducationAssessmentGradeRevisionDto> SaveGradeDraftAsync(Guid submissionId, SaveEducationAssessmentGradeDraftDto input);
    [Authorize(EducationPermissions.AssessmentManage)] Task<EducationAssessmentGradeRevisionDto> ConfirmGradeAsync(Guid questionGradeId, ConfirmEducationAssessmentGradeDto input);
    [Authorize(EducationPermissions.AssessmentManage)] Task<EducationAssessmentGradeRevisionDto?> GetGradeOperationAsync(Guid submissionId, Guid operationId);
    [Authorize(EducationPermissions.AssessmentManage)] Task<EducationAssessmentFinalizationPreviewDto> PreviewFinalizationAsync(Guid taskId);
    [Authorize(EducationPermissions.AssessmentManage)] Task<EducationAssessmentClosureDto> CloseTaskAsync(Guid taskId, CloseEducationAssessmentTaskDto input);
    [Authorize(EducationPermissions.AssessmentManage)] Task<EducationAssessmentClosureDto?> GetClosureOperationAsync(Guid taskId, Guid operationId);
    [Authorize(EducationPermissions.AssessmentManage)] Task<EducationAssessmentResultReleaseDto> PublishResultsAsync(Guid taskId, PublishEducationAssessmentResultsDto input);
    [Authorize(EducationPermissions.AssessmentManage)] Task<EducationAssessmentResultReleaseDto?> GetReleaseOperationAsync(Guid taskId, Guid operationId);
    [Authorize(EducationPermissions.AssessmentParticipate)] Task<EducationAssessmentStudentResultDto> GetMyResultAsync(Guid recipientId);
    [Authorize(EducationPermissions.AssessmentParticipate)] Task<EducationAssessmentStudentResultDto> GetMyResultForSchoolAsync(Guid recipientId, Guid schoolId, Guid? expectedTaskRevisionId = null, Guid? expectedResultReleaseId = null);
    [Authorize(EducationPermissions.AssessmentParticipate)] Task<EducationAssessmentScoreReviewRequestDto> RequestScoreReviewAsync(Guid recipientId, RequestEducationAssessmentScoreReviewDto input);
    [Authorize(EducationPermissions.AssessmentParticipate)] Task<EducationAssessmentScoreReviewRequestDto> GetMyScoreReviewAsync(Guid requestId);
    [Authorize(EducationPermissions.AssessmentManage)] Task<PagedResultDto<EducationAssessmentScoreReviewRequestDto>> GetScoreReviewsAsync(Guid taskId, PagedResultRequestDto input);
    [Authorize(EducationPermissions.AssessmentManage)] Task<EducationAssessmentScoreReviewRequestDto> ResolveScoreReviewAsync(Guid requestId, ResolveEducationAssessmentScoreReviewDto input);
    [Authorize(EducationPermissions.AssessmentManage)] Task<EducationAssessmentScoreReviewRequestDto?> GetScoreReviewResolutionAsync(Guid requestId, Guid operationId);
}
