using System;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

public class SchoolInvitation : AggregateRoot<Guid>, IMultiTenant
{
    public const int MaximumValidityDays = 7;
    public Guid? TenantId { get; private set; }
    public Guid SchoolId { get; private set; }
    public Guid InvitedByUserId { get; private set; }
    public Guid TargetUserId { get; private set; }
    public SchoolMemberRole Role { get; private set; }
    public DateTime EffectiveFromUtc { get; private set; }
    public DateTime? EffectiveToUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public string RequestFingerprint { get; private set; } = "";
    public SchoolInvitationStatus Status { get; private set; }
    public Guid? MembershipId { get; private set; }
    public Guid? CompletedByUserId { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public Guid? CompletionOperationId { get; private set; }
    public string? CompletionFingerprint { get; private set; }
    public SchoolInvitationDeclineReason? DeclineReason { get; private set; }
    [DisableAuditing] public string? RevocationReason { get; private set; }

    protected SchoolInvitation() { }
    public SchoolInvitation(Guid id, Guid? tenantId, Guid schoolId, Guid inviter, Guid target, SchoolMemberRole role,
        DateTime fromUtc, DateTime? toUtc, DateTime expiresUtc, bool confirmVerifiedAccount, string fingerprint, DateTime now)
        : base(EducationRules.RequireId(id))
    {
        if (!confirmVerifiedAccount || role is not (SchoolMemberRole.Teacher or SchoolMemberRole.Student) || inviter == target ||
            EducationRules.RequireUtc(expiresUtc) <= EducationRules.RequireUtc(now) || expiresUtc > now.AddDays(MaximumValidityDays) || toUtc <= now) throw Invalid();
        EducationRules.ValidatePeriod(fromUtc, toUtc);
        TenantId = tenantId; SchoolId = EducationRules.RequireId(schoolId); InvitedByUserId = EducationRules.RequireId(inviter); TargetUserId = EducationRules.RequireId(target);
        Role = role; EffectiveFromUtc = fromUtc; EffectiveToUtc = toUtc; ExpiresAtUtc = expiresUtc; RecordedAtUtc = now;
        RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint);
    }
    public SchoolInvitationStatus StatusAt(DateTime now) => Status == SchoolInvitationStatus.Pending &&
        (EducationRules.RequireUtc(now) >= ExpiresAtUtc || EffectiveToUtc <= now) ? SchoolInvitationStatus.Expired : Status;

    public void Accept(Guid actor, Guid operation, string fingerprint, SchoolMembership membership, bool confirmIdentityAndSchool, DateTime now)
    {
        var from = EffectiveFromUtc > now ? EffectiveFromUtc : now;
        if (actor != TargetUserId || !confirmIdentityAndSchool || membership.TenantId != TenantId || membership.SchoolId != SchoolId || membership.UserId != TargetUserId ||
            membership.Role != Role || membership.EffectiveFromUtc > from || membership.EffectiveToUtc.HasValue && (!EffectiveToUtc.HasValue || EffectiveToUtc > membership.EffectiveToUtc)) throw Invalid();
        Complete(actor, operation, fingerprint, SchoolInvitationStatus.Accepted, now); MembershipId = membership.Id;
    }
    public void Decline(Guid actor, Guid operation, string fingerprint, SchoolInvitationDeclineReason reason, DateTime now)
    {
        if (actor != TargetUserId || !Enum.IsDefined(reason)) throw Invalid();
        Complete(actor, operation, fingerprint, SchoolInvitationStatus.Declined, now); DeclineReason = reason;
    }
    public void Revoke(Guid actor, Guid operation, string fingerprint, string reason, DateTime now)
    {
        var text = Check.NotNullOrWhiteSpace(reason, nameof(reason), 1000).Trim();
        Complete(actor, operation, fingerprint, SchoolInvitationStatus.Revoked, now); RevocationReason = text;
    }
    private void Complete(Guid actor, Guid operation, string fingerprint, SchoolInvitationStatus status, DateTime now)
    {
        if (StatusAt(now) != SchoolInvitationStatus.Pending || now < RecordedAtUtc) throw new BusinessException(SchoolInvitationErrors.Unavailable);
        Status = status; CompletedByUserId = EducationRules.RequireId(actor); CompletionOperationId = EducationRules.RequireId(operation);
        CompletionFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint); CompletedAtUtc = EducationRules.RequireUtc(now);
    }
    private static BusinessException Invalid() => new(SchoolInvitationErrors.Invalid);
}
