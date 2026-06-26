using System.ComponentModel.DataAnnotations;

namespace KnowPulse.KnowledgeBase;

public class CreateKnowledgeVersionDto
{
    [Required]
    [StringLength(256)]
    public string Title { get; set; } = string.Empty;

    [StringLength(8192)]
    public string? ContentText { get; set; }
}
