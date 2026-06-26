using System;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.KnowledgeBase;

public class KnowledgeVersionDto : FullAuditedEntityDto<Guid>
{
    public Guid KnowledgeItemId { get; set; }
    public int VersionNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? ContentText { get; set; }
    public KnowledgeVersionStatus Status { get; set; }
    public bool IsCurrentPublished { get; set; }
    public DateTime? PublishedTime { get; set; }
    public DateTime? WithdrawnTime { get; set; }
    public DateTime? ArchivedTime { get; set; }
    public DateTime? DisabledTime { get; set; }
}
