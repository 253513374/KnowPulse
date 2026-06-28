using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.RagAI;

/// <summary>
/// RAG 调用日志。该表只保存摘要、hash、模型元数据和结果状态，
/// 不保存完整问题、完整 prompt、完整回答或完整知识正文。
/// </summary>
public class RagCallLog : CreationAuditedAggregateRoot<Guid>, IMultiTenant
{
    public const int MaxQueryHashLength = 64;
    public const int MaxQuerySummaryLength = 256;
    public const int MaxModelProviderLength = 64;
    public const int MaxModelNameLength = 128;
    public const int MaxModelVersionLength = 64;
    public const int MaxCollectionNameLength = 128;
    public const int MaxFailureStageLength = 64;
    public const int MaxFailureReasonLength = 512;

    public Guid? TenantId { get; protected set; }

    public Guid? UserId { get; private set; }

    public RagCallType CallType { get; private set; }

    public RagCallStatus Status { get; private set; }

    public string QueryHash { get; private set; } = string.Empty;

    public string QuerySummary { get; private set; } = string.Empty;

    public string ModelProvider { get; private set; } = string.Empty;

    public string ModelName { get; private set; } = string.Empty;

    public string ModelVersion { get; private set; } = string.Empty;

    public string CollectionName { get; private set; } = string.Empty;

    public int TopK { get; private set; }

    public int RetrievedChunkCount { get; private set; }

    public int CitationCount { get; private set; }

    public int PromptTokenCount { get; private set; }

    public int CompletionTokenCount { get; private set; }

    public int TotalTokenCount { get; private set; }

    public decimal EstimatedCostUsd { get; private set; }

    public double? Confidence { get; private set; }

    public bool NeedHumanReview { get; private set; }

    public int ExecutionDurationMs { get; private set; }

    public string? FailureStage { get; private set; }

    public string? FailureReason { get; private set; }

    /// <summary>
    /// EF Core 使用的受保护构造函数。
    /// </summary>
    protected RagCallLog()
    {
    }

    /// <summary>
    /// 创建一次 RAG 调用审计日志。
    /// 构造函数只接受摘要、hash、模型元数据和统计字段，避免调用方误把完整 prompt 或原文写入日志。
    /// </summary>
    public RagCallLog(
        Guid id,
        Guid? tenantId,
        Guid? userId,
        RagCallType callType,
        RagCallStatus status,
        string queryHash,
        string querySummary,
        string modelProvider,
        string modelName,
        string modelVersion,
        string collectionName,
        int topK,
        int retrievedChunkCount,
        int citationCount,
        int promptTokenCount,
        int completionTokenCount,
        decimal estimatedCostUsd,
        double? confidence,
        bool needHumanReview,
        int executionDurationMs,
        string? failureStage = null,
        string? failureReason = null)
        : base(id)
    {
        if (topK < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(topK));
        }

        if (retrievedChunkCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retrievedChunkCount));
        }

        if (citationCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(citationCount));
        }

        if (promptTokenCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(promptTokenCount));
        }

        if (completionTokenCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(completionTokenCount));
        }

        if (estimatedCostUsd < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(estimatedCostUsd));
        }

        if (executionDurationMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(executionDurationMs));
        }

        TenantId = tenantId;
        UserId = userId;
        CallType = callType;
        Status = status;
        QueryHash = Check.NotNullOrWhiteSpace(queryHash, nameof(queryHash), MaxQueryHashLength);
        QuerySummary = Check.NotNull(querySummary, nameof(querySummary)).Length <= MaxQuerySummaryLength
            ? querySummary
            : querySummary[..MaxQuerySummaryLength];
        ModelProvider = Check.NotNullOrWhiteSpace(modelProvider, nameof(modelProvider), MaxModelProviderLength);
        ModelName = Check.NotNullOrWhiteSpace(modelName, nameof(modelName), MaxModelNameLength);
        ModelVersion = Check.NotNullOrWhiteSpace(modelVersion, nameof(modelVersion), MaxModelVersionLength);
        CollectionName = Check.NotNullOrWhiteSpace(collectionName, nameof(collectionName), MaxCollectionNameLength);
        TopK = topK;
        RetrievedChunkCount = retrievedChunkCount;
        CitationCount = citationCount;
        PromptTokenCount = promptTokenCount;
        CompletionTokenCount = completionTokenCount;
        TotalTokenCount = promptTokenCount + completionTokenCount;
        EstimatedCostUsd = estimatedCostUsd;
        Confidence = confidence;
        NeedHumanReview = needHumanReview;
        ExecutionDurationMs = executionDurationMs;
        FailureStage = Check.Length(failureStage, nameof(failureStage), MaxFailureStageLength);
        FailureReason = Check.Length(failureReason, nameof(failureReason), MaxFailureReasonLength);
    }
}
