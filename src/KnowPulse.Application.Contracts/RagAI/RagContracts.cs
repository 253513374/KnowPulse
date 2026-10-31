using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

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
    Task<KnowledgeIngestionResult> IngestAsync(
        Guid knowledgeVersionId,
        Guid? knowledgeIndexJobId = null,
        bool completeIndexJob = true,
        CancellationToken cancellationToken = default);
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
/// 向量库维护接口，负责 collection 和 payload index 的初始化/健康检查。
/// </summary>
public interface IKnowledgeVectorStoreMaintenance
{
    /// <summary>
    /// 确保知识向量 collection 和必需 payload index 存在。
    /// 该方法是幂等维护入口，适合应用启动检查或运维脚本调用。
    /// </summary>
    Task<KnowledgeVectorCollectionHealthResult> EnsureKnowledgeCollectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取知识向量 collection 健康状态，不创建或修改任何外部资源。
    /// </summary>
    Task<KnowledgeVectorCollectionHealthResult> GetKnowledgeCollectionHealthAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Embedding 客户端接口，封装 LiteLLM 或 Microsoft.Extensions.AI 的具体调用。
/// 业务 pipeline 只依赖该接口，避免模型 SDK 或 HTTP 细节散落到应用服务。
/// </summary>
public interface IKnowledgeEmbeddingClient
{
    /// <summary>
    /// 为输入文本生成 embedding。
    /// 实现类必须校验返回向量维度，避免污染目标 collection。
    /// </summary>
    Task<KnowledgeEmbeddingClientResult> GenerateAsync(
        KnowledgeEmbeddingClientRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Embedding pipeline 接口，负责为已生成的 chunk 创建 embedding 并保存主库追踪记录。
/// </summary>
public interface IKnowledgeEmbeddingPipeline
{
    /// <summary>
    /// 为指定知识版本的 active chunk 生成 embedding 追踪记录。
    /// </summary>
    Task<KnowledgeEmbeddingRunResult> GenerateForVersionAsync(
        Guid knowledgeVersionId,
        Guid? embeddingModelVersionId = null,
        Guid? knowledgeIndexJobId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询指定模型版本下需要重建 embedding 的 chunk。
    /// </summary>
    Task<IReadOnlyList<KnowledgeChunkEmbeddingRebuildDto>> GetChunksRequiringRebuildAsync(
        Guid embeddingModelVersionId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 权限感知知识检索接口，负责把用户查询转换为向量检索请求，并做 PostgreSQL 二次权限校验。
/// MVP-0 仅定义契约，实际 RAG 检索在后续阶段实现。
/// </summary>
public interface IKnowledgeRetriever : IApplicationService
{
    /// <summary>
    /// 检索当前用户可访问的知识分块。
    /// </summary>
    [Authorize(KnowPulsePermissions.Rag.Search)]
    Task<IReadOnlyList<RetrievedChunk>> SearchAsync(RagSearchRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// RAG 问答服务接口，负责基于检索结果生成带引用的回答。
/// 模型调用应通过统一模型网关或 Microsoft.Extensions.AI 封装，便于审计、限流和替换模型。
/// </summary>
public interface IRagAnswerService : IApplicationService
{
    /// <summary>
    /// 根据问题生成回答，并返回引用来源、置信度和是否需要人工复核。
    /// </summary>
    [Authorize(KnowPulsePermissions.Rag.Answer)]
    Task<RagAnswerResult> AnswerAsync(RagAnswerRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// RAG 调用日志查询接口，用于审计、排障和评测集复用 citation 链路。
/// </summary>
public interface IRagCallLogAppService : IApplicationService
{
    /// <summary>
    /// 分页查询 RAG 调用日志及其 citation 链路。
    /// </summary>
    [Authorize(KnowPulsePermissions.Rag.AuditLogs)]
    Task<PagedResultDto<RagCallLogDto>> GetListAsync(GetRagCallLogListInput input);
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
/// Embedding 客户端请求。携带输入文本、模型版本和目标 collection 信息，供实现类校验维度和路由模型。
/// </summary>
public class KnowledgeEmbeddingClientRequest
{
    /// <summary>
    /// 需要生成 embedding 的文本。
    /// </summary>
    public string Input { get; set; } = string.Empty;

    /// <summary>
    /// embedding provider 逻辑名称。
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// embedding 模型 alias。
    /// </summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>
    /// 业务侧模型版本。
    /// </summary>
    public string ModelVersion { get; set; } = string.Empty;

    /// <summary>
    /// 期望返回的向量维度。
    /// </summary>
    public int VectorDimension { get; set; }

    /// <summary>
    /// 目标向量 collection。
    /// </summary>
    public string CollectionName { get; set; } = string.Empty;
}

/// <summary>
/// Embedding client 返回结果。
/// 成功时携带向量，失败时携带阶段和原因，调用方据此更新索引任务。
/// </summary>
public class KnowledgeEmbeddingClientResult
{
    /// <summary>
    /// 是否成功生成 embedding。
    /// </summary>
    public bool Succeeded { get; set; }

    /// <summary>
    /// 成功返回的向量。
    /// </summary>
    public ReadOnlyMemory<float> Embedding { get; set; }

    /// <summary>
    /// 失败阶段，默认 embedding。
    /// </summary>
    public string? FailureStage { get; set; }

    /// <summary>
    /// 失败原因摘要。
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// 创建成功结果。
    /// </summary>
    public static KnowledgeEmbeddingClientResult Success(ReadOnlyMemory<float> embedding)
    {
        return new KnowledgeEmbeddingClientResult
        {
            Succeeded = true,
            Embedding = embedding
        };
    }

    /// <summary>
    /// 创建失败结果。
    /// </summary>
    public static KnowledgeEmbeddingClientResult Failed(string failureReason, string failureStage = "embedding")
    {
        return new KnowledgeEmbeddingClientResult
        {
            Succeeded = false,
            FailureStage = failureStage,
            FailureReason = failureReason
        };
    }
}

/// <summary>
/// Embedding pipeline 执行结果。
/// </summary>
public class KnowledgeEmbeddingRunResult
{
    /// <summary>
    /// 本次处理的知识版本编号。
    /// </summary>
    public Guid KnowledgeVersionId { get; set; }

    /// <summary>
    /// 本次使用的 embedding 模型版本编号。
    /// </summary>
    public Guid? EmbeddingModelVersionId { get; set; }

    /// <summary>
    /// 是否成功完成全部 chunk 的 embedding 记录生成。
    /// </summary>
    public bool Succeeded { get; set; }

    /// <summary>
    /// 本次实际新增或替换的 embedding 记录数量。
    /// </summary>
    public int ProcessedChunkCount { get; set; }

    /// <summary>
    /// 失败阶段。
    /// </summary>
    public string? FailureStage { get; set; }

    /// <summary>
    /// 失败原因摘要。
    /// </summary>
    public string? FailureReason { get; set; }
}

/// <summary>
/// 知识向量 collection 健康检查结果。
/// </summary>
public class KnowledgeVectorCollectionHealthResult
{
    /// <summary>
    /// collection 名称。
    /// </summary>
    public string CollectionName { get; set; } = string.Empty;

    /// <summary>
    /// 配置期望的向量维度。
    /// </summary>
    public int ExpectedVectorDimension { get; set; }

    /// <summary>
    /// 实际读取到的向量维度。collection 不存在或结构异常时为空。
    /// </summary>
    public int? ActualVectorDimension { get; set; }

    /// <summary>
    /// collection 是否存在。
    /// </summary>
    public bool CollectionExists { get; set; }

    /// <summary>
    /// 实际维度是否与配置一致。
    /// </summary>
    public bool VectorDimensionMatches { get; set; }

    /// <summary>
    /// 应存在的 payload index 字段。
    /// </summary>
    public IReadOnlyList<string> RequiredPayloadIndexes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 当前 collection 已存在的 payload index 字段。
    /// </summary>
    public IReadOnlyList<string> ExistingPayloadIndexes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 当前缺失的 payload index 字段。
    /// </summary>
    public IReadOnlyList<string> MissingPayloadIndexes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// collection 存在、维度匹配且 payload index 齐全时视为健康。
    /// </summary>
    public bool IsHealthy => CollectionExists &&
                             VectorDimensionMatches &&
                             MissingPayloadIndexes.Count == 0;
}

/// <summary>
/// 需要重建 embedding 的 chunk 描述。
/// </summary>
public class KnowledgeChunkEmbeddingRebuildDto
{
    /// <summary>
    /// 需要重建的 chunk 编号。
    /// </summary>
    public Guid KnowledgeChunkId { get; set; }

    /// <summary>
    /// chunk 所属知识版本编号。
    /// </summary>
    public Guid KnowledgeVersionId { get; set; }

    /// <summary>
    /// 目标 embedding 模型版本编号。
    /// </summary>
    public Guid EmbeddingModelVersionId { get; set; }

    /// <summary>
    /// 当前 chunk 内容 hash。
    /// </summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>
    /// 需要重建的原因，例如 MissingEmbeddingRecord、ContentHashChanged 或 VectorDimensionChanged。
    /// </summary>
    public string Reason { get; set; } = string.Empty;
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
    /// 权限范围类型。默认 All，向量库只做前置过滤，最终授权仍回 PostgreSQL 校验。
    /// </summary>
    public string Visibility { get; set; } = "All";

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
    public IReadOnlyList<Guid> KnowledgeItemIds { get; set; } = Array.Empty<Guid>();
    /// <summary>服务端课程范围 SQL 得出的分块集合；null 不应用课程过滤，空集合必须不命中。</summary>
    public IReadOnlyList<Guid>? AllowedCurriculumChunkIds { get; set; }

    /// <summary>由业务主库授权得到的指定共享版本，不能由终端自报。</summary>
    public IReadOnlyList<Guid> AllowedSharedVersionIds { get; set; } = Array.Empty<Guid>();

    public KnowledgeSearchScope Scope { get; set; }

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
    /// 当前用户可见用户编号集合。通常包含当前用户本人，也可由调用方补充代理访问上下文。
    /// </summary>
    public IReadOnlyList<Guid> UserIds { get; set; } = Array.Empty<Guid>();

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
public enum KnowledgeSearchScope
{
    AllAccessible = 0,
    PersonalLibrary = 1,
    SchoolShared = 2
}

public class RagSearchRequest
{
    public IReadOnlyList<Guid> KnowledgeItemIds { get; set; } = Array.Empty<Guid>();
    public EducationCurriculumScopeDto? CurriculumScope { get; set; }

    public KnowledgeSearchScope Scope { get; set; }

    public Guid? SchoolId { get; set; }

    /// <summary>只由内部出题服务设置；HTTP 输入不能降低出题用途校验。</summary>
    [JsonIgnore]
    public bool ForQuestionGeneration { get; set; }

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
    /// <summary>当前 SQL 复核通过的精确课程映射和资料关联，模型只能引用这些教学依据。</summary>
    public IReadOnlyList<Guid> TextbookKnowledgePointMappingIds { get; set; } = Array.Empty<Guid>();
    public IReadOnlyList<Guid> CurriculumBindingIds { get; set; } = Array.Empty<Guid>();

    public Guid? SchoolKnowledgeShareId { get; set; }
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
    public KnowledgeSearchScope Scope { get; set; }

    public Guid? SchoolId { get; set; }

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

    /// <summary>
    /// 问答时最多检索的上下文分块数量。
    /// </summary>
    public int TopK { get; set; } = 5;
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
    public Guid? SchoolKnowledgeShareId { get; set; }
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

/// <summary>
/// RAG 调用日志分页查询条件，用于按用户、模型、调用状态和时间窗口筛选审计记录。
/// </summary>
public class GetRagCallLogListInput : PagedAndSortedResultRequestDto
{
    /// <summary>
    /// 按调用用户过滤。
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// 按 citation 中引用的知识版本过滤。
    /// </summary>
    public Guid? KnowledgeVersionId { get; set; }

    /// <summary>
    /// 按模型版本过滤。
    /// </summary>
    public string? ModelVersion { get; set; }

    /// <summary>
    /// 按调用类型过滤。
    /// </summary>
    public RagCallType? CallType { get; set; }

    /// <summary>
    /// 按调用状态过滤。
    /// </summary>
    public RagCallStatus? Status { get; set; }

    /// <summary>
    /// 调用时间窗口开始。
    /// </summary>
    public DateTime? StartTime { get; set; }

    /// <summary>
    /// 调用时间窗口结束。
    /// </summary>
    public DateTime? EndTime { get; set; }
}

/// <summary>
/// RAG 调用日志 DTO。
/// 该 DTO 只包含摘要、hash、模型元数据、统计信息和 citation，不包含完整 prompt 或完整知识正文。
/// </summary>
public class RagCallLogDto : EntityDto<Guid>
{
    /// <summary>
    /// 调用所在租户。
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 调用用户。
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// 调用类型。
    /// </summary>
    public RagCallType CallType { get; set; }

    /// <summary>
    /// 调用状态。
    /// </summary>
    public RagCallStatus Status { get; set; }

    /// <summary>
    /// 用户问题 hash，用于去重和排障，不暴露完整问题。
    /// </summary>
    public string QueryHash { get; set; } = string.Empty;

    /// <summary>
    /// 用户问题摘要，长度受控。
    /// </summary>
    public string QuerySummary { get; set; } = string.Empty;

    /// <summary>
    /// 模型 provider。
    /// </summary>
    public string ModelProvider { get; set; } = string.Empty;

    /// <summary>
    /// 模型 alias 或名称。
    /// </summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>
    /// 模型版本。
    /// </summary>
    public string ModelVersion { get; set; } = string.Empty;

    /// <summary>
    /// 检索 collection。
    /// </summary>
    public string CollectionName { get; set; } = string.Empty;

    /// <summary>
    /// 请求的 TopK。
    /// </summary>
    public int TopK { get; set; }

    /// <summary>
    /// 返回的授权 chunk 数。
    /// </summary>
    public int RetrievedChunkCount { get; set; }

    /// <summary>
    /// citation 数量。
    /// </summary>
    public int CitationCount { get; set; }

    /// <summary>
    /// prompt token 数。
    /// </summary>
    public int PromptTokenCount { get; set; }

    /// <summary>
    /// completion token 数。
    /// </summary>
    public int CompletionTokenCount { get; set; }

    /// <summary>
    /// 总 token 数。
    /// </summary>
    public int TotalTokenCount { get; set; }

    /// <summary>
    /// 预估成本，单位 USD。
    /// </summary>
    public decimal EstimatedCostUsd { get; set; }

    /// <summary>
    /// 调用置信度。
    /// </summary>
    public double? Confidence { get; set; }

    /// <summary>
    /// 是否需要人工复核。
    /// </summary>
    public bool NeedHumanReview { get; set; }

    /// <summary>
    /// 执行耗时，单位毫秒。
    /// </summary>
    public int ExecutionDurationMs { get; set; }

    /// <summary>
    /// 失败阶段。
    /// </summary>
    public string? FailureStage { get; set; }

    /// <summary>
    /// 失败原因摘要。
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// 日志创建时间。
    /// </summary>
    public DateTime CreationTime { get; set; }

    /// <summary>
    /// 本次调用的 citation 链路。
    /// </summary>
    public IReadOnlyList<RagCitationRecordDto> Citations { get; set; } = Array.Empty<RagCitationRecordDto>();
}

/// <summary>
/// RAG citation 审计 DTO。
/// </summary>
public class RagCitationRecordDto : EntityDto<Guid>
{
    /// <summary>
    /// 所属 RAG 调用日志编号。
    /// </summary>
    public Guid RagCallLogId { get; set; }

    /// <summary>
    /// 引用知识条目编号。
    /// </summary>
    public Guid KnowledgeItemId { get; set; }

    /// <summary>
    /// 引用知识版本编号。
    /// </summary>
    public Guid KnowledgeVersionId { get; set; }

    /// <summary>
    /// 引用 chunk 编号。
    /// </summary>
    public Guid ChunkId { get; set; }

    /// <summary>
    /// 在本次回答中的引用排名，从 1 开始。
    /// </summary>
    public int Rank { get; set; }

    /// <summary>
    /// 向量检索得分。
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// 来源标题。
    /// </summary>
    public string SourceTitle { get; set; } = string.Empty;

    /// <summary>
    /// 来源页码。
    /// </summary>
    public int? SourcePage { get; set; }

    /// <summary>
    /// 引用摘录 hash。
    /// </summary>
    public string QuoteHash { get; set; } = string.Empty;

    /// <summary>
    /// 引用短摘录。长度受控，不保存完整 chunk。
    /// </summary>
    public string QuotePreview { get; set; } = string.Empty;
}
