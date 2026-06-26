using System;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.KnowledgeBase;

public class KnowledgeCategoryDto : FullAuditedEntityDto<Guid>
{
    public Guid? ParentId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
