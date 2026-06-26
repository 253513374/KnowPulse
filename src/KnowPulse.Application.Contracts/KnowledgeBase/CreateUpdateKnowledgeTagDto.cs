using System.ComponentModel.DataAnnotations;

namespace KnowPulse.KnowledgeBase;

public class CreateUpdateKnowledgeTagDto
{
    [Required]
    [StringLength(64)]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
