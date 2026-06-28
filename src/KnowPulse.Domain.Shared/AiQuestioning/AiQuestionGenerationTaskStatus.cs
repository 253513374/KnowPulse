namespace KnowPulse.AiQuestioning;

/// <summary>
/// AI 出题任务的生命周期状态。
/// 任务状态只描述“候选题生成流程”的执行结果，不代表题目已经进入正式题库；
/// 候选题仍必须通过题库审核流后，才可能成为可组卷的正式题。
/// </summary>
public enum AiQuestionGenerationTaskStatus
{
    /// <summary>
    /// 任务已创建但尚未进入执行管道。
    /// 当前 MVP-2 实现会同步执行，保留该状态用于后续切换后台任务。
    /// </summary>
    Pending = 0,

    /// <summary>
    /// 任务正在执行 RAG 检索、prompt 拼装、模型调用或输出校验。
    /// </summary>
    Processing = 1,

    /// <summary>
    /// 任务执行完成，可能生成了一个或多个候选题。
    /// 具体候选题是否可提交审核，需要继续查看候选题状态和校验错误。
    /// </summary>
    Generated = 2,

    /// <summary>
    /// 任务失败，例如无授权上下文、模型失败或模型输出无法解析。
    /// 失败原因会落到任务失败字段和模型调用日志中。
    /// </summary>
    Failed = 3,

    /// <summary>
    /// 任务被取消。
    /// MVP-2 当前没有主动取消入口，但保留状态以避免后续扩展破坏持久化枚举。
    /// </summary>
    Cancelled = 4
}
