using System;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.Organization;

public class JobLevelDto : FullAuditedEntityDto<Guid>
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public int SecurityLevel { get; set; }
    public bool IsActive { get; set; }
}
