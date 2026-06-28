using System;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.KnowledgeBase;

/// <summary>
/// 知识反馈 DTO，包含来源知识、处理状态、处理结论和关联修订版本信息。
/// </summary>
public class KnowledgeFeedbackDto : FullAuditedEntityDto<Guid>
{
    /// <summary>
    /// 反馈来源知识条目 ID。
    /// </summary>
    public Guid KnowledgeItemId { get; set; }

    /// <summary>
    /// 反馈来源知识标题。
    /// </summary>
    public string KnowledgeTitle { get; set; } = string.Empty;

    /// <summary>
    /// 反馈来源知识版本 ID；用户未指定版本时为空。
    /// </summary>
    public Guid? KnowledgeVersionId { get; set; }

    /// <summary>
    /// 来源知识版本标题。
    /// </summary>
    public string? KnowledgeVersionTitle { get; set; }

    /// <summary>
    /// 来源知识版本号。
    /// </summary>
    public int? KnowledgeVersionNumber { get; set; }

    /// <summary>
    /// 反馈提交人用户 ID。
    /// </summary>
    public Guid SubmitterUserId { get; set; }

    /// <summary>
    /// 反馈类型。
    /// </summary>
    public KnowledgeFeedbackType Type { get; set; }

    /// <summary>
    /// 反馈处理状态。
    /// </summary>
    public KnowledgeFeedbackStatus Status { get; set; }

    /// <summary>
    /// 反馈标题。
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 反馈描述。
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 当前处理人用户 ID。
    /// </summary>
    public Guid? HandlerUserId { get; set; }

    /// <summary>
    /// 最近一次处理时间。
    /// </summary>
    public DateTime? HandledTime { get; set; }

    /// <summary>
    /// 处理备注或处理结论。
    /// </summary>
    public string? Resolution { get; set; }

    /// <summary>
    /// 关联的修订知识版本 ID。
    /// </summary>
    public Guid? LinkedKnowledgeVersionId { get; set; }

    /// <summary>
    /// 关联修订版本标题。
    /// </summary>
    public string? LinkedKnowledgeVersionTitle { get; set; }

    /// <summary>
    /// 关联修订版本号。
    /// </summary>
    public int? LinkedKnowledgeVersionNumber { get; set; }

    /// <summary>
    /// 反馈关闭时间。
    /// </summary>
    public DateTime? ClosedTime { get; set; }

    /// <summary>
    /// 反馈关闭原因。
    /// </summary>
    public string? CloseReason { get; set; }
}
