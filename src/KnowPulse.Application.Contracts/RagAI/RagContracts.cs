using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KnowPulse.RagAI;

/// <summary>
/// 知识摄取管道接口，负责把已发布或待索引的知识版本转成可检索的分块和向量记录。
/// 该接口用于隔离 Microsoft.Extensions.DataIngestion 等预览依赖，避免外部库类型泄漏到业务应用层。
/// </summary>
public interface IKnowledgeIngestionPipeline
{
    /// <summary>
    /// 摄取指定知识版本，并返回本次摄取生成的分块结果或失败信息。
    /// </summary>
    Task<KnowledgeIngestionResult> IngestAsync(Guid knowledgeVersionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// 知识向量库接口，封装 Qdrant 或其他向量存储的写入、检索和删除能力。
/// 业务代码只依赖本项目契约，后续更换向量库或 provider 时不影响应用服务。
/// </summary>
public interface IKnowledgeVectorStore
{
    /// <summary>
    /// 写入或更新知识分块向量。
    /// </summary>
    Task UpsertChunksAsync(IReadOnlyList<KnowledgeChunkVectorRecord> chunks, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据查询向量和权限过滤条件检索候选分块。
    /// </summary>
    Task<IReadOnlyList<VectorSearchHit>> SearchAsync(KnowledgeVectorSearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除某个知识版本对应的全部向量分块，用于版本下架、归档或重新索引。
    /// </summary>
    Task DeleteByKnowledgeVersionAsync(Guid knowledgeVersionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// 权限感知知识检索接口，负责把用户查询转换为向量检索请求，并做 PostgreSQL 二次权限校验。
/// MVP-0 仅定义契约，实际 RAG 检索在后续阶段实现。
/// </summary>
public interface IKnowledgeRetriever
{
    /// <summary>
    /// 检索当前用户可访问的知识分块。
    /// </summary>
    Task<IReadOnlyList<RetrievedChunk>> SearchAsync(RagSearchRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// RAG 问答服务接口，负责基于检索结果生成带引用的回答。
/// 模型调用应通过统一模型网关或 Microsoft.Extensions.AI 封装，便于审计、限流和替换模型。
/// </summary>
public interface IRagAnswerService
{
    /// <summary>
    /// 根据问题生成回答，并返回引用来源、置信度和是否需要人工复核。
    /// </summary>
    Task<RagAnswerResult> AnswerAsync(RagAnswerRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// 知识摄取结果，记录一次知识版本摄取是否成功、失败阶段以及生成的分块向量。
/// </summary>
public class KnowledgeIngestionResult
{
    /// <summary>
    /// 被摄取的知识版本编号。
    /// </summary>
    public Guid KnowledgeVersionId { get; set; }

    /// <summary>
    /// 本次摄取是否成功。
    /// </summary>
    public bool Succeeded { get; set; }

    /// <summary>
    /// 失败阶段，例如读取、清洗、分块、Embedding 或写入向量库。
    /// </summary>
    public string? FailureStage { get; set; }

    /// <summary>
    /// 失败原因，供后台任务、运维页面和审计排查使用。
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// 本次摄取生成的知识分块向量记录。
    /// </summary>
    public IReadOnlyList<KnowledgeChunkVectorRecord> Chunks { get; set; } = Array.Empty<KnowledgeChunkVectorRecord>();
}

/// <summary>
/// 知识分块向量记录，表示一个可写入向量库的 chunk。
/// Payload 中保存业务过滤字段，Embedding 中保存模型生成的向量。
/// </summary>
public class KnowledgeChunkVectorRecord
{
    /// <summary>
    /// 分块编号。
    /// </summary>
    public Guid ChunkId { get; set; }

    /// <summary>
    /// 所属知识文档编号。
    /// </summary>
    public Guid KnowledgeItemId { get; set; }

    /// <summary>
    /// 所属知识版本编号。
    /// </summary>
    public Guid KnowledgeVersionId { get; set; }

    /// <summary>
    /// 所属租户编号。
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 分块文本内容。
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 分块文本的向量表示。
    /// </summary>
    public ReadOnlyMemory<float> Embedding { get; set; }

    /// <summary>
    /// 写入向量库的业务 payload，用于权限过滤、引用和排查。
    /// </summary>
    public KnowledgeVectorPayload Payload { get; set; } = new();
}

/// <summary>
/// 向量库 payload，保存检索前过滤、检索后引用和审计所需的业务元数据。
/// 这些字段必须来自业务主库，不能由模型自由生成。
/// </summary>
public class KnowledgeVectorPayload
{
    /// <summary>
    /// 所属租户编号。
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 所属知识文档编号。
    /// </summary>
    public Guid KnowledgeItemId { get; set; }

    /// <summary>
    /// 所属知识版本编号。
    /// </summary>
    public Guid KnowledgeVersionId { get; set; }

    /// <summary>
    /// 分块编号。
    /// </summary>
    public Guid ChunkId { get; set; }

    /// <summary>
    /// 知识分类编号。
    /// </summary>
    public Guid? CategoryId { get; set; }

    /// <summary>
    /// 标签编号集合。
    /// </summary>
    public IReadOnlyList<Guid> TagIds { get; set; } = Array.Empty<Guid>();

    /// <summary>
    /// 可访问部门编号集合，用于向量库 payload filter 的前置过滤。
    /// </summary>
    public IReadOnlyList<Guid> DepartmentIds { get; set; } = Array.Empty<Guid>();

    /// <summary>
    /// 可访问岗位编号集合。
    /// </summary>
    public IReadOnlyList<Guid> PositionIds { get; set; } = Array.Empty<Guid>();

    /// <summary>
    /// 可访问角色编号集合。
    /// </summary>
    public IReadOnlyList<Guid> RoleIds { get; set; } = Array.Empty<Guid>();

    /// <summary>
    /// 可访问用户编号集合。
    /// </summary>
    public IReadOnlyList<Guid> UserIds { get; set; } = Array.Empty<Guid>();

    /// <summary>
    /// 访问该分块需要的最低安全等级。
    /// </summary>
    public int SecurityLevel { get; set; }

    /// <summary>
    /// 是否来自已发布版本。默认 RAG 检索只应返回已发布内容。
    /// </summary>
    public bool IsPublished { get; set; }

    /// <summary>
    /// 可选生效开始时间。
    /// </summary>
    public DateTime? EffectiveFrom { get; set; }

    /// <summary>
    /// 可选生效结束时间。
    /// </summary>
    public DateTime? EffectiveTo { get; set; }

    /// <summary>
    /// 引用来源标题。
    /// </summary>
    public string SourceTitle { get; set; } = string.Empty;

    /// <summary>
    /// 引用来源页码，适用于 PDF、Word 等分页文档。
    /// </summary>
    public int? SourcePage { get; set; }

    /// <summary>
    /// 生成向量使用的模型名称。
    /// </summary>
    public string EmbeddingModel { get; set; } = string.Empty;

    /// <summary>
    /// 生成向量使用的模型版本。
    /// </summary>
    public string EmbeddingModelVersion { get; set; } = string.Empty;
}

/// <summary>
/// 向量检索请求，包含查询向量、TopK 和权限过滤条件。
/// </summary>
public class KnowledgeVectorSearchRequest
{
    /// <summary>
    /// 当前租户编号。
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 查询文本对应的向量表示。
    /// </summary>
    public ReadOnlyMemory<float> QueryEmbedding { get; set; }

    /// <summary>
    /// 返回候选分块数量。
    /// </summary>
    public int TopK { get; set; } = 20;

    /// <summary>
    /// 检索前权限过滤条件。
    /// </summary>
    public KnowledgeVectorPermissionFilter PermissionFilter { get; set; } = new();
}

/// <summary>
/// 向量检索权限过滤条件，表达当前访问人能看哪些范围的数据。
/// 向量库过滤只是前置过滤，正式回答前仍需要回业务主库做二次校验。
/// </summary>
public class KnowledgeVectorPermissionFilter
{
    /// <summary>
    /// 当前租户编号。
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 当前用户编号。
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// 当前用户所属部门集合。
    /// </summary>
    public IReadOnlyList<Guid> DepartmentIds { get; set; } = Array.Empty<Guid>();

    /// <summary>
    /// 当前用户岗位集合。
    /// </summary>
    public IReadOnlyList<Guid> PositionIds { get; set; } = Array.Empty<Guid>();

    /// <summary>
    /// 当前用户角色集合。
    /// </summary>
    public IReadOnlyList<Guid> RoleIds { get; set; } = Array.Empty<Guid>();

    /// <summary>
    /// 当前用户安全等级。
    /// </summary>
    public int SecurityLevel { get; set; }

    /// <summary>
    /// 是否只检索已发布内容。
    /// </summary>
    public bool PublishedOnly { get; set; } = true;

    /// <summary>
    /// 当前时间，用于生效期过滤。
    /// </summary>
    public DateTime Now { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 向量检索命中结果。
/// </summary>
public class VectorSearchHit
{
    /// <summary>
    /// 命中的分块编号。
    /// </summary>
    public Guid ChunkId { get; set; }

    /// <summary>
    /// 命中分块所属知识文档编号。
    /// </summary>
    public Guid KnowledgeItemId { get; set; }

    /// <summary>
    /// 命中分块所属知识版本编号。
    /// </summary>
    public Guid KnowledgeVersionId { get; set; }

    /// <summary>
    /// 向量相似度得分。
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// 命中分块文本。
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 命中分块的业务 payload。
    /// </summary>
    public KnowledgeVectorPayload Payload { get; set; } = new();
}

/// <summary>
/// RAG 检索请求，使用自然语言问题和访问人身份发起检索。
/// </summary>
public class RagSearchRequest
{
    /// <summary>
    /// 用户查询文本。
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// 当前租户编号。
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 当前用户编号。
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// 返回分块数量。
    /// </summary>
    public int TopK { get; set; } = 10;
}

/// <summary>
/// RAG 检索返回的业务分块，已经过向量检索和业务权限校验。
/// </summary>
public class RetrievedChunk
{
    /// <summary>
    /// 分块编号。
    /// </summary>
    public Guid ChunkId { get; set; }

    /// <summary>
    /// 所属知识文档编号。
    /// </summary>
    public Guid KnowledgeItemId { get; set; }

    /// <summary>
    /// 所属知识版本编号。
    /// </summary>
    public Guid KnowledgeVersionId { get; set; }

    /// <summary>
    /// 分块文本。
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 检索相关性得分。
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// 引用来源标题。
    /// </summary>
    public string SourceTitle { get; set; } = string.Empty;

    /// <summary>
    /// 引用来源页码。
    /// </summary>
    public int? SourcePage { get; set; }
}

/// <summary>
/// RAG 问答请求。
/// </summary>
public class RagAnswerRequest
{
    /// <summary>
    /// 用户问题。
    /// </summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// 当前租户编号。
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 当前用户编号。
    /// </summary>
    public Guid? UserId { get; set; }
}

/// <summary>
/// RAG 问答结果，包含回答、引用、置信度和人审标记。
/// </summary>
public class RagAnswerResult
{
    /// <summary>
    /// 模型生成的回答。
    /// </summary>
    public string Answer { get; set; } = string.Empty;

    /// <summary>
    /// 回答引用的知识来源。
    /// </summary>
    public IReadOnlyList<RagCitation> Citations { get; set; } = Array.Empty<RagCitation>();

    /// <summary>
    /// 回答置信度。低置信度结果应进入人工复核或提示无法确定。
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// 是否需要人工复核。
    /// </summary>
    public bool NeedHumanReview { get; set; }
}

/// <summary>
/// RAG 回答引用来源。
/// </summary>
public class RagCitation
{
    /// <summary>
    /// 引用知识文档编号。
    /// </summary>
    public Guid KnowledgeItemId { get; set; }

    /// <summary>
    /// 引用知识版本编号。
    /// </summary>
    public Guid KnowledgeVersionId { get; set; }

    /// <summary>
    /// 引用分块编号。
    /// </summary>
    public Guid ChunkId { get; set; }

    /// <summary>
    /// 引用来源标题。
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 引用来源页码。
    /// </summary>
    public int? Page { get; set; }

    /// <summary>
    /// 引用摘录。需要控制长度，避免把原文大段回传给前端。
    /// </summary>
    public string Quote { get; set; } = string.Empty;
}
