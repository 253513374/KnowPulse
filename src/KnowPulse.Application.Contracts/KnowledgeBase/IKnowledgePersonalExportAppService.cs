using System;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace KnowPulse.KnowledgeBase;

public interface IKnowledgePersonalExportAppService : IApplicationService
{
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<PersonalKnowledgeExportDto> CreateAsync(CreatePersonalKnowledgeExportDto input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<PersonalKnowledgeExportDto> GetAsync(Guid id);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<PagedResultDto<PersonalKnowledgeExportDto>> GetListAsync(PagedResultRequestDto input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<PersonalKnowledgeExportDto> RetryAsync(Guid id);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<PersonalKnowledgeExportDto> CancelAsync(Guid id);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<IRemoteStreamContent> DownloadAsync(Guid id);
}
