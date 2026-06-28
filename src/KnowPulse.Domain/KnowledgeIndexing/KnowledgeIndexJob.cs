using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.KnowledgeIndexing;

/// <summary>
/// 知识版本索引任务。任务记录是索引 pipeline 的业务事实，
/// 用于追踪发布、重建、权限变更和删除等触发来源，以及失败阶段和重试状态。
/// </summary>
public class KnowledgeIndexJob : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public const int MaxFailureStageLength = 64;
    public const int MaxFailureReasonLength = 2048;

    public Guid? TenantId { get; protected set; }

    public Guid KnowledgeItemId { get; private set; }

    public Guid KnowledgeVersionId { get; private set; }

    public Guid? EmbeddingModelVersionId { get; private set; }

    public Guid? RequestedByUserId { get; private set; }

    public KnowledgeIndexJobTriggerType TriggerType { get; private set; }

    public KnowledgeIndexJobStatus Status { get; private set; }

    public int RetryCount { get; private set; }

    public int MaxRetryCount { get; private set; }

    public string? FailureStage { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTime? StartedTime { get; private set; }

    public DateTime? CompletedTime { get; private set; }

    public DateTime? NextRetryTime { get; private set; }

    /// <summary>
    /// EF Core 使用的受保护构造函数。
    /// </summary>
    protected KnowledgeIndexJob()
    {
    }

    /// <summary>
    /// 创建知识索引任务。
    /// 任务从 Pending 开始，后续由 pipeline 或管理员入口推进到 Processing、Indexed、Failed 等状态。
    /// </summary>
    public KnowledgeIndexJob(
        Guid id,
        Guid? tenantId,
        Guid knowledgeItemId,
        Guid knowledgeVersionId,
        KnowledgeIndexJobTriggerType triggerType,
        Guid? embeddingModelVersionId = null,
        Guid? requestedByUserId = null,
        int maxRetryCount = 3)
        : base(id)
    {
        if (maxRetryCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRetryCount));
        }

        TenantId = tenantId;
        KnowledgeItemId = knowledgeItemId;
        KnowledgeVersionId = knowledgeVersionId;
        TriggerType = triggerType;
        EmbeddingModelVersionId = embeddingModelVersionId;
        RequestedByUserId = requestedByUserId;
        MaxRetryCount = maxRetryCount;
        Status = KnowledgeIndexJobStatus.Pending;
    }

    /// <summary>
    /// 绑定本次任务实际使用的 embedding 模型版本。
    /// 该字段可能在任务创建时未知，进入 embedding 阶段后再补齐。
    /// </summary>
    public void SetEmbeddingModelVersion(Guid? embeddingModelVersionId)
    {
        EmbeddingModelVersionId = embeddingModelVersionId;
    }

    /// <summary>
    /// 开始处理任务，并清理上一轮失败和重试信息。
    /// </summary>
    public void Start(DateTime now)
    {
        Status = KnowledgeIndexJobStatus.Processing;
        StartedTime = now;
        CompletedTime = null;
        FailureStage = null;
        FailureReason = null;
        NextRetryTime = null;
    }

    /// <summary>
    /// 标记任务索引完成，并清理失败信息。
    /// </summary>
    public void MarkIndexed(DateTime now)
    {
        Status = KnowledgeIndexJobStatus.Indexed;
        CompletedTime = now;
        FailureStage = null;
        FailureReason = null;
        NextRetryTime = null;
    }

    /// <summary>
    /// 标记任务失败。
    /// RetryCount 每次失败都会递增，超过 MaxRetryCount 后不再保留下次重试时间。
    /// </summary>
    public void MarkFailed(string failureStage, string failureReason, DateTime now, DateTime? nextRetryTime = null)
    {
        Status = KnowledgeIndexJobStatus.Failed;
        CompletedTime = now;
        FailureStage = Check.NotNullOrWhiteSpace(failureStage, nameof(failureStage), MaxFailureStageLength);
        FailureReason = Check.NotNullOrWhiteSpace(failureReason, nameof(failureReason), MaxFailureReasonLength);
        RetryCount++;
        NextRetryTime = RetryCount <= MaxRetryCount ? nextRetryTime : null;
    }

    /// <summary>
    /// 标记任务结果已过期。
    /// 用于版本撤回、归档、权限变化或重新索引使旧任务结果不再代表当前知识状态。
    /// </summary>
    public void MarkOutdated(string? reason = null)
    {
        Status = KnowledgeIndexJobStatus.Outdated;
        FailureStage = null;
        FailureReason = Check.Length(reason, nameof(reason), MaxFailureReasonLength);
        NextRetryTime = null;
    }

    /// <summary>
    /// 标记任务对应的索引结果已删除。
    /// </summary>
    public void MarkDeleted(DateTime now)
    {
        Status = KnowledgeIndexJobStatus.Deleted;
        CompletedTime = now;
        NextRetryTime = null;
    }
}
