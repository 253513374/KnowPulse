using System;

namespace KnowPulse.KnowledgeIndexing;

/// <summary>
/// 标记索引任务失败的输入 DTO。
/// 调用方只传失败阶段和排障摘要，避免把完整文档内容写入任务表。
/// </summary>
public class MarkKnowledgeIndexJobFailedDto
{
    /// <summary>
    /// 失败阶段，例如 reader、processor、chunker、embedding 或 vector-store。
    /// </summary>
    public string FailureStage { get; set; } = string.Empty;

    /// <summary>
    /// 失败原因摘要。
    /// </summary>
    public string FailureReason { get; set; } = string.Empty;

    /// <summary>
    /// 下一次计划重试时间。为空表示当前失败不安排自动重试。
    /// </summary>
    public DateTime? NextRetryTime { get; set; }
}
