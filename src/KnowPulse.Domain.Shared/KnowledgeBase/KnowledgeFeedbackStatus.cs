namespace KnowPulse.KnowledgeBase;

/// <summary>
/// 知识反馈处理状态。状态只描述反馈处理进展，不改变知识版本生命周期。
/// </summary>
public enum KnowledgeFeedbackStatus
{
    /// <summary>
    /// 用户已提交，等待管理员处理。
    /// </summary>
    Submitted = 0,

    /// <summary>
    /// 管理员已接手处理。
    /// </summary>
    Processing = 1,

    /// <summary>
    /// 已给出处理结论，可关联修订版本。
    /// </summary>
    Resolved = 2,

    /// <summary>
    /// 已关闭，不再继续处理。
    /// </summary>
    Closed = 3
}
