namespace KnowPulse.AiQuestioning;

/// <summary>
/// AI 出题 prompt 默认模板。
/// 该类型只保存 MVP-2 AI-001 的模板基线，不执行模型调用。
/// </summary>
public static class AiQuestionPromptDefaults
{
    /// <summary>
    /// 默认 prompt 模板业务键。
    /// </summary>
    public const string TemplateKey = "mvp2-ai-question-candidate";

    /// <summary>
    /// 默认 prompt 版本。
    /// </summary>
    public const string Version = "mvp2-ai-question-v1";

    /// <summary>
    /// 默认模板说明。
    /// </summary>
    public const string Description = "MVP-2 AI question candidate generation prompt template.";

    /// <summary>
    /// 默认系统提示词。
    /// </summary>
    public const string SystemPrompt =
        "你是 KnowPulse 企业知识库 AI 出题助手。只能依据已经通过权限校验的知识片段生成候选题。" +
        "不得使用外部常识补制度、流程、数字或日期。不得生成正式考试答案泄露、越权来源或无 citation 的题。" +
        "输出必须是 JSON，不要输出 Markdown。";

    /// <summary>
    /// 默认用户提示词模板。
    /// </summary>
    public const string UserPromptTemplate =
        "请基于以下已授权知识片段生成候选题。\n" +
        "要求：题型={questionType}；难度={difficulty}；数量={count}；知识范围={knowledgeScope}；安全等级={securityLevel}。\n" +
        "JSON schema：{\"questions\":[{\"type\":\"SingleChoice|MultipleChoice|TrueFalse|FillBlank\",\"difficulty\":\"Easy|Normal|Hard\",\"title\":\"...\",\"stem\":\"...\",\"options\":[{\"optionKey\":\"A\",\"content\":\"...\"}],\"answers\":[\"A\"],\"analysis\":\"...\",\"knowledgePoints\":[\"...\"],\"citations\":[{\"chunkId\":\"GUID\"}]}]}。\n" +
        "每道题必须至少引用一个 citations.chunkId，chunkId 必须来自下方知识片段。\n" +
        "已授权知识片段：\n{contexts}";
}
