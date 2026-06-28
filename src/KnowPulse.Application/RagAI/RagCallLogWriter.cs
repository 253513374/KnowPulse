using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Volo.Abp.Users;

namespace KnowPulse.RagAI;

/// <summary>
/// RAG 调用日志写入器。
/// Search、Answer 和后续评测任务都通过它写入脱敏日志，避免各自实现 hash、摘要和 citation 规则。
/// </summary>
public interface IRagCallLogWriter
{
    /// <summary>
    /// 写入一次 RAG 调用日志及其 citation 记录。
    /// </summary>
    Task<RagCallLog> WriteAsync(
        RagCallLogWriteRequest request,
        IReadOnlyList<RetrievedChunk> citations,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// RAG 调用日志写入请求。
/// Query 可以是完整用户问题，但写入器只会保存 hash 和受控长度摘要。
/// </summary>
public class RagCallLogWriteRequest
{
    /// <summary>
    /// 调用类型：检索或问答。
    /// </summary>
    public RagCallType CallType { get; set; }

    /// <summary>
    /// 调用结果状态。
    /// </summary>
    public RagCallStatus Status { get; set; }

    /// <summary>
    /// 原始用户问题或检索 query。写库前会被规范化、hash 和截断。
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// 模型 provider 逻辑名称。
    /// </summary>
    public string ModelProvider { get; set; } = string.Empty;

    /// <summary>
    /// 模型 alias 或模型名称。
    /// </summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>
    /// 业务侧模型版本。
    /// </summary>
    public string ModelVersion { get; set; } = string.Empty;

    /// <summary>
    /// 检索使用的向量 collection。
    /// </summary>
    public string CollectionName { get; set; } = string.Empty;

    /// <summary>
    /// 请求的 TopK。
    /// </summary>
    public int TopK { get; set; }

    /// <summary>
    /// 通过授权校验并返回给上层的 chunk 数。
    /// </summary>
    public int RetrievedChunkCount { get; set; }

    /// <summary>
    /// prompt token 数。没有网关 usage 时由调用方传估算值。
    /// </summary>
    public int PromptTokenCount { get; set; }

    /// <summary>
    /// completion token 数。没有网关 usage 时由调用方传估算值。
    /// </summary>
    public int CompletionTokenCount { get; set; }

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
    /// 调用耗时，单位毫秒。
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
}

/// <summary>
/// 默认 RAG 调用日志写入器。
/// 使用独立非事务 UnitOfWork 写入审计日志，尽量保留失败调用的排障线索。
/// </summary>
public class RagCallLogWriter : IRagCallLogWriter, ITransientDependency
{
    private readonly IRepository<RagCallLog, Guid> _callLogRepository;
    private readonly IRepository<RagCitationRecord, Guid> _citationRepository;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    /// <summary>
    /// 创建 RAG 调用日志写入器。
    /// </summary>
    public RagCallLogWriter(
        IRepository<RagCallLog, Guid> callLogRepository,
        IRepository<RagCitationRecord, Guid> citationRepository,
        IGuidGenerator guidGenerator,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _callLogRepository = callLogRepository;
        _citationRepository = citationRepository;
        _guidGenerator = guidGenerator;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
        _unitOfWorkManager = unitOfWorkManager;
    }

    /// <summary>
    /// 写入一条 RAG 调用日志及其 citation 记录。
    /// </summary>
    public async Task<RagCallLog> WriteAsync(
        RagCallLogWriteRequest request,
        IReadOnlyList<RetrievedChunk> citations,
        CancellationToken cancellationToken = default)
    {
        // 日志写入使用独立 UnitOfWork：即使外层 RAG 调用最终失败或回滚，
        // 仍尽量保留一次可排查的调用记录。审计日志不是业务状态的事务参与者。
        using var uow = _unitOfWorkManager.Begin(
            new AbpUnitOfWorkOptions { IsTransactional = false },
            requiresNew: true);

        var normalizedQuery = NormalizeInline(request.Query);
        // QueryHash 用于排查重复问题和评测复用；QuerySummary 只保留受控长度文本，
        // 避免把用户完整问题或可能包含的敏感内容长期保存。
        var callLog = new RagCallLog(
            _guidGenerator.Create(),
            _currentTenant.Id,
            _currentUser.Id,
            request.CallType,
            request.Status,
            ComputeHash(normalizedQuery),
            Trim(normalizedQuery, RagCallLog.MaxQuerySummaryLength),
            request.ModelProvider,
            request.ModelName,
            request.ModelVersion,
            request.CollectionName,
            request.TopK,
            request.RetrievedChunkCount,
            citations.Count,
            request.PromptTokenCount,
            request.CompletionTokenCount,
            request.EstimatedCostUsd,
            request.Confidence,
            request.NeedHumanReview,
            request.ExecutionDurationMs,
            TrimOptional(request.FailureStage, RagCallLog.MaxFailureStageLength),
            TrimOptional(request.FailureReason, RagCallLog.MaxFailureReasonLength));

        callLog = await _callLogRepository.InsertAsync(callLog, autoSave: true, cancellationToken: cancellationToken);

        for (var index = 0; index < citations.Count; index++)
        {
            var citation = citations[index];
            // citation 保存 quote hash 和短 preview，而不是完整 chunk。
            // 真正的知识正文仍以 KnowledgeChunk / KnowledgeVersion 为准，审计表只负责串联来源链路。
            var quotePreview = Trim(NormalizeInline(citation.Content), RagCitationRecord.MaxQuotePreviewLength);
            await _citationRepository.InsertAsync(
                new RagCitationRecord(
                    _guidGenerator.Create(),
                    _currentTenant.Id,
                    callLog.Id,
                    citation.KnowledgeItemId,
                    citation.KnowledgeVersionId,
                    citation.ChunkId,
                    index + 1,
                    citation.Score,
                    citation.SourceTitle,
                    citation.SourcePage,
                    ComputeHash(citation.Content),
                    quotePreview),
                autoSave: true,
                cancellationToken: cancellationToken);
        }

        await uow.CompleteAsync(cancellationToken);
        return callLog;
    }

    /// <summary>
    /// 粗略估算文本 token 数。
    /// </summary>
    public static int EstimateTokenCount(string? value)
    {
        // 模型不可用或返回 usage 为空时使用粗略估算，保证审计字段不为空。
        // 正常 DeepSeek/LiteLLM chat 调用会优先写入网关返回的真实 usage。
        return string.IsNullOrWhiteSpace(value) ? 0 : Math.Max(1, value.Length / 2);
    }

    /// <summary>
    /// 将多行文本规范化为单行摘要文本。
    /// </summary>
    private static string NormalizeInline(string? value)
    {
        return (value ?? string.Empty)
            .Replace("\r\n", " ")
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }

    /// <summary>
    /// 将字符串截断到最大长度。
    /// </summary>
    private static string Trim(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength].TrimEnd();
    }

    /// <summary>
    /// 规范化并截断可空字符串。
    /// </summary>
    private static string? TrimOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Trim(NormalizeInline(value), maxLength);
    }

    /// <summary>
    /// 计算文本的 SHA-256 hash。
    /// </summary>
    private static string ComputeHash(string value)
    {
        // 使用 SHA-256 保存可比对指纹，避免为了追踪问题/引用而保存完整敏感文本。
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
