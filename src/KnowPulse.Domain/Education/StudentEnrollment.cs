using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

public class StudentEnrollment : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; protected set; }
    public Guid SchoolId { get; private set; }
    public Guid SchoolClassId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public DateTime EffectiveFromUtc { get; private set; }
    public DateTime? EffectiveToUtc { get; private set; }

    protected StudentEnrollment() { }

    public StudentEnrollment(Guid id, Guid? tenantId, Guid schoolId, Guid schoolClassId, Guid studentUserId,
        DateTime effectiveFromUtc, DateTime? effectiveToUtc = null) : base(EducationRules.RequireId(id))
    {
        EducationRules.ValidatePeriod(effectiveFromUtc, effectiveToUtc);
        TenantId = tenantId;
        SchoolId = EducationRules.RequireId(schoolId);
        SchoolClassId = EducationRules.RequireId(schoolClassId);
        StudentUserId = EducationRules.RequireId(studentUserId);
        EffectiveFromUtc = effectiveFromUtc;
        EffectiveToUtc = effectiveToUtc;
    }

    public bool IsEffectiveAt(DateTime atUtc) => EducationRules.IsEffectiveAt(EffectiveFromUtc, EffectiveToUtc, atUtc);

    public void EndAt(DateTime toUtc)
    {
        EffectiveToUtc = EducationRules.EndPeriod(EffectiveFromUtc, EffectiveToUtc, toUtc);
    }
}
