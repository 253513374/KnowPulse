using System;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.KnowledgeBase;

public class KnowledgeAttachmentDto : FullAuditedEntityDto<Guid>
{
    public Guid KnowledgeVersionId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
}
