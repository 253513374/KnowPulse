namespace KnowPulse.QuestionBank;

/// <summary>
/// 题目创建来源。
/// 用于区分人工维护题和由 AI 候选题提交后形成的题目。
/// </summary>
public enum QuestionCreationSource
{
    /// <summary>
    /// 人工创建或维护的题目。
    /// </summary>
    Manual = 0,

    /// <summary>
    /// AI 候选题提交后形成的题目。
    /// 仍需经过人工审核后才能进入正式题库。
    /// </summary>
    AiGenerated = 1
}
