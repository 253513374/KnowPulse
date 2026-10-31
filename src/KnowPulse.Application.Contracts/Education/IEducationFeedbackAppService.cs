using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.Education;

public interface IEducationFeedbackAppService : IApplicationService
{
    [Authorize(EducationPermissions.FeedbackManage)] Task<List<EducationFeedbackTeachingDto>> GetTeachingListAsync();
    [Authorize(EducationPermissions.FeedbackManage)] Task<PagedResultDto<EducationFeedbackTargetDto>> GetStudentsAsync(Guid teachingAssignmentId, PagedResultRequestDto input);
    [Authorize(EducationPermissions.FeedbackManage)] Task<EducationFeedbackTargetContextDto> GetTargetAsync(EducationFeedbackRecipientInput input);
    [Authorize(EducationPermissions.FeedbackManage)] Task<List<EducationFeedbackAssessmentGateDto>> GetAssessmentListAsync(EducationFeedbackRecipientInput input);
    [Authorize(EducationPermissions.FeedbackManage)] Task<EducationTeacherObservationDto> CreateObservationAsync(CreateEducationTeacherObservationDto input);
    [Authorize(EducationPermissions.FeedbackManage)] Task<PagedResultDto<EducationTeacherObservationDto>> GetObservationsAsync(EducationFeedbackRecipientInput input, PagedResultRequestDto page);
    [Authorize(EducationPermissions.FeedbackManage)] Task<EducationTeacherObservationDto> CreateObservationForSchoolAsync(Guid schoolId, CreateEducationTeacherObservationDto input);
    [Authorize(EducationPermissions.FeedbackManage)] Task<PagedResultDto<EducationTeacherObservationDto>> GetObservationsForSchoolAsync(Guid schoolId, EducationFeedbackRecipientInput input, PagedResultRequestDto page);
    [Authorize(EducationPermissions.FeedbackManage)] Task<EducationTeacherObservationDto?> GetObservationOperationAsync(Guid schoolId, Guid operationId, EducationFeedbackRecipientInput input);
    [Authorize(EducationPermissions.FeedbackManage)] Task<PagedResultDto<EducationFeedbackPhraseDto>> GetPhrasesAsync(EducationFeedbackPhraseScopeDto input, PagedResultRequestDto page, bool includeArchived = false);
    [Authorize(EducationPermissions.FeedbackManage)] Task<EducationFeedbackPhraseDto?> GetPhraseAsync(Guid phraseId, EducationFeedbackPhraseScopeDto input);
    [Authorize(EducationPermissions.FeedbackManage)] Task<EducationFeedbackPhraseRevisionDto> SavePhraseAsync(Guid phraseId, SaveEducationFeedbackPhraseDto input);
    [Authorize(EducationPermissions.FeedbackManage)] Task<EducationFeedbackPhraseRevisionDto?> GetPhraseOperationAsync(Guid phraseId, Guid operationId, EducationFeedbackPhraseScopeDto input);
    [Authorize(EducationPermissions.FeedbackManage)] Task<EducationFeedbackRevisionDto> SaveDraftAsync(Guid feedbackId, SaveEducationFeedbackDraftDto input);
    [Authorize(EducationPermissions.FeedbackManage)] Task<EducationFeedbackDto> GetAsync(Guid id);
    [Authorize(EducationPermissions.FeedbackManage)] Task<EducationFeedbackDto?> GetMyWorkspaceAsync(Guid feedbackId);
    [Authorize(EducationPermissions.FeedbackManage)] Task<PagedResultDto<EducationFeedbackDto>> GetListAsync(Guid teachingAssignmentId, PagedResultRequestDto input);
    [Authorize(EducationPermissions.FeedbackManage)] Task<EducationFeedbackPublishPreviewDto> PreviewPublishAsync(Guid feedbackId);
    [Authorize(EducationPermissions.FeedbackManage)] Task<EducationFeedbackRevisionDto> PublishAsync(Guid feedbackId, PublishEducationFeedbackDto input);
    [Authorize(EducationPermissions.FeedbackManage)] Task<EducationFeedbackRevisionDto> WithdrawAsync(Guid feedbackId, WithdrawEducationFeedbackDto input);
    [Authorize(EducationPermissions.FeedbackManage)] Task<EducationFeedbackRevisionDto?> GetOperationAsync(Guid feedbackId, Guid operationId);
    [Authorize(EducationPermissions.FeedbackView)] Task<PagedResultDto<EducationStudentFeedbackDto>> GetMyListAsync(PagedResultRequestDto input, Guid? schoolId = null);
    [Authorize(EducationPermissions.FeedbackView)] Task<EducationStudentFeedbackDto> OpenMyAsync(Guid feedbackId, Guid expectedVersionId);
    [Authorize(EducationPermissions.FeedbackView)] Task<EducationStudentFeedbackDto> OpenMyForSchoolAsync(Guid feedbackId, Guid expectedVersionId, Guid schoolId);
    [Authorize(EducationPermissions.FeedbackView)] Task<PagedResultDto<EducationFeedbackNoticeDto>> GetMyNoticesAsync(PagedResultRequestDto input, Guid? schoolId = null);
    [Authorize(EducationPermissions.FeedbackView)] Task<EducationFeedbackNoticeDto> ReadMyNoticeAsync(Guid noticeId, Guid schoolId);
}
