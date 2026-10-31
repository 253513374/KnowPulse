using System;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace KnowPulse.KnowledgeBase;

public interface IKnowledgeSchoolSharingAppService : IApplicationService
{
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<SchoolKnowledgeShareDto> ShareVersionAsync(CreateSchoolKnowledgeShareDto input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<PagedResultDto<SchoolKnowledgeShareDto>> GetMySharesAsync(GetSchoolKnowledgeShareListInput input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<PagedResultDto<SchoolKnowledgeShareDto>> GetSchoolSharesAsync(Guid schoolId, GetSchoolKnowledgeShareListInput input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<SchoolKnowledgeShareDto> GetAsync(Guid shareId);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<SchoolKnowledgeShareDetailDto> GetDetailAsync(Guid shareId);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<IRemoteStreamContent> DownloadAttachmentAsync(Guid shareId, Guid attachmentId);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<SchoolKnowledgeShareDto> RevokeAsync(Guid shareId);
}
