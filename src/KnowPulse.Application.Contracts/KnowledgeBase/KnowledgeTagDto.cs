using System;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.KnowledgeBase;

public class KnowledgeTagDto : FullAuditedEntityDto<Guid>
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
