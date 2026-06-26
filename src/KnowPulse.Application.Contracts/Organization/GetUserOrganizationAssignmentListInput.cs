using System;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.Organization;

public class GetUserOrganizationAssignmentListInput : PagedAndSortedResultRequestDto
{
    public Guid? UserId { get; set; }
    public Guid? DepartmentId { get; set; }
    public bool? IsActive { get; set; }
}
