using System;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.KnowledgeBase;

public interface IKnowledgeCategoryAppService : IApplicationService
{
    [Authorize(KnowPulsePermissions.KnowledgeBase.Categories)]
    Task<PagedResultDto<KnowledgeCategoryDto>> GetListAsync(PagedAndSortedResultRequestDto input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.Categories)]
    Task<KnowledgeCategoryDto> GetAsync(Guid id);
    [Authorize(KnowPulsePermissions.KnowledgeBase.Categories)]
    Task<KnowledgeCategoryDto> CreateAsync(CreateUpdateKnowledgeCategoryDto input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.Categories)]
    Task<KnowledgeCategoryDto> UpdateAsync(Guid id, CreateUpdateKnowledgeCategoryDto input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.Categories)]
    Task DeleteAsync(Guid id);
}
