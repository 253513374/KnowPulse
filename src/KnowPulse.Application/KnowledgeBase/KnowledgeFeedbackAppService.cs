using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.KnowledgeIndexing;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace KnowPulse.KnowledgeBase;

/// <summary>
/// 知识反馈应用服务，负责用户提交纠错建议和管理员处理反馈闭环。
/// 处理反馈只维护反馈状态、处理结论和关联版本，不直接修改已发布知识正文或发布状态。
/// </summary>
[Authorize]
public class KnowledgeFeedbackAppService : KnowPulseAppService, IKnowledgeFeedbackAppService
{
    private readonly IRepository<KnowledgeFeedback, Guid> _feedbackRepository;
    private readonly IRepository<KnowledgeItem, Guid> _itemRepository;
    private readonly IRepository<KnowledgeLibrary, Guid> _libraryRepository;
    private readonly IRepository<KnowledgeVersion, Guid> _versionRepository;
    private readonly IRepository<KnowledgeIndexJob, Guid> _indexJobRepository;
    private readonly IRepository<KnowledgePermissionScope, Guid> _permissionScopeRepository;
    private readonly IKnowledgeAccessContextProvider _accessContextProvider;

    public KnowledgeFeedbackAppService(
        IRepository<KnowledgeFeedback, Guid> feedbackRepository,
        IRepository<KnowledgeItem, Guid> itemRepository,
        IRepository<KnowledgeLibrary, Guid> libraryRepository,
        IRepository<KnowledgeVersion, Guid> versionRepository,
        IRepository<KnowledgeIndexJob, Guid> indexJobRepository,
        IRepository<KnowledgePermissionScope, Guid> permissionScopeRepository,
        IKnowledgeAccessContextProvider accessContextProvider)
    {
        _feedbackRepository = feedbackRepository;
        _itemRepository = itemRepository;
        _libraryRepository = libraryRepository;
        _versionRepository = versionRepository;
        _indexJobRepository = indexJobRepository;
        _permissionScopeRepository = permissionScopeRepository;
        _accessContextProvider = accessContextProvider;
    }

    /// <summary>
    /// 当前用户提交知识反馈。提交前会校验知识可见性，避免对不可访问知识构造反馈。
    /// </summary>
    /// <param name="input">反馈提交输入。</param>
    /// <returns>提交后的反馈记录。</returns>
    [Authorize]
    public virtual async Task<KnowledgeFeedbackDto> SubmitAsync(SubmitKnowledgeFeedbackDto input)
    {
        var submitterUserId = GetCurrentUserId();
        var item = await GetVisibleItemAsync(input.KnowledgeItemId);
        await EnsureSourceVersionBelongsToItemAsync(item.Id, input.KnowledgeVersionId);

        var feedback = await _feedbackRepository.InsertAsync(
            new KnowledgeFeedback(
                GuidGenerator.Create(),
                CurrentTenant.Id,
                item.Id,
                input.KnowledgeVersionId,
                submitterUserId,
                input.Type,
                input.Title.Trim(),
                input.Description.Trim()),
            autoSave: true);

        return await MapToDtoAsync(feedback);
    }

    /// <summary>
    /// 管理端分页查询知识反馈。
    /// </summary>
    /// <param name="input">反馈筛选和分页条件。</param>
    /// <returns>反馈分页结果。</returns>
    [Authorize]
    public virtual async Task<PagedResultDto<KnowledgeFeedbackDto>> GetListAsync(GetKnowledgeFeedbackListInput input)
    {
        var queryable = await GetPermittedFeedbackQueryAsync();
        queryable = ApplyFilters(queryable, input);

        var totalCount = await AsyncExecuter.CountAsync(queryable);
        var feedbacks = await AsyncExecuter.ToListAsync(queryable
            .OrderByDescending(x => x.CreationTime)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount));

        return new PagedResultDto<KnowledgeFeedbackDto>(
            totalCount,
            await MapToDtosAsync(feedbacks));
    }

    /// <summary>
    /// 管理端查询单条反馈详情。
    /// </summary>
    /// <param name="id">反馈 ID。</param>
    /// <returns>反馈详情。</returns>
    [Authorize]
    public virtual async Task<KnowledgeFeedbackDto> GetAsync(Guid id)
    {
        return await MapToDtoAsync(await GetAuthorizedFeedbackAsync(id));
    }

    /// <summary>
    /// 管理员接手处理反馈。
    /// </summary>
    /// <param name="id">反馈 ID。</param>
    /// <param name="input">接手处理输入。</param>
    /// <returns>处理后的反馈详情。</returns>
    [Authorize]
    public virtual async Task<KnowledgeFeedbackDto> ProcessAsync(Guid id, ProcessKnowledgeFeedbackDto input)
    {
        var feedback = await GetAuthorizedFeedbackAsync(id);
        feedback.StartProcessing(GetCurrentUserId(), DateTime.UtcNow, TrimToNull(input.Resolution));
        feedback = await _feedbackRepository.UpdateAsync(feedback, autoSave: true);
        return await MapToDtoAsync(feedback);
    }

    /// <summary>
    /// 从已接手反馈创建修订草稿，并在同一工作单元内自动关联反馈。
    /// 新草稿复制来源版本正文；附件不自动复制，避免旧附件被误认为已完成修订。
    /// </summary>
    [Authorize]
    [UnitOfWork]
    public virtual async Task<KnowledgeFeedbackDto> CreateRevisionAsync(Guid id)
    {
        var feedback = await GetAuthorizedFeedbackAsync(id);
        var item = await _itemRepository.GetAsync(feedback.KnowledgeItemId);
        await CheckKnowledgeItemPermissionAsync(item, KnowPulsePermissions.KnowledgeBase.Versions);
        if (feedback.Status != KnowledgeFeedbackStatus.Processing)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.KnowledgeFeedbackRevisionRequiresProcessing);
        }

        if (feedback.LinkedKnowledgeVersionId.HasValue)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.KnowledgeFeedbackRevisionAlreadyLinked);
        }

        var queryable = await _versionRepository.GetQueryableAsync();
        var versions = await AsyncExecuter.ToListAsync(
            queryable.Where(x => x.KnowledgeItemId == item.Id).OrderByDescending(x => x.VersionNumber));
        var sourceVersion = feedback.KnowledgeVersionId.HasValue
            ? versions.FirstOrDefault(x => x.Id == feedback.KnowledgeVersionId.Value)
            : versions.FirstOrDefault(x => x.Id == item.CurrentPublishedVersionId) ?? versions.FirstOrDefault();
        var nextVersionNumber = versions.Count == 0 ? 1 : versions.Max(x => x.VersionNumber) + 1;

        var revision = await _versionRepository.InsertAsync(
            new KnowledgeVersion(
                GuidGenerator.Create(),
                CurrentTenant.Id,
                item.Id,
                nextVersionNumber,
                sourceVersion?.Title ?? item.Title,
                sourceVersion?.ContentText),
            autoSave: false);

        feedback.LinkKnowledgeVersion(
            GetCurrentUserId(),
            DateTime.UtcNow,
            revision.Id,
            feedback.Resolution ?? "已从反馈创建修订草稿");
        feedback = await _feedbackRepository.UpdateAsync(feedback, autoSave: true);

        return await MapToDtoAsync(feedback);
    }

    /// <summary>
    /// 管理员标记反馈已处理，可关联同一知识下的修订版本。
    /// </summary>
    /// <param name="id">反馈 ID。</param>
    /// <param name="input">完成处理输入。</param>
    /// <returns>已处理的反馈详情。</returns>
    [Authorize]
    public virtual async Task<KnowledgeFeedbackDto> ResolveAsync(Guid id, ResolveKnowledgeFeedbackDto input)
    {
        var feedback = await GetAuthorizedFeedbackAsync(id);
        var linkedVersionId = feedback.LinkedKnowledgeVersionId;
        if (!linkedVersionId.HasValue)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.KnowledgeFeedbackRevisionRequired);
        }

        var linkedVersion = await EnsureLinkedVersionBelongsToFeedbackItemAsync(feedback, linkedVersionId);
        if (linkedVersion.Status != KnowledgeVersionStatus.Published || !linkedVersion.IsCurrentPublished)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.KnowledgeFeedbackRevisionNotPublished);
        }

        var indexQueryable = await _indexJobRepository.GetQueryableAsync();
        var latestIndexJob = await AsyncExecuter.FirstOrDefaultAsync(indexQueryable
            .Where(x => x.KnowledgeVersionId == linkedVersion.Id)
            .OrderByDescending(x => x.CreationTime));
        if (latestIndexJob?.Status != KnowledgeIndexJobStatus.Indexed)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.KnowledgeFeedbackRevisionNotIndexed);
        }

        feedback.Resolve(
            GetCurrentUserId(),
            DateTime.UtcNow,
            input.Resolution.Trim(),
            linkedVersion.Id);
        feedback = await _feedbackRepository.UpdateAsync(feedback, autoSave: true);

        return await MapToDtoAsync(feedback);
    }

    /// <summary>
    /// 管理员关闭反馈。
    /// </summary>
    /// <param name="id">反馈 ID。</param>
    /// <param name="input">关闭输入。</param>
    /// <returns>关闭后的反馈详情。</returns>
    [Authorize]
    public virtual async Task<KnowledgeFeedbackDto> CloseAsync(Guid id, CloseKnowledgeFeedbackDto input)
    {
        var feedback = await GetAuthorizedFeedbackAsync(id);
        feedback.Close(GetCurrentUserId(), DateTime.UtcNow, TrimToNull(input.CloseReason));
        feedback = await _feedbackRepository.UpdateAsync(feedback, autoSave: true);
        return await MapToDtoAsync(feedback);
    }

    /// <summary>
    /// 管理员为反馈关联同一知识下的修订版本。
    /// </summary>
    /// <param name="id">反馈 ID。</param>
    /// <param name="input">关联版本输入。</param>
    /// <returns>关联后的反馈详情。</returns>
    [Authorize]
    public virtual async Task<KnowledgeFeedbackDto> LinkVersionAsync(Guid id, LinkKnowledgeFeedbackVersionDto input)
    {
        var feedback = await GetAuthorizedFeedbackAsync(id);
        await EnsureLinkedVersionBelongsToFeedbackItemAsync(feedback, input.LinkedKnowledgeVersionId);

        feedback.LinkKnowledgeVersion(
            GetCurrentUserId(),
            DateTime.UtcNow,
            input.LinkedKnowledgeVersionId,
            TrimToNull(input.Resolution));
        feedback = await _feedbackRepository.UpdateAsync(feedback, autoSave: true);

        return await MapToDtoAsync(feedback);
    }

    private IQueryable<KnowledgeFeedback> ApplyFilters(IQueryable<KnowledgeFeedback> queryable, GetKnowledgeFeedbackListInput input)
    {
        if (input.KnowledgeItemId.HasValue)
        {
            queryable = queryable.Where(x => x.KnowledgeItemId == input.KnowledgeItemId.Value);
        }

        if (input.KnowledgeVersionId.HasValue)
        {
            queryable = queryable.Where(x => x.KnowledgeVersionId == input.KnowledgeVersionId.Value);
        }

        if (input.SubmitterUserId.HasValue)
        {
            queryable = queryable.Where(x => x.SubmitterUserId == input.SubmitterUserId.Value);
        }

        if (input.Type.HasValue)
        {
            queryable = queryable.Where(x => x.Type == input.Type.Value);
        }

        if (input.Status.HasValue)
        {
            queryable = queryable.Where(x => x.Status == input.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(input.Keyword))
        {
            var keyword = input.Keyword.Trim();
            queryable = queryable.Where(x =>
                x.Title.Contains(keyword) ||
                x.Description.Contains(keyword) ||
                (x.Resolution != null && x.Resolution.Contains(keyword)));
        }

        return queryable;
    }

    private async Task<KnowledgeItem> GetVisibleItemAsync(Guid id)
    {
        var item = await _itemRepository.GetAsync(id);
        await CheckKnowledgeItemPermissionAsync(item, KnowPulsePermissions.KnowledgeBase.Items);
        var scopes = await GetEnabledScopesAsync(id);
        var accessContext = await _accessContextProvider.CreateAsync();

        if (!KnowledgeAccessEvaluator.CanAccess(item, scopes, accessContext))
        {
            throw new AbpAuthorizationException("The current user is not allowed to access this knowledge item.");
        }

        return item;
    }

    private async Task<KnowledgeFeedback> GetAuthorizedFeedbackAsync(Guid id)
    {
        var feedback = await _feedbackRepository.GetAsync(id);
        var item = await _itemRepository.GetAsync(feedback.KnowledgeItemId);
        await CheckKnowledgeItemPermissionAsync(item, KnowPulsePermissions.KnowledgeBase.Feedback);
        if (feedback.TenantId != item.TenantId)
        {
            throw new AbpAuthorizationException("The feedback does not belong to the knowledge item's tenant.");
        }

        return feedback;
    }

    private async Task<IQueryable<KnowledgeFeedback>> GetPermittedFeedbackQueryAsync()
    {
        var canManageOrganization = await IsMvpPermissionGrantedAsync(KnowPulsePermissions.KnowledgeBase.Feedback);
        var canManagePersonal = await IsMvpPermissionGrantedAsync(KnowPulsePermissions.KnowledgeBase.PersonalLibrary);
        if (!canManageOrganization && !canManagePersonal)
        {
            throw new AbpAuthorizationException("The current user is not allowed to manage knowledge feedback.");
        }

        var userId = CurrentUser.Id;
        var tenantId = CurrentTenant.Id;
        var libraries = await _libraryRepository.GetQueryableAsync();
        var items = await _itemRepository.GetQueryableAsync();
        var permittedItems = items.Where(item => item.TenantId == tenantId &&
            ((canManageOrganization && !item.LibraryId.HasValue && !item.OwnerUserId.HasValue) ||
             (canManagePersonal && userId.HasValue && item.LibraryId.HasValue && item.OwnerUserId == userId &&
              libraries.Any(library => library.Id == item.LibraryId.Value &&
                  library.OwnerUserId == userId.Value && library.TenantId == item.TenantId))));
        var feedback = await _feedbackRepository.GetQueryableAsync();
        return feedback.Where(entry => permittedItems.Any(item => item.Id == entry.KnowledgeItemId && item.TenantId == entry.TenantId));
    }

    private async Task<List<KnowledgePermissionScope>> GetEnabledScopesAsync(Guid itemId)
    {
        var queryable = await _permissionScopeRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(queryable.Where(x => x.KnowledgeItemId == itemId && x.IsEnabled));
    }

    private async Task EnsureSourceVersionBelongsToItemAsync(Guid itemId, Guid? versionId)
    {
        if (!versionId.HasValue)
        {
            return;
        }

        var version = await _versionRepository.GetAsync(versionId.Value);
        if (version.KnowledgeItemId != itemId)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.KnowledgeFeedbackVersionItemMismatch);
        }
    }

    private async Task<KnowledgeVersion> EnsureLinkedVersionBelongsToFeedbackItemAsync(KnowledgeFeedback feedback, Guid? linkedVersionId)
    {
        if (!linkedVersionId.HasValue)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.KnowledgeFeedbackRevisionRequired);
        }

        var version = await _versionRepository.GetAsync(linkedVersionId.Value);
        if (version.KnowledgeItemId != feedback.KnowledgeItemId)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.KnowledgeFeedbackVersionItemMismatch);
        }

        return version;
    }

    private async Task<List<KnowledgeFeedbackDto>> MapToDtosAsync(IReadOnlyCollection<KnowledgeFeedback> feedbacks)
    {
        var result = new List<KnowledgeFeedbackDto>();
        foreach (var feedback in feedbacks)
        {
            result.Add(await MapToDtoAsync(feedback));
        }

        return result;
    }

    private async Task<KnowledgeFeedbackDto> MapToDtoAsync(KnowledgeFeedback feedback)
    {
        var item = await _itemRepository.FindAsync(feedback.KnowledgeItemId);
        var sourceVersion = feedback.KnowledgeVersionId.HasValue
            ? await FindFeedbackVersionAsync(feedback.KnowledgeItemId, feedback.KnowledgeVersionId.Value)
            : null;
        var linkedVersion = feedback.LinkedKnowledgeVersionId.HasValue
            ? await FindFeedbackVersionAsync(feedback.KnowledgeItemId, feedback.LinkedKnowledgeVersionId.Value)
            : null;
        KnowledgeIndexJob? linkedVersionLatestIndexJob = null;
        if (linkedVersion != null)
        {
            var indexQueryable = await _indexJobRepository.GetQueryableAsync();
            linkedVersionLatestIndexJob = await AsyncExecuter.FirstOrDefaultAsync(indexQueryable
                .Where(x => x.KnowledgeVersionId == linkedVersion.Id)
                .OrderByDescending(x => x.CreationTime));
        }

        return new KnowledgeFeedbackDto
        {
            Id = feedback.Id,
            CreationTime = feedback.CreationTime,
            CreatorId = feedback.CreatorId,
            LastModificationTime = feedback.LastModificationTime,
            LastModifierId = feedback.LastModifierId,
            IsDeleted = feedback.IsDeleted,
            DeleterId = feedback.DeleterId,
            DeletionTime = feedback.DeletionTime,
            KnowledgeItemId = feedback.KnowledgeItemId,
            KnowledgeTitle = item?.Title ?? feedback.KnowledgeItemId.ToString("N"),
            KnowledgeVersionId = feedback.KnowledgeVersionId,
            KnowledgeVersionTitle = sourceVersion?.Title,
            KnowledgeVersionNumber = sourceVersion?.VersionNumber,
            SubmitterUserId = feedback.SubmitterUserId,
            Type = feedback.Type,
            Status = feedback.Status,
            Title = feedback.Title,
            Description = feedback.Description,
            HandlerUserId = feedback.HandlerUserId,
            HandledTime = feedback.HandledTime,
            Resolution = feedback.Resolution,
            LinkedKnowledgeVersionId = feedback.LinkedKnowledgeVersionId,
            LinkedKnowledgeVersionTitle = linkedVersion?.Title,
            LinkedKnowledgeVersionNumber = linkedVersion?.VersionNumber,
            LinkedKnowledgeVersionStatus = linkedVersion?.Status,
            LinkedKnowledgeVersionIsCurrentPublished = linkedVersion?.IsCurrentPublished == true,
            LinkedKnowledgeVersionIndexStatus = linkedVersionLatestIndexJob?.Status,
            ClosedTime = feedback.ClosedTime,
            CloseReason = feedback.CloseReason
        };
    }

    private async Task<KnowledgeVersion?> FindFeedbackVersionAsync(Guid itemId, Guid versionId)
    {
        var versions = await _versionRepository.GetQueryableAsync();
        return await AsyncExecuter.FirstOrDefaultAsync(versions.Where(version => version.Id == versionId && version.KnowledgeItemId == itemId));
    }

    private Guid GetCurrentUserId()
    {
        if (!CurrentUser.Id.HasValue)
        {
            throw new AbpAuthorizationException("Authentication is required for this operation.");
        }

        return CurrentUser.Id.Value;
    }

    private static string? TrimToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
