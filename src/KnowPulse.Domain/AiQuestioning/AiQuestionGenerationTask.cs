using System;
using KnowPulse.QuestionBank;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.AiQuestioning;

/// <summary>
/// AI 出题任务聚合根。
/// 任务记录一次“从授权 RAG 检索到模型生成候选题”的执行事实，
/// 不直接代表任何正式题库题目，也不允许被考试模块直接引用。
/// </summary>
public class AiQuestionGenerationTask : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    /// <summary>
    /// 管理员输入的知识范围最大长度。
    /// </summary>
    public const int MaxKnowledgeScopeLength = 512;

    /// <summary>
    /// 实际 RAG 检索 query 最大长度。
    /// </summary>
    public const int MaxRetrievalQueryLength = 1024;

    /// <summary>
    /// prompt 版本最大长度。
    /// </summary>
    public const int MaxPromptVersionLength = 64;

    /// <summary>
    /// 模型 provider 最大长度。
    /// </summary>
    public const int MaxModelProviderLength = 64;

    /// <summary>
    /// 模型 alias 最大长度。
    /// </summary>
    public const int MaxModelNameLength = 128;

    /// <summary>
    /// 业务模型版本最大长度。
    /// </summary>
    public const int MaxModelVersionLength = 64;

    /// <summary>
    /// 任务标签最大长度。
    /// </summary>
    public const int MaxTagsLength = 512;

    /// <summary>
    /// 失败阶段最大长度，例如 retrieval、chat、parse。
    /// </summary>
    public const int MaxFailureStageLength = 64;

    /// <summary>
    /// 失败原因摘要最大长度，不保存完整 prompt 或知识正文。
    /// </summary>
    public const int MaxFailureReasonLength = 512;

    /// <summary>
    /// 所属租户。
    /// </summary>
    public Guid? TenantId { get; protected set; }

    public Guid? OwnerTeacherId { get; private set; }
    public string KnowledgeItemIdsJson { get; private set; } = "[]";

    public void SetTeacherSource(Guid teacherId, string knowledgeItemIdsJson)
    {
        if (teacherId == Guid.Empty || (OwnerTeacherId.HasValue && OwnerTeacherId != teacherId))
            throw new ArgumentException("出题任务归属不能被改写。", nameof(teacherId));
        OwnerTeacherId = teacherId;
        KnowledgeItemIdsJson = Check.NotNullOrWhiteSpace(knowledgeItemIdsJson, nameof(knowledgeItemIdsJson));
    }

    /// <summary>
    /// 任务生命周期状态。
    /// </summary>
    public AiQuestionGenerationTaskStatus Status { get; private set; }

    /// <summary>
    /// 本任务使用的 prompt 模板 ID。
    /// </summary>
    public Guid PromptTemplateId { get; private set; }

    /// <summary>
    /// 本任务使用的 prompt 版本快照。
    /// </summary>
    public string PromptVersion { get; private set; } = string.Empty;

    /// <summary>
    /// 模型 provider 逻辑名称，MVP-2 默认 LiteLLM。
    /// </summary>
    public string ModelProvider { get; private set; } = string.Empty;

    /// <summary>
    /// 模型 alias，避免业务库保存供应商密钥或具体敏感配置。
    /// </summary>
    public string ModelName { get; private set; } = string.Empty;

    /// <summary>
    /// 业务侧模型版本，用于和索引、prompt、调用日志对齐。
    /// </summary>
    public string ModelVersion { get; private set; } = string.Empty;

    /// <summary>
    /// 本任务要求生成的题型。
    /// </summary>
    public QuestionType Type { get; private set; }

    /// <summary>
    /// 本任务要求生成的题目难度。
    /// </summary>
    public QuestionDifficulty Difficulty { get; private set; }

    /// <summary>
    /// 用户请求生成的候选题数量。
    /// </summary>
    public int RequestedCount { get; private set; }

    /// <summary>
    /// 通过结构化校验的候选题数量。
    /// </summary>
    public int GeneratedCount { get; private set; }

    /// <summary>
    /// RAG 检索上下文数量上限。
    /// </summary>
    public int TopK { get; private set; }

    /// <summary>
    /// 管理员输入的知识范围描述。
    /// </summary>
    public string KnowledgeScope { get; private set; } = string.Empty;

    /// <summary>
    /// 实际发送给 RAG 检索器的 query。
    /// </summary>
    public string RetrievalQuery { get; private set; } = string.Empty;

    /// <summary>
    /// 候选题适用岗位。
    /// </summary>
    public Guid? ApplicablePositionId { get; private set; }

    /// <summary>
    /// 候选题安全等级。
    /// </summary>
    public int SecurityLevel { get; private set; }

    /// <summary>
    /// 候选题标签。
    /// </summary>
    public string? Tags { get; private set; }

    /// <summary>
    /// 任务开始执行时间。
    /// </summary>
    public DateTime? StartedTime { get; private set; }

    /// <summary>
    /// 任务完成或失败时间。
    /// </summary>
    public DateTime? CompletedTime { get; private set; }

    /// <summary>
    /// 失败阶段摘要。
    /// </summary>
    public string? FailureStage { get; private set; }

    /// <summary>
    /// 失败原因摘要，长度受控。
    /// </summary>
    public string? FailureReason { get; private set; }

    /// <summary>
    /// EF Core 使用的构造函数。
    /// </summary>
    protected AiQuestionGenerationTask()
    {
    }

    /// <summary>
    /// 创建一个待执行的 AI 出题任务。
    /// 构造函数只记录任务约束；实际 RAG 检索、模型调用和候选题落库由应用服务执行。
    /// </summary>
    public AiQuestionGenerationTask(
        Guid id,
        Guid? tenantId,
        Guid promptTemplateId,
        string promptVersion,
        string modelProvider,
        string modelName,
        string modelVersion,
        QuestionType type,
        QuestionDifficulty difficulty,
        int requestedCount,
        int topK,
        string knowledgeScope,
        string retrievalQuery,
        Guid? applicablePositionId,
        int securityLevel,
        string? tags)
        : base(id)
    {
        if (requestedCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedCount));
        }

        if (topK < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(topK));
        }

        if (securityLevel is < 0 or > 10)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.InvalidSecurityLevel);
        }

        TenantId = tenantId;
        PromptTemplateId = promptTemplateId;
        PromptVersion = Check.NotNullOrWhiteSpace(promptVersion, nameof(promptVersion), MaxPromptVersionLength);
        ModelProvider = Check.NotNullOrWhiteSpace(modelProvider, nameof(modelProvider), MaxModelProviderLength);
        ModelName = Check.NotNullOrWhiteSpace(modelName, nameof(modelName), MaxModelNameLength);
        ModelVersion = Check.NotNullOrWhiteSpace(modelVersion, nameof(modelVersion), MaxModelVersionLength);
        Type = type;
        Difficulty = difficulty;
        RequestedCount = requestedCount;
        TopK = topK;
        KnowledgeScope = Check.NotNullOrWhiteSpace(knowledgeScope, nameof(knowledgeScope), MaxKnowledgeScopeLength);
        RetrievalQuery = Check.NotNullOrWhiteSpace(retrievalQuery, nameof(retrievalQuery), MaxRetrievalQueryLength);
        ApplicablePositionId = applicablePositionId;
        SecurityLevel = securityLevel;
        Tags = Check.Length(tags, nameof(tags), MaxTagsLength);
        Status = AiQuestionGenerationTaskStatus.Pending;
    }

    /// <summary>
    /// 标记任务开始处理，并清空上一次执行的失败摘要。
    /// 重试任务时复用同一个任务 ID，因此这里需要重置 CompletedTime 和失败字段。
    /// </summary>
    public void MarkProcessing(DateTime now)
    {
        Status = AiQuestionGenerationTaskStatus.Processing;
        StartedTime ??= now;
        CompletedTime = null;
        FailureStage = null;
        FailureReason = null;
    }

    /// <summary>
    /// 标记任务生成完成。
    /// generatedCount 只统计通过校验的候选题，校验失败候选题不计入成功数。
    /// </summary>
    public void MarkGenerated(int generatedCount, DateTime now)
    {
        GeneratedCount = generatedCount;
        Status = AiQuestionGenerationTaskStatus.Generated;
        CompletedTime = now;
        FailureStage = null;
        FailureReason = null;
    }

    /// <summary>
    /// 标记任务失败。
    /// 失败记录用于页面展示和审计排障，不保存完整 prompt、完整模型响应或完整知识正文。
    /// </summary>
    public void MarkFailed(string failureStage, string failureReason, DateTime now)
    {
        Status = AiQuestionGenerationTaskStatus.Failed;
        CompletedTime = now;
        FailureStage = Check.Length(failureStage, nameof(failureStage), MaxFailureStageLength);
        FailureReason = Check.Length(failureReason, nameof(failureReason), MaxFailureReasonLength);
    }
}
