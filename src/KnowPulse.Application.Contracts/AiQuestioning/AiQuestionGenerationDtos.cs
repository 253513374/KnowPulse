using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using KnowPulse.QuestionBank;
using KnowPulse.RagAI;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.AiQuestioning;

/// <summary>
/// AI 出题任务列表查询条件。
/// 该 DTO 只用于管理端查看生成任务，不返回模型原始 prompt 或完整知识正文。
/// </summary>
public class GetAiQuestionGenerationTaskListInput : PagedAndSortedResultRequestDto
{
    /// <summary>
    /// 按任务生命周期状态过滤，例如失败任务用于重试，已生成任务用于查看候选题。
    /// </summary>
    public AiQuestionGenerationTaskStatus? Status { get; set; }

    /// <summary>
    /// 按目标题型过滤，和题库题型枚举保持一致。
    /// </summary>
    public QuestionType? Type { get; set; }

    /// <summary>
    /// 按目标难度过滤，方便运营人员检查某个难度档的候选题质量。
    /// </summary>
    public QuestionDifficulty? Difficulty { get; set; }

    /// <summary>
    /// 按适用岗位过滤。为空表示不限定岗位。
    /// </summary>
    public Guid? ApplicablePositionId { get; set; }

    /// <summary>
    /// 关键字过滤，匹配任务的知识范围或实际检索 query。
    /// </summary>
    public string? Keyword { get; set; }
}

/// <summary>
/// AI 出题 prompt 模板列表查询条件。
/// 模板查询面向管理端排障和版本追溯，不包含任何模型调用结果。
/// </summary>
public class GetAiQuestionPromptTemplateListInput : PagedAndSortedResultRequestDto
{
    /// <summary>
    /// 按模板业务键过滤。
    /// </summary>
    public string? TemplateKey { get; set; }

    /// <summary>
    /// 按 prompt 版本过滤。
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// 按启用状态过滤。
    /// </summary>
    public bool? IsActive { get; set; }

    /// <summary>
    /// 关键字过滤，匹配模板键、版本或说明。
    /// </summary>
    public string? Keyword { get; set; }
}

/// <summary>
/// AI 出题模型调用日志列表查询条件。
/// 日志只返回受控摘要、hash、模型元数据和状态，不返回完整 prompt 或完整模型响应。
/// </summary>
public class GetAiQuestionModelCallLogListInput : PagedAndSortedResultRequestDto
{
    /// <summary>
    /// 按 AI 出题任务过滤。
    /// </summary>
    public Guid? TaskId { get; set; }

    /// <summary>
    /// 按 prompt 版本过滤。
    /// </summary>
    public string? PromptVersion { get; set; }

    /// <summary>
    /// 按模型调用状态过滤。
    /// </summary>
    public RagCallStatus? Status { get; set; }

    /// <summary>
    /// 按模型 provider 过滤。
    /// </summary>
    public string? ModelProvider { get; set; }

    /// <summary>
    /// 按模型 alias 过滤。
    /// </summary>
    public string? ModelName { get; set; }

    /// <summary>
    /// 按业务侧模型版本过滤。
    /// </summary>
    public string? ModelVersion { get; set; }

    /// <summary>
    /// 日志创建时间起点。
    /// </summary>
    public DateTime? CreatedFrom { get; set; }

    /// <summary>
    /// 日志创建时间终点。
    /// </summary>
    public DateTime? CreatedTo { get; set; }
}

/// <summary>
/// 创建 AI 出题任务的输入。
/// 用户只声明出题约束，真正可用的知识上下文仍由服务端 RAG 检索和权限二次校验决定。
/// </summary>
public class CreateAiQuestionGenerationTaskDto
{
    /// <summary>
    /// 本次生成希望得到的题型。
    /// 模型输出若与该题型不一致，会被结构化校验标记为失败候选题。
    /// </summary>
    [Required]
    public QuestionType Type { get; set; } = QuestionType.SingleChoice;

    /// <summary>
    /// 本次生成希望得到的题目难度。
    /// 难度会进入 prompt，也会作为候选题和最终题库草稿的难度字段。
    /// </summary>
    [Required]
    public QuestionDifficulty Difficulty { get; set; } = QuestionDifficulty.Normal;

    /// <summary>
    /// 期望候选题数量。MVP-2 限制在 1 到 10，避免单次模型调用生成过大响应。
    /// </summary>
    [Range(1, 10)]
    public int RequestedCount { get; set; } = 3;

    /// <summary>
    /// RAG 检索上下文数量上限。候选题必须引用这些已授权 chunk 中的至少一个。
    /// </summary>
    [Range(1, 10)]
    public int TopK { get; set; } = 5;

    /// <summary>
    /// 管理员输入的知识范围描述，例如“员工请假流程”。
    /// 该字段会参与检索 query 和 prompt，但不会替代权限校验。
    /// </summary>
    [Required]
    [StringLength(512)]
    public string KnowledgeScope { get; set; } = string.Empty;

    /// <summary>
    /// 可选知识点文本，多个知识点可以用逗号或自然语言描述。
    /// 生成后的候选题仍以模型返回的结构化知识点列表为准。
    /// </summary>
    [StringLength(512)]
    public string? KnowledgePointText { get; set; }

    /// <summary>
    /// 候选题适用岗位。为空表示岗位不限。
    /// </summary>
    public Guid? ApplicablePositionId { get; set; }

    /// <summary>
    /// 候选题安全等级。后续转换为题库草稿时会沿用该值。
    /// </summary>
    [Range(0, 10)]
    public int SecurityLevel { get; set; }

    /// <summary>
    /// 候选题标签文本。服务端会规范化重复标签。
    /// </summary>
    [StringLength(512)]
    public string? Tags { get; set; }
}

/// <summary>
/// 旧版候选题提交入口的兼容输入。
/// 当前入口仅采纳为草稿，字段不再参与审核记录。
/// </summary>
public class SubmitAiQuestionCandidateDto
{
    /// <summary>
    /// 旧版提交说明。新流程中采纳草稿不会产生审核意见。
    /// </summary>
    [StringLength(512)]
    public string? Comment { get; set; }
}

/// <summary>
/// AI 出题任务详情 DTO。
/// 任务详情包含候选题和模型调用摘要，供页面一次性展示生成结果和失败原因。
/// </summary>
public class AiQuestionGenerationTaskDto : FullAuditedEntityDto<Guid>
{
    /// <summary>
    /// 任务当前生命周期状态。
    /// </summary>
    public AiQuestionGenerationTaskStatus Status { get; set; }

    /// <summary>
    /// 本次任务要求生成的题型。
    /// </summary>
    public QuestionType Type { get; set; }

    /// <summary>
    /// 本次任务要求生成的题目难度。
    /// </summary>
    public QuestionDifficulty Difficulty { get; set; }

    /// <summary>
    /// 用户请求的候选题数量。
    /// </summary>
    public int RequestedCount { get; set; }

    /// <summary>
    /// 已通过结构化校验的候选题数量。
    /// 校验失败的候选题仍可能保存在 Candidates 中，但不会计入该值。
    /// </summary>
    public int GeneratedCount { get; set; }

    /// <summary>
    /// 本次任务请求的 RAG 上下文数量。
    /// </summary>
    public int TopK { get; set; }

    /// <summary>
    /// 管理员输入的知识范围描述。
    /// </summary>
    public string KnowledgeScope { get; set; } = string.Empty;

    /// <summary>
    /// 服务端拼出的实际检索 query，用于审计为什么召回这些 citation。
    /// </summary>
    public string RetrievalQuery { get; set; } = string.Empty;

    /// <summary>
    /// 本次任务使用的 prompt 版本，保证候选题可以回溯到生成规则。
    /// </summary>
    public string PromptVersion { get; set; } = string.Empty;

    /// <summary>
    /// 模型网关逻辑 provider。MVP-2 默认通过 LiteLLM 统一代理。
    /// </summary>
    public string ModelProvider { get; set; } = string.Empty;

    /// <summary>
    /// 模型 alias，而不是具体供应商模型或密钥。
    /// </summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>
    /// 业务侧模型版本，用于和 RAG 索引/审计记录对齐。
    /// </summary>
    public string ModelVersion { get; set; } = string.Empty;

    /// <summary>
    /// 候选题适用岗位。
    /// </summary>
    public Guid? ApplicablePositionId { get; set; }

    /// <summary>
    /// 候选题安全等级。
    /// </summary>
    public int SecurityLevel { get; set; }

    /// <summary>
    /// 候选题标签。
    /// </summary>
    public string? Tags { get; set; }

    /// <summary>
    /// 任务开始执行时间。
    /// </summary>
    public DateTime? StartedTime { get; set; }

    /// <summary>
    /// 任务完成或失败时间。
    /// </summary>
    public DateTime? CompletedTime { get; set; }

    /// <summary>
    /// 失败阶段，例如 retrieval、chat 或 parse。
    /// </summary>
    public string? FailureStage { get; set; }

    /// <summary>
    /// 失败原因摘要。该字段不包含完整 prompt 或完整知识正文。
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// 本任务生成的候选题列表。
    /// </summary>
    public List<AiQuestionCandidateDto> Candidates { get; set; } = new();

    /// <summary>
    /// 本任务的模型调用日志摘要，用于排障和成本/质量审计。
    /// </summary>
    public List<AiQuestionModelCallLogDto> CallLogs { get; set; } = new();
}

/// <summary>
/// AI 出题 prompt 模板 DTO。
/// 模板内容只返回给有 AI 出题权限的管理端，用于审查版本和排障。
/// </summary>
public class AiQuestionPromptTemplateDto : FullAuditedEntityDto<Guid>
{
    /// <summary>
    /// 模板业务键。
    /// </summary>
    public string TemplateKey { get; set; } = string.Empty;

    /// <summary>
    /// prompt 版本。
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// 模板说明。
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 系统提示词。
    /// </summary>
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>
    /// 用户提示词模板。
    /// </summary>
    public string UserPromptTemplate { get; set; } = string.Empty;

    /// <summary>
    /// 是否启用该版本。
    /// </summary>
    public bool IsActive { get; set; }
}

/// <summary>
/// AI 出题模型配置 DTO。
/// 该 DTO 只暴露 alias、版本、限制和密钥环境变量名，不暴露真实 key。
/// </summary>
public class AiQuestionModelConfigurationDto
{
    /// <summary>
    /// prompt 模板业务键。
    /// </summary>
    public string PromptTemplateKey { get; set; } = string.Empty;

    /// <summary>
    /// 当前默认 prompt 版本。
    /// </summary>
    public string PromptVersion { get; set; } = string.Empty;

    /// <summary>
    /// 模型 provider 逻辑名称。
    /// </summary>
    public string ModelProvider { get; set; } = string.Empty;

    /// <summary>
    /// 模型 alias。
    /// </summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>
    /// 业务侧模型版本。
    /// </summary>
    public string ModelVersion { get; set; } = string.Empty;

    /// <summary>
    /// LiteLLM 网关地址。
    /// </summary>
    public string GatewayBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// LiteLLM API key 环境变量名。
    /// </summary>
    public string ApiKeyEnvironmentVariable { get; set; } = string.Empty;

    /// <summary>
    /// 单任务候选题数量上限。
    /// </summary>
    public int MaxQuestionsPerTask { get; set; }

    /// <summary>
    /// 单次 prompt 注入上下文数量上限。
    /// </summary>
    public int MaxContextChunks { get; set; }

    /// <summary>
    /// 单个 chunk 进入 prompt 的最大长度。
    /// </summary>
    public int MaxPromptChunkLength { get; set; }

    /// <summary>
    /// citation 摘录最大长度。
    /// </summary>
    public int MaxCitationExcerptLength { get; set; }

    /// <summary>
    /// 模型最大输出 token 数。
    /// </summary>
    public int MaxOutputTokens { get; set; }

    /// <summary>
    /// 模型 temperature。
    /// </summary>
    public double Temperature { get; set; }
}

/// <summary>
/// AI 候选题详情 DTO。
/// 候选题可以被查看和提交审核，但在 LinkedQuestionId 为空时不属于正式题库。
/// </summary>
public class AiQuestionCandidateDto : FullAuditedEntityDto<Guid>
{
    /// <summary>
    /// 候选题所属 AI 出题任务。
    /// </summary>
    public Guid TaskId { get; set; }

    /// <summary>
    /// 候选题状态，决定页面是否允许提交审核。
    /// </summary>
    public AiQuestionCandidateStatus Status { get; set; }

    /// <summary>
    /// 候选题题型。
    /// </summary>
    public QuestionType Type { get; set; }

    /// <summary>
    /// 候选题难度。
    /// </summary>
    public QuestionDifficulty Difficulty { get; set; }

    /// <summary>
    /// 候选题标题。
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 候选题题干。
    /// </summary>
    public string Stem { get; set; } = string.Empty;

    /// <summary>
    /// 候选题适用岗位。
    /// </summary>
    public Guid? ApplicablePositionId { get; set; }

    /// <summary>
    /// 候选题安全等级。
    /// </summary>
    public int SecurityLevel { get; set; }

    /// <summary>
    /// 候选题标签。
    /// </summary>
    public string? Tags { get; set; }

    /// <summary>
    /// 候选题解析，来自模型结构化输出。
    /// </summary>
    public string? Analysis { get; set; }

    /// <summary>
    /// 结构化校验错误。非空时不能提交题库审核。
    /// </summary>
    public string? ValidationErrors { get; set; }

    /// <summary>
    /// 候选题采纳后生成的题库草稿 ID。
    /// 为空表示仍未进入题库工作流。
    /// </summary>
    public Guid? LinkedQuestionId { get; set; }

    /// <summary>
    /// 相似度最高的已有题目 ID，用于页面提示重复风险。
    /// </summary>
    public Guid? HighestSimilarQuestionId { get; set; }

    /// <summary>
    /// 与最高相似题的相似度分值，范围 0 到 1。
    /// </summary>
    public double? HighestSimilarityScore { get; set; }

    /// <summary>
    /// 相似题提示摘要，展示在审核页面。
    /// </summary>
    public string? SimilaritySummary { get; set; }

    /// <summary>
    /// 选择题选项。非选择题可以为空。
    /// </summary>
    public List<AiQuestionOptionDto> Options { get; set; } = new();

    /// <summary>
    /// 标准答案。提交题库审核前必须满足题型对应的答案约束。
    /// </summary>
    public List<AiQuestionAnswerDto> Answers { get; set; } = new();

    /// <summary>
    /// 候选题关联的知识点名称。
    /// </summary>
    public List<string> KnowledgePoints { get; set; } = new();

    /// <summary>
    /// 候选题引用的已授权知识来源。
    /// AI 候选题至少需要一个 citation 才能提交审核。
    /// </summary>
    public List<AiQuestionCandidateCitationDto> Citations { get; set; } = new();
}

/// <summary>
/// AI 候选题选项 DTO。
/// 该结构和题库选项 DTO 分开，避免候选题未审核前被误认为正式题库事实。
/// </summary>
public class AiQuestionOptionDto
{
    /// <summary>
    /// 选项键，例如 A、B、C。
    /// </summary>
    public string OptionKey { get; set; } = string.Empty;

    /// <summary>
    /// 选项正文。
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 选项显示顺序。
    /// </summary>
    public int DisplayOrder { get; set; }
}

/// <summary>
/// AI 候选题答案 DTO。
/// 选择题答案通常是选项键，判断题是 true/false 或 对/错，填空题是文本答案。
/// </summary>
public class AiQuestionAnswerDto
{
    /// <summary>
    /// 答案值。
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// 答案显示顺序，多答案题用于保持模型输出顺序。
    /// </summary>
    public int DisplayOrder { get; set; }
}

/// <summary>
/// AI 候选题 citation DTO。
/// Citation 保存的是短摘录和 hash，不保存完整知识正文。
/// </summary>
public class AiQuestionCandidateCitationDto : EntityDto<Guid>
{
    /// <summary>
    /// citation 所属候选题 ID。
    /// </summary>
    public Guid CandidateId { get; set; }

    /// <summary>
    /// 引用知识条目 ID。
    /// </summary>
    public Guid KnowledgeItemId { get; set; }

    /// <summary>
    /// 引用知识版本 ID，保证后续知识更新后仍能解释当时生成依据。
    /// </summary>
    public Guid KnowledgeVersionId { get; set; }

    /// <summary>
    /// 引用 chunk ID，是 RAG 和题目来源追溯的最小单元。
    /// </summary>
    public Guid ChunkId { get; set; }

    /// <summary>
    /// 引用排序，从 1 开始。
    /// </summary>
    public int Rank { get; set; }

    /// <summary>
    /// 检索相关性分值，来源于 RAG 检索结果。
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// 引用来源标题。
    /// </summary>
    public string SourceTitle { get; set; } = string.Empty;

    /// <summary>
    /// 引用来源页码。非分页来源为空。
    /// </summary>
    public int? SourcePage { get; set; }

    /// <summary>
    /// 受控长度的引用摘录，用于审核页面查看依据。
    /// </summary>
    public string SourceExcerpt { get; set; } = string.Empty;

    /// <summary>
    /// 引用原文 hash，用于追溯和比对，不暴露完整 chunk。
    /// </summary>
    public string SourceHash { get; set; } = string.Empty;
}

/// <summary>
/// AI 出题模型调用日志 DTO。
/// 该日志只保存摘要、hash、模型元数据和 token 统计，避免审计表持久化完整 prompt。
/// </summary>
public class AiQuestionModelCallLogDto : EntityDto<Guid>
{
    /// <summary>
    /// 调用所属 AI 出题任务。
    /// </summary>
    public Guid TaskId { get; set; }

    /// <summary>
    /// 调用使用的 prompt 模板 ID。
    /// </summary>
    public Guid PromptTemplateId { get; set; }

    /// <summary>
    /// 调用使用的 prompt 版本。
    /// </summary>
    public string PromptVersion { get; set; } = string.Empty;

    /// <summary>
    /// 模型调用结果状态。
    /// </summary>
    public RagCallStatus Status { get; set; }

    /// <summary>
    /// 检索 query 的 hash，用于排查重复任务，不保存完整敏感输入。
    /// </summary>
    public string QueryHash { get; set; } = string.Empty;

    /// <summary>
    /// 检索 query 的受控长度摘要。
    /// </summary>
    public string QuerySummary { get; set; } = string.Empty;

    /// <summary>
    /// 模型 provider 逻辑名称。
    /// </summary>
    public string ModelProvider { get; set; } = string.Empty;

    /// <summary>
    /// 模型 alias。
    /// </summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>
    /// 业务侧模型版本。
    /// </summary>
    public string ModelVersion { get; set; } = string.Empty;

    /// <summary>
    /// 本次调用使用的 RAG 上下文数量。
    /// </summary>
    public int TopK { get; set; }

    /// <summary>
    /// 实际通过授权校验并进入 prompt 的 chunk 数。
    /// </summary>
    public int RetrievedChunkCount { get; set; }

    /// <summary>
    /// 本次调用生成并通过校验的候选题数量。
    /// </summary>
    public int CandidateCount { get; set; }

    /// <summary>
    /// prompt token 数。模型未返回 usage 时使用估算值。
    /// </summary>
    public int PromptTokenCount { get; set; }

    /// <summary>
    /// completion token 数。模型未返回 usage 时使用估算值。
    /// </summary>
    public int CompletionTokenCount { get; set; }

    /// <summary>
    /// 总 token 数。
    /// </summary>
    public int TotalTokenCount { get; set; }

    /// <summary>
    /// 调用耗时，单位毫秒。
    /// </summary>
    public int ExecutionDurationMs { get; set; }

    /// <summary>
    /// 失败阶段，例如 retrieval、chat 或 parse。
    /// </summary>
    public string? FailureStage { get; set; }

    /// <summary>
    /// 失败原因摘要。不得包含完整 prompt 或完整知识正文。
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// 日志创建时间。
    /// </summary>
    public DateTime CreationTime { get; set; }
}
