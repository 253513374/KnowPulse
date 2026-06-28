using System;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.KnowledgeIndexing;

/// <summary>
/// 索引任务输出 DTO。
/// 返回给前端和 API 调用方的字段全部来自 KnowledgeIndexJob 聚合，
/// 用于展示任务来源、状态流转、失败原因和重试计划。
/// </summary>
public class KnowledgeIndexJobDto : FullAuditedEntityDto<Guid>
{
    /// <summary>
    /// 被索引的知识条目编号。
    /// </summary>
    public Guid KnowledgeItemId { get; set; }

    /// <summary>
    /// 被索引的知识版本编号。
    /// </summary>
    public Guid KnowledgeVersionId { get; set; }

    /// <summary>
    /// 本次任务使用或计划使用的 embedding 模型版本。
    /// </summary>
    public Guid? EmbeddingModelVersionId { get; set; }

    /// <summary>
    /// 发起任务的用户编号。后台任务或系统触发时可能为空。
    /// </summary>
    public Guid? RequestedByUserId { get; set; }

    /// <summary>
    /// 任务触发来源。
    /// </summary>
    public KnowledgeIndexJobTriggerType TriggerType { get; set; }

    /// <summary>
    /// 当前任务状态。
    /// </summary>
    public KnowledgeIndexJobStatus Status { get; set; }

    /// <summary>
    /// 已失败重试次数。
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// 允许重试的最大次数。
    /// </summary>
    public int MaxRetryCount { get; set; }

    /// <summary>
    /// 失败阶段，例如 reader、chunker、embedding 或 vector-store。
    /// </summary>
    public string? FailureStage { get; set; }

    /// <summary>
    /// 失败原因。这里保存排障摘要，不应写入完整知识正文或敏感附件内容。
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// 任务开始处理时间。
    /// </summary>
    public DateTime? StartedTime { get; set; }

    /// <summary>
    /// 任务完成、失败或删除时间。
    /// </summary>
    public DateTime? CompletedTime { get; set; }

    /// <summary>
    /// 下一次可重试时间。超过最大重试次数后为空。
    /// </summary>
    public DateTime? NextRetryTime { get; set; }
}
