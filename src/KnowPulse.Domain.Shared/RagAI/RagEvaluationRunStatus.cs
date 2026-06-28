namespace KnowPulse.RagAI;

/// <summary>
/// RAG 评测运行状态。越权召回命中会让运行进入 Failed，并标记硬失败。
/// </summary>
public enum RagEvaluationRunStatus
{
    /// <summary>
    /// 评测运行中。
    /// </summary>
    Running = 0,

    /// <summary>
    /// 全部用例通过，且没有越权召回。
    /// </summary>
    Passed = 1,

    /// <summary>
    /// 存在未通过用例或越权召回。
    /// </summary>
    Failed = 2
}
