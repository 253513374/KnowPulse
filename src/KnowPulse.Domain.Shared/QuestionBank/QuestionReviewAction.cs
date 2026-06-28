namespace KnowPulse.QuestionBank;

/// <summary>
/// 题目审核或状态操作类型。
/// 用于题目审核记录和审计追踪。
/// </summary>
public enum QuestionReviewAction
{
    /// <summary>
    /// 提交审核。
    /// </summary>
    Submitted = 0,

    /// <summary>
    /// 审核通过。
    /// </summary>
    Approved = 1,

    /// <summary>
    /// 审核驳回。
    /// </summary>
    Rejected = 2,

    /// <summary>
    /// 退回修改。
    /// </summary>
    ReturnedForRevision = 3,

    /// <summary>
    /// 停用题目。
    /// </summary>
    Disabled = 4,

    /// <summary>
    /// 归档题目。
    /// </summary>
    Archived = 5
}
