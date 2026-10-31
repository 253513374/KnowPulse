using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

public class EducationTeachingPlanHintReview : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid PlanId { get; private set; }
    public Guid PlanRevisionId { get; private set; }
    public int RevisionNumber { get; private set; }
    public Guid? CurrentRevisionId { get; private set; }
    protected EducationTeachingPlanHintReview() { }
    public EducationTeachingPlanHintReview(EducationTeachingPlanQuestionExposure exposure, EducationTeachingPlanRevision published) : base(exposure.Id)
    {
        if (exposure.TenantId != published.TenantId || exposure.StudentUserId != published.StudentUserId || exposure.PlanId != published.PlanId ||
            exposure.PlanRevisionId != published.Id || published.Kind != EducationTeachingPlanRevisionKind.Published ||
            !published.ContainsQuestion(new(exposure.TaskId, exposure.TaskRevisionId, exposure.RecipientId, exposure.ExamPaperId,
                exposure.PaperQuestionId, exposure.OriginalQuestionId, exposure.DefinitionRevisionId)))
            throw new BusinessException(EducationLearningErrorCodes.InvalidEvidence);
        TenantId = exposure.TenantId; OwnerUserId = published.OwnerUserId; StudentUserId = exposure.StudentUserId;
        PlanId = exposure.PlanId; PlanRevisionId = published.Id;
    }
    public void Advance(EducationTeachingPlanHintReviewRevision revision)
    {
        if (revision.ExposureId != Id || revision.TenantId != TenantId || revision.OwnerUserId != OwnerUserId || revision.StudentUserId != StudentUserId ||
            revision.PlanId != PlanId || revision.PlanRevisionId != PlanRevisionId || revision.RevisionNumber != RevisionNumber + 1 ||
            revision.BasedOnRevisionId != CurrentRevisionId || revision.ExpectedConcurrencyStamp != ConcurrencyStamp)
            throw new BusinessException(EducationLearningErrorCodes.InvalidEvidence);
        CurrentRevisionId = revision.Id; RevisionNumber = revision.RevisionNumber;
    }
}

public class EducationTeachingPlanHintReviewRevision : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid ExposureId { get; private set; }
    public Guid PlanId { get; private set; }
    public Guid PlanRevisionId { get; private set; }
    public int RevisionNumber { get; private set; }
    public Guid? BasedOnRevisionId { get; private set; }
    public EducationLearningExposureUse Use { get; private set; }
    public string Reason { get; private set; } = "";
    public string ExpectedConcurrencyStamp { get; private set; } = "";
    public string RequestFingerprint { get; private set; } = "";
    public DateTime RecordedAtUtc { get; private set; }
    protected EducationTeachingPlanHintReviewRevision() { }
    public EducationTeachingPlanHintReviewRevision(Guid id, EducationTeachingPlanHintReview root, EducationLearningExposureUse use,
        bool confirmedExactContext, string reason, string stamp, string fingerprint, DateTime now) : base(EducationRules.RequireId(id))
    {
        if (!Enum.IsDefined(use) || !confirmedExactContext || stamp != root.ConcurrencyStamp)
            throw new BusinessException(EducationLearningErrorCodes.InvalidEvidence);
        TenantId = root.TenantId; OwnerUserId = root.OwnerUserId; StudentUserId = root.StudentUserId; ExposureId = root.Id;
        PlanId = root.PlanId; PlanRevisionId = root.PlanRevisionId; RevisionNumber = checked(root.RevisionNumber + 1); BasedOnRevisionId = root.CurrentRevisionId;
        Use = use; Reason = Check.NotNullOrWhiteSpace(reason, nameof(reason), 2048);
        ExpectedConcurrencyStamp = Check.NotNullOrWhiteSpace(stamp, nameof(stamp), 40);
        RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint); RecordedAtUtc = EducationRules.RequireUtc(now);
    }
}
