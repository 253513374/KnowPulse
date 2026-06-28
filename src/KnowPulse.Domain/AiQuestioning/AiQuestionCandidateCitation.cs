using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.AiQuestioning;

/// <summary>
/// AI 候选题引用的知识来源。
/// Citation 记录候选题生成时实际引用的 RAG chunk，保证人工审核可以追溯题目依据，
/// 同时只保存短摘录和 hash，避免把完整知识正文复制到候选题表。
/// </summary>
public class AiQuestionCandidateCitation : FullAuditedEntity<Guid>, IMultiTenant
{
    /// <summary>
    /// 来源标题最大长度。
    /// </summary>
    public const int MaxSourceTitleLength = 256;

    /// <summary>
    /// 来源摘录最大长度。摘录用于审核查看依据，不承载完整知识正文。
    /// </summary>
    public const int MaxSourceExcerptLength = 512;

    /// <summary>
    /// 来源正文 SHA-256 hash 最大长度。
    /// </summary>
    public const int MaxSourceHashLength = 64;

    /// <summary>
    /// 所属租户。
    /// </summary>
    public Guid? TenantId { get; protected set; }

    /// <summary>
    /// citation 所属候选题 ID。
    /// </summary>
    public Guid CandidateId { get; private set; }

    /// <summary>
    /// 引用的知识条目 ID。
    /// </summary>
    public Guid KnowledgeItemId { get; private set; }

    /// <summary>
    /// 引用的知识版本 ID，保证知识更新后仍可回溯当时依据。
    /// </summary>
    public Guid KnowledgeVersionId { get; private set; }

    /// <summary>
    /// 引用的 RAG chunk ID，是候选题可追溯性的最小来源单元。
    /// </summary>
    public Guid ChunkId { get; private set; }

    /// <summary>
    /// citation 在候选题中的展示顺序，从 1 开始。
    /// </summary>
    public int Rank { get; private set; }

    /// <summary>
    /// RAG 检索相关性分值，范围 0 到 1。
    /// </summary>
    public double Score { get; private set; }

    /// <summary>
    /// 来源标题。
    /// </summary>
    public string SourceTitle { get; private set; } = string.Empty;

    /// <summary>
    /// 来源页码。非分页来源为空。
    /// </summary>
    public int? SourcePage { get; private set; }

    /// <summary>
    /// 受控长度的来源摘录。
    /// </summary>
    public string SourceExcerpt { get; private set; } = string.Empty;

    /// <summary>
    /// 来源正文 hash，用于后续比对和审计，不暴露完整 chunk。
    /// </summary>
    public string SourceHash { get; private set; } = string.Empty;

    /// <summary>
    /// EF Core 使用的构造函数。
    /// </summary>
    protected AiQuestionCandidateCitation()
    {
    }

    /// <summary>
    /// 创建候选题 citation。
    /// rank 和 score 在领域层强制校验，避免页面出现不可解释的引用排序或相关性值。
    /// </summary>
    public AiQuestionCandidateCitation(
        Guid id,
        Guid? tenantId,
        Guid candidateId,
        Guid knowledgeItemId,
        Guid knowledgeVersionId,
        Guid chunkId,
        int rank,
        double score,
        string sourceTitle,
        int? sourcePage,
        string sourceExcerpt,
        string sourceHash)
        : base(id)
    {
        if (rank < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(rank));
        }

        if (score < 0 || score > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(score));
        }

        CandidateId = candidateId;
        TenantId = tenantId;
        KnowledgeItemId = knowledgeItemId;
        KnowledgeVersionId = knowledgeVersionId;
        ChunkId = chunkId;
        Rank = rank;
        Score = score;
        SourceTitle = Check.NotNullOrWhiteSpace(sourceTitle, nameof(sourceTitle), MaxSourceTitleLength);
        SourcePage = sourcePage;
        SourceExcerpt = Check.NotNullOrWhiteSpace(sourceExcerpt, nameof(sourceExcerpt), MaxSourceExcerptLength);
        SourceHash = Check.NotNullOrWhiteSpace(sourceHash, nameof(sourceHash), MaxSourceHashLength);
    }
}
