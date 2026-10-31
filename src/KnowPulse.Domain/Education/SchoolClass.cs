using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

/// <summary>按学期保存班级年级快照；升学创建新学期班级，不改写过去的年级。</summary>
public class SchoolClass : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public const int MaxCodeLength = 64;
    public const int MaxNameLength = 128;
    public Guid? TenantId { get; protected set; }
    public Guid SchoolId { get; private set; }
    public Guid AcademicTermId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public int Grade { get; private set; }

    protected SchoolClass() { }

    public SchoolClass(Guid id, Guid? tenantId, Guid schoolId, Guid academicTermId, string code, string name, int grade)
        : base(EducationRules.RequireId(id))
    {
        if (grade < 7 || grade > 9)
        {
            throw new BusinessException(EducationErrorCodes.InvalidGrade);
        }

        TenantId = tenantId;
        SchoolId = EducationRules.RequireId(schoolId);
        AcademicTermId = EducationRules.RequireId(academicTermId);
        Code = EducationRules.NormalizeCode(code, MaxCodeLength);
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), MaxNameLength).Trim();
        Grade = grade;
    }
}
