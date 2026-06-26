using System;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace KnowPulse.KnowledgeBase;

public interface IKnowledgeItemAppService : IApplicationService
{
    [Authorize(KnowPulsePermissions.KnowledgeBase.Items)]
    Task<PagedResultDto<KnowledgeItemDto>> GetListAsync(PagedAndSortedResultRequestDto input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.Items)]
    Task<KnowledgeItemDto> GetAsync(Guid id);
    [Authorize(KnowPulsePermissions.KnowledgeBase.Items)]
    Task<KnowledgeItemDto> CreateAsync(CreateKnowledgeItemDto input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.Items)]
    Task<KnowledgeItemDto> UpdateAsync(Guid id, UpdateKnowledgeItemDto input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.Items)]
    Task DeleteAsync(Guid id);

    [Authorize(KnowPulsePermissions.KnowledgeBase.Items)]
    Task<ListResultDto<KnowledgeVersionDto>> GetVersionsAsync(Guid id);
    [Authorize(KnowPulsePermissions.KnowledgeBase.Versions)]
    Task<KnowledgeVersionDto> CreateVersionAsync(Guid id, CreateKnowledgeVersionDto input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.Versions)]
    Task<KnowledgeVersionDto> PublishVersionAsync(Guid id, Guid versionId);
    [Authorize(KnowPulsePermissions.KnowledgeBase.Versions)]
    Task<KnowledgeVersionDto> WithdrawVersionAsync(Guid id, Guid versionId);
    [Authorize(KnowPulsePermissions.KnowledgeBase.Versions)]
    Task<KnowledgeVersionDto> ArchiveVersionAsync(Guid id, Guid versionId);

    [Authorize(KnowPulsePermissions.KnowledgeBase.Attachments)]
    Task<ListResultDto<KnowledgeAttachmentDto>> GetAttachmentsAsync(Guid id, Guid versionId);
    [Authorize(KnowPulsePermissions.KnowledgeBase.Attachments)]
    Task<KnowledgeAttachmentDto> UploadAttachmentAsync(Guid id, Guid versionId, IRemoteStreamContent file);
    [Authorize(KnowPulsePermissions.KnowledgeBase.Attachments)]
    Task<IRemoteStreamContent> DownloadAttachmentAsync(Guid id, Guid versionId, Guid attachmentId);
    [Authorize(KnowPulsePermissions.KnowledgeBase.Attachments)]
    Task DeleteAttachmentAsync(Guid id, Guid versionId, Guid attachmentId);

    [Authorize(KnowPulsePermissions.KnowledgeBase.PermissionScopes)]
    Task<ListResultDto<KnowledgePermissionScopeDto>> GetPermissionScopesAsync(Guid id);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PermissionScopes)]
    Task<KnowledgePermissionScopeDto> CreatePermissionScopeAsync(Guid id, CreateKnowledgePermissionScopeDto input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.PermissionScopes)]
    Task DeletePermissionScopeAsync(Guid id, Guid scopeId);
}
