namespace KnowPulse;

/// <summary>
/// KnowPulse 领域异常错误码，供应用服务、本地化资源和前端错误提示保持一致。
/// </summary>
public static class KnowPulseDomainErrorCodes
{
    /// <summary>
    /// 安全级别超出知识权限模型允许范围。
    /// </summary>
    public const string InvalidSecurityLevel = "KnowPulse:00001";

    /// <summary>
    /// 尝试操作已经归档的知识版本。
    /// </summary>
    public const string KnowledgeVersionAlreadyArchived = "KnowPulse:00002";

    /// <summary>
    /// 当前操作要求知识版本处于已发布状态。
    /// </summary>
    public const string KnowledgeVersionNotPublished = "KnowPulse:00003";

    /// <summary>
    /// 索引任务重试只允许针对失败状态的任务执行。
    /// </summary>
    public const string KnowledgeIndexJobRetryRequiresFailedStatus = "KnowPulse:00004";

    /// <summary>
    /// 知识反馈当前状态不允许进入处理中。
    /// </summary>
    public const string KnowledgeFeedbackCannotProcess = "KnowPulse:00005";

    /// <summary>
    /// 知识反馈当前状态不允许标记为已处理。
    /// </summary>
    public const string KnowledgeFeedbackCannotResolve = "KnowPulse:00006";

    /// <summary>
    /// 知识反馈当前状态不允许关闭或关联版本。
    /// </summary>
    public const string KnowledgeFeedbackClosed = "KnowPulse:00007";

    /// <summary>
    /// 知识反馈关联的知识版本不属于反馈来源知识。
    /// </summary>
    public const string KnowledgeFeedbackVersionItemMismatch = "KnowPulse:00008";

    /// <summary>
    /// 发布知识时没有提交显式访问策略。
    /// </summary>
    public const string KnowledgeAccessPolicyRequired = "KnowPulse:00009";

    /// <summary>
    /// 全员范围与定向范围不能同时出现在同一访问策略中。
    /// </summary>
    public const string KnowledgeAccessPolicyAllCannotBeCombined = "KnowPulse:00010";

    /// <summary>
    /// 同一访问目标不能重复配置。
    /// </summary>
    public const string KnowledgeAccessPolicyDuplicateScope = "KnowPulse:00011";

    /// <summary>
    /// 定向访问范围缺少目标编号。
    /// </summary>
    public const string KnowledgeAccessPolicyTargetRequired = "KnowPulse:00012";

    /// <summary>
    /// 当前索引任务状态不允许开始或继续执行。
    /// </summary>
    public const string KnowledgeIndexJobCannotStart = "KnowPulse:00013";

    /// <summary>
    /// 索引任务已经耗尽自动重试次数。
    /// </summary>
    public const string KnowledgeIndexJobRetryExhausted = "KnowPulse:00014";

    /// <summary>
    /// 只有处理中任务可以执行人工恢复。
    /// </summary>
    public const string KnowledgeIndexJobRecoveryRequiresProcessingStatus = "KnowPulse:00015";

    /// <summary>
    /// 索引任务与准备处理的知识版本不匹配。
    /// </summary>
    public const string KnowledgeIndexJobVersionMismatch = "KnowPulse:00016";

    /// <summary>
    /// 创建反馈修订前必须先接手反馈。
    /// </summary>
    public const string KnowledgeFeedbackRevisionRequiresProcessing = "KnowPulse:00017";

    /// <summary>
    /// 反馈已经关联修订版本，不能重复创建。
    /// </summary>
    public const string KnowledgeFeedbackRevisionAlreadyLinked = "KnowPulse:00018";

    /// <summary>
    /// 完成反馈前必须存在关联修订版本。
    /// </summary>
    public const string KnowledgeFeedbackRevisionRequired = "KnowPulse:00019";

    /// <summary>
    /// 完成反馈前，关联修订必须成为当前发布版本。
    /// </summary>
    public const string KnowledgeFeedbackRevisionNotPublished = "KnowPulse:00020";

    /// <summary>
    /// 完成反馈前，关联修订必须完成索引。
    /// </summary>
    public const string KnowledgeFeedbackRevisionNotIndexed = "KnowPulse:00021";

    public const string PersonalKnowledgeRequiresOwnerPolicy = "KnowPulse:00022";

    public const string KnowledgeIndexCompletionRequiresPipeline = "KnowPulse:00023";

    public const string PersonalKnowledgeAttachmentRequiresDraft = "KnowPulse:00024";

    public const string PersonalKnowledgeAttachmentTooLarge = "KnowPulse:00025";

    public const string PersonalKnowledgeAttachmentLimitReached = "KnowPulse:00026";

    public const string QuestionAnswerRequired = "KnowPulse:01001";

    public const string QuestionChoiceOptionsRequired = "KnowPulse:01002";

    public const string QuestionAnswerMustMatchOption = "KnowPulse:01003";

    public const string QuestionSingleChoiceRequiresOneAnswer = "KnowPulse:01004";

    public const string QuestionTrueFalseAnswerInvalid = "KnowPulse:01005";

    public const string QuestionSubmitRequiresDraftStatus = "KnowPulse:01006";

    public const string QuestionReviewRequiresPendingStatus = "KnowPulse:01007";

    public const string QuestionCannotEditArchived = "KnowPulse:01008";

    public const string QuestionCannotDisableArchived = "KnowPulse:01009";

    public const string QuestionMultipleChoiceRequiresAtLeastTwoAnswers = "KnowPulse:01010";

    public const string QuestionSourceRequired = "KnowPulse:01011";

    public const string QuestionReviewCommentRequired = "KnowPulse:01012";

    public const string AiQuestionCandidateNotReady = "KnowPulse:02001";

    public const string AiQuestionCandidateAlreadySubmitted = "KnowPulse:02002";

    public const string AiQuestionCandidateCitationRequired = "KnowPulse:02003";

    public const string ExamPlanWindowInvalid = "KnowPulse:03001";

    public const string ExamPlanDurationInvalid = "KnowPulse:03002";

    public const string ExamPlanAttemptLimitInvalid = "KnowPulse:03003";

    public const string ExamPlanNotPublished = "KnowPulse:03004";

    public const string ExamPlanNotStarted = "KnowPulse:03005";

    public const string ExamPlanEnded = "KnowPulse:03006";

    public const string ExamPaperQuestionInsufficient = "KnowPulse:03007";

    public const string ExamPaperSnapshotTooLarge = "KnowPulse:03008";

    public const string ExamQuestionScoreInvalid = "KnowPulse:03009";

    public const string ExamNotAssigned = "KnowPulse:03010";

    public const string ExamAttemptLimitReached = "KnowPulse:03011";

    public const string ExamAttemptNumberInvalid = "KnowPulse:03012";

    public const string ExamAttemptCannotSave = "KnowPulse:03013";

    public const string ExamAttemptCannotSubmit = "KnowPulse:03014";

    public const string ExamAttemptCannotScore = "KnowPulse:03015";

    public const string ExamAttemptExpired = "KnowPulse:03016";

    public const string ExamAnswerVersionInvalid = "KnowPulse:03017";

    public const string ExamAnswerTooLarge = "KnowPulse:03018";

    public const string ExamAttemptUserMismatch = "KnowPulse:03019";

    public const string ExamPaperQuestionNotFound = "KnowPulse:03020";

    public const string LearningPathAssignmentRequired = "KnowPulse:04001";

    public const string LearningPathAssignmentTargetRequired = "KnowPulse:04002";

    public const string LearningPathContentRequired = "KnowPulse:04003";

    public const string RagEvaluationNoCases = "KnowPulse:05001";

    public const string AiModelDeploymentInactive = "KnowPulse:06001";

    public const string AiModelDeploymentCapabilityMismatch = "KnowPulse:06002";

    public const string AiModelEmbeddingSwitchRequiresRebuild = "KnowPulse:06003";

    public const string AiModelUsageBindingMissing = "KnowPulse:06004";
}
