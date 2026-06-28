namespace KnowPulse.AiQuestioning;

/// <summary>
/// AI 候选题的状态。
/// 候选题状态和正式题库中的 <c>QuestionStatus</c> 分离，避免模型输出绕过人工审核直接进入考试链路。
/// </summary>
public enum AiQuestionCandidateStatus
{
    /// <summary>
    /// 候选题已通过结构化校验和 citation 校验，可以由用户确认后提交题库审核。
    /// </summary>
    Generated = 0,

    /// <summary>
    /// 候选题存在结构问题、安全问题或缺少 citation，只能展示给人工排查，不能提交审核。
    /// </summary>
    ValidationFailed = 1,

    /// <summary>
    /// 候选题已经被转换为题库草稿并提交到题库审核流。
    /// 对应正式题仍处于待审核状态，不可直接组卷。
    /// </summary>
    SubmittedForReview = 2,

    /// <summary>
    /// 候选题被人工驳回。
    /// 当前 MVP-2 主要通过题库审核驳回正式题，此状态用于后续候选题层面的独立处理。
    /// </summary>
    Rejected = 3
}
