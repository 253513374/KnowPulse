using System.Collections.Generic;

namespace KnowPulse.RagAI;

/// <summary>
/// RAG/AI 配置根节点。该配置只保存模型 alias、维度、collection 和网关连接策略；
/// 真实模型密钥必须通过环境变量或安全配置源提供，不能写入仓库。
/// </summary>
public class RagAiOptions
{
    /// <summary>
    /// appsettings 中的配置节名称。
    /// </summary>
    public const string SectionName = "RagAI";

    /// <summary>
    /// embedding 模型、版本、维度和默认 collection 配置。
    /// </summary>
    public RagEmbeddingOptions Embedding { get; set; } = new();

    /// <summary>
    /// 向量库连接、collection 和 payload index 维护配置。
    /// </summary>
    public RagVectorStoreOptions VectorStore { get; set; } = new();

    /// <summary>
    /// LiteLLM 网关地址、alias 和调用参数配置。
    /// </summary>
    public LiteLlmGatewayOptions LiteLlm { get; set; } = new();

    /// <summary>
    /// AI 出题 prompt 版本、上下文和输出控制。
    /// </summary>
    public AiQuestionGenerationOptions AiQuestionGeneration { get; set; } = new();
}

/// <summary>
/// Embedding 相关配置。
/// Provider 和模型名在业务表中作为索引事实保存，不能随意覆盖历史记录。
/// </summary>
public class RagEmbeddingOptions
{
    /// <summary>
    /// embedding provider 逻辑名称。MVP-1 默认通过 LiteLLM 统一代理。
    /// </summary>
    public string Provider { get; set; } = "LiteLLM";

    /// <summary>
    /// LiteLLM 中配置的 embedding 模型 alias。
    /// </summary>
    public string ModelName { get; set; } = "knowpulse-embedding-default";

    /// <summary>
    /// 业务侧模型版本号，用于区分索引重建批次和灰度切换。
    /// </summary>
    public string ModelVersion { get; set; } = "2026-06-mvp1";

    /// <summary>
    /// 向量维度。该值必须与模型输出和 Qdrant collection 配置一致。
    /// </summary>
    public int VectorDimension { get; set; } = 1536;

    /// <summary>
    /// 当前默认写入的知识分块 collection。
    /// </summary>
    public string CollectionName { get; set; } = "knowledge_chunks_1536_mvp1";

    /// <summary>
    /// 批量 embedding 的上限，后续接入真正批处理 client 时使用。
    /// </summary>
    public int MaxBatchSize { get; set; } = 64;

    /// <summary>
    /// embedding 请求超时时间，单位秒。
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;
}

/// <summary>
/// 向量库配置。
/// collection 作为可重建投影存储，真实知识状态和权限仍以 PostgreSQL 为准。
/// </summary>
public class RagVectorStoreOptions
{
    /// <summary>
    /// 向量库 provider 逻辑名称。
    /// </summary>
    public string Provider { get; set; } = "Qdrant";

    /// <summary>
    /// Qdrant HTTP endpoint。
    /// </summary>
    public string Endpoint { get; set; } = "http://localhost:6333";

    /// <summary>
    /// 保存知识 chunk 向量的 collection 名称。
    /// </summary>
    public string KnowledgeCollectionName { get; set; } = "knowledge_chunks_1536_mvp1";

    /// <summary>
    /// collection 期望向量维度。
    /// </summary>
    public int VectorDimension { get; set; } = 1536;

    /// <summary>
    /// collection 距离算法。
    /// </summary>
    public string Distance { get; set; } = "Cosine";

    /// <summary>
    /// payload index 维护策略。MVP-1 使用显式索引，避免不同环境静默漂移。
    /// </summary>
    public string PayloadIndexMode { get; set; } = "Explicit";

    /// <summary>
    /// Qdrant API key 所在环境变量名。为空时按本地无认证模式访问。
    /// </summary>
    public string? ApiKeyEnvironmentVariable { get; set; }
}

/// <summary>
/// LiteLLM 网关配置。
/// 应用只使用 alias；真实供应商模型、key 和 fallback 由 LiteLLM 配置托管。
/// </summary>
public class LiteLlmGatewayOptions
{
    /// <summary>
    /// LiteLLM OpenAI-compatible 网关地址。
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:4000";

    /// <summary>
    /// embedding alias。
    /// </summary>
    public string EmbeddingModel { get; set; } = "knowpulse-embedding-default";

    /// <summary>
    /// chat completion alias。
    /// </summary>
    public string ChatModel { get; set; } = "knowpulse-chat-default";

    /// <summary>
    /// 应用访问 LiteLLM 所需 API key 的环境变量名。
    /// </summary>
    public string ApiKeyEnvironmentVariable { get; set; } = "KNOWPULSE_LITELLM_API_KEY";

    /// <summary>
    /// LiteLLM 请求超时时间，单位秒。
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// chat completion 最大输出 token 数。
    /// </summary>
    public int ChatMaxTokens { get; set; } = 800;

    /// <summary>
    /// chat completion temperature。企业知识问答默认低温度以减少发挥。
    /// </summary>
    public double ChatTemperature { get; set; } = 0.2;

    /// <summary>
    /// 备用 embedding alias 列表，用于配置和环境校验脚本确认 fallback 路径存在。
    /// </summary>
    public List<string> FallbackModels { get; set; } = ["knowpulse-embedding-fallback"];
}

public class AiQuestionGenerationOptions
{
    /// <summary>
    /// AI 出题 prompt 模板业务键。
    /// </summary>
    public string PromptTemplateKey { get; set; } = "mvp2-ai-question-candidate";

    /// <summary>
    /// AI 出题 prompt 版本。任务和调用日志都会保存该版本快照。
    /// </summary>
    public string PromptVersion { get; set; } = "mvp2-ai-question-v1";

    /// <summary>
    /// AI 出题模型 provider 逻辑名称。
    /// </summary>
    public string ModelProvider { get; set; } = "LiteLLM";

    /// <summary>
    /// AI 出题 chat 模型 alias，由 LiteLLM 配置映射到真实供应商模型。
    /// </summary>
    public string ModelName { get; set; } = "knowpulse-chat-default";

    /// <summary>
    /// 业务侧 AI 出题模型版本，用于和 prompt 版本、调用日志一起追溯生成环境。
    /// </summary>
    public string ModelVersion { get; set; } = "2026-06-mvp2-questioning";

    /// <summary>
    /// 单个 AI 出题任务最多允许生成的候选题数量。
    /// </summary>
    public int MaxQuestionsPerTask { get; set; } = 10;

    /// <summary>
    /// 单次 prompt 最多注入的已授权知识 chunk 数。
    /// </summary>
    public int MaxContextChunks { get; set; } = 6;

    /// <summary>
    /// 单个 chunk 进入 prompt 前的最大摘录长度。
    /// </summary>
    public int MaxPromptChunkLength { get; set; } = 1200;

    /// <summary>
    /// 候选题 citation 摘录最大长度。
    /// </summary>
    public int MaxCitationExcerptLength { get; set; } = 220;

    /// <summary>
    /// AI 出题模型最大输出 token 数。
    /// </summary>
    public int MaxOutputTokens { get; set; } = 800;

    /// <summary>
    /// AI 出题模型 temperature。
    /// </summary>
    public double Temperature { get; set; } = 0.2;
}
