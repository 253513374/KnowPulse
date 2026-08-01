namespace KnowPulse.QuestionBank;

/// <summary>
/// 待审核题目的人工审核决策。
/// </summary>
public enum QuestionReviewDecision
{
    /// <summary>
    /// 审核通过，题目进入正式题库。
    /// </summary>
    Approve = 0,

    /// <summary>
    /// 退回作者修改，必须说明需要修改的内容。
    /// </summary>
    ReturnForRevision = 1,

    /// <summary>
    /// 驳回题目，必须说明不予采用的原因。
    /// </summary>
    Reject = 2
}
