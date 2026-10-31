using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using KnowPulse.Education;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.QuestionBank;

/// <summary>
/// 题目分页查询输入。
/// 支持按题型、状态、难度、创建来源、岗位、知识点和关键字组合筛选。
/// </summary>
public class GetQuestionListInput : PagedAndSortedResultRequestDto
{
    /// <summary>教材组卷时仅返回此当前范围内已确认评分的课程题。</summary>
    public EducationCurriculumScopeDto? CurriculumScope { get; set; }
    /// <summary>
    /// 按草稿、审核队列或正式题库任务视图筛选。
    /// 指定后由服务端统一映射状态，避免页面复制状态组合判断。
    /// </summary>
    public QuestionWorklistKind? Worklist { get; set; }

    /// <summary>
    /// 题型筛选。
    /// 为空表示不过滤题型。
    /// </summary>
    public QuestionType? Type { get; set; }

    /// <summary>
    /// 题目状态筛选。
    /// 为空表示不过滤状态；正式题库查询会在服务端强制限定为审核通过。
    /// </summary>
    public QuestionStatus? Status { get; set; }

    /// <summary>
    /// 题目难度筛选。
    /// 为空表示不过滤难度。
    /// </summary>
    public QuestionDifficulty? Difficulty { get; set; }

    /// <summary>
    /// 创建来源筛选。
    /// 为空表示同时查询人工维护题和 AI 候选题提交后的题目。
    /// </summary>
    public QuestionCreationSource? CreationSource { get; set; }

    /// <summary>
    /// 适用岗位编号筛选。
    /// 为空表示不过滤岗位。
    /// </summary>
    public Guid? ApplicablePositionId { get; set; }

    /// <summary>
    /// 知识点名称关键字筛选。
    /// 服务端会匹配题目关联知识点名称中包含该文本的题目。
    /// </summary>
    public string? KnowledgePoint { get; set; }

    /// <summary>
    /// 题目关键字筛选。
    /// 服务端会匹配标题、题干和标签。
    /// </summary>
    public string? Keyword { get; set; }

    /// <summary>
    /// 是否只返回可用于新考试组卷的题目。
    /// 当前等价于只返回审核通过题目。
    /// </summary>
    public bool OnlyUsableForExam { get; set; }
}

/// <summary>
/// 创建题目输入。
/// 包含题目基础信息、题目定义、标准解析、知识点和来源引用。
/// </summary>
public class SaveQuestionDraftDto
{
    /// <summary>逐空、多选或人工评分规则。草稿可未完成，正式确认时完整校验。</summary>
    public QuestionScoringDefinition? ScoringDefinition { get; set; }
    /// <summary>手工新建课程题时必需；同编号同参数重试返回原草稿。编辑时忽略。</summary>
    public Guid? OperationId { get; set; }
    [StringLength(40)] public string? ConcurrencyStamp { get; set; }
    public EducationCurriculumScopeDto? CurriculumScope { get; set; }
    [Range(0.01, 1000)] public decimal CurriculumScore { get; set; } = 1;
    public List<QuestionCurriculumPointWeight> CurriculumPointWeights { get; set; } = [];
    /// <summary>
    /// 题型。
    /// 不同题型会触发不同的选项和答案校验规则。
    /// </summary>
    [Required]
    public QuestionType Type { get; set; }

    /// <summary>
    /// 题目难度。
    /// 未指定时默认为普通难度。
    /// </summary>
    [Required]
    public QuestionDifficulty Difficulty { get; set; } = QuestionDifficulty.Normal;

    /// <summary>
    /// 题目标题，最长 256 个字符。
    /// </summary>
    [Required]
    [StringLength(256)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 题干正文，最长 4000 个字符。
    /// </summary>
    [Required]
    [StringLength(4000)]
    public string Stem { get; set; } = string.Empty;

    /// <summary>
    /// 适用岗位编号。
    /// 为空表示题目不限定特定岗位。
    /// </summary>
    public Guid? ApplicablePositionId { get; set; }

    /// <summary>
    /// 题目安全等级，取值范围为 0 到 10。
    /// </summary>
    [Range(0, 10)]
    public int SecurityLevel { get; set; }

    /// <summary>
    /// 题目标签，最长 512 个字符。
    /// 多个标签建议使用英文逗号分隔。
    /// </summary>
    [StringLength(512)]
    public string? Tags { get; set; }

    /// <summary>
    /// 标准解析或答案说明，最长 2048 个字符。
    /// </summary>
    [StringLength(2048)]
    public string? Analysis { get; set; }

    /// <summary>
    /// 选择题选项列表。
    /// 单选题和多选题至少需要两个有效选项。
    /// </summary>
    public List<CreateUpdateQuestionOptionDto> Options { get; set; } = new();

    /// <summary>
    /// 标准答案列表。
    /// 单选题必须只有一个答案，多选题至少两个答案，判断题必须是 true/false 或 对/错。
    /// </summary>
    public List<CreateUpdateQuestionAnswerDto> Answers { get; set; } = new();

    /// <summary>
    /// 题目关联的知识点名称列表。
    /// 服务端会去除空白项并按忽略大小写去重。
    /// </summary>
    public List<string> KnowledgePointNames { get; set; } = new();

    /// <summary>
    /// 题目来源引用列表。
    /// 每个来源至少需要包含知识库引用字段或人工来源说明。
    /// </summary>
    public List<CreateUpdateQuestionSourceDto> Sources { get; set; } = new();
}

public class ConfirmQuestionCurriculumDto
{
    [Required] public EducationCurriculumScopeDto Scope { get; set; } = new();
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = string.Empty;
    [Range(0.01, 1000)] public decimal Score { get; set; } = 1;
    public List<QuestionCurriculumPointWeight> PointWeights { get; set; } = [];
}

/// <summary>
/// 兼容现有题目创建接口的输入。
/// <see cref="CreationSource"/> 仅用于旧客户端兼容，服务端创建人工草稿时不会采用调用方传入值。
/// </summary>
public class CreateQuestionDto : SaveQuestionDraftDto
{
    /// <summary>
    /// 旧版调用方提交的创建来源。新工作流会由服务端按入口确定真实来源。
    /// </summary>
    [Required]
    public QuestionCreationSource CreationSource { get; set; } = QuestionCreationSource.Manual;
}

/// <summary>
/// 更新题目输入。
/// 与创建题目输入保持一致；更新已通过、已驳回、已退回修改或已停用题目会让题目回到草稿状态。
/// </summary>
public class UpdateQuestionDto : CreateQuestionDto
{
}

/// <summary>
/// 题目审核决策输入。
/// </summary>
public class DecideQuestionReviewDto
{
    /// <summary>课程题审核必需，同编号同请求恢复同一审核决策。</summary>
    public Guid? OperationId { get; set; }
    [StringLength(40)] public string? ConcurrencyStamp { get; set; }
    /// <summary>
    /// 审核决策。
    /// </summary>
    [Required]
    public QuestionReviewDecision Decision { get; set; }

    /// <summary>
    /// 审核意见。退回或驳回时必须填写。
    /// </summary>
    [StringLength(512)]
    public string? Comment { get; set; }
}

/// <summary>
/// 题目可用性变更输入。
/// </summary>
public class ChangeQuestionAvailabilityDto
{
    /// <summary>
    /// 可用性变更动作。
    /// </summary>
    [Required]
    public QuestionAvailabilityAction Action { get; set; }
}

/// <summary>
/// 创建或更新题目选项输入。
/// </summary>
public class CreateUpdateQuestionOptionDto
{
    /// <summary>
    /// 选项键，最长 16 个字符，例如 A、B、C、D。
    /// </summary>
    [Required]
    [StringLength(16)]
    public string OptionKey { get; set; } = string.Empty;

    /// <summary>
    /// 选项内容，最长 1024 个字符。
    /// </summary>
    [Required]
    [StringLength(1024)]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 展示顺序。
    /// 传入 0 时服务端会按输入列表顺序自动补齐。
    /// </summary>
    public int DisplayOrder { get; set; }
}

/// <summary>
/// 创建或更新题目标准答案输入。
/// </summary>
public class CreateUpdateQuestionAnswerDto
{
    /// <summary>
    /// 答案值，最长 512 个字符。
    /// 选择题应填写选项键；判断题应填写 true/false 或 对/错；填空题填写标准答案文本。
    /// </summary>
    [Required]
    [StringLength(512)]
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// 答案顺序。
    /// 传入 0 时服务端会按输入列表顺序自动补齐。
    /// </summary>
    public int DisplayOrder { get; set; }
}

/// <summary>
/// 创建或更新题目来源输入。
/// </summary>
public class CreateUpdateQuestionSourceDto
{
    public Guid? SchoolKnowledgeShareId { get; set; }
    public List<Guid> CurriculumBindingIds { get; set; } = [];
    public List<Guid> TextbookKnowledgePointMappingIds { get; set; } = [];
    /// <summary>
    /// 关联的知识条目编号。
    /// </summary>
    public Guid? KnowledgeItemId { get; set; }

    /// <summary>
    /// 关联的知识版本编号。
    /// </summary>
    public Guid? KnowledgeVersionId { get; set; }

    /// <summary>
    /// 关联的知识分块编号。
    /// </summary>
    public Guid? ChunkId { get; set; }

    /// <summary>
    /// 来源标题，最长 256 个字符。
    /// </summary>
    [StringLength(256)]
    public string? SourceTitle { get; set; }

    /// <summary>
    /// 来源摘录，最长 512 个字符。
    /// </summary>
    [StringLength(512)]
    public string? SourceExcerpt { get; set; }

    /// <summary>
    /// 来源内容哈希，最长 64 个字符。
    /// </summary>
    [StringLength(64)]
    public string? SourceHash { get; set; }

    /// <summary>
    /// 人工来源说明，最长 512 个字符。
    /// 未关联知识库对象时必须填写该字段。
    /// </summary>
    [StringLength(512)]
    public string? ManualSourceDescription { get; set; }
}

/// <summary>
/// 题目审核意见输入。
/// </summary>
public class ReviewQuestionDto
{
    public Guid? OperationId { get; set; }
    [StringLength(40)] public string? ConcurrencyStamp { get; set; }
    /// <summary>
    /// 审核意见或退回原因，最长 512 个字符。
    /// </summary>
    [StringLength(512)]
    public string? Comment { get; set; }
}
