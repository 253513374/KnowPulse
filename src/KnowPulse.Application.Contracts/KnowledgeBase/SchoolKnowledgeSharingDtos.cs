using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using KnowPulse.KnowledgeIndexing;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.KnowledgeBase;

public class CreateSchoolKnowledgeShareDto
{
    public Guid IdempotencyKey { get; set; }
    public Guid KnowledgeItemId { get; set; }
    public Guid KnowledgeVersionId { get; set; }
    public Guid SchoolId { get; set; }
    [StringLength(1024)]
    public string? SharedSummary { get; set; }
    public SchoolKnowledgeShareUses Uses { get; set; } = SchoolKnowledgeShareUses.Read | SchoolKnowledgeShareUses.Retrieve;
}

public class GetSchoolKnowledgeShareListInput : PagedResultRequestDto
{
    public Guid? KnowledgeItemId { get; set; }
    public Guid? SchoolId { get; set; }
    public bool? IsRevoked { get; set; }
    [StringLength(256)]
    public string? Keyword { get; set; }
}

public class SchoolKnowledgeShareDto : EntityDto<Guid>
{
    public List<Guid> CurriculumBindingIds { get; set; } = new();
    public Guid OwnerUserId { get; set; }
    public Guid KnowledgeItemId { get; set; }
    public Guid KnowledgeVersionId { get; set; }
    public Guid SchoolId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public SchoolKnowledgeShareUses Uses { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime SharedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}

public class SchoolKnowledgeShareDetailDto
{
    public SchoolKnowledgeShareDto Share { get; set; } = new();
    public int VersionNumber { get; set; }
    public string? ContentText { get; set; }
    public KnowledgeIndexJobStatus? IndexStatus { get; set; }
    public List<KnowledgeAttachmentDto> Attachments { get; set; } = new();
}
