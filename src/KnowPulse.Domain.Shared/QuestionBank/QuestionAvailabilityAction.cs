namespace KnowPulse.QuestionBank;

/// <summary>
/// 题目可用性管理动作。
/// </summary>
public enum QuestionAvailabilityAction
{
    /// <summary>
    /// 停止题目进入后续新试卷。
    /// </summary>
    Disable = 0,

    /// <summary>
    /// 将题目转入历史归档。
    /// </summary>
    Archive = 1
}
