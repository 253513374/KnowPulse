using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Exam;

public class ExamAssignment : FullAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; protected set; }

    public Guid PlanId { get; private set; }

    public ExamAssignmentScopeType ScopeType { get; private set; }

    public Guid? TargetId { get; private set; }

    public bool IsActive { get; private set; }

    protected ExamAssignment()
    {
    }

    public ExamAssignment(
        Guid id,
        Guid? tenantId,
        Guid planId,
        ExamAssignmentScopeType scopeType,
        Guid? targetId)
        : base(id)
    {
        TenantId = tenantId;
        PlanId = planId;
        ScopeType = scopeType;
        TargetId = scopeType == ExamAssignmentScopeType.All ? null : targetId;
        IsActive = true;
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
    }
}
