namespace KnowPulse.Learning;

/// <summary>
/// 学习中心内容完成状态，统一用于知识阅读和必考考试展示。
/// </summary>
public enum LearningContentCompletionStatus
{
    /// <summary>
    /// 未开始学习或考试。
    /// </summary>
    NotStarted = 0,

    /// <summary>
    /// 已阅读、已开始考试或已有未通过记录。
    /// </summary>
    InProgress = 1,

    /// <summary>
    /// 已完成阅读或已满足考试完成条件。
    /// </summary>
    Completed = 2,

    /// <summary>
    /// 当前用户不可参加或不可访问该学习内容。
    /// </summary>
    Unavailable = 3
}
