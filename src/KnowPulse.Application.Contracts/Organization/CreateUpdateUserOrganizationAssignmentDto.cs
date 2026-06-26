using System;
using System.ComponentModel.DataAnnotations;

namespace KnowPulse.Organization;

public class CreateUpdateUserOrganizationAssignmentDto
{
    [Required]
    public Guid UserId { get; set; }

    [Required]
    public Guid DepartmentId { get; set; }

    public Guid? PositionId { get; set; }

    public Guid? JobLevelId { get; set; }

    [Range(0, int.MaxValue)]
    public int SecurityLevel { get; set; }

    public bool IsPrimary { get; set; }

    public bool IsActive { get; set; } = true;
}
