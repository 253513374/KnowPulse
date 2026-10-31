using System;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

public class TeachingRelationRequest : AggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid SchoolId { get; private set; }
    public Guid TeacherUserId { get; private set; }
    public Guid SchoolMembershipId { get; private set; }
    public Guid? OriginalAssignmentId { get; private set; }
    public string? OriginalAssignmentStamp { get; private set; }
    public DateTime? OriginalEndsAtUtc { get; private set; }
    [DisableAuditing] public string RequestText { get; private set; } = "";
    public DateTime RecordedAtUtc { get; private set; }
    public string RequestFingerprint { get; private set; } = "";
    public TeachingRelationRequestStatus Status { get; private set; }
    public TeachingRelationResolution? Resolution { get; private set; }
    [DisableAuditing] public string? ResolutionText { get; private set; }
    public Guid? ResultingAssignmentId { get; private set; }
    public DateTime? EndedAtUtc { get; private set; }
    public Guid? CompletedByUserId { get; private set; }
    public Guid? CompletionOperationId { get; private set; }
    public string? CompletionFingerprint { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    protected TeachingRelationRequest() { }
    public TeachingRelationRequest(Guid id, SchoolMembership member, TeachingAssignment? original, string text, bool confirmSharing, string fingerprint, DateTime now)
        : base(EducationRules.RequireId(id))
    {
        if (!confirmSharing || member.Role != SchoolMemberRole.Teacher || !member.IsEffectiveAt(now) || original != null &&
            (original.TenantId != member.TenantId || original.SchoolId != member.SchoolId || original.TeacherUserId != member.UserId)) throw Invalid();
        TenantId = member.TenantId; SchoolId = member.SchoolId; TeacherUserId = member.UserId; SchoolMembershipId = member.Id;
        OriginalAssignmentId = original?.Id; OriginalAssignmentStamp = original?.ConcurrencyStamp; OriginalEndsAtUtc = original?.EffectiveToUtc;
        RequestText = Check.NotNullOrWhiteSpace(text, nameof(text), 2000).Trim(); RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint); RecordedAtUtc = EducationRules.RequireUtc(now);
    }
    public void Process(Guid actor, Guid operation, string fingerprint, TeachingRelationResolution resolution, string text, TeachingAssignment? result, DateTime? ended, bool confirmed, DateTime now)
    {
        if (!confirmed || !Enum.IsDefined(resolution) || resolution is TeachingRelationResolution.End or TeachingRelationResolution.Replace && !OriginalAssignmentId.HasValue ||
            resolution == TeachingRelationResolution.Add && OriginalAssignmentId.HasValue ||
            (resolution is TeachingRelationResolution.Add or TeachingRelationResolution.Replace) != (result != null) ||
            (resolution is TeachingRelationResolution.End or TeachingRelationResolution.Replace) != ended.HasValue ||
            result != null && (result.TenantId != TenantId || result.SchoolId != SchoolId || result.TeacherUserId != TeacherUserId || result.Id == OriginalAssignmentId) ||
            ended.HasValue && EducationRules.RequireUtc(ended.Value) < now) throw Invalid();
        var reason = Check.NotNullOrWhiteSpace(text, nameof(text), 2000).Trim(); Complete(actor, operation, fingerprint, now);
        Status = TeachingRelationRequestStatus.Processed; Resolution = resolution; ResolutionText = reason; ResultingAssignmentId = result?.Id; EndedAtUtc = ended;
    }
    public void Withdraw(Guid actor, Guid operation, string fingerprint, DateTime now)
    {
        if (actor != TeacherUserId) throw Invalid(); Complete(actor, operation, fingerprint, now); Status = TeachingRelationRequestStatus.Withdrawn;
    }
    private void Complete(Guid actor, Guid operation, string fingerprint, DateTime now)
    {
        if (Status != TeachingRelationRequestStatus.Pending || EducationRules.RequireUtc(now) < RecordedAtUtc) throw new BusinessException(TeachingRelationRequestErrors.Changed);
        CompletedByUserId = EducationRules.RequireId(actor); CompletionOperationId = EducationRules.RequireId(operation);
        CompletionFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint); CompletedAtUtc = now;
    }
    private static BusinessException Invalid() => new(TeachingRelationRequestErrors.Invalid);
}
