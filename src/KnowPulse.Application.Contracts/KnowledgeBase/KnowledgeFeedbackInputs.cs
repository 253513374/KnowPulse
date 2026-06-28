using System;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.KnowledgeBase;

/// <summary>
/// 知识反馈分页查询输入。
/// </summary>
public class GetKnowledgeFeedbackListInput : PagedAndSortedResultRequestDto
{
    /// <summary>
    /// 按来源知识条目筛选。
    /// </summary>
    public Guid? KnowledgeItemId { get; set; }

    /// <summary>
    /// 按来源知识版本筛选。
    /// </summary>
    public Guid? KnowledgeVersionId { get; set; }

    /// <summary>
    /// 按提交人筛选。
    /// </summary>
    public Guid? SubmitterUserId { get; set; }

    /// <summary>
    /// 按反馈类型筛选。
    /// </summary>
    public KnowledgeFeedbackType? Type { get; set; }

    /// <summary>
    /// 按处理状态筛选。
    /// </summary>
    public KnowledgeFeedbackStatus? Status { get; set; }

    /// <summary>
    /// 标题、描述或处理结论关键字。
    /// </summary>
    public string? Keyword { get; set; }
}

/// <summary>
/// 用户提交知识反馈输入。
/// </summary>
public class SubmitKnowledgeFeedbackDto
{
    /// <summary>
    /// 反馈来源知识条目 ID。
    /// </summary>
    public Guid KnowledgeItemId { get; set; }

    /// <summary>
    /// 反馈来源知识版本 ID；为空表示对知识条目整体反馈。
    /// </summary>
    public Guid? KnowledgeVersionId { get; set; }

    /// <summary>
    /// 反馈类型。
    /// </summary>
    public KnowledgeFeedbackType Type { get; set; } = KnowledgeFeedbackType.Problem;

    /// <summary>
    /// 反馈标题。
    /// </summary>
    [Required]
    [StringLength(256)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 反馈描述，说明具体问题、过期内容、错误内容、缺失内容或建议。
    /// </summary>
    [Required]
    [StringLength(2048)]
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// 管理员接手处理反馈输入。
/// </summary>
public class ProcessKnowledgeFeedbackDto
{
    /// <summary>
    /// 初步处理备注。
    /// </summary>
    [StringLength(2048)]
    public string? Resolution { get; set; }
}

/// <summary>
/// 管理员标记反馈已处理输入。
/// </summary>
public class ResolveKnowledgeFeedbackDto
{
    /// <summary>
    /// 必填处理结论。
    /// </summary>
    [Required]
    [StringLength(2048)]
    public string Resolution { get; set; } = string.Empty;

    /// <summary>
    /// 可选关联修订版本 ID。版本必须属于反馈来源知识条目。
    /// </summary>
    public Guid? LinkedKnowledgeVersionId { get; set; }
}

/// <summary>
/// 管理员关闭反馈输入。
/// </summary>
public class CloseKnowledgeFeedbackDto
{
    /// <summary>
    /// 关闭原因。
    /// </summary>
    [StringLength(1024)]
    public string? CloseReason { get; set; }
}

/// <summary>
/// 管理员关联反馈修订版本输入。
/// </summary>
public class LinkKnowledgeFeedbackVersionDto
{
    /// <summary>
    /// 修订版本 ID。版本必须属于反馈来源知识条目。
    /// </summary>
    public Guid LinkedKnowledgeVersionId { get; set; }

    /// <summary>
    /// 关联版本时的处理备注。
    /// </summary>
    [StringLength(2048)]
    public string? Resolution { get; set; }
}
