using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.AiModelManagement;
using KnowPulse.KnowledgeBase;
using KnowPulse.KnowledgeIndexing;
using Microsoft.Extensions.Options;
using Volo.Abp.Domain.Repositories;

namespace KnowPulse.RagAI;

/// <summary>
/// 权限化知识检索。Qdrant 只做候选召回和 payload 粗过滤，
/// 返回给调用方前必须回 PostgreSQL 校验知识状态、版本和权限范围。
/// </summary>
[Volo.Abp.RemoteService(false)]
public class KnowledgeRetriever : KnowPulseAppService, IKnowledgeRetriever
{
    // 向量库只能做候选召回，TopK 过小会导致权限二次校验后没有足够结果；
    // 因此这里先多取一批候选，再由 PostgreSQL 状态和权限规则裁掉。
    private const int MaxCandidateMultiplier = 3;
    private const int MaxCandidateLimit = 100;

    private readonly IKnowledgeEmbeddingClient _embeddingClient;
    private readonly IKnowledgeVectorStore _vectorStore;
    private readonly IKnowledgeAccessContextProvider _accessContextProvider;
    private readonly IRepository<KnowledgeItem, Guid> _itemRepository;
    private readonly IRepository<KnowledgeVersion, Guid> _versionRepository;
    private readonly IRepository<KnowledgePermissionScope, Guid> _permissionScopeRepository;
    private readonly IRepository<KnowledgeChunk, Guid> _chunkRepository;
    private readonly IRagCallLogWriter _callLogWriter;
    private readonly IAiModelResolver _modelResolver;
    private readonly RagAiOptions _options;

    /// <summary>
    /// 创建权限化知识检索服务。
    /// </summary>
    public KnowledgeRetriever(
        IKnowledgeEmbeddingClient embeddingClient,
        IKnowledgeVectorStore vectorStore,
        IKnowledgeAccessContextProvider accessContextProvider,
        IRepository<KnowledgeItem, Guid> itemRepository,
        IRepository<KnowledgeVersion, Guid> versionRepository,
        IRepository<KnowledgePermissionScope, Guid> permissionScopeRepository,
        IRepository<KnowledgeChunk, Guid> chunkRepository,
        IRagCallLogWriter callLogWriter,
        IAiModelResolver modelResolver,
        IOptions<RagAiOptions> options)
    {
        _embeddingClient = embeddingClient;
        _vectorStore = vectorStore;
        _accessContextProvider = accessContextProvider;
        _itemRepository = itemRepository;
        _versionRepository = versionRepository;
        _permissionScopeRepository = permissionScopeRepository;
        _chunkRepository = chunkRepository;
        _callLogWriter = callLogWriter;
        _modelResolver = modelResolver;
        _options = options.Value;
    }

    /// <summary>
    /// 检索当前用户可访问的知识分块，并写入检索审计日志。
    /// </summary>
    public virtual async Task<IReadOnlyList<RetrievedChunk>> SearchAsync(
        RagSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var topK = request.TopK <= 0 ? 10 : request.TopK;
        var modelSelection = await _modelResolver.ResolveEmbeddingAsync(cancellationToken);

        // 空问题不进入 embedding / Qdrant，仍写入 NoContext 日志，便于前端和审计看到调用事实。
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            await WriteSearchLogAsync(
                request,
                topK,
                RagCallStatus.NoContext,
                retrievedChunkCount: 0,
                citations: Array.Empty<RetrievedChunk>(),
                modelSelection,
                stopwatch,
                failureStage: "input",
                failureReason: "Empty query.",
                cancellationToken);

            return Array.Empty<RetrievedChunk>();
        }

        // 检索结果包含业务知识片段。即使后续 app service/controller 已有权限标记，
        // 这里仍做运行时身份兜底，避免内部调用绕过 HTTP 授权层。
        if (!CurrentUser.IsAuthenticated)
        {
            await WriteSearchLogAsync(
                request,
                topK,
                RagCallStatus.Denied,
                retrievedChunkCount: 0,
                citations: Array.Empty<RetrievedChunk>(),
                modelSelection,
                stopwatch,
                failureStage: "authentication",
                failureReason: "Authenticated user is required.",
                cancellationToken);

            return Array.Empty<RetrievedChunk>();
        }

        var accessContext = await _accessContextProvider.CreateAsync(cancellationToken);
        // DTO 中的 UserId/TenantId 只允许表达“当前主体”的上下文，不允许调用方替别人检索。
        // 真正的可见范围来自服务端的 KnowledgeAccessContext，而不是请求体里的自报身份。
        if ((request.UserId.HasValue && accessContext.UserId.HasValue && request.UserId.Value != accessContext.UserId.Value) ||
            (request.TenantId.HasValue && CurrentTenant.Id.HasValue && request.TenantId.Value != CurrentTenant.Id.Value))
        {
            await WriteSearchLogAsync(
                request,
                topK,
                RagCallStatus.Denied,
                retrievedChunkCount: 0,
                citations: Array.Empty<RetrievedChunk>(),
                modelSelection,
                stopwatch,
                failureStage: "access-check",
                failureReason: "Requested user or tenant does not match the current principal.",
                cancellationToken);

            return Array.Empty<RetrievedChunk>();
        }

        ReadOnlyMemory<float> embedding;
        try
        {
            embedding = await GenerateQueryEmbeddingAsync(request.Query, modelSelection, cancellationToken);
        }
        catch (Exception ex)
        {
            await WriteSearchLogAsync(
                request,
                topK,
                RagCallStatus.Failed,
                retrievedChunkCount: 0,
                citations: Array.Empty<RetrievedChunk>(),
                modelSelection,
                stopwatch,
                failureStage: "embedding",
                failureReason: ex.Message,
                cancellationToken);
            throw;
        }

        var tenantId = CurrentTenant.Id ?? request.TenantId;
        IReadOnlyList<VectorSearchHit> vectorHits;
        try
        {
            // Qdrant payload filter 只做前置缩小范围：租户、发布态、组织范围和安全等级。
            // 后面的 ApplyBusinessAuthorizationAsync 仍会回 PostgreSQL 验证版本生命周期和权限事实。
            vectorHits = await _vectorStore.SearchAsync(new KnowledgeVectorSearchRequest
            {
                TenantId = tenantId,
                QueryEmbedding = embedding,
                TopK = CalculateCandidateLimit(topK),
                PermissionFilter = new KnowledgeVectorPermissionFilter
                {
                    KnowledgeItemIds = request.KnowledgeItemIds,
                    TenantId = tenantId,
                    UserId = accessContext.UserId,
                    DepartmentIds = accessContext.DepartmentIds.ToArray(),
                    PositionIds = accessContext.PositionIds.ToArray(),
                    RoleIds = accessContext.RoleIds.ToArray(),
                    UserIds = accessContext.UserId.HasValue
                        ? new[] { accessContext.UserId.Value }
                        : Array.Empty<Guid>(),
                    SecurityLevel = accessContext.SecurityLevel,
                    PublishedOnly = true
                }
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            await WriteSearchLogAsync(
                request,
                topK,
                RagCallStatus.Failed,
                retrievedChunkCount: 0,
                citations: Array.Empty<RetrievedChunk>(),
                modelSelection,
                stopwatch,
                failureStage: "vector-store",
                failureReason: ex.Message,
                cancellationToken);
            throw;
        }

        IReadOnlyList<RetrievedChunk> results;
        try
        {
            // 这是 MVP-1 RAG 安全边界的核心：任何 Qdrant 命中都必须重新读取业务主库。
            // 如果主库中版本已下架、权限已收窄或知识已停用，命中会在这里被丢弃。
            var scopedHits = request.KnowledgeItemIds.Count == 0 ? vectorHits :
                vectorHits.Where(x => request.KnowledgeItemIds.Contains(x.KnowledgeItemId)).ToList();
            results = await ApplyBusinessAuthorizationAsync(scopedHits, accessContext, tenantId, topK, request.KnowledgeItemIds, cancellationToken);
        }
        catch (Exception ex)
        {
            await WriteSearchLogAsync(
                request,
                topK,
                RagCallStatus.Failed,
                retrievedChunkCount: 0,
                citations: Array.Empty<RetrievedChunk>(),
                modelSelection,
                stopwatch,
                failureStage: "secondary-check",
                failureReason: ex.Message,
                cancellationToken);
            throw;
        }

        await WriteSearchLogAsync(
            request,
            topK,
            results.Count == 0 ? RagCallStatus.NoContext : RagCallStatus.Succeeded,
            results.Count,
            results,
            modelSelection,
            stopwatch,
            results.Count == 0 ? (vectorHits.Count == 0 ? "retrieval" : "secondary-check") : null,
            results.Count == 0
                ? (vectorHits.Count == 0
                    ? "No vector candidates were retrieved."
                    : "All vector candidates were rejected by PostgreSQL authorization check.")
                : null,
            cancellationToken);

        return results;
    }

    /// <summary>
    /// 写入 RAG Search 调用日志。
    /// </summary>
    private Task WriteSearchLogAsync(
        RagSearchRequest request,
        int topK,
        RagCallStatus status,
        int retrievedChunkCount,
        IReadOnlyList<RetrievedChunk> citations,
        AiModelSelection modelSelection,
        Stopwatch stopwatch,
        string? failureStage,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        // 日志只保存 query hash、受控长度摘要和 citation metadata。
        // 不保存完整 prompt 或完整知识正文，避免 RAG 审计表成为敏感内容副本。
        return _callLogWriter.WriteAsync(
            new RagCallLogWriteRequest
            {
                CallType = RagCallType.Search,
                Status = status,
                Query = request.Query,
                ModelProvider = modelSelection.Provider,
                ModelName = modelSelection.Alias,
                ModelVersion = modelSelection.BusinessModelVersion,
                CollectionName = modelSelection.CollectionName ?? _options.Embedding.CollectionName,
                TopK = topK,
                RetrievedChunkCount = retrievedChunkCount,
                PromptTokenCount = RagCallLogWriter.EstimateTokenCount(request.Query),
                CompletionTokenCount = 0,
                EstimatedCostUsd = 0,
                Confidence = citations.Count == 0 ? null : Math.Round(citations.Average(x => Math.Clamp(x.Score, 0, 1)), 2),
                NeedHumanReview = citations.Count == 0 || citations.Max(x => x.Score) < 0.65,
                ExecutionDurationMs = ToElapsedMilliseconds(stopwatch),
                FailureStage = failureStage,
                FailureReason = failureReason
            },
            citations,
            cancellationToken);
    }

    /// <summary>
    /// 为用户 query 生成检索向量。
    /// </summary>
    private async Task<ReadOnlyMemory<float>> GenerateQueryEmbeddingAsync(
        string query,
        AiModelSelection modelSelection,
        CancellationToken cancellationToken)
    {
        var result = await _embeddingClient.GenerateAsync(new KnowledgeEmbeddingClientRequest
        {
            Input = query,
            Provider = modelSelection.Provider,
            ModelName = modelSelection.Alias,
            ModelVersion = modelSelection.BusinessModelVersion,
            VectorDimension = modelSelection.EmbeddingDimension ?? _options.Embedding.VectorDimension,
            CollectionName = modelSelection.CollectionName ?? _options.Embedding.CollectionName
        }, cancellationToken);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.FailureReason ?? "Query embedding generation failed.");
        }

        return result.Embedding;
    }

    /// <summary>
    /// 对 Qdrant 候选命中执行 PostgreSQL 二次状态和权限校验。
    /// </summary>
    private async Task<IReadOnlyList<RetrievedChunk>> ApplyBusinessAuthorizationAsync(
        IReadOnlyList<VectorSearchHit> vectorHits,
        KnowledgeAccessContext accessContext,
        Guid? tenantId,
        int topK,
        IReadOnlyList<Guid> selectedKnowledgeIds,
        CancellationToken cancellationToken)
    {
        if (vectorHits.Count == 0)
        {
            return Array.Empty<RetrievedChunk>();
        }

        var chunksById = await GetChunksByIdAsync(vectorHits.Select(x => x.ChunkId), cancellationToken);
        if (chunksById.Count == 0)
        {
            return Array.Empty<RetrievedChunk>();
        }

        // 批量读取候选 chunk 关联的版本、知识文档和权限范围，避免在循环内逐条打数据库。
        // 这里读取的是 PostgreSQL 业务事实，优先级高于 Qdrant payload 中的旧快照。
        var versionsById = await GetVersionsByIdAsync(chunksById.Values.Select(x => x.KnowledgeVersionId), cancellationToken);
        var itemsById = await GetItemsByIdAsync(chunksById.Values.Select(x => x.KnowledgeItemId), cancellationToken);
        var scopesByItemId = await GetScopeLookupAsync(chunksById.Values.Select(x => x.KnowledgeItemId), cancellationToken);
        var results = new List<RetrievedChunk>();
        var returnedChunkIds = new HashSet<Guid>();

        foreach (var hit in vectorHits)
        {
            if (!chunksById.TryGetValue(hit.ChunkId, out var chunk) ||
                !versionsById.TryGetValue(chunk.KnowledgeVersionId, out var version) ||
                !itemsById.TryGetValue(chunk.KnowledgeItemId, out var item))
            {
                continue;
            }

            if ((selectedKnowledgeIds.Count > 0 && !selectedKnowledgeIds.Contains(item.Id)) ||
                !CanReturnChunk(chunk, version, item, scopesByItemId.GetValueOrDefault(item.Id) ?? new List<KnowledgePermissionScope>(), accessContext, tenantId))
            {
                continue;
            }

            if (!returnedChunkIds.Add(chunk.Id))
            {
                continue;
            }

            results.Add(new RetrievedChunk
            {
                ChunkId = chunk.Id,
                KnowledgeItemId = item.Id,
                KnowledgeVersionId = version.Id,
                Content = chunk.Content,
                Score = hit.Score,
                SourceTitle = string.IsNullOrWhiteSpace(chunk.SourceTitle) ? item.Title : chunk.SourceTitle,
                SourcePage = chunk.SourcePage
            });

            if (results.Count >= topK)
            {
                break;
            }
        }

        return results;
    }

    /// <summary>
    /// 按 chunk id 批量读取知识分块。
    /// </summary>
    private async Task<Dictionary<Guid, KnowledgeChunk>> GetChunksByIdAsync(
        IEnumerable<Guid> chunkIds,
        CancellationToken cancellationToken)
    {
        var ids = chunkIds.Where(x => x != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, KnowledgeChunk>();
        }

        var queryable = await _chunkRepository.GetQueryableAsync();
        var chunks = await AsyncExecuter.ToListAsync(queryable.Where(x => ids.Contains(x.Id)), cancellationToken);
        return chunks.ToDictionary(x => x.Id);
    }

    /// <summary>
    /// 按版本 id 批量读取知识版本。
    /// </summary>
    private async Task<Dictionary<Guid, KnowledgeVersion>> GetVersionsByIdAsync(
        IEnumerable<Guid> versionIds,
        CancellationToken cancellationToken)
    {
        var ids = versionIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, KnowledgeVersion>();
        }

        var queryable = await _versionRepository.GetQueryableAsync();
        var versions = await AsyncExecuter.ToListAsync(queryable.Where(x => ids.Contains(x.Id)), cancellationToken);
        return versions.ToDictionary(x => x.Id);
    }

    /// <summary>
    /// 按知识条目 id 批量读取知识条目。
    /// </summary>
    private async Task<Dictionary<Guid, KnowledgeItem>> GetItemsByIdAsync(
        IEnumerable<Guid> itemIds,
        CancellationToken cancellationToken)
    {
        var ids = itemIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, KnowledgeItem>();
        }

        var queryable = await _itemRepository.GetQueryableAsync();
        var items = await AsyncExecuter.ToListAsync(queryable.Where(x => ids.Contains(x.Id)), cancellationToken);
        return items.ToDictionary(x => x.Id);
    }

    /// <summary>
    /// 按知识条目 id 批量读取启用的权限范围。
    /// </summary>
    private async Task<Dictionary<Guid, List<KnowledgePermissionScope>>> GetScopeLookupAsync(
        IEnumerable<Guid> itemIds,
        CancellationToken cancellationToken)
    {
        var ids = itemIds.Distinct().ToList();
        var lookup = ids.ToDictionary(id => id, _ => new List<KnowledgePermissionScope>());
        if (ids.Count == 0)
        {
            return lookup;
        }

        var queryable = await _permissionScopeRepository.GetQueryableAsync();
        var scopes = await AsyncExecuter.ToListAsync(
            queryable.Where(x => ids.Contains(x.KnowledgeItemId) && x.IsEnabled),
            cancellationToken);

        foreach (var scope in scopes)
        {
            lookup[scope.KnowledgeItemId].Add(scope);
        }

        return lookup;
    }

    /// <summary>
    /// 判断某个 chunk 在当前主库状态和访问上下文下是否可返回。
    /// </summary>
    private bool CanReturnChunk(
        KnowledgeChunk chunk,
        KnowledgeVersion version,
        KnowledgeItem item,
        IReadOnlyList<KnowledgePermissionScope> scopes,
        KnowledgeAccessContext accessContext,
        Guid? tenantId)
    {
        // 返回条件故意同时校验 chunk 快照、KnowledgeItem 当前发布版本和 KnowledgeVersion 状态。
        // 这样即使 Qdrant 中存在旧 payload，权限收窄、下架、归档或停用后也不会泄漏 citation。
        return chunk.IsActive &&
               chunk.IsPublishedSnapshot &&
               item.IsActive &&
               item.CurrentPublishedVersionId == version.Id &&
               version.KnowledgeItemId == item.Id &&
               version.Status == KnowledgeVersionStatus.Published &&
               version.IsCurrentPublished &&
               TenantMatches(chunk.TenantId, tenantId) &&
               TenantMatches(version.TenantId, tenantId) &&
               TenantMatches(item.TenantId, tenantId) &&
               KnowledgeAccessEvaluator.CanAccess(item, scopes, accessContext, IsLegacyAdministrator);
    }

    /// <summary>
    /// 判断实体租户与当前检索租户是否一致。
    /// </summary>
    private static bool TenantMatches(Guid? entityTenantId, Guid? tenantId)
    {
        return entityTenantId == tenantId;
    }

    /// <summary>
    /// 根据请求 TopK 计算向量库候选召回数量。
    /// </summary>
    private static int CalculateCandidateLimit(int topK)
    {
        if (topK > MaxCandidateLimit / MaxCandidateMultiplier)
        {
            return MaxCandidateLimit;
        }

        return Math.Min(Math.Max(topK * MaxCandidateMultiplier, topK), MaxCandidateLimit);
    }

    /// <summary>
    /// 将 Stopwatch 耗时转换为 int 毫秒值。
    /// </summary>
    private static int ToElapsedMilliseconds(Stopwatch stopwatch)
    {
        return stopwatch.ElapsedMilliseconds > int.MaxValue
            ? int.MaxValue
            : (int)stopwatch.ElapsedMilliseconds;
    }
}
