namespace KnowPulse.QuestionBank;

/// <summary>
/// 题目类型。
/// 决定题目选项和标准答案的校验规则。
/// </summary>
public enum QuestionType
{
    /// <summary>
    /// 单选题。
    /// 必须提供至少两个选项，并且只能有一个标准答案。
    /// </summary>
    SingleChoice = 0,

    /// <summary>
    /// 多选题。
    /// 必须提供至少两个选项，并且至少有两个标准答案。
    /// </summary>
    MultipleChoice = 1,

    /// <summary>
    /// 判断题。
    /// 不需要选项，标准答案必须是 true/false 或 对/错。
    /// </summary>
    TrueFalse = 2,

    /// <summary>
    /// 填空题。
    /// 不需要选项，至少需要一个标准答案文本。
    /// </summary>
    FillBlank = 3
}
