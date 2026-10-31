using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace KnowPulse.RagAI;

/// <summary>
/// RAG 调用日志查询服务。
/// 该服务只提供审计查询，不负责写入日志；写入统一由 IRagCallLogWriter 处理脱敏和 citation 链路。
/// </summary>
[Authorize(KnowPulsePermissions.Rag.AuditLogs)]
public class RagCallLogAppService : KnowPulseAppService, IRagCallLogAppService
{
    private readonly IRepository<RagCallLog, Guid> _callLogRepository;
    private readonly IRepository<RagCitationRecord, Guid> _citationRepository;

    /// <summary>
    /// 创建 RAG 调用日志查询服务。
    /// </summary>
    public RagCallLogAppService(
        IRepository<RagCallLog, Guid> callLogRepository,
        IRepository<RagCitationRecord, Guid> citationRepository)
    {
        _callLogRepository = callLogRepository;
        _citationRepository = citationRepository;
    }

    /// <summary>
    /// 分页查询 RAG 调用日志，并附带每条日志的 citation 链路。
    /// </summary>
    public virtual async Task<PagedResultDto<RagCallLogDto>> GetListAsync(GetRagCallLogListInput input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Rag.AuditLogs);
        var sourceAccess = LazyServiceProvider.LazyGetRequiredService<KnowledgeSourceAccessGuard>();
        var query = (await _callLogRepository.GetQueryableAsync())
            .Where(x => x.TenantId == CurrentTenant.Id &&
                        (x.PersonalOwnerUserId == null || x.PersonalOwnerUserId == CurrentUser.Id));

        // 每个过滤条件都保持可选，支持审计页按人、模型、时间、调用类型或知识版本逐步缩小范围。
        if (input.UserId.HasValue)
        {
            query = query.Where(x => x.UserId == input.UserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(input.ModelVersion))
        {
            query = query.Where(x => x.ModelVersion == input.ModelVersion);
        }

        if (input.CallType.HasValue)
        {
            query = query.Where(x => x.CallType == input.CallType.Value);
        }

        if (input.Status.HasValue)
        {
            query = query.Where(x => x.Status == input.Status.Value);
        }

        if (input.StartTime.HasValue)
        {
            query = query.Where(x => x.CreationTime >= input.StartTime.Value);
        }

        if (input.EndTime.HasValue)
        {
            query = query.Where(x => x.CreationTime <= input.EndTime.Value);
        }

        if (input.KnowledgeVersionId.HasValue)
        {
            // 知识版本过滤需要先走 citation 表，因为一次问答可能引用多个版本。
            var citationQueryable = await _citationRepository.GetQueryableAsync();
            var callIds = await AsyncExecuter.ToListAsync(
                citationQueryable
                    .Where(x => x.KnowledgeVersionId == input.KnowledgeVersionId.Value)
                    .Select(x => x.RagCallLogId)
                    .Distinct());

            if (callIds.Count == 0)
            {
                return new PagedResultDto<RagCallLogDto>(0, Array.Empty<RagCallLogDto>());
            }

            query = query.Where(x => callIds.Contains(x.Id));
        }

        var logs = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.CreationTime));
        var citationsByCallId = await GetCitationsByCallIdAsync(logs.Select(x => x.Id));
        var visible = new List<RagCallLog>();
        foreach (var log in logs)
        {
            var canRead = true;
            foreach (var citation in citationsByCallId.GetValueOrDefault(log.Id) ?? new List<RagCitationRecord>())
            {
                if (!await sourceAccess.CanReadSourceAsync(citation.KnowledgeItemId, citation.KnowledgeVersionId, citation.ChunkId))
                {
                    canRead = false;
                    break;
                }
            }

            if (canRead)
            {
                visible.Add(log);
            }
        }

        return new PagedResultDto<RagCallLogDto>(
            visible.Count,
            visible.Skip(input.SkipCount).Take(input.MaxResultCount)
                .Select(x => MapToDto(x, citationsByCallId.GetValueOrDefault(x.Id) ?? new List<RagCitationRecord>())).ToList());
    }

    /// <summary>
    /// 批量读取日志对应的 citation 记录并按调用日志分组。
    /// </summary>
    private async Task<Dictionary<Guid, List<RagCitationRecord>>> GetCitationsByCallIdAsync(IEnumerable<Guid> callLogIds)
    {
        var ids = callLogIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, List<RagCitationRecord>>();
        }

        var queryable = await _citationRepository.GetQueryableAsync();
        // 批量读取 citation，避免分页日志列表逐条查询来源链路。
        var citations = await AsyncExecuter.ToListAsync(
            queryable.Where(x => ids.Contains(x.RagCallLogId)).OrderBy(x => x.Rank));

        return citations
            .GroupBy(x => x.RagCallLogId)
            .ToDictionary(x => x.Key, x => x.ToList());
    }

    /// <summary>
    /// 将调用日志聚合和 citation 记录映射为审计 DTO。
    /// </summary>
    private static RagCallLogDto MapToDto(RagCallLog log, IReadOnlyList<RagCitationRecord> citations)
    {
        return new RagCallLogDto
        {
            Id = log.Id,
            TenantId = log.TenantId,
            UserId = log.UserId,
            CallType = log.CallType,
            Status = log.Status,
            QueryHash = log.QueryHash,
            QuerySummary = log.QuerySummary,
            ModelProvider = log.ModelProvider,
            ModelName = log.ModelName,
            ModelVersion = log.ModelVersion,
            CollectionName = log.CollectionName,
            TopK = log.TopK,
            RetrievedChunkCount = log.RetrievedChunkCount,
            CitationCount = log.CitationCount,
            PromptTokenCount = log.PromptTokenCount,
            CompletionTokenCount = log.CompletionTokenCount,
            TotalTokenCount = log.TotalTokenCount,
            EstimatedCostUsd = log.EstimatedCostUsd,
            Confidence = log.Confidence,
            NeedHumanReview = log.NeedHumanReview,
            ExecutionDurationMs = log.ExecutionDurationMs,
            FailureStage = log.FailureStage,
            FailureReason = log.FailureReason,
            CreationTime = log.CreationTime,
            Citations = citations.Select(MapCitationToDto).ToList()
        };
    }

    /// <summary>
    /// 将 citation 领域记录映射为审计 DTO。
    /// </summary>
    private static RagCitationRecordDto MapCitationToDto(RagCitationRecord citation)
    {
        return new RagCitationRecordDto
        {
            Id = citation.Id,
            RagCallLogId = citation.RagCallLogId,
            KnowledgeItemId = citation.KnowledgeItemId,
            KnowledgeVersionId = citation.KnowledgeVersionId,
            ChunkId = citation.ChunkId,
            Rank = citation.Rank,
            Score = citation.Score,
            SourceTitle = citation.SourceTitle,
            SourcePage = citation.SourcePage,
            QuoteHash = citation.QuoteHash,
            QuotePreview = citation.QuotePreview
        };
    }
}
