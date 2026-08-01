using System;
using KnowPulse.QuestionBank;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.AiQuestioning;

/// <summary>
/// AI 模型输出的候选题实体。
/// 候选题是正式题库之前的隔离区：它保存模型结构化输出、校验结果、citation 和相似题提示，
/// 只有被人工采纳后才会转换为题库草稿，草稿仍需单独提交审核。
/// </summary>
public class AiQuestionCandidate : FullAuditedEntity<Guid>, IMultiTenant
{
    /// <summary>
    /// 候选题标题最大长度，和题库题目标题长度保持接近，避免提交审核时再被截断。
    /// </summary>
    public const int MaxTitleLength = 256;

    /// <summary>
    /// 候选题题干最大长度，保留足够空间给情景题，但限制模型异常长输出。
    /// </summary>
    public const int MaxStemLength = 4000;

    /// <summary>
    /// 标签文本最大长度。标签在 MVP-2 中以逗号分隔文本保存。
    /// </summary>
    public const int MaxTagsLength = 512;

    /// <summary>
    /// 解析文本最大长度，避免模型把大段知识原文复制到解析中。
    /// </summary>
    public const int MaxAnalysisLength = 2048;

    /// <summary>
    /// 结构化 JSON 字段最大长度，覆盖选项、答案和知识点列表。
    /// </summary>
    public const int MaxJsonLength = 8000;

    /// <summary>
    /// 校验错误摘要最大长度，用于页面展示和排查，不保存完整模型响应。
    /// </summary>
    public const int MaxValidationErrorsLength = 2048;

    /// <summary>
    /// 相似题提示摘要最大长度。
    /// </summary>
    public const int MaxSimilaritySummaryLength = 512;

    /// <summary>
    /// 所属租户，保证候选题不会跨租户展示或提交审核。
    /// </summary>
    public Guid? TenantId { get; protected set; }

    /// <summary>
    /// 候选题所属 AI 出题任务 ID。
    /// </summary>
    public Guid TaskId { get; private set; }

    /// <summary>
    /// 候选题状态，决定页面是否允许提交题库审核。
    /// </summary>
    public AiQuestionCandidateStatus Status { get; private set; }

    /// <summary>
    /// 候选题题型。创建时使用任务要求的题型，而不是盲信模型输出。
    /// </summary>
    public QuestionType Type { get; private set; }

    /// <summary>
    /// 候选题难度。创建时使用任务要求的难度。
    /// </summary>
    public QuestionDifficulty Difficulty { get; private set; }

    /// <summary>
    /// 候选题标题。
    /// </summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>
    /// 候选题题干。
    /// </summary>
    public string Stem { get; private set; } = string.Empty;

    /// <summary>
    /// 候选题适用岗位。为空表示不限定岗位。
    /// </summary>
    public Guid? ApplicablePositionId { get; private set; }

    /// <summary>
    /// 候选题安全等级，提交题库审核时会继承到正式题草稿。
    /// </summary>
    public int SecurityLevel { get; private set; }

    /// <summary>
    /// 候选题标签。
    /// </summary>
    public string? Tags { get; private set; }

    /// <summary>
    /// 候选题解析，来自模型结构化输出。
    /// </summary>
    public string? Analysis { get; private set; }

    /// <summary>
    /// 选项 JSON。候选题未审核前不直接复用正式题库选项实体。
    /// </summary>
    public string OptionsJson { get; private set; } = "[]";

    /// <summary>
    /// 答案 JSON。提交审核前会转换为正式题库答案实体。
    /// </summary>
    public string AnswersJson { get; private set; } = "[]";

    /// <summary>
    /// 知识点名称 JSON，用于保留模型抽取的知识点标签。
    /// </summary>
    public string KnowledgePointsJson { get; private set; } = "[]";

    /// <summary>
    /// 结构化校验错误。非空时候选题不能提交审核。
    /// </summary>
    public string? ValidationErrors { get; private set; }

    /// <summary>
    /// 候选题采纳后生成的题库草稿 ID。
    /// 为空表示该候选题仍停留在候选区。
    /// </summary>
    public Guid? LinkedQuestionId { get; private set; }

    /// <summary>
    /// 相似度最高的已有题目 ID，用于提示重复或高度相似风险。
    /// </summary>
    public Guid? HighestSimilarQuestionId { get; private set; }

    /// <summary>
    /// 与最高相似题的相似度分值，范围 0 到 1。
    /// </summary>
    public double? HighestSimilarityScore { get; private set; }

    /// <summary>
    /// 相似题提示摘要，页面展示给审核人员参考。
    /// </summary>
    public string? SimilaritySummary { get; private set; }

    /// <summary>
    /// EF Core 使用的构造函数。
    /// </summary>
    protected AiQuestionCandidate()
    {
    }

    /// <summary>
    /// 创建一个 AI 候选题。
    /// 构造函数负责长度和安全等级边界校验；题型语义校验、citation 校验和相似题检测由应用服务完成。
    /// </summary>
    public AiQuestionCandidate(
        Guid id,
        Guid? tenantId,
        Guid taskId,
        AiQuestionCandidateStatus status,
        QuestionType type,
        QuestionDifficulty difficulty,
        string title,
        string stem,
        Guid? applicablePositionId,
        int securityLevel,
        string? tags,
        string? analysis,
        string optionsJson,
        string answersJson,
        string knowledgePointsJson,
        string? validationErrors)
        : base(id)
    {
        if (securityLevel is < 0 or > 10)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.InvalidSecurityLevel);
        }

        TenantId = tenantId;
        TaskId = taskId;
        Status = status;
        Type = type;
        Difficulty = difficulty;
        Title = Check.NotNullOrWhiteSpace(title, nameof(title), MaxTitleLength);
        Stem = Check.NotNullOrWhiteSpace(stem, nameof(stem), MaxStemLength);
        ApplicablePositionId = applicablePositionId;
        SecurityLevel = securityLevel;
        Tags = Check.Length(tags, nameof(tags), MaxTagsLength);
        Analysis = Check.Length(analysis, nameof(analysis), MaxAnalysisLength);
        OptionsJson = Check.NotNull(optionsJson, nameof(optionsJson)).Length <= MaxJsonLength
            ? optionsJson
            : throw new ArgumentOutOfRangeException(nameof(optionsJson));
        AnswersJson = Check.NotNull(answersJson, nameof(answersJson)).Length <= MaxJsonLength
            ? answersJson
            : throw new ArgumentOutOfRangeException(nameof(answersJson));
        KnowledgePointsJson = Check.NotNull(knowledgePointsJson, nameof(knowledgePointsJson)).Length <= MaxJsonLength
            ? knowledgePointsJson
            : throw new ArgumentOutOfRangeException(nameof(knowledgePointsJson));
        ValidationErrors = Check.Length(validationErrors, nameof(validationErrors), MaxValidationErrorsLength);
    }

    /// <summary>
    /// 写入最高相似题检测结果。
    /// 分值只接受 0 到 1，低于提示阈值时调用方应传入空值以保持页面干净。
    /// </summary>
    public void SetSimilarity(Guid? similarQuestionId, double? score, string? summary)
    {
        if (score is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(score));
        }

        HighestSimilarQuestionId = similarQuestionId;
        HighestSimilarityScore = score;
        SimilaritySummary = Check.Length(summary, nameof(summary), MaxSimilaritySummaryLength);
    }

    /// <summary>
    /// 标记候选题已经被人工采纳为题库草稿。
    /// 该方法只记录关联和状态，草稿仍需编辑确认后主动提交审核。
    /// </summary>
    public void MarkAdoptedAsDraft(Guid questionId)
    {
        LinkedQuestionId = questionId;
        Status = AiQuestionCandidateStatus.AdoptedAsDraft;
    }
}
