using System;
using KnowPulse.RagAI;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.AiQuestioning;

/// <summary>
/// AI 出题模型调用日志。
/// 日志用于排查生成失败、审计模型版本和统计 token 成本，只保存 query 摘要、hash 和模型元数据，
/// 不保存完整 prompt、完整模型响应或完整知识正文。
/// </summary>
public class AiQuestionModelCallLog : CreationAuditedEntity<Guid>, IMultiTenant
{
    /// <summary>
    /// prompt 版本最大长度。
    /// </summary>
    public const int MaxPromptVersionLength = 64;

    /// <summary>
    /// 检索 query hash 最大长度。
    /// </summary>
    public const int MaxQueryHashLength = 64;

    /// <summary>
    /// 检索 query 摘要最大长度。
    /// </summary>
    public const int MaxQuerySummaryLength = 256;

    /// <summary>
    /// 模型 provider 最大长度。
    /// </summary>
    public const int MaxModelProviderLength = 64;

    /// <summary>
    /// 模型 alias 最大长度。
    /// </summary>
    public const int MaxModelNameLength = 128;

    /// <summary>
    /// 业务侧模型版本最大长度。
    /// </summary>
    public const int MaxModelVersionLength = 64;

    /// <summary>
    /// 失败阶段最大长度，例如 retrieval、chat 或 parse。
    /// </summary>
    public const int MaxFailureStageLength = 64;

    /// <summary>
    /// 失败原因摘要最大长度。
    /// </summary>
    public const int MaxFailureReasonLength = 512;

    /// <summary>
    /// 所属租户。
    /// </summary>
    public Guid? TenantId { get; protected set; }

    /// <summary>
    /// 调用所属 AI 出题任务 ID。
    /// </summary>
    public Guid TaskId { get; private set; }

    /// <summary>
    /// 调用使用的 prompt 模板 ID。
    /// </summary>
    public Guid PromptTemplateId { get; private set; }

    /// <summary>
    /// 调用使用的 prompt 版本。
    /// </summary>
    public string PromptVersion { get; private set; } = string.Empty;

    /// <summary>
    /// 调用结果状态，复用 RAG 调用状态以统一审计语义。
    /// </summary>
    public RagCallStatus Status { get; private set; }

    /// <summary>
    /// 检索 query hash，用于重复任务排查和审计关联。
    /// </summary>
    public string QueryHash { get; private set; } = string.Empty;

    /// <summary>
    /// 检索 query 受控长度摘要。
    /// </summary>
    public string QuerySummary { get; private set; } = string.Empty;

    /// <summary>
    /// 模型 provider 逻辑名称，MVP-2 默认 LiteLLM。
    /// </summary>
    public string ModelProvider { get; private set; } = string.Empty;

    /// <summary>
    /// 模型 alias。
    /// </summary>
    public string ModelName { get; private set; } = string.Empty;

    /// <summary>
    /// 业务侧模型版本，用于和索引版本、prompt 版本一起定位生成环境。
    /// </summary>
    public string ModelVersion { get; private set; } = string.Empty;

    /// <summary>
    /// 本次任务请求的 RAG 上下文数量。
    /// </summary>
    public int TopK { get; private set; }

    /// <summary>
    /// 实际召回并通过授权过滤的 chunk 数。
    /// </summary>
    public int RetrievedChunkCount { get; private set; }

    /// <summary>
    /// 本次调用生成并通过校验的候选题数量。
    /// </summary>
    public int CandidateCount { get; private set; }

    /// <summary>
    /// prompt token 数。模型未返回 usage 时由调用方估算。
    /// </summary>
    public int PromptTokenCount { get; private set; }

    /// <summary>
    /// completion token 数。模型未返回 usage 时由调用方估算。
    /// </summary>
    public int CompletionTokenCount { get; private set; }

    /// <summary>
    /// 总 token 数。
    /// </summary>
    public int TotalTokenCount { get; private set; }

    /// <summary>
    /// 调用耗时，单位毫秒。
    /// </summary>
    public int ExecutionDurationMs { get; private set; }

    /// <summary>
    /// 失败阶段。成功调用为空。
    /// </summary>
    public string? FailureStage { get; private set; }

    /// <summary>
    /// 失败原因摘要。成功调用为空。
    /// </summary>
    public string? FailureReason { get; private set; }

    /// <summary>
    /// EF Core 使用的构造函数。
    /// </summary>
    protected AiQuestionModelCallLog()
    {
    }

    /// <summary>
    /// 创建模型调用日志。
    /// 计数和耗时不允许为负数，避免后续审计统计出现不可解释的数据。
    /// </summary>
    public AiQuestionModelCallLog(
        Guid id,
        Guid? tenantId,
        Guid taskId,
        Guid promptTemplateId,
        string promptVersion,
        RagCallStatus status,
        string queryHash,
        string querySummary,
        string modelProvider,
        string modelName,
        string modelVersion,
        int topK,
        int retrievedChunkCount,
        int candidateCount,
        int promptTokenCount,
        int completionTokenCount,
        int totalTokenCount,
        int executionDurationMs,
        string? failureStage,
        string? failureReason)
        : base(id)
    {
        if (topK < 0 || retrievedChunkCount < 0 || candidateCount < 0 ||
            promptTokenCount < 0 || completionTokenCount < 0 || totalTokenCount < 0 || executionDurationMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(topK));
        }

        TenantId = tenantId;
        TaskId = taskId;
        PromptTemplateId = promptTemplateId;
        PromptVersion = Check.NotNullOrWhiteSpace(promptVersion, nameof(promptVersion), MaxPromptVersionLength);
        Status = status;
        QueryHash = Check.NotNullOrWhiteSpace(queryHash, nameof(queryHash), MaxQueryHashLength);
        QuerySummary = Check.NotNullOrWhiteSpace(querySummary, nameof(querySummary), MaxQuerySummaryLength);
        ModelProvider = Check.NotNullOrWhiteSpace(modelProvider, nameof(modelProvider), MaxModelProviderLength);
        ModelName = Check.NotNullOrWhiteSpace(modelName, nameof(modelName), MaxModelNameLength);
        ModelVersion = Check.NotNullOrWhiteSpace(modelVersion, nameof(modelVersion), MaxModelVersionLength);
        TopK = topK;
        RetrievedChunkCount = retrievedChunkCount;
        CandidateCount = candidateCount;
        PromptTokenCount = promptTokenCount;
        CompletionTokenCount = completionTokenCount;
        TotalTokenCount = totalTokenCount;
        ExecutionDurationMs = executionDurationMs;
        FailureStage = Check.Length(failureStage, nameof(failureStage), MaxFailureStageLength);
        FailureReason = Check.Length(failureReason, nameof(failureReason), MaxFailureReasonLength);
    }
}
