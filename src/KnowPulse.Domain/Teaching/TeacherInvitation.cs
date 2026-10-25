using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Teaching;

public class TeacherInvitation : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; protected set; }
    public Guid TeacherId { get; private set; }
    public string InvitationCode { get; private set; } = string.Empty;

    protected TeacherInvitation() { }

    public TeacherInvitation(Guid id, Guid? tenantId, Guid teacherId, string invitationCode) : base(id)
    {
        TenantId = tenantId;
        TeacherId = teacherId;
        InvitationCode = Check.NotNullOrWhiteSpace(invitationCode, nameof(invitationCode), 32);
    }
}
