using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Teaching;

public class TeachingRelationship : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; protected set; }
    public Guid TeacherId { get; private set; }
    public Guid StudentId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public TeachingRelationshipStatus Status { get; private set; }

    protected TeachingRelationship() { }

    public TeachingRelationship(Guid id, Guid? tenantId, Guid teacherId, Guid studentId, string displayName) : base(id)
    {
        TenantId = tenantId;
        TeacherId = teacherId;
        StudentId = studentId;
        Apply(displayName);
    }

    public void Apply(string displayName)
    {
        if (Status == TeachingRelationshipStatus.Active) return;
        DisplayName = Check.NotNullOrWhiteSpace(displayName, nameof(displayName), 64);
        Status = TeachingRelationshipStatus.Pending;
    }

    public void Confirm()
    {
        if (Status == TeachingRelationshipStatus.Removed)
            throw new UserFriendlyException("请先让学生重新申请加入。");
        Status = TeachingRelationshipStatus.Active;
    }

    public void Remove() => Status = TeachingRelationshipStatus.Removed;
}
