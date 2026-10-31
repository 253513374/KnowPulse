using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using KnowPulse.KnowledgeIndexing;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace KnowPulse.KnowledgeBase;

/// <summary>
/// 知识文档应用服务，负责知识条目、版本、附件和权限范围的 MVP-0 后端闭环。
/// 该服务是页面和 REST API 的业务入口，会在每个操作中显式执行权限检查，
/// 并在读取知识时叠加知识权限范围，避免仅依赖前端菜单隐藏。
/// </summary>
[Authorize]
public class KnowledgeItemAppService : KnowPulseAppService, IKnowledgeItemAppService
{
    private readonly IRepository<KnowledgeItem, Guid> _itemRepository;
    private readonly IRepository<KnowledgeVersion, Guid> _versionRepository;
    private readonly IRepository<KnowledgeAttachment, Guid> _attachmentRepository;
    private readonly IRepository<KnowledgePermissionScope, Guid> _permissionScopeRepository;
    private readonly IRepository<KnowledgeIndexJob, Guid> _indexJobRepository;
    private readonly IRepository<KnowledgeChunk, Guid> _chunkRepository;
    private readonly IKnowledgeAccessContextProvider _accessContextProvider;
    private readonly IBlobContainer<KnowledgeAttachmentBlobContainer> _attachmentContainer;

    /// <summary>
    /// 创建知识文档应用服务。
    /// </summary>
    public KnowledgeItemAppService(
        IRepository<KnowledgeItem, Guid> itemRepository,
        IRepository<KnowledgeVersion, Guid> versionRepository,
        IRepository<KnowledgeAttachment, Guid> attachmentRepository,
        IRepository<KnowledgePermissionScope, Guid> permissionScopeRepository,
        IRepository<KnowledgeIndexJob, Guid> indexJobRepository,
        IRepository<KnowledgeChunk, Guid> chunkRepository,
        IKnowledgeAccessContextProvider accessContextProvider,
        IBlobContainer<KnowledgeAttachmentBlobContainer> attachmentContainer)
    {
        _itemRepository = itemRepository;
        _versionRepository = versionRepository;
        _attachmentRepository = attachmentRepository;
        _permissionScopeRepository = permissionScopeRepository;
        _indexJobRepository = indexJobRepository;
        _chunkRepository = chunkRepository;
        _accessContextProvider = accessContextProvider;
        _attachmentContainer = attachmentContainer;
    }

    /// <summary>
    /// 分页获取当前用户可见的知识文档。
    /// 这里先读取 MVP-0 范围内的知识集合，再按知识权限范围进行内存过滤；
    /// 后续数据量上来后可以把粗过滤下推到数据库查询。
    /// </summary>
    public virtual async Task<PagedResultDto<KnowledgeItemDto>> GetListAsync(PagedAndSortedResultRequestDto input)
    {
        var canManageOrganization = await IsMvpPermissionGrantedAsync(KnowPulsePermissions.KnowledgeBase.Items);
        var canManagePersonal = await IsMvpPermissionGrantedAsync(KnowPulsePermissions.KnowledgeBase.PersonalLibrary);
        if (!canManageOrganization && !canManagePersonal)
        {
            throw new AbpAuthorizationException("Knowledge access permission is required.");
        }
        var queryable = await _itemRepository.GetQueryableAsync();
        var libraries = await LazyServiceProvider.LazyGetRequiredService<IRepository<KnowledgeLibrary, Guid>>().GetQueryableAsync();
        queryable = queryable.Where(x =>
            (canManageOrganization && !x.LibraryId.HasValue && !x.OwnerUserId.HasValue) ||
            (canManagePersonal && x.LibraryId.HasValue && x.OwnerUserId == CurrentUser.Id && x.TenantId == CurrentTenant.Id &&
             libraries.Any(library => !library.IsDeleted && library.Id == x.LibraryId &&
                 library.OwnerUserId == x.OwnerUserId && library.TenantId == x.TenantId)));
        var allItems = await AsyncExecuter.ToListAsync(queryable.OrderBy(x => x.Title));
        var scopesByItemId = await GetScopeLookupAsync(allItems.Select(x => x.Id));
        var accessContext = await _accessContextProvider.CreateAsync();

        var visibleItems = allItems
            .Where(item => KnowledgeAccessEvaluator.CanAccess(item, scopesByItemId[item.Id], accessContext))
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
        return MapToDto(await GetVisibleItemAsync(id));
    }

    /// <summary>
    /// 创建知识文档。新建文档只包含元信息，正文需要通过创建版本单独维护。
    /// </summary>
    public virtual async Task<KnowledgeItemDto> CreateAsync(CreateKnowledgeItemDto input)
    {
        KnowledgeItem item;
        if (input.LibraryId.HasValue)
        {
            await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.PersonalLibrary);
            var library = await LazyServiceProvider.LazyGetRequiredService<IRepository<KnowledgeLibrary, Guid>>()
                .GetAsync(input.LibraryId.Value);
            if (library.OwnerUserId != CurrentUser.Id || library.TenantId != CurrentTenant.Id)
            {
                throw new AbpAuthorizationException("Only the current user's personal library can be used.");
            }
            item = new KnowledgeItem(GuidGenerator.Create(), CurrentTenant.Id, input.Title,
                input.CategoryId, input.Summary, library.Id, library.OwnerUserId);
        }
        else
        {
            await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Items);
            item = new KnowledgeItem(GuidGenerator.Create(), CurrentTenant.Id, input.Title, input.CategoryId, input.Summary);
        }
        await ValidatePersonalCategoryAsync(item, input.CategoryId);
        if (input.Organization != null)
        {
            if (!item.LibraryId.HasValue) throw new BusinessException(PersonalKnowledgeOrganizationErrorCodes.InvalidOrganization);
            await LazyServiceProvider.LazyGetRequiredService<KnowledgeLibraryAppService>().ApplyOrganizationAsync(item, input.Organization);
        }
        return MapToDto(await _itemRepository.InsertAsync(item, autoSave: true));
    }

    /// <summary>
    /// 更新知识文档的标题、摘要、分类和启用状态。
    /// </summary>
    public virtual async Task<KnowledgeItemDto> UpdateAsync(Guid id, UpdateKnowledgeItemDto input)
    {
        var item = await GetItemForOperationAsync(id, KnowPulsePermissions.KnowledgeBase.Items);
        await ValidatePersonalCategoryAsync(item, input.CategoryId);
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
        var item = await GetItemForOperationAsync(id, KnowPulsePermissions.KnowledgeBase.Items);
        await MarkItemIndexOutdatedAsync(id, "Knowledge item deleted.");
        await _itemRepository.DeleteAsync(item);
    }

    /// <summary>
    /// 获取指定知识文档的版本列表。调用前会先校验知识本身是否对当前用户可见。
    /// </summary>
    public virtual async Task<ListResultDto<KnowledgeVersionDto>> GetVersionsAsync(Guid id)
    {
        await GetVisibleItemAsync(id);

        var queryable = await _versionRepository.GetQueryableAsync();
        var versions = await AsyncExecuter.ToListAsync(queryable.Where(x => x.KnowledgeItemId == id).OrderByDescending(x => x.VersionNumber));

        return new ListResultDto<KnowledgeVersionDto>(versions.Select(MapToDto).ToList());
    }

    /// <summary>
    /// 为知识文档创建新版本。版本号按该知识已有版本数量递增，初始状态为草稿。
    /// </summary>
    public virtual async Task<KnowledgeVersionDto> CreateVersionAsync(Guid id, CreateKnowledgeVersionDto input)
    {
        var item = await GetItemForOperationAsync(id, KnowPulsePermissions.KnowledgeBase.Versions);
        return MapToDto(await AppendVersionAsync(item, GuidGenerator.Create(), input.Title, input.ContentText));
    }

    internal async Task<KnowledgeVersion> AppendVersionAsync(KnowledgeItem item, Guid versionId, string title, string? content)
    {
        // Legacy and notebook saves share the item barrier and never reuse a soft-deleted version number.
        if (item.LibraryId.HasValue) await _itemRepository.UpdateManyAsync([item], autoSave: true);
        using var deleted = LazyServiceProvider.LazyGetRequiredService<IDataFilter>().Disable<ISoftDelete>();
        var query = (await _versionRepository.GetQueryableAsync()).Where(x => x.KnowledgeItemId == item.Id && x.TenantId == item.TenantId);
        var maximum = await AsyncExecuter.MaxAsync(query.Select(x => (int?)x.VersionNumber)) ?? 0;
        return await _versionRepository.InsertAsync(new KnowledgeVersion(versionId, item.TenantId, item.Id, checked(maximum + 1), title, content), autoSave: true);
    }

    /// <summary>
    /// 发布指定版本，并维护“同一知识只有一个当前发布版本”的业务规则。
    /// </summary>
    [UnitOfWork]
    public virtual async Task<KnowledgeVersionDto> PublishVersionAsync(Guid id, Guid versionId)
    {
        var item = await GetItemForOperationAsync(id, KnowPulsePermissions.KnowledgeBase.Versions);
        var version = await GetVersionOfItemAsync(id, versionId);
        version.EnsureCanPublish();

        // 个人资料准备可检索时显式持久化所有者策略；组织旧入口保留首次发布全员兼容。
        var scopes = await GetEnabledScopesAsync(id);
        if (item.LibraryId.HasValue)
        {
            await ReplacePermissionScopesAsync(item,
                [new KnowledgeAccessPolicyRule(KnowledgePermissionScopeType.User, item.OwnerUserId, null)]);
        }
        else if (scopes.Count == 0)
        {
            await _permissionScopeRepository.InsertAsync(
                new KnowledgePermissionScope(
                    GuidGenerator.Create(),
                    CurrentTenant.Id,
                    id,
                    KnowledgePermissionScopeType.All),
                autoSave: false);
        }

        var (updatedVersion, _) = await PublishVersionCoreAsync(item, version);
        return MapToDto(updatedVersion);
    }

    /// <summary>
    /// 使用显式访问策略发布知识版本。策略替换、当前版本切换、旧索引失效和新索引任务创建
    /// 在同一个工作单元中完成；任一步失败都会回滚发布事实。
    /// </summary>
    [UnitOfWork]
    public virtual async Task<PublishKnowledgeVersionResultDto> PublishVersionWithAccessPolicyAsync(
        Guid id,
        PublishKnowledgeVersionInput input)
    {
        var item = await GetItemForOperationAsync(id, KnowPulsePermissions.KnowledgeBase.Versions);
        await CheckKnowledgeItemPermissionAsync(item, KnowPulsePermissions.KnowledgeBase.PermissionScopes);

        var rules = input.PermissionScopes
            .Select(x => new KnowledgeAccessPolicyRule(x.ScopeType, x.TargetId, x.MinimumSecurityLevel))
            .ToList();
        KnowledgeAccessPolicyValidator.EnsureValidForItem(item, rules);
        var version = await GetVersionOfItemAsync(id, input.KnowledgeVersionId);
        version.EnsureCanPublish();
        var scopes = await ReplacePermissionScopesAsync(item, rules);
        var (updatedVersion, indexJob) = await PublishVersionCoreAsync(item, version);

        return new PublishKnowledgeVersionResultDto
        {
            Version = MapToDto(updatedVersion),
            PermissionScopes = scopes.Select(MapToDto).ToList(),
            IndexJob = MapToDto(indexJob)
        };
    }

    private async Task<(KnowledgeVersion Version, KnowledgeIndexJob IndexJob)> PublishVersionCoreAsync(
        KnowledgeItem item,
        KnowledgeVersion version)
    {
        version.EnsureCanPublish();
        var sharedVersions = await KnowledgeIndexVersionQuery.GetSharedVersionsAsync(LazyServiceProvider);
        var retainedVersionIds = await AsyncExecuter.ToListAsync(sharedVersions
            .Where(x => x.KnowledgeItemId == item.Id && x.Id != version.Id).Select(x => x.Id));
        await MarkItemIndexOutdatedAsync(item.Id, "Knowledge version or access policy republished.", retainedVersionIds);

        var queryable = await _versionRepository.GetQueryableAsync();
        var currentVersions = await AsyncExecuter.ToListAsync(queryable.Where(x => x.KnowledgeItemId == item.Id && x.IsCurrentPublished));

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
        var updatedVersion = await _versionRepository.UpdateAsync(version, autoSave: true);
        var indexJob = await CreateIndexJobAsync(item.Id, version.Id, KnowledgeIndexJobTriggerType.Publish);

        return (updatedVersion, indexJob);
    }

    /// <summary>
    /// 下架指定版本。如果该版本是知识文档当前发布版本，同步清空知识上的当前版本引用。
    /// </summary>
    public virtual async Task<KnowledgeVersionDto> WithdrawVersionAsync(Guid id, Guid versionId)
    {
        var item = await GetItemForOperationAsync(id, KnowPulsePermissions.KnowledgeBase.Versions);
        var version = await GetVersionOfItemAsync(id, versionId);

        version.Withdraw(DateTime.UtcNow);
        if (item.CurrentPublishedVersionId == version.Id)
        {
            item.SetCurrentPublishedVersion(null);
            await _itemRepository.UpdateAsync(item);
        }

        var updatedVersion = await _versionRepository.UpdateAsync(version, autoSave: true);
        await MarkVersionIndexOutdatedAsync(version.Id, "Version withdrawn.");

        return MapToDto(updatedVersion);
    }

    /// <summary>
    /// 归档指定版本。归档后不再作为当前发布版本参与访问。
    /// </summary>
    [UnitOfWork]
    public virtual async Task<KnowledgeVersionDto> ArchiveVersionAsync(Guid id, Guid versionId)
    {
        var item = await GetItemForOperationAsync(id, KnowPulsePermissions.KnowledgeBase.Versions);
        var version = await GetVersionOfItemAsync(id, versionId);

        version.Archive(DateTime.UtcNow);
        if (item.CurrentPublishedVersionId == version.Id)
        {
            item.SetCurrentPublishedVersion(null);
            await _itemRepository.UpdateAsync(item);
        }

        var updatedVersion = await _versionRepository.UpdateAsync(version, autoSave: true);
        await MarkVersionIndexOutdatedAsync(version.Id, "Version archived.");

        return MapToDto(updatedVersion);
    }

    /// <summary>
    /// 获取指定版本的附件列表。附件列表读取前同样校验知识可见性，避免通过版本编号绕过权限。
    /// </summary>
    public virtual async Task<ListResultDto<KnowledgeAttachmentDto>> GetAttachmentsAsync(Guid id, Guid versionId)
    {
        await GetVisibleItemAsync(id, KnowPulsePermissions.KnowledgeBase.Attachments);
        await GetVersionOfItemAsync(id, versionId);

        var queryable = await _attachmentRepository.GetQueryableAsync();
        var attachments = await AsyncExecuter.ToListAsync(
            queryable.Where(x => x.KnowledgeVersionId == versionId).OrderBy(x => x.FileName));

        return new ListResultDto<KnowledgeAttachmentDto>(attachments.Select(MapToDto).ToList());
    }

    /// <summary>
    /// 上传知识版本附件。文件内容写入 BlobStoring，数据库只保存附件元数据。
    /// </summary>
    public virtual Task<KnowledgeAttachmentDto> UploadAttachmentAsync(Guid id, Guid versionId, IRemoteStreamContent file)
        => UploadAttachmentCoreAsync(id, versionId, file);

    internal Task<KnowledgeAttachmentDto> UploadNotebookAttachmentAsync(Guid id, Guid versionId, Guid operationId, IRemoteStreamContent file, Func<KnowledgeAttachmentDto, Task> record)
        => UploadAttachmentCoreAsync(id, versionId, file, operationId, record);

    private async Task<KnowledgeAttachmentDto> UploadAttachmentCoreAsync(Guid id, Guid versionId, IRemoteStreamContent file,
        Guid? stableId = null, Func<KnowledgeAttachmentDto, Task>? record = null)
    {
        var item = await GetItemForOperationAsync(id, KnowPulsePermissions.KnowledgeBase.Attachments);
        Check.NotNull(file, nameof(file));
        var isPersonal = item.LibraryId.HasValue || item.OwnerUserId.HasValue;
        using var privateUow = isPersonal
            ? LazyServiceProvider.LazyGetRequiredService<IUnitOfWorkManager>().Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true)
            : null;
        if (isPersonal)
        {
            // 独立事务重新读取归属及版本，附件数量和版本并发标记共享一个提交边界。
            await GetItemForOperationAsync(id, KnowPulsePermissions.KnowledgeBase.Attachments);
        }
        var version = await GetVersionOfItemAsync(id, versionId);
        PersonalKnowledgeAttachmentOptions? limits = null;
        if (isPersonal)
        {
            version.EnsureCanChangePersonalAttachments();
            limits = LazyServiceProvider.LazyGetRequiredService<Microsoft.Extensions.Options.IOptions<PersonalKnowledgeAttachmentOptions>>().Value;
            limits.EnsureValid();
            if (file.ContentLength > limits.MaxFileBytes)
            {
                throw new BusinessException(KnowPulseDomainErrorCodes.PersonalKnowledgeAttachmentTooLarge)
                    .WithData("MaxFileBytes", limits.MaxFileBytes);
            }
            var attachments = await _attachmentRepository.GetQueryableAsync();
            if (await AsyncExecuter.CountAsync(attachments.Where(x => x.KnowledgeVersionId == versionId)) >= limits.MaxFilesPerVersion)
            {
                throw new BusinessException(KnowPulseDomainErrorCodes.PersonalKnowledgeAttachmentLimitReached)
                    .WithData("MaxFilesPerVersion", limits.MaxFilesPerVersion);
            }
        }

        var attachmentId = stableId ?? GuidGenerator.Create();
        var fileName = NormalizeFileName(file.FileName);
        var contentType = NormalizeContentType(file.ContentType);
        var blobName = BuildAttachmentBlobName(id, versionId, attachmentId, fileName);

        using var stream = file.GetStream();
        using var boundedContent = isPersonal ? new MemoryStream() : null;
        var size = isPersonal ? 0 : GetContentLength(file, stream);
        if (limits != null)
        {
            // 只读取上限加一个探测字节；不相信声明长度，超限时尚未向 Blob 写入任何内容。
            var buffer = new byte[81920];
            while (true)
            {
                var remaining = limits.MaxFileBytes - size;
                var readSize = remaining < buffer.Length ? (int)remaining + 1 : buffer.Length;
                var read = await stream.ReadAsync(buffer.AsMemory(0, readSize));
                if (read == 0)
                {
                    break;
                }
                if (read > remaining)
                {
                    throw new BusinessException(KnowPulseDomainErrorCodes.PersonalKnowledgeAttachmentTooLarge)
                        .WithData("MaxFileBytes", limits.MaxFileBytes);
                }
                await boundedContent!.WriteAsync(buffer.AsMemory(0, read));
                size += read;
            }
            boundedContent!.Position = 0;
            // 单个 UpdateAsync 不会将已跟踪且未改变的实体标成 Modified。
            // 显式更新该版本以轮换 ConcurrencyStamp，令竞争上传/发布在 Blob 写入前失败。
            await _versionRepository.UpdateManyAsync([version], autoSave: true);
        }

        var blobSaved = false;
        try
        {
            if (stableId.HasValue && await _attachmentContainer.ExistsAsync(blobName))
            {
                using var stored = await _attachmentContainer.GetAsync(blobName);
                var expected = SHA256.HashData(boundedContent!.GetBuffer().AsSpan(0, checked((int)boundedContent.Length)));
                using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var bytes = new byte[81920]; long length = 0;
                int read; while ((read = await stored.ReadAsync(bytes.AsMemory())) > 0) { length += read; if (length > size) break; digest.AppendData(bytes, 0, read); }
                if (length != size || !digest.GetHashAndReset().AsSpan().SequenceEqual(expected)) throw new BusinessException(PersonalNotebookErrors.OperationConflict);
            }
            else { await _attachmentContainer.SaveAsync(blobName, boundedContent ?? stream, overrideExisting: false); blobSaved = true; }
            var attachment = new KnowledgeAttachment(
                attachmentId, CurrentTenant.Id, versionId, fileName, blobName, contentType, size);
            var result = MapToDto(await _attachmentRepository.InsertAsync(attachment, autoSave: true));
            if (record != null) await record(result);
            if (privateUow != null)
            {
                await privateUow.CompleteAsync();
            }
            return result;
        }
        catch
        {
            if (isPersonal && blobSaved)
            {
                try
                {
                    await _attachmentContainer.DeleteAsync(blobName);
                }
                catch (Exception cleanupError)
                {
                    Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(Logger, cleanupError,
                        "Could not remove an uncommitted personal attachment blob {BlobName}.", blobName);
                }
            }
            throw;
        }
    }

    /// <summary>
    /// 下载指定附件。返回远程流内容，由 ABP 框架负责序列化给 HTTP 调用方。
    /// </summary>
    public virtual async Task<IRemoteStreamContent> DownloadAttachmentAsync(Guid id, Guid versionId, Guid attachmentId)
    {
        await GetVisibleItemAsync(id, KnowPulsePermissions.KnowledgeBase.Attachments);
        await GetVersionOfItemAsync(id, versionId);

        var attachment = await GetAttachmentOfVersionAsync(versionId, attachmentId);
        var stream = await _attachmentContainer.GetAsync(attachment.BlobName);

        return new RemoteStreamContent(stream, attachment.FileName, attachment.ContentType, attachment.Size, disposeStream: true);
    }

    /// <summary>
    /// 删除附件元数据，数据库提交成功后清理对应文件。
    /// </summary>
    public virtual async Task DeleteAttachmentAsync(Guid id, Guid versionId, Guid attachmentId)
    {
        var item = await GetItemForOperationAsync(id, KnowPulsePermissions.KnowledgeBase.Attachments);
        var isPersonal = item.LibraryId.HasValue || item.OwnerUserId.HasValue;
        using var privateUow = isPersonal
            ? LazyServiceProvider.LazyGetRequiredService<IUnitOfWorkManager>().Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true)
            : null;
        if (isPersonal)
        {
            await GetItemForOperationAsync(id, KnowPulsePermissions.KnowledgeBase.Attachments);
        }
        var version = await GetVersionOfItemAsync(id, versionId);
        if (isPersonal)
        {
            version.EnsureCanChangePersonalAttachments();
        }

        var attachment = await GetAttachmentOfVersionAsync(versionId, attachmentId);
        if (isPersonal)
        {
            // 强制已跟踪版本参与并发校验，不能让附件删除绕过同时发生的发布。
            await _versionRepository.UpdateManyAsync([version], autoSave: true);
        }
        var metadataUow = privateUow ?? LazyServiceProvider.LazyGetRequiredService<IUnitOfWorkManager>().Current
            ?? throw new InvalidOperationException("An active unit of work is required to delete an attachment.");
        await _attachmentRepository.DeleteAsync(attachment, autoSave: true);
        metadataUow.OnCompleted(async () =>
        {
            try
            {
                await _attachmentContainer.DeleteAsync(attachment.BlobName);
            }
            catch (Exception cleanupError)
            {
                // 元数据已提交，清理失败只留下不可下载的孤立文件，不能伪装成可回滚的删除失败。
                Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(Logger, cleanupError,
                    "Could not remove a committed deleted attachment blob {BlobName}.", attachment.BlobName);
            }
        });
        if (privateUow != null)
        {
            await privateUow.CompleteAsync();
        }
    }

    /// <summary>
    /// 获取知识文档的权限范围列表。该接口面向管理员配置，不按当前用户可见性过滤。
    /// </summary>
    public virtual async Task<ListResultDto<KnowledgePermissionScopeDto>> GetPermissionScopesAsync(Guid id)
    {
        await GetItemForOperationAsync(id, KnowPulsePermissions.KnowledgeBase.PermissionScopes);

        var queryable = await _permissionScopeRepository.GetQueryableAsync();
        var scopes = await AsyncExecuter.ToListAsync(queryable.Where(x => x.KnowledgeItemId == id).OrderBy(x => x.ScopeType));

        return new ListResultDto<KnowledgePermissionScopeDto>(scopes.Select(MapToDto).ToList());
    }

    /// <summary>
    /// 为知识文档新增权限范围，支持全员、部门、岗位、角色和指定用户。
    /// </summary>
    public virtual async Task<KnowledgePermissionScopeDto> CreatePermissionScopeAsync(Guid id, CreateKnowledgePermissionScopeDto input)
    {
        var item = await GetItemForOperationAsync(id, KnowPulsePermissions.KnowledgeBase.PermissionScopes);

        var targetId = input.ScopeType == KnowledgePermissionScopeType.All ? null : input.TargetId;
        var rule = new KnowledgeAccessPolicyRule(input.ScopeType, targetId, input.MinimumSecurityLevel);
        KnowledgeAccessPolicyValidator.EnsureValidForItem(item, [rule]);

        var existingScopes = await GetEnabledScopesAsync(id);
        // “全员”和定向范围是互斥模式。新增另一种模式时原子移除旧模式，
        // 避免全员规则继续生效而让管理员误以为已经限制访问；同一目标再次保存视为覆盖更新。
        var scopesToReplace = existingScopes.Where(x =>
            (input.ScopeType == KnowledgePermissionScopeType.All
                ? x.ScopeType != KnowledgePermissionScopeType.All
                : x.ScopeType == KnowledgePermissionScopeType.All) ||
            (x.ScopeType == input.ScopeType && x.TargetId == targetId));
        foreach (var conflictingScope in scopesToReplace)
        {
            await _permissionScopeRepository.DeleteAsync(conflictingScope, autoSave: false);
        }

        var scope = new KnowledgePermissionScope(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            id,
            input.ScopeType,
            targetId,
            input.MinimumSecurityLevel);

        var insertedScope = await _permissionScopeRepository.InsertAsync(scope, autoSave: true);
        await QueuePermissionChangedReindexAsync(item);

        return MapToDto(insertedScope);
    }

    /// <summary>
    /// 删除知识文档上的权限范围。传入的范围不属于当前知识时直接忽略，保持接口幂等。
    /// </summary>
    public virtual async Task DeletePermissionScopeAsync(Guid id, Guid scopeId)
    {
        var item = await GetItemForOperationAsync(id, KnowPulsePermissions.KnowledgeBase.PermissionScopes);
        var scope = await _permissionScopeRepository.GetAsync(scopeId);
        if (scope.KnowledgeItemId != id)
        {
            return;
        }

        if (item.LibraryId.HasValue && scope.IsEnabled)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.PersonalKnowledgeRequiresOwnerPolicy);
        }
        if (item.CurrentPublishedVersionId.HasValue && scope.IsEnabled)
        {
            var enabledScopes = await GetEnabledScopesAsync(id);
            if (enabledScopes.Count == 1)
            {
                throw new BusinessException(KnowPulseDomainErrorCodes.KnowledgeAccessPolicyRequired);
            }
        }

        await _permissionScopeRepository.DeleteAsync(scope);
        await QueuePermissionChangedReindexAsync(item);
    }

    /// <summary>
    /// 创建索引任务。任务作为异步 pipeline 的事实入口，实际 chunk/embedding 写入由后续处理器消费。
    /// </summary>
    private Task<KnowledgeIndexJob> CreateIndexJobAsync(
        Guid knowledgeItemId,
        Guid knowledgeVersionId,
        KnowledgeIndexJobTriggerType triggerType)
    {
        var job = new KnowledgeIndexJob(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            knowledgeItemId,
            knowledgeVersionId,
            triggerType,
            requestedByUserId: CurrentUser.Id);

        return _indexJobRepository.InsertAsync(job, autoSave: true);
    }

    /// <summary>
    /// 权限范围变更会让已写入向量库的 payload 变旧；先使旧 chunk 不可检索，再为当前发布版本创建重建任务。
    /// </summary>
    private async Task QueuePermissionChangedReindexAsync(KnowledgeItem item)
    {
        var sharedVersions = await KnowledgeIndexVersionQuery.GetSharedVersionsAsync(LazyServiceProvider);
        var retainedVersionIds = await AsyncExecuter.ToListAsync(sharedVersions
            .Where(x => x.KnowledgeItemId == item.Id && x.Id != item.CurrentPublishedVersionId).Select(x => x.Id));
        await MarkItemIndexOutdatedAsync(item.Id, "Permission scope changed.", retainedVersionIds);
        if (item.CurrentPublishedVersionId.HasValue)
        {
            await CreateIndexJobAsync(
                item.Id,
                item.CurrentPublishedVersionId.Value,
                KnowledgeIndexJobTriggerType.PermissionChanged);
        }
    }

    /// <summary>
    /// 将当前知识的访问范围整体替换为已经通过领域校验的策略快照。
    /// 删除和新增都留在当前工作单元中，不产生发布瞬间的半配置状态。
    /// </summary>
    private async Task<List<KnowledgePermissionScope>> ReplacePermissionScopesAsync(
        KnowledgeItem item,
        IReadOnlyCollection<KnowledgeAccessPolicyRule> rules)
    {
        // 先完整构造新策略，再触碰现有持久化状态；任何领域校验失败都不会留下半替换的跟踪实体。
        var scopes = rules.Select(rule => new KnowledgePermissionScope(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            item.Id,
            rule.ScopeType,
            rule.TargetId,
            rule.MinimumSecurityLevel)).ToList();

        var queryable = await _permissionScopeRepository.GetQueryableAsync();
        var existingScopes = await AsyncExecuter.ToListAsync(queryable.Where(x => x.KnowledgeItemId == item.Id));
        foreach (var existingScope in existingScopes)
        {
            await _permissionScopeRepository.DeleteAsync(existingScope, autoSave: false);
        }

        foreach (var scope in scopes)
        {
            await _permissionScopeRepository.InsertAsync(scope, autoSave: false);
        }

        return scopes;
    }

    /// <summary>
    /// 将指定知识版本的索引任务和 active chunk 标记为过期。
    /// </summary>
    private async Task MarkVersionIndexOutdatedAsync(Guid knowledgeVersionId, string reason)
    {
        var chunkQueryable = await _chunkRepository.GetQueryableAsync();
        var chunks = await AsyncExecuter.ToListAsync(
            chunkQueryable.Where(x => x.KnowledgeVersionId == knowledgeVersionId && x.IsActive));
        foreach (var chunk in chunks)
        {
            chunk.MarkOutdated();
            await _chunkRepository.UpdateAsync(chunk, autoSave: true);
        }

        var jobQueryable = await _indexJobRepository.GetQueryableAsync();
        var jobs = await AsyncExecuter.ToListAsync(
            jobQueryable.Where(x => x.KnowledgeVersionId == knowledgeVersionId)
                .Where(x => x.Status == KnowledgeIndexJobStatus.Pending ||
                            x.Status == KnowledgeIndexJobStatus.Processing ||
                            x.Status == KnowledgeIndexJobStatus.Indexed));
        foreach (var job in jobs)
        {
            job.MarkOutdated(reason);
            await _indexJobRepository.UpdateAsync(job, autoSave: true);
        }
    }

    /// <summary>
    /// 将指定知识条目的索引任务和 active chunk 标记为过期；另行授权的学校共享版本保留独立索引。
    /// </summary>
    private async Task MarkItemIndexOutdatedAsync(Guid knowledgeItemId, string reason,
        IReadOnlyCollection<Guid>? retainedVersionIds = null)
    {
        retainedVersionIds ??= Array.Empty<Guid>();
        var chunkQueryable = await _chunkRepository.GetQueryableAsync();
        var chunks = await AsyncExecuter.ToListAsync(
            chunkQueryable.Where(x => x.KnowledgeItemId == knowledgeItemId && x.IsActive &&
                !retainedVersionIds.Contains(x.KnowledgeVersionId)));
        foreach (var chunk in chunks)
        {
            chunk.MarkOutdated();
            await _chunkRepository.UpdateAsync(chunk, autoSave: true);
        }

        var jobQueryable = await _indexJobRepository.GetQueryableAsync();
        var jobs = await AsyncExecuter.ToListAsync(
            jobQueryable.Where(x => x.KnowledgeItemId == knowledgeItemId &&
                !retainedVersionIds.Contains(x.KnowledgeVersionId))
                .Where(x => x.Status == KnowledgeIndexJobStatus.Pending ||
                            x.Status == KnowledgeIndexJobStatus.Processing ||
                            x.Status == KnowledgeIndexJobStatus.Indexed));
        foreach (var job in jobs)
        {
            job.MarkOutdated(reason);
            await _indexJobRepository.UpdateAsync(job, autoSave: true);
        }
    }

    /// <summary>
    /// 获取当前用户可见的知识条目。
    /// </summary>
    private async Task ValidatePersonalCategoryAsync(KnowledgeItem item, Guid? categoryId)
    {
        if (!item.LibraryId.HasValue || !categoryId.HasValue) return;
        var category = await LazyServiceProvider.LazyGetRequiredService<IRepository<KnowledgeCategory, Guid>>()
            .FindAsync(categoryId.Value);
        if (category == null || category.TenantId != item.TenantId)
        {
            throw new AbpAuthorizationException("The category is unavailable in the personal library's tenant.");
        }
    }

    private async Task<KnowledgeItem> GetItemForOperationAsync(Guid id, string permission)
    {
        var item = await _itemRepository.GetAsync(id);
        await CheckKnowledgeItemPermissionAsync(item, permission);
        if (!item.LibraryId.HasValue && permission != KnowPulsePermissions.KnowledgeBase.Items)
        {
            await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Items);
        }
        return item;
    }

    private async Task<KnowledgeItem> GetVisibleItemAsync(Guid id, string permission = KnowPulsePermissions.KnowledgeBase.Items)
    {
        var item = await GetItemForOperationAsync(id, permission);
        var scopes = await GetEnabledScopesAsync(id);
        var accessContext = await _accessContextProvider.CreateAsync();

        // 知识权限范围是知识库的二次业务授权，不等同于 ABP 功能权限。
        // 用户必须同时拥有功能权限，并命中知识本身的可见范围。
        if (!KnowledgeAccessEvaluator.CanAccess(item, scopes, accessContext))
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
    internal static string NormalizeFileName(string? fileName)
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
    internal static string NormalizeContentType(string? contentType)
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
            LibraryId = item.LibraryId,
            OwnerUserId = item.OwnerUserId,
            ConcurrencyStamp = item.ConcurrencyStamp,
            MaterialType = item.MaterialType,
            MaterialYear = item.MaterialYear,
            SubjectName = item.SubjectName,
            CustomTags = item.GetPersonalTags().ToList(),
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

    /// <summary>
    /// 将发布动作创建的索引任务映射为结果 DTO。
    /// </summary>
    private static KnowledgeIndexJobDto MapToDto(KnowledgeIndexJob job)
    {
        return new KnowledgeIndexJobDto
        {
            Id = job.Id,
            CreationTime = job.CreationTime,
            CreatorId = job.CreatorId,
            LastModificationTime = job.LastModificationTime,
            LastModifierId = job.LastModifierId,
            IsDeleted = job.IsDeleted,
            DeleterId = job.DeleterId,
            DeletionTime = job.DeletionTime,
            KnowledgeItemId = job.KnowledgeItemId,
            KnowledgeVersionId = job.KnowledgeVersionId,
            EmbeddingModelVersionId = job.EmbeddingModelVersionId,
            RequestedByUserId = job.RequestedByUserId,
            RetryOfJobId = job.RetryOfJobId,
            TriggerType = job.TriggerType,
            Status = job.Status,
            RetryCount = job.RetryCount,
            MaxRetryCount = job.MaxRetryCount,
            FailureStage = job.FailureStage,
            FailureReason = job.FailureReason,
            StartedTime = job.StartedTime,
            CompletedTime = job.CompletedTime,
            NextRetryTime = job.NextRetryTime,
            ProcessingStage = job.ProcessingStage,
            ProgressPercent = job.ProgressPercent,
            LastHeartbeatTime = job.LastHeartbeatTime,
            CanRetry = job.CanRetry,
            IsStale = job.IsStale(DateTime.UtcNow, TimeSpan.FromMinutes(15))
        };
    }
}
