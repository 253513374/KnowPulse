using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Organization;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;

namespace KnowPulse.KnowledgeBase;

/// <summary>
/// 知识文档应用服务，负责知识条目、版本、附件和权限范围的 MVP-0 后端闭环。
/// 该服务是页面和 REST API 的业务入口，会在每个操作中显式执行权限检查，
/// 并在读取知识时叠加知识权限范围，避免仅依赖前端菜单隐藏。
/// </summary>
[Authorize(KnowPulsePermissions.KnowledgeBase.Items)]
public class KnowledgeItemAppService : KnowPulseAppService, IKnowledgeItemAppService
{
    private readonly IRepository<KnowledgeItem, Guid> _itemRepository;
    private readonly IRepository<KnowledgeVersion, Guid> _versionRepository;
    private readonly IRepository<KnowledgeAttachment, Guid> _attachmentRepository;
    private readonly IRepository<KnowledgePermissionScope, Guid> _permissionScopeRepository;
    private readonly IRepository<UserOrganizationAssignment, Guid> _assignmentRepository;
    private readonly IIdentityRoleRepository _roleRepository;
    private readonly IBlobContainer<KnowledgeAttachmentBlobContainer> _attachmentContainer;

    public KnowledgeItemAppService(
        IRepository<KnowledgeItem, Guid> itemRepository,
        IRepository<KnowledgeVersion, Guid> versionRepository,
        IRepository<KnowledgeAttachment, Guid> attachmentRepository,
        IRepository<KnowledgePermissionScope, Guid> permissionScopeRepository,
        IRepository<UserOrganizationAssignment, Guid> assignmentRepository,
        IIdentityRoleRepository roleRepository,
        IBlobContainer<KnowledgeAttachmentBlobContainer> attachmentContainer)
    {
        _itemRepository = itemRepository;
        _versionRepository = versionRepository;
        _attachmentRepository = attachmentRepository;
        _permissionScopeRepository = permissionScopeRepository;
        _assignmentRepository = assignmentRepository;
        _roleRepository = roleRepository;
        _attachmentContainer = attachmentContainer;
    }

    /// <summary>
    /// 分页获取当前用户可见的知识文档。
    /// 这里先读取 MVP-0 范围内的知识集合，再按知识权限范围进行内存过滤；
    /// 后续数据量上来后可以把粗过滤下推到数据库查询。
    /// </summary>
    public virtual async Task<PagedResultDto<KnowledgeItemDto>> GetListAsync(PagedAndSortedResultRequestDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Items);
        var queryable = await _itemRepository.GetQueryableAsync();
        var allItems = await AsyncExecuter.ToListAsync(queryable.OrderBy(x => x.Title));
        var scopesByItemId = await GetScopeLookupAsync(allItems.Select(x => x.Id));
        var accessContext = await CreateAccessContextAsync();

        var visibleItems = allItems
            .Where(item => KnowledgeAccessEvaluator.CanAccess(scopesByItemId[item.Id], accessContext))
            .ToList();

        var pagedItems = visibleItems
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount)
            .Select(MapToDto)
            .ToList();

        return new PagedResultDto<KnowledgeItemDto>(visibleItems.Count, pagedItems);
    }

    /// <summary>
    /// 获取单个知识文档详情，并校验当前用户是否命中该文档的知识权限范围。
    /// </summary>
    public virtual async Task<KnowledgeItemDto> GetAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Items);
        return MapToDto(await GetVisibleItemAsync(id));
    }

    /// <summary>
    /// 创建知识文档。新建文档只包含元信息，正文需要通过创建版本单独维护。
    /// </summary>
    public virtual async Task<KnowledgeItemDto> CreateAsync(CreateKnowledgeItemDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Items);
        var item = new KnowledgeItem(GuidGenerator.Create(), CurrentTenant.Id, input.Title, input.CategoryId, input.Summary);
        return MapToDto(await _itemRepository.InsertAsync(item, autoSave: true));
    }

    /// <summary>
    /// 更新知识文档的标题、摘要、分类和启用状态。
    /// </summary>
    public virtual async Task<KnowledgeItemDto> UpdateAsync(Guid id, UpdateKnowledgeItemDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Items);
        var item = await _itemRepository.GetAsync(id);
        item.Rename(input.Title);
        item.ChangeSummary(input.Summary);
        item.ChangeCategory(input.CategoryId);
        item.SetActive(input.IsActive);

        return MapToDto(await _itemRepository.UpdateAsync(item, autoSave: true));
    }

    /// <summary>
    /// 删除知识文档。当前使用仓储删除，审计和软删除能力由 ABP 基础设施处理。
    /// </summary>
    public virtual async Task DeleteAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Items);
        await _itemRepository.DeleteAsync(id);
    }

    /// <summary>
    /// 获取指定知识文档的版本列表。调用前会先校验知识本身是否对当前用户可见。
    /// </summary>
    public virtual async Task<ListResultDto<KnowledgeVersionDto>> GetVersionsAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Items);
        await GetVisibleItemAsync(id);

        var queryable = await _versionRepository.GetQueryableAsync();
        var versions = await AsyncExecuter.ToListAsync(queryable.Where(x => x.KnowledgeItemId == id).OrderByDescending(x => x.VersionNumber));

        return new ListResultDto<KnowledgeVersionDto>(versions.Select(MapToDto).ToList());
    }

    /// <summary>
    /// 为知识文档创建新版本。版本号按该知识已有版本数量递增，初始状态为草稿。
    /// </summary>
    [Authorize(KnowPulsePermissions.KnowledgeBase.Versions)]
    public virtual async Task<KnowledgeVersionDto> CreateVersionAsync(Guid id, CreateKnowledgeVersionDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Versions);
        await _itemRepository.GetAsync(id);

        var queryable = await _versionRepository.GetQueryableAsync();
        var versionCount = await AsyncExecuter.CountAsync(queryable.Where(x => x.KnowledgeItemId == id));

        var version = new KnowledgeVersion(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            id,
            versionCount + 1,
            input.Title,
            input.ContentText);

        return MapToDto(await _versionRepository.InsertAsync(version, autoSave: true));
    }

    /// <summary>
    /// 发布指定版本，并维护“同一知识只有一个当前发布版本”的业务规则。
    /// </summary>
    [Authorize(KnowPulsePermissions.KnowledgeBase.Versions)]
    public virtual async Task<KnowledgeVersionDto> PublishVersionAsync(Guid id, Guid versionId)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Versions);
        var item = await _itemRepository.GetAsync(id);
        var version = await GetVersionOfItemAsync(id, versionId);

        var queryable = await _versionRepository.GetQueryableAsync();
        var currentVersions = await AsyncExecuter.ToListAsync(queryable.Where(x => x.KnowledgeItemId == id && x.IsCurrentPublished));

        // 数据库有唯一索引兜底，这里仍主动清理旧发布标记，保证领域状态和页面展示立即一致。
        foreach (var currentVersion in currentVersions)
        {
            if (currentVersion.Id != version.Id)
            {
                currentVersion.ClearCurrentPublished();
                await _versionRepository.UpdateAsync(currentVersion, autoSave: true);
            }
        }

        version.Publish(DateTime.UtcNow);
        item.SetCurrentPublishedVersion(version.Id);

        await _itemRepository.UpdateAsync(item);
        return MapToDto(await _versionRepository.UpdateAsync(version, autoSave: true));
    }

    /// <summary>
    /// 下架指定版本。如果该版本是知识文档当前发布版本，同步清空知识上的当前版本引用。
    /// </summary>
    [Authorize(KnowPulsePermissions.KnowledgeBase.Versions)]
    public virtual async Task<KnowledgeVersionDto> WithdrawVersionAsync(Guid id, Guid versionId)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Versions);
        var item = await _itemRepository.GetAsync(id);
        var version = await GetVersionOfItemAsync(id, versionId);

        version.Withdraw(DateTime.UtcNow);
        if (item.CurrentPublishedVersionId == version.Id)
        {
            item.SetCurrentPublishedVersion(null);
            await _itemRepository.UpdateAsync(item);
        }

        return MapToDto(await _versionRepository.UpdateAsync(version, autoSave: true));
    }

    /// <summary>
    /// 归档指定版本。归档后不再作为当前发布版本参与访问。
    /// </summary>
    [Authorize(KnowPulsePermissions.KnowledgeBase.Versions)]
    public virtual async Task<KnowledgeVersionDto> ArchiveVersionAsync(Guid id, Guid versionId)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Versions);
        var item = await _itemRepository.GetAsync(id);
        var version = await GetVersionOfItemAsync(id, versionId);

        version.Archive(DateTime.UtcNow);
        if (item.CurrentPublishedVersionId == version.Id)
        {
            item.SetCurrentPublishedVersion(null);
            await _itemRepository.UpdateAsync(item);
        }

        return MapToDto(await _versionRepository.UpdateAsync(version, autoSave: true));
    }

    /// <summary>
    /// 获取指定版本的附件列表。附件列表读取前同样校验知识可见性，避免通过版本编号绕过权限。
    /// </summary>
    [Authorize(KnowPulsePermissions.KnowledgeBase.Attachments)]
    public virtual async Task<ListResultDto<KnowledgeAttachmentDto>> GetAttachmentsAsync(Guid id, Guid versionId)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Attachments);
        await GetVisibleItemAsync(id);
        await GetVersionOfItemAsync(id, versionId);

        var queryable = await _attachmentRepository.GetQueryableAsync();
        var attachments = await AsyncExecuter.ToListAsync(
            queryable.Where(x => x.KnowledgeVersionId == versionId).OrderBy(x => x.FileName));

        return new ListResultDto<KnowledgeAttachmentDto>(attachments.Select(MapToDto).ToList());
    }

    /// <summary>
    /// 上传知识版本附件。文件内容写入 BlobStoring，数据库只保存附件元数据。
    /// </summary>
    [Authorize(KnowPulsePermissions.KnowledgeBase.Attachments)]
    public virtual async Task<KnowledgeAttachmentDto> UploadAttachmentAsync(Guid id, Guid versionId, IRemoteStreamContent file)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Attachments);
        Check.NotNull(file, nameof(file));

        await _itemRepository.GetAsync(id);
        await GetVersionOfItemAsync(id, versionId);

        var attachmentId = GuidGenerator.Create();
        var fileName = NormalizeFileName(file.FileName);
        var contentType = NormalizeContentType(file.ContentType);
        var blobName = BuildAttachmentBlobName(id, versionId, attachmentId, fileName);

        // 先保存对象存储，再写入元数据；如果对象保存失败，不会产生悬空附件记录。
        using var stream = file.GetStream();
        var size = GetContentLength(file, stream);
        await _attachmentContainer.SaveAsync(blobName, stream, overrideExisting: false);

        var attachment = new KnowledgeAttachment(
            attachmentId,
            CurrentTenant.Id,
            versionId,
            fileName,
            blobName,
            contentType,
            size);

        return MapToDto(await _attachmentRepository.InsertAsync(attachment, autoSave: true));
    }

    /// <summary>
    /// 下载指定附件。返回远程流内容，由 ABP 框架负责序列化给 HTTP 调用方。
    /// </summary>
    [Authorize(KnowPulsePermissions.KnowledgeBase.Attachments)]
    public virtual async Task<IRemoteStreamContent> DownloadAttachmentAsync(Guid id, Guid versionId, Guid attachmentId)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Attachments);
        await GetVisibleItemAsync(id);
        await GetVersionOfItemAsync(id, versionId);

        var attachment = await GetAttachmentOfVersionAsync(versionId, attachmentId);
        var stream = await _attachmentContainer.GetAsync(attachment.BlobName);

        return new RemoteStreamContent(stream, attachment.FileName, attachment.ContentType, attachment.Size, disposeStream: true);
    }

    /// <summary>
    /// 删除指定附件，同时删除对象存储中的文件和业务主库中的元数据。
    /// </summary>
    [Authorize(KnowPulsePermissions.KnowledgeBase.Attachments)]
    public virtual async Task DeleteAttachmentAsync(Guid id, Guid versionId, Guid attachmentId)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Attachments);
        await _itemRepository.GetAsync(id);
        await GetVersionOfItemAsync(id, versionId);

        var attachment = await GetAttachmentOfVersionAsync(versionId, attachmentId);
        await _attachmentContainer.DeleteAsync(attachment.BlobName);
        await _attachmentRepository.DeleteAsync(attachment, autoSave: true);
    }

    /// <summary>
    /// 获取知识文档的权限范围列表。该接口面向管理员配置，不按当前用户可见性过滤。
    /// </summary>
    [Authorize(KnowPulsePermissions.KnowledgeBase.PermissionScopes)]
    public virtual async Task<ListResultDto<KnowledgePermissionScopeDto>> GetPermissionScopesAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.PermissionScopes);
        await _itemRepository.GetAsync(id);

        var queryable = await _permissionScopeRepository.GetQueryableAsync();
        var scopes = await AsyncExecuter.ToListAsync(queryable.Where(x => x.KnowledgeItemId == id).OrderBy(x => x.ScopeType));

        return new ListResultDto<KnowledgePermissionScopeDto>(scopes.Select(MapToDto).ToList());
    }

    /// <summary>
    /// 为知识文档新增权限范围，支持全员、部门、岗位、角色和指定用户。
    /// </summary>
    [Authorize(KnowPulsePermissions.KnowledgeBase.PermissionScopes)]
    public virtual async Task<KnowledgePermissionScopeDto> CreatePermissionScopeAsync(Guid id, CreateKnowledgePermissionScopeDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.PermissionScopes);
        await _itemRepository.GetAsync(id);

        var scope = new KnowledgePermissionScope(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            id,
            input.ScopeType,
            input.TargetId,
            input.MinimumSecurityLevel);

        return MapToDto(await _permissionScopeRepository.InsertAsync(scope, autoSave: true));
    }

    /// <summary>
    /// 删除知识文档上的权限范围。传入的范围不属于当前知识时直接忽略，保持接口幂等。
    /// </summary>
    [Authorize(KnowPulsePermissions.KnowledgeBase.PermissionScopes)]
    public virtual async Task DeletePermissionScopeAsync(Guid id, Guid scopeId)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.PermissionScopes);
        var scope = await _permissionScopeRepository.GetAsync(scopeId);
        if (scope.KnowledgeItemId != id)
        {
            return;
        }

        await _permissionScopeRepository.DeleteAsync(scope);
    }

    private async Task<KnowledgeItem> GetVisibleItemAsync(Guid id)
    {
        var item = await _itemRepository.GetAsync(id);
        var scopes = await GetEnabledScopesAsync(id);
        var accessContext = await CreateAccessContextAsync();

        // 知识权限范围是知识库的二次业务授权，不等同于 ABP 功能权限。
        // 用户必须同时拥有功能权限，并命中知识本身的可见范围。
        if (!KnowledgeAccessEvaluator.CanAccess(scopes, accessContext))
        {
            throw new AbpAuthorizationException("The current user is not allowed to access this knowledge item.");
        }

        return item;
    }

    /// <summary>
    /// 读取指定知识的启用权限范围。停用范围保留审计，但不参与访问判定。
    /// </summary>
    private async Task<List<KnowledgePermissionScope>> GetEnabledScopesAsync(Guid itemId)
    {
        var queryable = await _permissionScopeRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(queryable.Where(x => x.KnowledgeItemId == itemId && x.IsEnabled));
    }

    /// <summary>
    /// 批量构建知识编号到权限范围的映射，避免列表页逐条查询授权范围。
    /// </summary>
    private async Task<Dictionary<Guid, List<KnowledgePermissionScope>>> GetScopeLookupAsync(IEnumerable<Guid> itemIds)
    {
        var ids = itemIds.Distinct().ToList();
        var lookup = ids.ToDictionary(id => id, _ => new List<KnowledgePermissionScope>());
        if (ids.Count == 0)
        {
            return lookup;
        }

        var queryable = await _permissionScopeRepository.GetQueryableAsync();
        var scopes = await AsyncExecuter.ToListAsync(queryable.Where(x => ids.Contains(x.KnowledgeItemId) && x.IsEnabled));

        foreach (var scope in scopes)
        {
            lookup[scope.KnowledgeItemId].Add(scope);
        }

        return lookup;
    }

    /// <summary>
    /// 根据当前登录用户组装知识访问上下文。
    /// 组织关系提供部门、岗位和安全等级，Identity 角色提供角色维度授权。
    /// </summary>
    private async Task<KnowledgeAccessContext> CreateAccessContextAsync()
    {
        if (!CurrentUser.IsAuthenticated || CurrentUser.Id == null)
        {
            return new KnowledgeAccessContext(null);
        }

        var userId = CurrentUser.Id.Value;
        var assignmentQueryable = await _assignmentRepository.GetQueryableAsync();
        var assignments = await AsyncExecuter.ToListAsync(assignmentQueryable.Where(x => x.UserId == userId && x.IsActive));
        var departmentIds = assignments.Select(x => x.DepartmentId).ToHashSet();
        var positionIds = assignments.Where(x => x.PositionId.HasValue).Select(x => x.PositionId!.Value).ToHashSet();
        var securityLevel = assignments.Count == 0 ? 0 : assignments.Max(x => x.SecurityLevel);
        var roleIds = await GetCurrentRoleIdsAsync();

        return new KnowledgeAccessContext(userId, departmentIds, positionIds, roleIds, securityLevel);
    }

    /// <summary>
    /// 将当前用户的角色名称解析为角色编号，便于和知识权限范围中的角色目标编号匹配。
    /// </summary>
    private async Task<IReadOnlySet<Guid>> GetCurrentRoleIdsAsync()
    {
        var roleNames = CurrentUser.Roles?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();

        if (roleNames.Length == 0)
        {
            return new HashSet<Guid>();
        }

        var roles = await _roleRepository.GetListAsync(roleNames);
        return roles.Select(x => x.Id).ToHashSet();
    }

    /// <summary>
    /// 读取版本并确认该版本属于指定知识，防止调用方用其他知识的版本编号越界操作。
    /// </summary>
    private async Task<KnowledgeVersion> GetVersionOfItemAsync(Guid itemId, Guid versionId)
    {
        var version = await _versionRepository.GetAsync(versionId);
        if (version.KnowledgeItemId != itemId)
        {
            throw new EntityNotFoundException(typeof(KnowledgeVersion), versionId);
        }

        return version;
    }

    /// <summary>
    /// 读取附件并确认该附件属于指定版本，防止跨版本下载或删除。
    /// </summary>
    private async Task<KnowledgeAttachment> GetAttachmentOfVersionAsync(Guid versionId, Guid attachmentId)
    {
        var attachment = await _attachmentRepository.GetAsync(attachmentId);
        if (attachment.KnowledgeVersionId != versionId)
        {
            throw new EntityNotFoundException(typeof(KnowledgeAttachment), attachmentId);
        }

        return attachment;
    }

    /// <summary>
    /// 生成对象存储中的附件路径。路径中包含租户、知识、版本和附件编号，便于排查和后续清理。
    /// </summary>
    private string BuildAttachmentBlobName(Guid itemId, Guid versionId, Guid attachmentId, string fileName)
    {
        var tenantSegment = CurrentTenant.Id?.ToString("N") ?? "host";
        return $"tenants/{tenantSegment}/knowledge-items/{itemId:N}/versions/{versionId:N}/attachments/{attachmentId:N}/{fileName}";
    }

    /// <summary>
    /// 归一化上传文件名：去掉路径信息、替换非法字符，并按领域模型限制截断长度。
    /// </summary>
    private static string NormalizeFileName(string? fileName)
    {
        var normalized = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "attachment";
        }

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            normalized = normalized.Replace(invalidChar, '_');
        }

        if (normalized.Length <= KnowledgeAttachment.MaxFileNameLength)
        {
            return normalized;
        }

        var extension = Path.GetExtension(normalized);
        var maxNameLength = KnowledgeAttachment.MaxFileNameLength - extension.Length;
        if (maxNameLength <= 0)
        {
            return normalized[..KnowledgeAttachment.MaxFileNameLength];
        }

        return normalized[..maxNameLength] + extension;
    }

    /// <summary>
    /// 归一化内容类型。浏览器未提供类型时使用二进制默认类型。
    /// </summary>
    private static string NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return "application/octet-stream";
        }

        return contentType.Length <= KnowledgeAttachment.MaxContentTypeLength
            ? contentType
            : contentType[..KnowledgeAttachment.MaxContentTypeLength];
    }

    /// <summary>
    /// 读取上传内容长度。无法从请求头读取时，尽量使用可寻址流的长度兜底。
    /// </summary>
    private static long GetContentLength(IRemoteStreamContent file, Stream stream)
    {
        if (file.ContentLength.HasValue)
        {
            return file.ContentLength.Value;
        }

        return stream.CanSeek ? stream.Length : 0;
    }

    /// <summary>
    /// 将知识文档领域对象映射为应用层 DTO。当前未使用 AutoMapper，保持映射逻辑显式可审计。
    /// </summary>
    private static KnowledgeItemDto MapToDto(KnowledgeItem item)
    {
        return new KnowledgeItemDto
        {
            Id = item.Id,
            CreationTime = item.CreationTime,
            CreatorId = item.CreatorId,
            LastModificationTime = item.LastModificationTime,
            LastModifierId = item.LastModifierId,
            IsDeleted = item.IsDeleted,
            DeleterId = item.DeleterId,
            DeletionTime = item.DeletionTime,
            CategoryId = item.CategoryId,
            CurrentPublishedVersionId = item.CurrentPublishedVersionId,
            Title = item.Title,
            Summary = item.Summary,
            IsActive = item.IsActive
        };
    }

    /// <summary>
    /// 将知识版本领域对象映射为应用层 DTO。
    /// </summary>
    private static KnowledgeVersionDto MapToDto(KnowledgeVersion version)
    {
        return new KnowledgeVersionDto
        {
            Id = version.Id,
            CreationTime = version.CreationTime,
            CreatorId = version.CreatorId,
            LastModificationTime = version.LastModificationTime,
            LastModifierId = version.LastModifierId,
            IsDeleted = version.IsDeleted,
            DeleterId = version.DeleterId,
            DeletionTime = version.DeletionTime,
            KnowledgeItemId = version.KnowledgeItemId,
            VersionNumber = version.VersionNumber,
            Title = version.Title,
            ContentText = version.ContentText,
            Status = version.Status,
            IsCurrentPublished = version.IsCurrentPublished,
            PublishedTime = version.PublishedTime,
            WithdrawnTime = version.WithdrawnTime,
            ArchivedTime = version.ArchivedTime,
            DisabledTime = version.DisabledTime
        };
    }

    /// <summary>
    /// 将附件元数据领域对象映射为应用层 DTO，不包含文件流内容。
    /// </summary>
    private static KnowledgeAttachmentDto MapToDto(KnowledgeAttachment attachment)
    {
        return new KnowledgeAttachmentDto
        {
            Id = attachment.Id,
            CreationTime = attachment.CreationTime,
            CreatorId = attachment.CreatorId,
            LastModificationTime = attachment.LastModificationTime,
            LastModifierId = attachment.LastModifierId,
            IsDeleted = attachment.IsDeleted,
            DeleterId = attachment.DeleterId,
            DeletionTime = attachment.DeletionTime,
            KnowledgeVersionId = attachment.KnowledgeVersionId,
            FileName = attachment.FileName,
            ContentType = attachment.ContentType,
            Size = attachment.Size
        };
    }

    /// <summary>
    /// 将知识权限范围领域对象映射为应用层 DTO。
    /// </summary>
    private static KnowledgePermissionScopeDto MapToDto(KnowledgePermissionScope scope)
    {
        return new KnowledgePermissionScopeDto
        {
            Id = scope.Id,
            CreationTime = scope.CreationTime,
            CreatorId = scope.CreatorId,
            LastModificationTime = scope.LastModificationTime,
            LastModifierId = scope.LastModifierId,
            IsDeleted = scope.IsDeleted,
            DeleterId = scope.DeleterId,
            DeletionTime = scope.DeletionTime,
            KnowledgeItemId = scope.KnowledgeItemId,
            ScopeType = scope.ScopeType,
            TargetId = scope.TargetId,
            MinimumSecurityLevel = scope.MinimumSecurityLevel,
            IsEnabled = scope.IsEnabled
        };
    }
}
