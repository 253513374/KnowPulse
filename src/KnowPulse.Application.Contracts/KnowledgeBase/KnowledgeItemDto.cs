using System;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.KnowledgeBase;

public class KnowledgeItemDto : FullAuditedEntityDto<Guid>
{
    public Guid? OwnerTeacherId { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? CurrentPublishedVersionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public bool IsActive { get; set; }
}
