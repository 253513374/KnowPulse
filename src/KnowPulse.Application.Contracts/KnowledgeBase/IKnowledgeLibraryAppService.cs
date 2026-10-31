using System;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.KnowledgeBase;

public interface IKnowledgeLibraryAppService : IApplicationService
{
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<PersonalKnowledgeUploadPolicyDto> GetUploadPolicyAsync();

    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<KnowledgeLibraryDto> GetMyLibraryAsync();

    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<PagedResultDto<KnowledgeItemDto>> GetMyItemsAsync(GetPersonalKnowledgeItemListInput input);

    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<KnowledgeItemDto> CreateItemAsync(CreateKnowledgeItemDto input);

    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<KnowledgeItemDto> UpdateOrganizationAsync(Guid itemId, UpdatePersonalKnowledgeOrganizationInput input);

    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<PersonalKnowledgeOrganizationOptionsDto> GetOrganizationOptionsAsync();
}
