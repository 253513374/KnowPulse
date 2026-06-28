namespace KnowPulse.QuestionBank;

/// <summary>
/// 题目难度。
/// 用于题库筛选、组卷策略和后续质量分析。
/// </summary>
public enum QuestionDifficulty
{
    /// <summary>
    /// 简单题。
    /// </summary>
    Easy = 0,

    /// <summary>
    /// 普通难度题。
    /// </summary>
    Normal = 1,

    /// <summary>
    /// 困难题。
    /// </summary>
    Hard = 2
}
