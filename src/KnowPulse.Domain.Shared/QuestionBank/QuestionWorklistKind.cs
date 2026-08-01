namespace KnowPulse.QuestionBank;

/// <summary>
/// 题库工作台按用户任务划分的列表类型。
/// </summary>
public enum QuestionWorklistKind
{
    /// <summary>
    /// 可编辑、可重新提交的草稿任务。
    /// </summary>
    Drafts = 0,

    /// <summary>
    /// 等待审核人处理的题目。
    /// </summary>
    ReviewQueue = 1,

    /// <summary>
    /// 已审核通过并可用于新试卷的正式题目。
    /// </summary>
    Formal = 2
}
