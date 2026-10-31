using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

public class AcademicTerm : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public const int MaxCodeLength = 64;
    public const int MaxNameLength = 128;
    public Guid? TenantId { get; protected set; }
    public Guid SchoolId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public DateTime StartsAtUtc { get; private set; }
    public DateTime EndsAtUtc { get; private set; }

    protected AcademicTerm() { }

    public AcademicTerm(Guid id, Guid? tenantId, Guid schoolId, string code, string name,
        DateTime startsAtUtc, DateTime endsAtUtc) : base(EducationRules.RequireId(id))
    {
        EducationRules.ValidatePeriod(startsAtUtc, endsAtUtc);
        TenantId = tenantId;
        SchoolId = EducationRules.RequireId(schoolId);
        Code = EducationRules.NormalizeCode(code, MaxCodeLength);
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), MaxNameLength).Trim();
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
    }
}
