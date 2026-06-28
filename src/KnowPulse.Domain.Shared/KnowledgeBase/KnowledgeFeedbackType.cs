namespace KnowPulse.KnowledgeBase;

/// <summary>
/// 知识反馈类型，覆盖用户对知识内容提出的问题、过期、错误、缺失和改进建议。
/// </summary>
public enum KnowledgeFeedbackType
{
    /// <summary>
    /// 一般问题或疑问。
    /// </summary>
    Problem = 0,

    /// <summary>
    /// 内容已过期，需要更新。
    /// </summary>
    Outdated = 1,

    /// <summary>
    /// 内容存在错误。
    /// </summary>
    Error = 2,

    /// <summary>
    /// 内容缺失，需要补充。
    /// </summary>
    Missing = 3,

    /// <summary>
    /// 改进建议。
    /// </summary>
    Suggestion = 4
}
