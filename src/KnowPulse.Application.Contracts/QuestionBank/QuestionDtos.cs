using System;
using System.Collections.Generic;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.QuestionBank;

/// <summary>
/// 题目详情 DTO。
/// 用于题库列表、详情页和 REST API 响应，包含题目基础信息、状态、题目定义、来源引用和审核记录。
/// </summary>
public class QuestionDto : FullAuditedEntityDto<Guid>
{
    public Guid? OwnerTeacherId { get; set; }
    /// <summary>
    /// 题型，例如单选、多选、判断或填空。
    /// </summary>
    public QuestionType Type { get; set; }

    /// <summary>
    /// 题目当前状态。
    /// 只有 <see cref="QuestionStatus.Approved"/> 状态的题目可进入正式题库和后续新试卷组卷。
    /// </summary>
    public QuestionStatus Status { get; set; }

    /// <summary>
    /// 题目难度。
    /// </summary>
    public QuestionDifficulty Difficulty { get; set; }

    /// <summary>
    /// 题目创建来源，区分人工维护题和 AI 候选题提交后的题目。
    /// </summary>
    public QuestionCreationSource CreationSource { get; set; }

    /// <summary>
    /// 题目标题，用于列表展示、检索和人工识别。
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 题干正文。
    /// </summary>
    public string Stem { get; set; } = string.Empty;

    /// <summary>
    /// 适用岗位编号。
    /// 为空表示题目不限定特定岗位。
    /// </summary>
    public Guid? ApplicablePositionId { get; set; }

    /// <summary>
    /// 题目安全等级。
    /// 数值越高代表访问和组卷时需要更高的安全等级。
    /// </summary>
    public int SecurityLevel { get; set; }

    /// <summary>
    /// 题目标签，多个标签以逗号分隔。
    /// </summary>
    public string? Tags { get; set; }

    /// <summary>
    /// 最近一次提交审核时间。
    /// </summary>
    public DateTime? SubmittedTime { get; set; }

    /// <summary>
    /// 最近一次审核、停用或归档产生的处理时间。
    /// </summary>
    public DateTime? ReviewedTime { get; set; }

    /// <summary>
    /// 最近一次审核人的用户编号。
    /// 系统操作或无登录用户上下文时可能为空。
    /// </summary>
    public Guid? ReviewedByUserId { get; set; }

    /// <summary>
    /// 当前题目是否可用于新考试组卷。
    /// 该字段由题目状态派生，当前仅审核通过题为 true。
    /// </summary>
    public bool IsUsableForExam { get; set; }

    /// <summary>
    /// 标准解析或答案说明。
    /// </summary>
    public string? Analysis { get; set; }

    /// <summary>
    /// 选择题选项。
    /// 单选题和多选题至少需要两个选项；判断题和填空题通常为空。
    /// </summary>
    public List<QuestionOptionDto> Options { get; set; } = new();

    /// <summary>
    /// 标准答案列表。
    /// 单选题通常只有一个答案，多选题可有多个答案，判断题使用 true/false 或 对/错。
    /// </summary>
    public List<QuestionAnswerDto> Answers { get; set; } = new();

    /// <summary>
    /// 题目关联的知识点名称列表。
    /// </summary>
    public List<QuestionKnowledgePointDto> KnowledgePoints { get; set; } = new();

    /// <summary>
    /// 题目来源引用列表。
    /// 可追溯到知识条目、知识版本、知识分块或人工来源描述。
    /// </summary>
    public List<QuestionSourceDto> Sources { get; set; } = new();

    /// <summary>
    /// 题目的审核和状态操作记录。
    /// </summary>
    public List<QuestionReviewDto> Reviews { get; set; } = new();
}

/// <summary>
/// 题目选项 DTO。
/// 表示选择题中的一个可选项。
/// </summary>
public class QuestionOptionDto : EntityDto<Guid>
{
    /// <summary>
    /// 选项键，例如 A、B、C、D。
    /// 标准答案会通过该键引用正确选项。
    /// </summary>
    public string OptionKey { get; set; } = string.Empty;

    /// <summary>
    /// 选项展示内容。
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 选项展示顺序，值越小越靠前。
    /// </summary>
    public int DisplayOrder { get; set; }
}

/// <summary>
/// 题目标准答案 DTO。
/// 选择题答案通常保存选项键，判断题保存 true/false 或 对/错，填空题保存标准文本。
/// </summary>
public class QuestionAnswerDto : EntityDto<Guid>
{
    /// <summary>
    /// 标准答案值。
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// 答案展示或比对顺序，值越小越靠前。
    /// </summary>
    public int DisplayOrder { get; set; }
}

/// <summary>
/// 题目知识点 DTO。
/// </summary>
public class QuestionKnowledgePointDto : EntityDto<Guid>
{
    /// <summary>
    /// 知识点名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// 题目来源 DTO。
/// 用于说明题目从哪个知识条目、知识版本、知识分块或人工材料中整理而来。
/// </summary>
public class QuestionSourceDto : EntityDto<Guid>
{
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
    /// 来源标题，用于页面展示和人工核验。
    /// </summary>
    public string? SourceTitle { get; set; }

    /// <summary>
    /// 来源摘录。
    /// 只保存短文本片段，避免在题库 DTO 中暴露完整知识正文。
    /// </summary>
    public string? SourceExcerpt { get; set; }

    /// <summary>
    /// 来源内容哈希。
    /// 用于判断引用片段是否被后续知识版本变更影响。
    /// </summary>
    public string? SourceHash { get; set; }

    /// <summary>
    /// 人工录入的来源说明。
    /// 当题目不是从知识库版本或分块生成时，用该字段保留来源依据。
    /// </summary>
    public string? ManualSourceDescription { get; set; }
}

/// <summary>
/// 题目审核记录 DTO。
/// 记录提交审核、审核通过、驳回、退回修改、停用和归档等状态操作。
/// </summary>
public class QuestionReviewDto : CreationAuditedEntityDto<Guid>
{
    /// <summary>
    /// 执行审核或状态操作的用户编号。
    /// </summary>
    public Guid? ReviewerUserId { get; set; }

    /// <summary>
    /// 审核或状态操作类型。
    /// </summary>
    public QuestionReviewAction Action { get; set; }

    /// <summary>
    /// 操作前的题目状态。
    /// </summary>
    public QuestionStatus FromStatus { get; set; }

    /// <summary>
    /// 操作后的题目状态。
    /// </summary>
    public QuestionStatus ToStatus { get; set; }

    /// <summary>
    /// 审核意见或系统状态操作说明。
    /// </summary>
    public string? Comment { get; set; }

    /// <summary>
    /// 审核或状态操作发生时间。
    /// </summary>
    public DateTime ReviewTime { get; set; }
}
