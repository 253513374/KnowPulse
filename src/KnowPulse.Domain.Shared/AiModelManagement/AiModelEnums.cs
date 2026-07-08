namespace KnowPulse.AiModelManagement;

/// <summary>
/// 模型部署的业务能力类型。业务调用按能力过滤候选模型，避免把 chat alias 误用于 embedding。
/// </summary>
public enum AiModelCapability
{
    Chat = 0,
    Embedding = 1
}

/// <summary>
/// KnowPulse 记录的上游模型供应商类型。真实调用仍统一经过 LiteLLM 网关。
/// </summary>
public enum AiModelProviderType
{
    LiteLLM = 0,
    OpenAI = 1,
    AzureOpenAI = 2,
    DeepSeek = 3,
    Tongyi = 4,
    Ollama = 5,
    Other = 99
}

/// <summary>
/// 业务用途绑定。应用代码只解析用途，不直接依赖某个默认 alias。
/// </summary>
public enum AiModelUsage
{
    RagAnswerChat = 0,
    AiQuestionGeneration = 1,
    KnowledgeEmbedding = 2,
    SubjectiveScoring = 3
}

/// <summary>
/// 模型健康快照状态。
/// </summary>
public enum AiModelHealthStatus
{
    Unknown = 0,
    Healthy = 1,
    Failed = 2,
    MissingKey = 3,
    Disabled = 4
}
