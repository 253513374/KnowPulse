using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.Learning;

/// <summary>
/// 学习路径分页查询输入。
/// </summary>
public class GetLearningPathListInput : PagedAndSortedResultRequestDto
{
    /// <summary>
    /// 名称或说明关键字。
    /// </summary>
    public string? Keyword { get; set; }

    /// <summary>
    /// 是否只查询启用或停用路径；为空表示不限制。
    /// </summary>
    public bool? IsActive { get; set; }
}

/// <summary>
/// 创建学习路径输入。结构与更新输入一致。
/// </summary>
public class CreateLearningPathDto : UpdateLearningPathDto
{
}

/// <summary>
/// 更新学习路径输入。路径配置采用整体替换方式保存。
/// </summary>
public class UpdateLearningPathDto
{
    /// <summary>
    /// 学习路径名称。
    /// </summary>
    [Required]
    [StringLength(256)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 学习路径说明。
    /// </summary>
    [StringLength(1024)]
    public string? Description { get; set; }

    /// <summary>
    /// 路径分配规则。至少需要一条分配规则。
    /// </summary>
    public List<LearningPathAssignmentInputDto> Assignments { get; set; } = new();

    /// <summary>
    /// 必学知识配置。
    /// </summary>
    public List<LearningPathKnowledgeInputDto> RequiredKnowledge { get; set; } = new();

    /// <summary>
    /// 推荐知识配置。
    /// </summary>
    public List<LearningPathKnowledgeInputDto> RecommendedKnowledge { get; set; } = new();

    /// <summary>
    /// 必考考试配置。
    /// </summary>
    public List<LearningPathRequiredExamInputDto> RequiredExams { get; set; } = new();
}

/// <summary>
/// 学习路径分配规则输入。
/// </summary>
public class LearningPathAssignmentInputDto
{
    /// <summary>
    /// 分配范围类型。
    /// </summary>
    public LearningPathAssignmentScopeType ScopeType { get; set; } = LearningPathAssignmentScopeType.Department;

    /// <summary>
    /// 分配目标 ID；全员范围传空，其他范围必须传对应部门、岗位或职级 ID。
    /// </summary>
    public Guid? TargetId { get; set; }
}

/// <summary>
/// 学习路径知识配置输入，同时用于必学知识和推荐知识。
/// </summary>
public class LearningPathKnowledgeInputDto
{
    /// <summary>
    /// 知识条目 ID。
    /// </summary>
    public Guid KnowledgeItemId { get; set; }

    /// <summary>
    /// 知识版本 ID；为空时使用知识条目的当前发布版本。
    /// </summary>
    public Guid? KnowledgeVersionId { get; set; }

    /// <summary>
    /// 展示顺序；小于等于 0 时由应用服务按输入顺序归一化。
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// 必学说明。
    /// </summary>
    [StringLength(512)]
    public string? Note { get; set; }

    /// <summary>
    /// 推荐理由。
    /// </summary>
    [StringLength(512)]
    public string? Reason { get; set; }
}

/// <summary>
/// 学习路径必考考试配置输入。
/// </summary>
public class LearningPathRequiredExamInputDto
{
    /// <summary>
    /// 考试计划 ID。必须引用已发布考试计划。
    /// </summary>
    public Guid ExamPlanId { get; set; }

    /// <summary>
    /// 展示顺序；小于等于 0 时由应用服务按输入顺序归一化。
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// 是否必须达到及格线才算完成。
    /// </summary>
    public bool PassingRequired { get; set; } = true;

    /// <summary>
    /// 考试说明或完成要求。
    /// </summary>
    [StringLength(512)]
    public string? Note { get; set; }
}

/// <summary>
/// 当前用户阅读或完成学习路径知识的输入。
/// </summary>
public class MarkLearningKnowledgeReadDto
{
    /// <summary>
    /// 学习路径 ID。
    /// </summary>
    public Guid LearningPathId { get; set; }

    /// <summary>
    /// 知识条目 ID。
    /// </summary>
    public Guid KnowledgeItemId { get; set; }

    /// <summary>
    /// 知识版本 ID。
    /// </summary>
    public Guid KnowledgeVersionId { get; set; }

    /// <summary>
    /// 是否同时标记为完成。
    /// </summary>
    public bool MarkCompleted { get; set; }
}
