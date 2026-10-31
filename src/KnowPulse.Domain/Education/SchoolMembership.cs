using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

public class SchoolMembership : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; protected set; }
    public Guid SchoolId { get; private set; }
    public Guid UserId { get; private set; }
    public SchoolMemberRole Role { get; private set; }
    public DateTime EffectiveFromUtc { get; private set; }
    public DateTime? EffectiveToUtc { get; private set; }

    protected SchoolMembership() { }

    public SchoolMembership(Guid id, Guid? tenantId, Guid schoolId, Guid userId, SchoolMemberRole role,
        DateTime effectiveFromUtc, DateTime? effectiveToUtc = null) : base(EducationRules.RequireId(id))
    {
        if (!Enum.IsDefined(role))
        {
            throw new BusinessException(EducationErrorCodes.InvalidRole);
        }

        EducationRules.ValidatePeriod(effectiveFromUtc, effectiveToUtc);
        TenantId = tenantId;
        SchoolId = EducationRules.RequireId(schoolId);
        UserId = EducationRules.RequireId(userId);
        Role = role;
        EffectiveFromUtc = effectiveFromUtc;
        EffectiveToUtc = effectiveToUtc;
    }

    public bool IsEffectiveAt(DateTime atUtc) => EducationRules.IsEffectiveAt(EffectiveFromUtc, EffectiveToUtc, atUtc);

    public void EndAt(DateTime toUtc)
    {
        EffectiveToUtc = EducationRules.EndPeriod(EffectiveFromUtc, EffectiveToUtc, toUtc);
    }
}
