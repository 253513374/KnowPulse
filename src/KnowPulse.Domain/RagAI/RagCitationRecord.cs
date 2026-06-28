using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.RagAI;

/// <summary>
/// RAG citation 记录。用于把问答或搜索调用追溯回知识、版本和 chunk，
/// 仅保存短摘录和 hash，不保存完整 chunk 正文。
/// </summary>
public class RagCitationRecord : CreationAuditedEntity<Guid>, IMultiTenant
{
    public const int MaxSourceTitleLength = 256;
    public const int MaxQuoteHashLength = 64;
    public const int MaxQuotePreviewLength = 180;

    public Guid? TenantId { get; protected set; }

    public Guid RagCallLogId { get; private set; }

    public Guid KnowledgeItemId { get; private set; }

    public Guid KnowledgeVersionId { get; private set; }

    public Guid ChunkId { get; private set; }

    public int Rank { get; private set; }

    public double Score { get; private set; }

    public string SourceTitle { get; private set; } = string.Empty;

    public int? SourcePage { get; private set; }

    public string QuoteHash { get; private set; } = string.Empty;

    public string QuotePreview { get; private set; } = string.Empty;

    /// <summary>
    /// EF Core 使用的受保护构造函数。
    /// </summary>
    protected RagCitationRecord()
    {
    }

    /// <summary>
    /// 创建 RAG citation 审计记录。
    /// citation 只保存短摘录和 hash，完整知识正文仍通过 KnowledgeChunk / KnowledgeVersion 追溯。
    /// </summary>
    public RagCitationRecord(
        Guid id,
        Guid? tenantId,
        Guid ragCallLogId,
        Guid knowledgeItemId,
        Guid knowledgeVersionId,
        Guid chunkId,
        int rank,
        double score,
        string sourceTitle,
        int? sourcePage,
        string quoteHash,
        string quotePreview)
        : base(id)
    {
        if (rank < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(rank));
        }

        if (sourcePage is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sourcePage));
        }

        TenantId = tenantId;
        RagCallLogId = ragCallLogId;
        KnowledgeItemId = knowledgeItemId;
        KnowledgeVersionId = knowledgeVersionId;
        ChunkId = chunkId;
        Rank = rank;
        Score = score;
        SourceTitle = Check.NotNull(sourceTitle, nameof(sourceTitle)).Length <= MaxSourceTitleLength
            ? sourceTitle
            : sourceTitle[..MaxSourceTitleLength];
        SourcePage = sourcePage;
        QuoteHash = Check.NotNullOrWhiteSpace(quoteHash, nameof(quoteHash), MaxQuoteHashLength);
        QuotePreview = Check.NotNull(quotePreview, nameof(quotePreview)).Length <= MaxQuotePreviewLength
            ? quotePreview
            : quotePreview[..MaxQuotePreviewLength];
    }
}
