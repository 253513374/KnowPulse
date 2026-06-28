namespace KnowPulse.QuestionBank;

/// <summary>
/// 题目状态。
/// 描述题目从草稿、审核、正式可用到停用或归档的生命周期。
/// </summary>
public enum QuestionStatus
{
    /// <summary>
    /// 草稿。
    /// 可继续编辑，尚未提交审核，不能进入正式题库。
    /// </summary>
    Draft = 0,

    /// <summary>
    /// 待审核。
    /// 题目已提交，等待具备审核权限的用户处理。
    /// </summary>
    PendingReview = 1,

    /// <summary>
    /// 审核通过。
    /// 题目进入正式题库，可用于后续新试卷组卷。
    /// </summary>
    Approved = 2,

    /// <summary>
    /// 已驳回。
    /// 题目未通过审核，可编辑后重新提交。
    /// </summary>
    Rejected = 3,

    /// <summary>
    /// 退回修改。
    /// 审核人要求补充或修订，编辑后需要重新提交审核。
    /// </summary>
    ReturnedForRevision = 4,

    /// <summary>
    /// 已停用。
    /// 题目保留历史和审计记录，但不再进入新试卷组卷。
    /// </summary>
    Disabled = 5,

    /// <summary>
    /// 已归档。
    /// 题目进入历史归档状态，不再允许编辑或停用。
    /// </summary>
    Archived = 6
}
