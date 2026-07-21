using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.KnowledgeIndexing;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;

namespace KnowPulse.KnowledgeBase;

/// <summary>
/// 以知识条目为中心聚合分类、版本、权限、索引和反馈信息。
/// 该服务是管理列表和详情页的唯一读入口，不改变任何领域状态。
/// </summary>
[Authorize(KnowPulsePermissions.KnowledgeBase.Items)]
public class KnowledgeManagementAppService : KnowPulseAppService, IKnowledgeManagementAppService
{
    private readonly IRepository<KnowledgeItem, Guid> _itemRepository;
    private readonly IRepository<KnowledgeCategory, Guid> _categoryRepository;
    private readonly IRepository<KnowledgeVersion, Guid> _versionRepository;
    private readonly IRepository<KnowledgeAttachment, Guid> _attachmentRepository;
    private readonly IRepository<KnowledgePermissionScope, Guid> _permissionScopeRepository;
    private readonly IRepository<KnowledgeIndexJob, Guid> _indexJobRepository;
    private readonly IRepository<KnowledgeFeedback, Guid> _feedbackRepository;
    private readonly IKnowledgeAccessContextProvider _accessContextProvider;

    public KnowledgeManagementAppService(
        IRepository<KnowledgeItem, Guid> itemRepository,
        IRepository<KnowledgeCategory, Guid> categoryRepository,
        IRepository<KnowledgeVersion, Guid> versionRepository,
        IRepository<KnowledgeAttachment, Guid> attachmentRepository,
        IRepository<KnowledgePermissionScope, Guid> permissionScopeRepository,
        IRepository<KnowledgeIndexJob, Guid> indexJobRepository,
        IRepository<KnowledgeFeedback, Guid> feedbackRepository,
        IKnowledgeAccessContextProvider accessContextProvider)
    {
        _itemRepository = itemRepository;
        _categoryRepository = categoryRepository;
        _versionRepository = versionRepository;
        _attachmentRepository = attachmentRepository;
        _permissionScopeRepository = permissionScopeRepository;
        _indexJobRepository = indexJobRepository;
        _feedbackRepository = feedbackRepository;
        _accessContextProvider = accessContextProvider;
    }

    /// <inheritdoc />
    public virtual async Task<KnowledgeManagementListResultDto> GetListAsync(GetKnowledgeManagementListInput input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Items);

        var snapshot = await LoadSnapshotAsync();
        var visibleItems = await GetVisibleItemsAsync(snapshot.Items, snapshot.Scopes);
        var models = visibleItems
            .Select(item => BuildListItem(
                item,
                snapshot.Categories,
                snapshot.Versions,
                snapshot.Scopes,
                snapshot.IndexJobs,
                snapshot.Feedback))
            .ToList();

        if (!string.IsNullOrWhiteSpace(input.Keyword))
        {
            var keyword = input.Keyword.Trim();
            models = models
                .Where(x =>
                    x.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    (x.Summary?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    x.CategoryName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var overview = BuildOverview(models);

        if (input.Status.HasValue)
        {
            models = models.Where(x => x.Status == input.Status.Value).ToList();
        }

        models = ApplySorting(models, input.Sorting);
        var pagedItems = models
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount)
            .ToList();

        return new KnowledgeManagementListResultDto
        {
            TotalCount = models.Count,
            Items = pagedItems,
            Overview = overview
        };
    }

    /// <inheritdoc />
    public virtual async Task<KnowledgeManagementDetailDto> GetAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Items);

        var item = await _itemRepository.GetAsync(id);
        var scopes = await LoadScopesAsync([id]);
        var accessContext = await _accessContextProvider.CreateAsync();
        if (!KnowledgeAccessEvaluator.CanAccess(scopes, accessContext))
        {
            throw new AbpAuthorizationException("The current user is not allowed to access this knowledge item.");
        }

        var category = item.CategoryId.HasValue
            ? await _categoryRepository.FindAsync(item.CategoryId.Value)
            : null;
        var versions = await LoadVersionsAsync([id]);
        var versionIds = versions.Select(x => x.Id).ToList();
        var indexJobs = await LoadIndexJobsAsync([id]);
        var feedback = await LoadFeedbackAsync([id]);
        var attachments = await LoadAttachmentsAsync(versionIds);

        var listItem = BuildListItem(
            item,
            category == null ? [] : [category],
            versions,
            scopes,
            indexJobs,
            feedback);

        return BuildDetail(listItem, versions, scopes, indexJobs, feedback, attachments);
    }

    private async Task<ManagementSnapshot> LoadSnapshotAsync()
    {
        var itemQueryable = await _itemRepository.GetQueryableAsync();
        var items = await AsyncExecuter.ToListAsync(itemQueryable);
        var itemIds = items.Select(x => x.Id).ToList();

        if (itemIds.Count == 0)
        {
            return new ManagementSnapshot([], [], [], [], [], []);
        }

        var categoryIds = items
            .Where(x => x.CategoryId.HasValue)
            .Select(x => x.CategoryId!.Value)
            .Distinct()
            .ToList();

        var categories = new List<KnowledgeCategory>();
        if (categoryIds.Count > 0)
        {
            var categoryQueryable = await _categoryRepository.GetQueryableAsync();
            categories = await AsyncExecuter.ToListAsync(categoryQueryable.Where(x => categoryIds.Contains(x.Id)));
        }

        return new ManagementSnapshot(
            items,
            categories,
            await LoadVersionsAsync(itemIds),
            await LoadScopesAsync(itemIds),
            await LoadIndexJobsAsync(itemIds),
            await LoadFeedbackAsync(itemIds));
    }

    private async Task<List<KnowledgeItem>> GetVisibleItemsAsync(
        IReadOnlyCollection<KnowledgeItem> items,
        IReadOnlyCollection<KnowledgePermissionScope> scopes)
    {
        var scopesByItem = scopes
            .GroupBy(x => x.KnowledgeItemId)
            .ToDictionary(x => x.Key, x => x.ToList());
        var accessContext = await _accessContextProvider.CreateAsync();

        return items
            .Where(item => KnowledgeAccessEvaluator.CanAccess(
                scopesByItem.GetValueOrDefault(item.Id) ?? [],
                accessContext))
            .ToList();
    }

    private async Task<List<KnowledgeVersion>> LoadVersionsAsync(IReadOnlyCollection<Guid> itemIds)
    {
        if (itemIds.Count == 0)
        {
            return [];
        }

        var queryable = await _versionRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(queryable.Where(x => itemIds.Contains(x.KnowledgeItemId)));
    }

    private async Task<List<KnowledgePermissionScope>> LoadScopesAsync(IReadOnlyCollection<Guid> itemIds)
    {
        if (itemIds.Count == 0)
        {
            return [];
        }

        var queryable = await _permissionScopeRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(
            queryable.Where(x => itemIds.Contains(x.KnowledgeItemId) && x.IsEnabled));
    }

    private async Task<List<KnowledgeIndexJob>> LoadIndexJobsAsync(IReadOnlyCollection<Guid> itemIds)
    {
        if (itemIds.Count == 0)
        {
            return [];
        }

        var queryable = await _indexJobRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(queryable.Where(x => itemIds.Contains(x.KnowledgeItemId)));
    }

    private async Task<List<KnowledgeFeedback>> LoadFeedbackAsync(IReadOnlyCollection<Guid> itemIds)
    {
        if (itemIds.Count == 0)
        {
            return [];
        }

        var queryable = await _feedbackRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(queryable.Where(x => itemIds.Contains(x.KnowledgeItemId)));
    }

    private async Task<List<KnowledgeAttachment>> LoadAttachmentsAsync(IReadOnlyCollection<Guid> versionIds)
    {
        if (versionIds.Count == 0)
        {
            return [];
        }

        var queryable = await _attachmentRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(queryable.Where(x => versionIds.Contains(x.KnowledgeVersionId)));
    }

    private static KnowledgeManagementListItemDto BuildListItem(
        KnowledgeItem item,
        IReadOnlyCollection<KnowledgeCategory> categories,
        IReadOnlyCollection<KnowledgeVersion> allVersions,
        IReadOnlyCollection<KnowledgePermissionScope> allScopes,
        IReadOnlyCollection<KnowledgeIndexJob> allIndexJobs,
        IReadOnlyCollection<KnowledgeFeedback> allFeedback)
    {
        var versions = allVersions
            .Where(x => x.KnowledgeItemId == item.Id)
            .OrderByDescending(x => x.VersionNumber)
            .ToList();
        var latestVersion = versions.FirstOrDefault();
        var currentVersion = item.CurrentPublishedVersionId.HasValue
            ? versions.FirstOrDefault(x => x.Id == item.CurrentPublishedVersionId.Value)
            : null;
        var scopes = allScopes.Where(x => x.KnowledgeItemId == item.Id && x.IsEnabled).ToList();
        var latestIndexJob = currentVersion == null
            ? null
            : allIndexJobs
                .Where(x => x.KnowledgeVersionId == currentVersion.Id)
                .OrderByDescending(GetIndexJobTime)
                .ThenByDescending(x => x.Id)
                .FirstOrDefault();
        var status = KnowledgeManagementStatusResolver.Resolve(
            item.IsActive,
            currentVersion?.Status,
            latestVersion?.Status,
            latestIndexJob?.Status);

        return new KnowledgeManagementListItemDto
        {
            Id = item.Id,
            CategoryId = item.CategoryId,
            CategoryName = categories.FirstOrDefault(x => x.Id == item.CategoryId)?.Name ?? "未分类",
            Title = item.Title,
            Summary = item.Summary,
            IsActive = item.IsActive,
            CreatorId = item.CreatorId,
            CreationTime = item.CreationTime,
            UpdatedTime = item.LastModificationTime ?? item.CreationTime,
            CurrentVersionId = currentVersion?.Id,
            CurrentVersionNumber = currentVersion?.VersionNumber,
            CurrentVersionTitle = currentVersion?.Title,
            CurrentVersionStatus = currentVersion?.Status,
            CurrentVersionPublishedTime = currentVersion?.PublishedTime,
            VersionCount = versions.Count,
            Status = status,
            StatusReason = GetStatusReason(status, latestIndexJob),
            IsAccessRestricted = IsAccessRestricted(scopes),
            HasExplicitAccessPolicy = scopes.Count > 0,
            AccessScopeText = BuildAccessScopeText(scopes),
            EnabledPermissionScopeCount = scopes.Count,
            LatestIndexJobId = latestIndexJob?.Id,
            LatestIndexStatus = latestIndexJob?.Status,
            LatestIndexFailureStage = latestIndexJob?.FailureStage,
            LatestIndexFailureReason = latestIndexJob?.FailureReason,
            LatestIndexTime = latestIndexJob == null ? null : GetIndexJobTime(latestIndexJob),
            PendingFeedbackCount = allFeedback.Count(x =>
                x.KnowledgeItemId == item.Id &&
                (x.Status == KnowledgeFeedbackStatus.Submitted ||
                 x.Status == KnowledgeFeedbackStatus.Processing))
        };
    }

    private static KnowledgeManagementDetailDto BuildDetail(
        KnowledgeManagementListItemDto item,
        IReadOnlyCollection<KnowledgeVersion> versions,
        IReadOnlyCollection<KnowledgePermissionScope> scopes,
        IReadOnlyCollection<KnowledgeIndexJob> indexJobs,
        IReadOnlyCollection<KnowledgeFeedback> feedback,
        IReadOnlyCollection<KnowledgeAttachment> attachments)
    {
        var versionDtos = versions
            .OrderByDescending(x => x.VersionNumber)
            .Select(MapVersion)
            .ToList();
        var indexJobDtos = indexJobs
            .OrderByDescending(GetIndexJobTime)
            .Select(MapIndexJob)
            .ToList();
        var feedbackDtos = feedback
            .OrderByDescending(x => x.CreationTime)
            .Take(10)
            .Select(MapFeedback)
            .ToList();

        return new KnowledgeManagementDetailDto
        {
            Id = item.Id,
            CategoryId = item.CategoryId,
            CategoryName = item.CategoryName,
            Title = item.Title,
            Summary = item.Summary,
            IsActive = item.IsActive,
            CreatorId = item.CreatorId,
            CreationTime = item.CreationTime,
            UpdatedTime = item.UpdatedTime,
            CurrentVersionId = item.CurrentVersionId,
            CurrentVersionNumber = item.CurrentVersionNumber,
            CurrentVersionTitle = item.CurrentVersionTitle,
            CurrentVersionStatus = item.CurrentVersionStatus,
            CurrentVersionPublishedTime = item.CurrentVersionPublishedTime,
            VersionCount = item.VersionCount,
            Status = item.Status,
            StatusReason = item.StatusReason,
            IsAccessRestricted = item.IsAccessRestricted,
            HasExplicitAccessPolicy = item.HasExplicitAccessPolicy,
            AccessScopeText = item.AccessScopeText,
            EnabledPermissionScopeCount = item.EnabledPermissionScopeCount,
            LatestIndexJobId = item.LatestIndexJobId,
            LatestIndexStatus = item.LatestIndexStatus,
            LatestIndexFailureStage = item.LatestIndexFailureStage,
            LatestIndexFailureReason = item.LatestIndexFailureReason,
            LatestIndexTime = item.LatestIndexTime,
            PendingFeedbackCount = item.PendingFeedbackCount,
            AttachmentCount = attachments.Count,
            Versions = versionDtos,
            PermissionScopes = scopes.OrderBy(x => x.ScopeType).Select(MapScope).ToList(),
            IndexJobs = indexJobDtos,
            RecentFeedback = feedbackDtos,
            RecentActivities = BuildActivities(item, versions, indexJobs, feedback)
        };
    }

    private static KnowledgeManagementOverviewDto BuildOverview(IReadOnlyCollection<KnowledgeManagementListItemDto> items)
    {
        return new KnowledgeManagementOverviewDto
        {
            TotalCount = items.Count,
            ReadyCount = items.Count(x => x.Status == KnowledgeManagementStatus.Ready),
            NeedsAttentionCount = items.Count(x => x.Status == KnowledgeManagementStatus.NeedsAttention),
            DraftCount = items.Count(x => x.Status == KnowledgeManagementStatus.Draft),
            PublishingCount = items.Count(x => x.Status == KnowledgeManagementStatus.Publishing)
        };
    }

    private static List<KnowledgeManagementListItemDto> ApplySorting(
        List<KnowledgeManagementListItemDto> items,
        string? sorting)
    {
        return sorting?.Trim().ToLowerInvariant() switch
        {
            "title" or "title asc" => items.OrderBy(x => x.Title).ToList(),
            "title desc" => items.OrderByDescending(x => x.Title).ToList(),
            "creationtime" or "creationtime asc" => items.OrderBy(x => x.CreationTime).ToList(),
            "creationtime desc" => items.OrderByDescending(x => x.CreationTime).ToList(),
            _ => items.OrderByDescending(x => x.UpdatedTime).ThenBy(x => x.Title).ToList()
        };
    }

    private static bool IsAccessRestricted(IReadOnlyCollection<KnowledgePermissionScope> scopes)
    {
        return scopes.Count > 0 && scopes.All(x => x.ScopeType != KnowledgePermissionScopeType.All);
    }

    private static string BuildAccessScopeText(IReadOnlyCollection<KnowledgePermissionScope> scopes)
    {
        if (scopes.Count == 0 || scopes.Any(x => x.ScopeType == KnowledgePermissionScopeType.All))
        {
            if (scopes.Count == 0)
            {
                return "全员（兼容规则）";
            }

            var minimumSecurityLevel = scopes
                .Where(x => x.ScopeType == KnowledgePermissionScopeType.All && x.MinimumSecurityLevel.HasValue)
                .Select(x => x.MinimumSecurityLevel)
                .FirstOrDefault();
            return minimumSecurityLevel.HasValue
                ? $"全员 · 安全等级 ≥ {minimumSecurityLevel.Value}"
                : "全员";
        }

        var parts = scopes
            .GroupBy(x => x.ScopeType)
            .OrderBy(x => x.Key)
            .Select(x => $"{GetScopeTypeText(x.Key)} {x.Count()} 个");
        return string.Join("、", parts);
    }

    private static string GetScopeTypeText(KnowledgePermissionScopeType type)
    {
        return type switch
        {
            KnowledgePermissionScopeType.Department => "部门",
            KnowledgePermissionScopeType.Position => "岗位",
            KnowledgePermissionScopeType.Role => "角色",
            KnowledgePermissionScopeType.User => "用户",
            _ => "全员"
        };
    }

    private static string GetStatusReason(
        KnowledgeManagementStatus status,
        KnowledgeIndexJob? latestIndexJob)
    {
        return status switch
        {
            KnowledgeManagementStatus.Draft => "尚未发布可用版本",
            KnowledgeManagementStatus.Publishing when latestIndexJob?.Status == KnowledgeIndexJobStatus.Pending
                => "索引任务等待处理",
            KnowledgeManagementStatus.Publishing => "正在构建问答索引",
            KnowledgeManagementStatus.Ready => "当前版本已发布并完成索引",
            KnowledgeManagementStatus.NeedsAttention when latestIndexJob == null
                => "已发布版本缺少索引任务",
            KnowledgeManagementStatus.NeedsAttention when latestIndexJob.Status == KnowledgeIndexJobStatus.Failed
                => string.IsNullOrWhiteSpace(latestIndexJob.FailureStage)
                    ? "索引失败"
                    : $"索引在 {latestIndexJob.FailureStage} 阶段失败",
            KnowledgeManagementStatus.NeedsAttention when latestIndexJob.Status == KnowledgeIndexJobStatus.Outdated
                => "索引结果已过期",
            KnowledgeManagementStatus.NeedsAttention when latestIndexJob.Status == KnowledgeIndexJobStatus.Deleted
                => "索引结果已删除",
            KnowledgeManagementStatus.Withdrawn => "最近版本已下架",
            KnowledgeManagementStatus.Archived => "最近版本已归档",
            KnowledgeManagementStatus.Disabled => "知识已停用",
            _ => "需要管理员检查"
        };
    }

    private static DateTime GetIndexJobTime(KnowledgeIndexJob job)
    {
        return job.CompletedTime ??
               job.StartedTime ??
               job.LastModificationTime ??
               job.CreationTime;
    }

    private static IReadOnlyList<KnowledgeManagementActivityDto> BuildActivities(
        KnowledgeManagementListItemDto item,
        IEnumerable<KnowledgeVersion> versions,
        IEnumerable<KnowledgeIndexJob> indexJobs,
        IEnumerable<KnowledgeFeedback> feedback)
    {
        var activities = new List<KnowledgeManagementActivityDto>
        {
            new()
            {
                Kind = "Created",
                Title = "创建知识",
                Description = item.Title,
                OccurredTime = item.CreationTime
            }
        };

        activities.AddRange(versions.Select(x => new KnowledgeManagementActivityDto
        {
            Kind = "Version",
            Title = x.PublishedTime.HasValue ? $"发布 v{x.VersionNumber}" : $"创建 v{x.VersionNumber} 草稿",
            Description = x.Title,
            OccurredTime = x.PublishedTime ?? x.CreationTime
        }));
        activities.AddRange(indexJobs.Select(x => new KnowledgeManagementActivityDto
        {
            Kind = "Index",
            Title = $"索引任务：{GetIndexStatusText(x.Status)}",
            Description = x.FailureReason,
            OccurredTime = GetIndexJobTime(x)
        }));
        activities.AddRange(feedback.Select(x => new KnowledgeManagementActivityDto
        {
            Kind = "Feedback",
            Title = $"知识反馈：{x.Title}",
            Description = x.Resolution,
            OccurredTime = x.HandledTime ?? x.CreationTime
        }));

        return activities
            .OrderByDescending(x => x.OccurredTime)
            .Take(10)
            .ToList();
    }

    private static string GetIndexStatusText(KnowledgeIndexJobStatus status)
    {
        return status switch
        {
            KnowledgeIndexJobStatus.Pending => "待处理",
            KnowledgeIndexJobStatus.Processing => "处理中",
            KnowledgeIndexJobStatus.Indexed => "已索引",
            KnowledgeIndexJobStatus.Failed => "失败",
            KnowledgeIndexJobStatus.Outdated => "已过期",
            KnowledgeIndexJobStatus.Deleted => "已删除",
            _ => status.ToString()
        };
    }

    private static KnowledgeVersionDto MapVersion(KnowledgeVersion version)
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

    private static KnowledgePermissionScopeDto MapScope(KnowledgePermissionScope scope)
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

    private static KnowledgeIndexJobDto MapIndexJob(KnowledgeIndexJob job)
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
            TriggerType = job.TriggerType,
            Status = job.Status,
            RetryCount = job.RetryCount,
            MaxRetryCount = job.MaxRetryCount,
            FailureStage = job.FailureStage,
            FailureReason = job.FailureReason,
            StartedTime = job.StartedTime,
            CompletedTime = job.CompletedTime,
            NextRetryTime = job.NextRetryTime
        };
    }

    private static KnowledgeManagementFeedbackDto MapFeedback(KnowledgeFeedback feedback)
    {
        return new KnowledgeManagementFeedbackDto
        {
            Id = feedback.Id,
            Type = feedback.Type,
            Status = feedback.Status,
            Title = feedback.Title,
            Description = feedback.Description,
            Resolution = feedback.Resolution,
            LinkedKnowledgeVersionId = feedback.LinkedKnowledgeVersionId,
            CreationTime = feedback.CreationTime,
            HandledTime = feedback.HandledTime
        };
    }

    private sealed record ManagementSnapshot(
        IReadOnlyList<KnowledgeItem> Items,
        IReadOnlyList<KnowledgeCategory> Categories,
        IReadOnlyList<KnowledgeVersion> Versions,
        IReadOnlyList<KnowledgePermissionScope> Scopes,
        IReadOnlyList<KnowledgeIndexJob> IndexJobs,
        IReadOnlyList<KnowledgeFeedback> Feedback);
}
