using System;
using System.Collections.Generic;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.KnowledgeBase;

public class KnowledgeItemDto : FullAuditedEntityDto<Guid>
{
    public Guid? OwnerTeacherId { get; set; }
    public string ConcurrencyStamp { get; set; } = string.Empty;
    public PersonalMaterialType MaterialType { get; set; }
    public int? MaterialYear { get; set; }
    public string? SubjectName { get; set; }
    public List<string> CustomTags { get; set; } = [];
    public Guid? LibraryId { get; set; }
    public Guid? OwnerUserId { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? CurrentPublishedVersionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public bool IsActive { get; set; }
}
