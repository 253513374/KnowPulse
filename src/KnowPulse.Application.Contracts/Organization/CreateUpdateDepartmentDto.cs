using System;
using System.ComponentModel.DataAnnotations;

namespace KnowPulse.Organization;

public class CreateUpdateDepartmentDto
{
    public Guid? ParentId { get; set; }

    [Required]
    [StringLength(64)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(128)]
    public string Name { get; set; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int SecurityLevel { get; set; }

    public bool IsActive { get; set; } = true;
}
