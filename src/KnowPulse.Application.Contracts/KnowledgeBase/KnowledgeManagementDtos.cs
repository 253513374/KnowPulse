using System;
using System.Collections.Generic;
using KnowPulse.KnowledgeIndexing;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.KnowledgeBase;

/// <summary>
/// 知识管理列表查询条件。
/// </summary>
public class GetKnowledgeManagementListInput : PagedAndSortedResultRequestDto
{
    public bool OwnOnly { get; set; }
    public bool SharedOnly { get; set; }
    /// <summary>
    /// 按标题、摘要或分类名称搜索。
    /// </summary>
    public string? Keyword { get; set; }

    /// <summary>
    /// 按管理端合成状态筛选。
    /// </summary>
    public KnowledgeManagementStatus? Status { get; set; }
}

/// <summary>
/// 知识管理列表顶部的状态概览。
/// </summary>
public class KnowledgeManagementOverviewDto
{
    public int TotalCount { get; set; }
    public int ReadyCount { get; set; }
    public int NeedsAttentionCount { get; set; }
    public int DraftCount { get; set; }
    public int PublishingCount { get; set; }
}

/// <summary>
/// 知识管理列表查询结果。
/// </summary>
public class KnowledgeManagementListResultDto
{
    public long TotalCount { get; set; }
    public IReadOnlyList<KnowledgeManagementListItemDto> Items { get; set; } = [];
    public KnowledgeManagementOverviewDto Overview { get; set; } = new();
}

/// <summary>
/// 面向知识管理员的聚合列表项。
/// </summary>
public class KnowledgeManagementListItemDto : EntityDto<Guid>
{
    public Guid? OwnerTeacherId { get; set; }
    public Guid? CategoryId { get; set; }
    public string CategoryName { get; set; } = "未分类";
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public bool IsActive { get; set; }
    public Guid? CreatorId { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime UpdatedTime { get; set; }

    public Guid? CurrentVersionId { get; set; }
    public int? CurrentVersionNumber { get; set; }
    public string? CurrentVersionTitle { get; set; }
    public KnowledgeVersionStatus? CurrentVersionStatus { get; set; }
    public DateTime? CurrentVersionPublishedTime { get; set; }
    public int VersionCount { get; set; }

    public KnowledgeManagementStatus Status { get; set; }
    public string StatusReason { get; set; } = string.Empty;

    public bool IsAccessRestricted { get; set; }
    public bool HasExplicitAccessPolicy { get; set; }
    public string AccessScopeText { get; set; } = "全员";
    public int EnabledPermissionScopeCount { get; set; }

    public Guid? LatestIndexJobId { get; set; }
    public KnowledgeIndexJobStatus? LatestIndexStatus { get; set; }
    public string? LatestIndexFailureStage { get; set; }
    public string? LatestIndexFailureReason { get; set; }
    public DateTime? LatestIndexTime { get; set; }

    public int PendingFeedbackCount { get; set; }
}

/// <summary>
/// 知识详情中的精简反馈记录。
/// </summary>
public class KnowledgeManagementFeedbackDto : EntityDto<Guid>
{
    public KnowledgeFeedbackType Type { get; set; }
    public KnowledgeFeedbackStatus Status { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Resolution { get; set; }
    public Guid? LinkedKnowledgeVersionId { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? HandledTime { get; set; }
}

/// <summary>
/// 由知识、版本、索引和反馈事实投影出的活动记录。
/// </summary>
public class KnowledgeManagementActivityDto
{
    public string Kind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime OccurredTime { get; set; }
}

/// <summary>
/// 以单个知识条目为上下文的管理详情。
/// </summary>
public class KnowledgeManagementDetailDto : KnowledgeManagementListItemDto
{
    public int AttachmentCount { get; set; }
    public IReadOnlyList<KnowledgeVersionDto> Versions { get; set; } = [];
    public IReadOnlyList<KnowledgePermissionScopeDto> PermissionScopes { get; set; } = [];
    public IReadOnlyList<KnowledgeIndexJobDto> IndexJobs { get; set; } = [];
    public IReadOnlyList<KnowledgeManagementFeedbackDto> RecentFeedback { get; set; } = [];
    public IReadOnlyList<KnowledgeManagementActivityDto> RecentActivities { get; set; } = [];
}
