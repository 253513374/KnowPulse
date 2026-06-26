using System;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.KnowledgeBase;

public interface IKnowledgeTagAppService : IApplicationService
{
    [Authorize(KnowPulsePermissions.KnowledgeBase.Tags)]
    Task<PagedResultDto<KnowledgeTagDto>> GetListAsync(PagedAndSortedResultRequestDto input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.Tags)]
    Task<KnowledgeTagDto> GetAsync(Guid id);
    [Authorize(KnowPulsePermissions.KnowledgeBase.Tags)]
    Task<KnowledgeTagDto> CreateAsync(CreateUpdateKnowledgeTagDto input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.Tags)]
    Task<KnowledgeTagDto> UpdateAsync(Guid id, CreateUpdateKnowledgeTagDto input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.Tags)]
    Task DeleteAsync(Guid id);
}
