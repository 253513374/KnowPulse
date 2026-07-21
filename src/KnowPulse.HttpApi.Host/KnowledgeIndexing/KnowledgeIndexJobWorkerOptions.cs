namespace KnowPulse.KnowledgeIndexing;

/// <summary>
/// 知识索引后台消费者配置。
/// </summary>
public class KnowledgeIndexJobWorkerOptions
{
    public const string SectionName = "KnowledgeIndexing:Worker";

    public bool IsEnabled { get; set; } = true;

    public int PollIntervalSeconds { get; set; } = 5;

    public int BatchSize { get; set; } = 5;
}
