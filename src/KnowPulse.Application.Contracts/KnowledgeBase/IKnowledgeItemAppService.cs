using System;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using KnowPulse.KnowledgeIndexing;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace KnowPulse.KnowledgeBase;

public interface IKnowledgeItemAppService : IApplicationService
{
    [Authorize(KnowPulsePermissions.KnowledgeBase.Items)]
    Task<ListResultDto<KnowledgeShareTeacherDto>> SearchShareTeachersAsync(string? keyword);

    [Authorize(KnowPulsePermissions.KnowledgeBase.PermissionScopes)]
    Task SetSharedTeachersAsync(Guid id, ShareKnowledgeWithTeachersDto input);

    [Authorize(KnowPulsePermissions.KnowledgeBase.Versions)]
    Task<KnowledgeIndexJobDto> RetryProcessingAsync(Guid id);

    [Authorize]
    Task<PagedResultDto<KnowledgeItemDto>> GetListAsync(PagedAndSortedResultRequestDto input);
    [Authorize]
    Task<KnowledgeItemDto> GetAsync(Guid id);
    [Authorize]
    Task<KnowledgeItemDto> CreateAsync(CreateKnowledgeItemDto input);
    [Authorize]
    Task<KnowledgeItemDto> UpdateAsync(Guid id, UpdateKnowledgeItemDto input);
    [Authorize]
    Task DeleteAsync(Guid id);

    [Authorize]
    Task<ListResultDto<KnowledgeVersionDto>> GetVersionsAsync(Guid id);
    [Authorize]
    Task<KnowledgeVersionDto> CreateVersionAsync(Guid id, CreateKnowledgeVersionDto input);
    [Authorize(KnowPulsePermissions.KnowledgeBase.Versions)]
    Task<KnowledgeVersionDto> UpdateDraftVersionAsync(Guid id, Guid versionId, CreateKnowledgeVersionDto input);
    [Authorize]
    Task<KnowledgeVersionDto> PublishVersionAsync(Guid id, Guid versionId);
    [Authorize]
    Task<PublishKnowledgeVersionResultDto> PublishVersionWithAccessPolicyAsync(Guid id, PublishKnowledgeVersionInput input);
    [Authorize]
    Task<KnowledgeVersionDto> WithdrawVersionAsync(Guid id, Guid versionId);
    [Authorize]
    Task<KnowledgeVersionDto> ArchiveVersionAsync(Guid id, Guid versionId);

    [Authorize]
    Task<ListResultDto<KnowledgeAttachmentDto>> GetAttachmentsAsync(Guid id, Guid versionId);
    [Authorize]
    Task<KnowledgeAttachmentDto> UploadAttachmentAsync(Guid id, Guid versionId, IRemoteStreamContent file);
    [Authorize]
    Task<IRemoteStreamContent> DownloadAttachmentAsync(Guid id, Guid versionId, Guid attachmentId);
    [Authorize]
    Task DeleteAttachmentAsync(Guid id, Guid versionId, Guid attachmentId);

    [Authorize]
    Task<ListResultDto<KnowledgePermissionScopeDto>> GetPermissionScopesAsync(Guid id);
    [Authorize]
    Task<KnowledgePermissionScopeDto> CreatePermissionScopeAsync(Guid id, CreateKnowledgePermissionScopeDto input);
    [Authorize]
    Task DeletePermissionScopeAsync(Guid id, Guid scopeId);
}
