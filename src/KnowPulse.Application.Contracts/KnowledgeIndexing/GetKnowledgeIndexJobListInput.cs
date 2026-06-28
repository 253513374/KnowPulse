using System;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.KnowledgeIndexing;

/// <summary>
/// 索引任务分页查询输入。
/// 所有过滤条件都是可选项，用于运维页按知识条目、版本或状态定位任务。
/// </summary>
public class GetKnowledgeIndexJobListInput : PagedAndSortedResultRequestDto
{
    /// <summary>
    /// 按知识条目过滤。
    /// </summary>
    public Guid? KnowledgeItemId { get; set; }

    /// <summary>
    /// 按知识版本过滤。
    /// </summary>
    public Guid? KnowledgeVersionId { get; set; }

    /// <summary>
    /// 按当前任务状态过滤。
    /// </summary>
    public KnowledgeIndexJobStatus? Status { get; set; }
}
