using System;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.Education;

public interface IEducationPreparationAppService : IApplicationService
{
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<PagedResultDto<KnowledgeMaterialCurriculumBindingDto>> GetMyBindingsAsync(GetMaterialBindingListInput input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<KnowledgeMaterialCurriculumBindingDto> ConfirmBindingAsync(ConfirmKnowledgeMaterialCurriculumBindingDto input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<KnowledgeMaterialCurriculumBindingDto> EndBindingAsync(Guid id, EndMaterialBindingDto input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<PagedResultDto<EducationMaterialVersionOptionDto>> GetMyMaterialVersionsAsync(GetEducationMaterialListInput input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<PagedResultDto<EducationMaterialChunkOptionDto>> GetMaterialChunksAsync(Guid versionId, PagedResultRequestDto input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<PagedResultDto<LessonPreparationListDto>> GetMyPreparationsAsync(GetLessonPreparationListInput input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<LessonPreparationDto> GetPreparationAsync(Guid id);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<PagedResultDto<LessonPreparationRevisionDto>> GetRevisionsAsync(Guid id, PagedResultRequestDto input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<LessonPreparationRevisionDto> GetRevisionAsync(Guid id, Guid revisionId);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<LessonPreparationDto> CreatePreparationAsync(CreateLessonPreparationDto input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<LessonPreparationDto> SavePreparationAsync(Guid id, SaveLessonPreparationDto input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<LessonPreparationDto> CopyPreparationAsync(Guid sourceRevisionId, CopyLessonPreparationDto input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<PagedResultDto<EducationPreparationReferenceOptionDto>> GetReferenceOptionsAsync(GetPreparationReferenceOptionsInput input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<Guid> CopyReflectionToMyLibraryAsync(Guid preparationId, Guid revisionId, CopyReflectionToKnowledgeDto input);
}
