using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

public class EducationSubject : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public const int MaxCodeLength = 64;
    public const int MaxNameLength = 128;
    public Guid? TenantId { get; protected set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;

    protected EducationSubject() { }

    public EducationSubject(Guid id, Guid? tenantId, string code, string name) : base(EducationRules.RequireId(id))
    {
        TenantId = tenantId;
        Code = EducationRules.NormalizeCode(code, MaxCodeLength);
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), MaxNameLength).Trim();
    }
}
