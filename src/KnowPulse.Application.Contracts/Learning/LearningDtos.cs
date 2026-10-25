using System;
using System.Collections.Generic;
using KnowPulse.Exam;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.Learning;

/// <summary>
/// 学习路径详情 DTO，用于管理端路径配置和个人学习中心展示。
/// </summary>
public class LearningPathDto : FullAuditedEntityDto<Guid>
{
    /// <summary>
    /// 学习路径名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 学习路径说明。
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 是否启用。停用路径不会出现在个人学习中心。
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// 配置版本，用于标识路径配置被编辑或停用的次数。
    /// </summary>
    public int ConfigVersion { get; set; }

    /// <summary>
    /// 路径分配规则列表。
    /// </summary>
    public List<LearningPathAssignmentDto> Assignments { get; set; } = new();

    /// <summary>
    /// 必学知识列表。个人视角下已按知识权限过滤。
    /// </summary>
    public List<LearningPathKnowledgeDto> RequiredKnowledge { get; set; } = new();

    /// <summary>
    /// 推荐知识列表。个人视角下已按知识权限过滤。
    /// </summary>
    public List<LearningPathKnowledgeDto> RecommendedKnowledge { get; set; } = new();

    /// <summary>
    /// 必考考试列表，包含考试完成状态和最近考试时间。
    /// </summary>
    public List<LearningPathRequiredExamDto> RequiredExams { get; set; } = new();
}

/// <summary>
/// 学习路径分配规则 DTO。
/// </summary>
public class LearningPathAssignmentDto : EntityDto<Guid>
{
    /// <summary>
    /// 所属学习路径 ID。
    /// </summary>
    public Guid LearningPathId { get; set; }

    /// <summary>
    /// 分配范围类型。
    /// </summary>
    public LearningPathAssignmentScopeType ScopeType { get; set; }

    /// <summary>
    /// 分配目标 ID；全员范围为空。
    /// </summary>
    public Guid? TargetId { get; set; }

    /// <summary>
    /// 分配规则是否启用。
    /// </summary>
    public bool IsActive { get; set; }
}

/// <summary>
/// 学习路径中的知识项 DTO，同时服务必学知识和推荐知识。
/// </summary>
public class LearningPathKnowledgeDto : EntityDto<Guid>
{
    /// <summary>
    /// 所属学习路径 ID。
    /// </summary>
    public Guid LearningPathId { get; set; }

    /// <summary>
    /// 知识条目 ID。
    /// </summary>
    public Guid KnowledgeItemId { get; set; }

    /// <summary>
    /// 学习路径固定的知识版本 ID。
    /// </summary>
    public Guid KnowledgeVersionId { get; set; }

    /// <summary>
    /// 知识标题。
    /// </summary>
    public string KnowledgeTitle { get; set; } = string.Empty;

    /// <summary>
    /// 知识版本标题。
    /// </summary>
    public string VersionTitle { get; set; } = string.Empty;

    /// <summary>
    /// 知识版本号。
    /// </summary>
    public int VersionNumber { get; set; }

    /// <summary>
    /// 展示顺序。
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// 必学知识说明。
    /// </summary>
    public string? Note { get; set; }

    /// <summary>
    /// 推荐知识理由。
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// 当前用户的完成状态。
    /// </summary>
    public LearningContentCompletionStatus CompletionStatus { get; set; }

    /// <summary>
    /// 当前用户最近阅读时间。
    /// </summary>
    public DateTime? LastReadTime { get; set; }

    /// <summary>
    /// 当前用户标记完成时间。
    /// </summary>
    public DateTime? CompletedTime { get; set; }

    /// <summary>
    /// 当前用户阅读次数。
    /// </summary>
    public int ReadCount { get; set; }
}

/// <summary>
/// 学习路径中的必考考试 DTO。
/// </summary>
public class LearningPathRequiredExamDto : EntityDto<Guid>
{
    /// <summary>
    /// 所属学习路径 ID。
    /// </summary>
    public Guid LearningPathId { get; set; }

    /// <summary>
    /// 考试计划 ID。
    /// </summary>
    public Guid ExamPlanId { get; set; }

    /// <summary>
    /// 考试名称。
    /// </summary>
    public string ExamName { get; set; } = string.Empty;

    /// <summary>
    /// 考试计划状态。
    /// </summary>
    public ExamPlanStatus ExamStatus { get; set; }

    /// <summary>
    /// 考试开始时间。
    /// </summary>
    public DateTime? StartTime { get; set; }

    /// <summary>
    /// 考试结束时间。
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// 展示顺序。
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// 是否要求达到及格线才算完成。
    /// </summary>
    public bool PassingRequired { get; set; }

    /// <summary>
    /// 管理员配置的考试说明。
    /// </summary>
    public string? Note { get; set; }

    /// <summary>
    /// 当前用户的考试完成状态。
    /// </summary>
    public LearningContentCompletionStatus CompletionStatus { get; set; }

    /// <summary>
    /// 考试计划是否实际分配给当前用户。未分配时学习中心展示但禁用入口。
    /// </summary>
    public bool IsAssignedToCurrentUser { get; set; } = true;

    /// <summary>
    /// 当前用户最近一次考试活动时间。
    /// </summary>
    public DateTime? LastAttemptTime { get; set; }

    /// <summary>
    /// 当前用户最近一次通过考试的时间。
    /// </summary>
    public DateTime? PassedTime { get; set; }

    /// <summary>
    /// 当前用户历史最高得分。
    /// </summary>
    public decimal? BestScore { get; set; }

    /// <summary>
    /// 考试及格分。
    /// </summary>
    public decimal? PassingScore { get; set; }
}
