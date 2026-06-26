using System;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.Organization;

public class PositionDto : FullAuditedEntityDto<Guid>
{
    public Guid? DepartmentId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SecurityLevel { get; set; }
    public bool IsActive { get; set; }
}
