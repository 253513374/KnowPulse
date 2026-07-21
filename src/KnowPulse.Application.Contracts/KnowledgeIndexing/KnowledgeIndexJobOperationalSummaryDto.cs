using System;

namespace KnowPulse.KnowledgeIndexing;

/// <summary>
/// 索引任务运维摘要，用于管理页快速识别队列积压、失败、重试和卡住任务。
/// </summary>
public class KnowledgeIndexJobOperationalSummaryDto
{
    public long PendingCount { get; set; }

    public long ProcessingCount { get; set; }

    public long FailedCount { get; set; }

    public long RetryDueCount { get; set; }

    public long ExhaustedCount { get; set; }

    public long StaleCount { get; set; }

    public DateTime? LastIndexedTime { get; set; }
}
