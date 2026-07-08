using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.AiModelManagement;
using KnowPulse.KnowledgeIndexing;
using Microsoft.Extensions.Options;
using Volo.Abp.Domain.Repositories;

namespace KnowPulse.RagAI;

/// <summary>
/// Embedding pipeline，负责把 active chunk 送入 embedding client，并把模型版本、维度、
/// content hash 与外部向量 ID 写入业务主库。真实向量写入 Qdrant 由 IKnowledgeVectorStore 后续阶段处理。
/// </summary>
public class KnowledgeEmbeddingPipeline : KnowPulseAppService, IKnowledgeEmbeddingPipeline
{
    private readonly IRepository<EmbeddingModelVersion, Guid> _modelVersionRepository;
    private readonly IRepository<KnowledgeChunk, Guid> _chunkRepository;
    private readonly IRepository<KnowledgeEmbeddingRecord, Guid> _embeddingRecordRepository;
    private readonly IRepository<KnowledgeIndexJob, Guid> _jobRepository;
    private readonly IKnowledgeEmbeddingClient _embeddingClient;
    private readonly IAiModelResolver _modelResolver;
    private readonly RagAiOptions _options;

    /// <summary>
    /// 创建 embedding 生成管道。
    /// </summary>
    public KnowledgeEmbeddingPipeline(
        IRepository<EmbeddingModelVersion, Guid> modelVersionRepository,
        IRepository<KnowledgeChunk, Guid> chunkRepository,
        IRepository<KnowledgeEmbeddingRecord, Guid> embeddingRecordRepository,
        IRepository<KnowledgeIndexJob, Guid> jobRepository,
        IKnowledgeEmbeddingClient embeddingClient,
        IAiModelResolver modelResolver,
        IOptions<RagAiOptions> options)
    {
        _modelVersionRepository = modelVersionRepository;
        _chunkRepository = chunkRepository;
        _embeddingRecordRepository = embeddingRecordRepository;
        _jobRepository = jobRepository;
        _embeddingClient = embeddingClient;
        _modelResolver = modelResolver;
        _options = options.Value;
    }

    /// <summary>
    /// 为指定知识版本的 active chunk 生成 embedding 追踪记录。
    /// </summary>
    public virtual async Task<KnowledgeEmbeddingRunResult> GenerateForVersionAsync(
        Guid knowledgeVersionId,
        Guid? embeddingModelVersionId = null,
        CancellationToken cancellationToken = default)
    {
        // 模型版本是 embedding 记录的事实维度。没有显式指定时，使用 RagAI 配置创建/复用默认版本。
        // 维度或 collection 变化时应形成新的 EmbeddingModelVersion，而不是覆盖旧记录。
        var modelVersion = embeddingModelVersionId.HasValue
            ? await _modelVersionRepository.GetAsync(embeddingModelVersionId.Value, cancellationToken: cancellationToken)
            : await GetOrCreateDefaultModelVersionAsync(cancellationToken);

        var job = await GetLatestIndexJobAsync(knowledgeVersionId, cancellationToken);
        if (job != null)
        {
            // 把任务和模型版本绑定，便于排查“哪次索引用了哪个模型/collection”。
            job.SetEmbeddingModelVersion(modelVersion.Id);
            await _jobRepository.UpdateAsync(job, autoSave: true, cancellationToken: cancellationToken);
        }

        var chunks = await GetActiveChunksAsync(knowledgeVersionId, cancellationToken);
        if (chunks.Count == 0)
        {
            await MarkJobFailedAsync(job, "No active chunks found for embedding.", cancellationToken);
            return Failed(knowledgeVersionId, modelVersion.Id, "No active chunks found for embedding.");
        }

        var processedCount = 0;
        foreach (var chunk in chunks)
        {
            // embedding client 是唯一允许触达 LiteLLM/模型网关的边界。
            // pipeline 不直接依赖供应商 SDK，保证模型替换不影响业务服务。
            var clientResult = await _embeddingClient.GenerateAsync(new KnowledgeEmbeddingClientRequest
            {
                Input = chunk.Content,
                Provider = modelVersion.Provider,
                ModelName = modelVersion.ModelName,
                ModelVersion = modelVersion.ModelVersion,
                VectorDimension = modelVersion.VectorDimension,
                CollectionName = modelVersion.CollectionName
            }, cancellationToken);

            if (!clientResult.Succeeded)
            {
                var reason = clientResult.FailureReason ?? "Embedding generation failed.";
                await MarkJobFailedAsync(job, reason, cancellationToken);
                return Failed(knowledgeVersionId, modelVersion.Id, reason, clientResult.FailureStage ?? "embedding");
            }

            if (clientResult.Embedding.Length != modelVersion.VectorDimension)
            {
                var reason = $"Embedding dimension mismatch. Expected {modelVersion.VectorDimension}, actual {clientResult.Embedding.Length}.";
                await MarkJobFailedAsync(job, reason, cancellationToken);
                return Failed(knowledgeVersionId, modelVersion.Id, reason);
            }

            if (await HasFreshEmbeddingRecordAsync(chunk, modelVersion, cancellationToken))
            {
                // content hash、模型版本和维度都一致时跳过，保证重复执行 pipeline 是幂等的。
                continue;
            }

            // 同一 chunk + 模型版本只保留一条 active embedding 记录；旧记录标记删除而不是物理覆盖。
            // 这能保留审计轨迹，同时让重建查询只关注 active 记录。
            await MarkExistingEmbeddingRecordsDeletedAsync(chunk.Id, modelVersion.Id, cancellationToken);
            await _embeddingRecordRepository.InsertAsync(
                new KnowledgeEmbeddingRecord(
                    GuidGenerator.Create(),
                    CurrentTenant.Id,
                    chunk.Id,
                    chunk.KnowledgeVersionId,
                    modelVersion.Id,
                    BuildVectorStoreRecordId(modelVersion.CollectionName, chunk),
                    chunk.ContentHash,
                    modelVersion.VectorDimension,
                    DateTime.UtcNow),
                autoSave: true,
                cancellationToken: cancellationToken);
            processedCount++;
        }

        if (job != null)
        {
            job.MarkIndexed(DateTime.UtcNow);
            await _jobRepository.UpdateAsync(job, autoSave: true, cancellationToken: cancellationToken);
        }

        return new KnowledgeEmbeddingRunResult
        {
            KnowledgeVersionId = knowledgeVersionId,
            EmbeddingModelVersionId = modelVersion.Id,
            Succeeded = true,
            ProcessedChunkCount = processedCount
        };
    }

    /// <summary>
    /// 查询指定模型版本下需要重建 embedding 的 chunk 列表。
    /// </summary>
    public virtual async Task<IReadOnlyList<KnowledgeChunkEmbeddingRebuildDto>> GetChunksRequiringRebuildAsync(
        Guid embeddingModelVersionId,
        CancellationToken cancellationToken = default)
    {
        // 该接口用于模型升级或内容变更后的重建计划：只比较主库事实，
        // 不依赖 Qdrant 当前是否仍有对应向量。
        var modelVersion = await _modelVersionRepository.GetAsync(embeddingModelVersionId, cancellationToken: cancellationToken);
        var chunkQueryable = await _chunkRepository.GetQueryableAsync();
        var chunks = await AsyncExecuter.ToListAsync(
            chunkQueryable.Where(x => x.IsActive).OrderBy(x => x.KnowledgeVersionId).ThenBy(x => x.Sequence),
            cancellationToken);

        if (chunks.Count == 0)
        {
            return Array.Empty<KnowledgeChunkEmbeddingRebuildDto>();
        }

        var chunkIds = chunks.Select(x => x.Id).ToList();
        var recordQueryable = await _embeddingRecordRepository.GetQueryableAsync();
        var records = await AsyncExecuter.ToListAsync(
            recordQueryable.Where(x => x.EmbeddingModelVersionId == modelVersion.Id &&
                                       x.IsActive &&
                                       chunkIds.Contains(x.KnowledgeChunkId)),
            cancellationToken);
        var recordsByChunkId = records
            .GroupBy(x => x.KnowledgeChunkId)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(r => r.IndexedTime).First());

        return chunks
            .Select(chunk => CreateRebuildDto(chunk, modelVersion, recordsByChunkId.GetValueOrDefault(chunk.Id)))
            .Where(x => x != null)
            .Cast<KnowledgeChunkEmbeddingRebuildDto>()
            .ToList();
    }

    /// <summary>
    /// 获取或创建当前配置对应的默认 embedding 模型版本。
    /// </summary>
    private async Task<EmbeddingModelVersion> GetOrCreateDefaultModelVersionAsync(CancellationToken cancellationToken)
    {
        var modelSelection = await _modelResolver.ResolveEmbeddingAsync(cancellationToken);
        var provider = modelSelection.Provider;
        var modelName = modelSelection.Alias;
        var modelVersionValue = modelSelection.BusinessModelVersion;
        var vectorDimension = modelSelection.EmbeddingDimension ?? _options.Embedding.VectorDimension;
        var collectionName = modelSelection.CollectionName ?? _options.Embedding.CollectionName;

        var queryable = await _modelVersionRepository.GetQueryableAsync();
        var existing = await AsyncExecuter.FirstOrDefaultAsync(
            queryable.Where(x => x.Provider == provider &&
                                 x.ModelName == modelName &&
                                 x.ModelVersion == modelVersionValue &&
                                 x.CollectionName == collectionName &&
                                 x.VectorDimension == vectorDimension),
            cancellationToken);

        if (existing != null)
        {
            if (!existing.IsDefault)
            {
                existing.MarkAsDefault();
                return await _modelVersionRepository.UpdateAsync(existing, autoSave: true, cancellationToken: cancellationToken);
            }

            return existing;
        }

        var modelVersion = new EmbeddingModelVersion(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            provider,
            modelName,
            modelVersionValue,
            vectorDimension,
            collectionName,
            isDefault: true,
            description: "Default embedding model resolved from AiModelManagement usage binding.");

        return await _modelVersionRepository.InsertAsync(modelVersion, autoSave: true, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 获取指定知识版本最近的可关联索引任务。
    /// </summary>
    private async Task<KnowledgeIndexJob?> GetLatestIndexJobAsync(Guid knowledgeVersionId, CancellationToken cancellationToken)
    {
        var queryable = await _jobRepository.GetQueryableAsync();
        return await AsyncExecuter.FirstOrDefaultAsync(
            queryable
                .Where(x => x.KnowledgeVersionId == knowledgeVersionId)
                .Where(x => x.Status == KnowledgeIndexJobStatus.Pending ||
                            x.Status == KnowledgeIndexJobStatus.Processing ||
                            x.Status == KnowledgeIndexJobStatus.Indexed)
                .OrderByDescending(x => x.CreationTime),
            cancellationToken);
    }

    /// <summary>
    /// 读取指定知识版本当前 active 的 chunk。
    /// </summary>
    private async Task<List<KnowledgeChunk>> GetActiveChunksAsync(Guid knowledgeVersionId, CancellationToken cancellationToken)
    {
        var queryable = await _chunkRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(
            queryable
                .Where(x => x.KnowledgeVersionId == knowledgeVersionId && x.IsActive)
                .OrderBy(x => x.Sequence),
            cancellationToken);
    }

    /// <summary>
    /// 判断 chunk 在指定模型版本下是否已有可复用的 active embedding 记录。
    /// </summary>
    private async Task<bool> HasFreshEmbeddingRecordAsync(
        KnowledgeChunk chunk,
        EmbeddingModelVersion modelVersion,
        CancellationToken cancellationToken)
    {
        var queryable = await _embeddingRecordRepository.GetQueryableAsync();
        return await AsyncExecuter.AnyAsync(
            queryable.Where(x => x.KnowledgeChunkId == chunk.Id &&
                                 x.EmbeddingModelVersionId == modelVersion.Id &&
                                 x.IsActive &&
                                 x.ContentHash == chunk.ContentHash &&
                                 x.VectorDimension == modelVersion.VectorDimension),
            cancellationToken);
    }

    /// <summary>
    /// 将同一 chunk 和模型版本下的旧 active embedding 记录标记为已删除。
    /// </summary>
    private async Task MarkExistingEmbeddingRecordsDeletedAsync(
        Guid chunkId,
        Guid modelVersionId,
        CancellationToken cancellationToken)
    {
        var queryable = await _embeddingRecordRepository.GetQueryableAsync();
        var records = await AsyncExecuter.ToListAsync(
            queryable.Where(x => x.KnowledgeChunkId == chunkId &&
                                 x.EmbeddingModelVersionId == modelVersionId &&
                                 x.IsActive),
            cancellationToken);

        foreach (var record in records)
        {
            record.MarkDeleted(DateTime.UtcNow);
            await _embeddingRecordRepository.UpdateAsync(record, autoSave: true, cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// 将关联索引任务标记为 embedding 阶段失败。
    /// </summary>
    private async Task MarkJobFailedAsync(KnowledgeIndexJob? job, string reason, CancellationToken cancellationToken)
    {
        if (job == null)
        {
            return;
        }

        job.MarkFailed("embedding", reason, DateTime.UtcNow);
        await _jobRepository.UpdateAsync(job, autoSave: true, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 创建 embedding pipeline 的失败返回结果。
    /// </summary>
    private static KnowledgeEmbeddingRunResult Failed(
        Guid knowledgeVersionId,
        Guid modelVersionId,
        string reason,
        string failureStage = "embedding")
    {
        return new KnowledgeEmbeddingRunResult
        {
            KnowledgeVersionId = knowledgeVersionId,
            EmbeddingModelVersionId = modelVersionId,
            Succeeded = false,
            FailureStage = failureStage,
            FailureReason = reason
        };
    }

    /// <summary>
    /// 根据 chunk 和已有记录判断是否需要重建 embedding。
    /// </summary>
    private static KnowledgeChunkEmbeddingRebuildDto? CreateRebuildDto(
        KnowledgeChunk chunk,
        EmbeddingModelVersion modelVersion,
        KnowledgeEmbeddingRecord? record)
    {
        // 三类需要重建的原因分别对应：从未生成、内容改变、模型维度不匹配。
        // 调用方可以据此决定是增量补齐还是新 collection 全量重建。
        if (record == null)
        {
            return new KnowledgeChunkEmbeddingRebuildDto
            {
                KnowledgeChunkId = chunk.Id,
                KnowledgeVersionId = chunk.KnowledgeVersionId,
                EmbeddingModelVersionId = modelVersion.Id,
                ContentHash = chunk.ContentHash,
                Reason = "MissingEmbeddingRecord"
            };
        }

        if (record.ContentHash != chunk.ContentHash)
        {
            return new KnowledgeChunkEmbeddingRebuildDto
            {
                KnowledgeChunkId = chunk.Id,
                KnowledgeVersionId = chunk.KnowledgeVersionId,
                EmbeddingModelVersionId = modelVersion.Id,
                ContentHash = chunk.ContentHash,
                Reason = "ContentHashChanged"
            };
        }

        if (record.VectorDimension != modelVersion.VectorDimension)
        {
            return new KnowledgeChunkEmbeddingRebuildDto
            {
                KnowledgeChunkId = chunk.Id,
                KnowledgeVersionId = chunk.KnowledgeVersionId,
                EmbeddingModelVersionId = modelVersion.Id,
                ContentHash = chunk.ContentHash,
                Reason = "VectorDimensionChanged"
            };
        }

        return null;
    }

    /// <summary>
    /// 构造外部向量库记录编号。
    /// </summary>
    private static string BuildVectorStoreRecordId(string collectionName, KnowledgeChunk chunk)
    {
        return chunk.Id.ToString("D");
    }
}
