using System;
using System.ComponentModel.DataAnnotations;

namespace KnowPulse.KnowledgeBase;

public class CreateUpdateKnowledgeCategoryDto
{
    public Guid? ParentId { get; set; }

    [Required]
    [StringLength(64)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(128)]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
