using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

public class TeachingAssignment : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; protected set; }
    public Guid SchoolId { get; private set; }
    public Guid AcademicTermId { get; private set; }
    public Guid SchoolClassId { get; private set; }
    public Guid EducationSubjectId { get; private set; }
    public Guid TeacherUserId { get; private set; }
    public DateTime EffectiveFromUtc { get; private set; }
    public DateTime? EffectiveToUtc { get; private set; }

    protected TeachingAssignment() { }

    public TeachingAssignment(Guid id, Guid? tenantId, Guid schoolId, Guid academicTermId, Guid schoolClassId,
        Guid educationSubjectId, Guid teacherUserId, DateTime effectiveFromUtc, DateTime? effectiveToUtc = null)
        : base(EducationRules.RequireId(id))
    {
        EducationRules.ValidatePeriod(effectiveFromUtc, effectiveToUtc);
        TenantId = tenantId;
        SchoolId = EducationRules.RequireId(schoolId);
        AcademicTermId = EducationRules.RequireId(academicTermId);
        SchoolClassId = EducationRules.RequireId(schoolClassId);
        EducationSubjectId = EducationRules.RequireId(educationSubjectId);
        TeacherUserId = EducationRules.RequireId(teacherUserId);
        EffectiveFromUtc = effectiveFromUtc;
        EffectiveToUtc = effectiveToUtc;
    }

    public bool IsEffectiveAt(DateTime atUtc) => EducationRules.IsEffectiveAt(EffectiveFromUtc, EffectiveToUtc, atUtc);

    public void EndAt(DateTime toUtc)
    {
        EffectiveToUtc = EducationRules.EndPeriod(EffectiveFromUtc, EffectiveToUtc, toUtc);
    }
}
