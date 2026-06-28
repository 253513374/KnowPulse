using System;

namespace KnowPulse.KnowledgeIndexing;

/// <summary>
/// 创建知识索引任务的输入 DTO。
/// 知识条目和知识版本必须属于同一个业务对象，应用服务会再次校验归属关系。
/// </summary>
public class CreateKnowledgeIndexJobDto
{
    /// <summary>
    /// 需要索引的知识条目编号。
    /// </summary>
    public Guid KnowledgeItemId { get; set; }

    /// <summary>
    /// 需要索引的知识版本编号。
    /// </summary>
    public Guid KnowledgeVersionId { get; set; }

    /// <summary>
    /// 指定的 embedding 模型版本。为空时由索引/embedding pipeline 使用当前默认模型配置。
    /// </summary>
    public Guid? EmbeddingModelVersionId { get; set; }

    /// <summary>
    /// 任务触发来源，用于区分发布、手动重建、权限变化、模型变化或删除。
    /// </summary>
    public KnowledgeIndexJobTriggerType TriggerType { get; set; } = KnowledgeIndexJobTriggerType.ManualRebuild;

    /// <summary>
    /// 最大自动重试次数。业务聚合会根据该值决定失败后是否继续保留下一次重试时间。
    /// </summary>
    public int MaxRetryCount { get; set; } = 3;
}
