using System;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.Organization;

public class UserOrganizationAssignmentDto : FullAuditedEntityDto<Guid>
{
    public Guid UserId { get; set; }
    public Guid DepartmentId { get; set; }
    public Guid? PositionId { get; set; }
    public Guid? JobLevelId { get; set; }
    public int SecurityLevel { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; }
}
