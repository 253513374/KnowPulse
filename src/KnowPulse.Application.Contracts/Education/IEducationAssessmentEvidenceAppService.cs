using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.Education;

public interface IEducationAssessmentEvidenceAppService : IApplicationService
{
    [Authorize(EducationPermissions.AssessmentManage)] Task<EducationQuestionEvidenceDto> GetEvidenceAsync(Guid questionGradeId);
    [Authorize(EducationPermissions.AssessmentManage)] Task<EducationEvidenceQualificationRevisionDto> SaveEvidenceQualificationAsync(Guid questionGradeId, SaveEducationEvidenceQualificationDto input);
    [Authorize(EducationPermissions.AssessmentManage)] Task<EducationEvidenceQualificationRevisionDto?> GetQualificationOperationAsync(Guid questionGradeId, Guid operationId);
    [Authorize(EducationPermissions.AssessmentManage)] Task<EducationOriginCandidatesDto> GetOriginCandidatesAsync(Guid questionGradeId, PagedResultRequestDto input);
    [Authorize(EducationPermissions.AssessmentManage)] Task<EducationOriginRelationDto> GetOriginRelationAsync(Guid questionGradeId, Guid secondQuestionGradeId);
    [Authorize(EducationPermissions.AssessmentManage)] Task<EducationOriginRelationRevisionDto> SaveOriginRelationAsync(Guid questionGradeId, SaveEducationOriginRelationDto input);
    [Authorize(EducationPermissions.AssessmentManage)] Task<EducationOriginRelationRevisionDto?> GetOriginOperationAsync(Guid questionGradeId, Guid secondQuestionGradeId, Guid operationId);
    [Authorize(EducationPermissions.AssessmentManage)] Task<PagedResultDto<EducationEvidenceExposureDto>> GetExposureContextsAsync(Guid questionGradeId, PagedResultRequestDto input);
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.FeedbackManage)] Task<EducationHintContextDto> GetHintContextAsync(Guid questionGradeId, Guid exposureId);
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.FeedbackManage)] Task<EducationHintReviewRevisionDto> SaveHintReviewAsync(Guid questionGradeId, Guid exposureId, SaveEducationHintReviewDto input);
    [Authorize(EducationPermissions.AssessmentManage), Authorize(EducationPermissions.FeedbackManage)] Task<EducationHintReviewRevisionDto?> GetHintReviewOperationAsync(Guid questionGradeId, Guid exposureId, Guid operationId);
}
