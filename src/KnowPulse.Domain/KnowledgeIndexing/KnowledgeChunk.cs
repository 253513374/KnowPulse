using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.KnowledgeIndexing;

/// <summary>
/// 知识分块。chunk 是 RAG 检索和 citation 的最小业务可追溯单元，
/// 必须能回到知识条目、知识版本、索引任务和原始来源位置。
/// </summary>
public class KnowledgeChunk : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public const int MaxContentLength = 8192;
    public const int MaxContentHashLength = 128;
    public const int MaxSourceTitleLength = 256;

    public Guid? TenantId { get; protected set; }

    public Guid KnowledgeItemId { get; private set; }

    public Guid KnowledgeVersionId { get; private set; }

    public Guid KnowledgeIndexJobId { get; private set; }

    public int Sequence { get; private set; }

    public string Content { get; private set; } = string.Empty;

    public string ContentHash { get; private set; } = string.Empty;

    public string SourceTitle { get; private set; } = string.Empty;

    public int? SourcePage { get; private set; }

    public int TokenCount { get; private set; }

    public bool IsActive { get; private set; }

    public bool IsPublishedSnapshot { get; private set; }

    public int SecurityLevelSnapshot { get; private set; }

    /// <summary>
    /// EF Core 使用的受保护构造函数。
    /// </summary>
    protected KnowledgeChunk()
    {
    }

    /// <summary>
    /// 创建一个知识分块快照。
    /// 分块保存发布状态和安全等级快照，后续检索仍会回主库校验当前知识状态和权限。
    /// </summary>
    public KnowledgeChunk(
        Guid id,
        Guid? tenantId,
        Guid knowledgeItemId,
        Guid knowledgeVersionId,
        Guid knowledgeIndexJobId,
        int sequence,
        string content,
        string contentHash,
        string sourceTitle,
        int? sourcePage = null,
        int tokenCount = 0,
        bool isPublishedSnapshot = true,
        int securityLevelSnapshot = 0)
        : base(id)
    {
        if (sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        if (sourcePage is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sourcePage));
        }

        if (tokenCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tokenCount));
        }

        if (securityLevelSnapshot < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(securityLevelSnapshot));
        }

        TenantId = tenantId;
        KnowledgeItemId = knowledgeItemId;
        KnowledgeVersionId = knowledgeVersionId;
        KnowledgeIndexJobId = knowledgeIndexJobId;
        Sequence = sequence;
        Content = Check.NotNullOrWhiteSpace(content, nameof(content), MaxContentLength);
        ContentHash = Check.NotNullOrWhiteSpace(contentHash, nameof(contentHash), MaxContentHashLength);
        SourceTitle = Check.NotNullOrWhiteSpace(sourceTitle, nameof(sourceTitle), MaxSourceTitleLength);
        SourcePage = sourcePage;
        TokenCount = tokenCount;
        IsPublishedSnapshot = isPublishedSnapshot;
        SecurityLevelSnapshot = securityLevelSnapshot;
        IsActive = true;
    }

    /// <summary>
    /// 将分块停用，但保留其历史内容，供旧 citation 和审计回放定位。
    /// </summary>
    public void Deactivate()
    {
        IsActive = false;
    }

    /// <summary>
    /// 标记分块已过期。
    /// 版本撤回、归档、重新索引或权限变化时使用该状态，避免旧 chunk 继续参与 RAG。
    /// </summary>
    public void MarkOutdated()
    {
        IsActive = false;
        IsPublishedSnapshot = false;
    }
}
