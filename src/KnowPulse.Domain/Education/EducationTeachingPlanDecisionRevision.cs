using System;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

public class EducationTeachingPlanDecisionRevision : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid PlanId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid PublishedRevisionId { get; private set; }
    public Guid? BasedOnDecisionId { get; private set; }
    public int RevisionNumber { get; private set; }
    public EducationPlanDecision Decision { get; private set; }
    public EducationPlanExecutionState Before { get; private set; }
    public EducationPlanExecutionState After { get; private set; }
    [DisableAuditing] public string Reason { get; private set; } = "";
    [DisableAuditing] public string EvidenceJson { get; private set; } = "";
    public string EvidenceFingerprint { get; private set; } = "";
    public string RequestFingerprint { get; private set; } = "";
    public string ExpectedConcurrencyStamp { get; private set; } = "";
    public DateTime RecordedAtUtc { get; private set; }
    protected EducationTeachingPlanDecisionRevision() { }
    public EducationTeachingPlanDecisionRevision(Guid id, EducationTeachingPlan plan, EducationTeachingPlanDecisionRevision? previous,
        EducationPlanDecision decision, string reason, EducationPlanExecutionEvidence evidence, string evidenceFingerprint,
        bool confirmReviewedEvidence, string requestFingerprint, string stamp, DateTime now) : base(EducationRules.RequireId(id))
    {
        if (!Enum.IsDefined(decision) || plan.PublishedRevisionId != evidence.PublishedRevisionId || evidence.PublishedRevisionId == Guid.Empty ||
            plan.CurrentDecisionId != previous?.Id || previous != null && (previous.PlanId != plan.Id || previous.OwnerUserId != plan.OwnerUserId ||
            previous.StudentUserId != plan.StudentUserId || previous.TenantId != plan.TenantId || previous.After != plan.ExecutionState) ||
            plan.ExecutionState is EducationPlanExecutionState.Ended or EducationPlanExecutionState.Terminated ||
            decision is EducationPlanDecision.Continue or EducationPlanDecision.Adjust or EducationPlanDecision.End && !confirmReviewedEvidence ||
            decision is EducationPlanDecision.Resume or EducationPlanDecision.Continue && !evidence.SourceIsCurrent ||
            decision == EducationPlanDecision.Resume && previous?.Decision == EducationPlanDecision.Adjust && previous.PublishedRevisionId == evidence.PublishedRevisionId ||
            evidence.Activities.Count > 50 || evidence.Activities.Any(x => x.ConfirmedGradeRevisionIds.Count > 500 || x.ConfirmedGradeRevisionIds.Any(id => id == Guid.Empty) ||
                x.ConfirmedGradeRevisionIds.Distinct().Count() != x.ConfirmedGradeRevisionIds.Count) || now < (previous?.RecordedAtUtc ?? DateTime.MinValue)) throw Invalid();
        After = decision switch {
            EducationPlanDecision.Pause when plan.ExecutionState == EducationPlanExecutionState.Running => EducationPlanExecutionState.Paused,
            EducationPlanDecision.Resume when plan.ExecutionState == EducationPlanExecutionState.Paused => EducationPlanExecutionState.Running,
            EducationPlanDecision.Continue when plan.ExecutionState == EducationPlanExecutionState.Running => EducationPlanExecutionState.Running,
            EducationPlanDecision.Adjust => EducationPlanExecutionState.Paused,
            EducationPlanDecision.End => EducationPlanExecutionState.Ended,
            EducationPlanDecision.Terminate => EducationPlanExecutionState.Terminated,
            _ => throw Invalid()
        };
        TenantId = plan.TenantId; PlanId = plan.Id; OwnerUserId = plan.OwnerUserId; StudentUserId = plan.StudentUserId;
        PublishedRevisionId = evidence.PublishedRevisionId; BasedOnDecisionId = plan.CurrentDecisionId; RevisionNumber = checked(plan.DecisionRevisionNumber + 1);
        Before = plan.ExecutionState; Decision = decision; Reason = Check.NotNullOrWhiteSpace(reason, nameof(reason), 2000);
        EvidenceJson = EducationLearningReportJson.Write(evidence, EducationPlanExecutionEvidence.MaxBytes); EvidenceFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(evidenceFingerprint);
        RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(requestFingerprint); ExpectedConcurrencyStamp = Check.NotNullOrWhiteSpace(stamp, nameof(stamp), 40);
        RecordedAtUtc = EducationRules.RequireUtc(now);
    }
    private static BusinessException Invalid() => new(EducationTeachingPlanErrors.Invalid);
}
