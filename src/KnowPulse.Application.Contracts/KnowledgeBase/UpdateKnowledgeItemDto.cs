using System;
using System.ComponentModel.DataAnnotations;

namespace KnowPulse.KnowledgeBase;

public class UpdateKnowledgeItemDto
{
    public Guid? CategoryId { get; set; }

    [Required]
    [StringLength(256)]
    public string Title { get; set; } = string.Empty;

    [StringLength(1024)]
    public string? Summary { get; set; }

    public bool IsActive { get; set; } = true;
}
