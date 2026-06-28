using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.KnowledgeIndexing;

/// <summary>
/// 知识分块 embedding 记录。真实向量保存在 Qdrant，
/// 业务主库只保存模型版本、维度、内容 hash 和外部向量记录 ID，便于追溯、删除和重建。
/// </summary>
public class KnowledgeEmbeddingRecord : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public const int MaxVectorStoreRecordIdLength = 256;
    public const int MaxContentHashLength = 128;

    public Guid? TenantId { get; protected set; }

    public Guid KnowledgeChunkId { get; private set; }

    public Guid KnowledgeVersionId { get; private set; }

    public Guid EmbeddingModelVersionId { get; private set; }

    public string VectorStoreRecordId { get; private set; } = string.Empty;

    public string ContentHash { get; private set; } = string.Empty;

    public int VectorDimension { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime IndexedTime { get; private set; }

    public DateTime? DeletedTime { get; private set; }

    /// <summary>
    /// EF Core 使用的受保护构造函数。
    /// </summary>
    protected KnowledgeEmbeddingRecord()
    {
    }

    /// <summary>
    /// 创建一条 embedding 主库追踪记录。
    /// 真实向量存放在外部向量库，这里只保存可审计、可重建和可删除的元数据。
    /// </summary>
    public KnowledgeEmbeddingRecord(
        Guid id,
        Guid? tenantId,
        Guid knowledgeChunkId,
        Guid knowledgeVersionId,
        Guid embeddingModelVersionId,
        string vectorStoreRecordId,
        string contentHash,
        int vectorDimension,
        DateTime indexedTime)
        : base(id)
    {
        if (vectorDimension <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(vectorDimension));
        }

        TenantId = tenantId;
        KnowledgeChunkId = knowledgeChunkId;
        KnowledgeVersionId = knowledgeVersionId;
        EmbeddingModelVersionId = embeddingModelVersionId;
        VectorStoreRecordId = Check.NotNullOrWhiteSpace(vectorStoreRecordId, nameof(vectorStoreRecordId), MaxVectorStoreRecordIdLength);
        ContentHash = Check.NotNullOrWhiteSpace(contentHash, nameof(contentHash), MaxContentHashLength);
        VectorDimension = vectorDimension;
        IndexedTime = indexedTime;
        IsActive = true;
    }

    /// <summary>
    /// 逻辑删除 embedding 记录。
    /// 旧记录不物理删除，避免索引重建后丢失模型版本和 content hash 审计链路。
    /// </summary>
    public void MarkDeleted(DateTime now)
    {
        IsActive = false;
        DeletedTime = now;
    }
}
