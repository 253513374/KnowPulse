using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

public class EducationCorrectionDelivery : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid PublicationId { get; private set; }
    public EducationCorrectionRevisionKind PublicationKind { get; private set; } = EducationCorrectionRevisionKind.Published;
    public Guid OwnerUserId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public DateTime AssignedAtUtc { get; private set; }
    public DateTime? FirstProvidedAtUtc { get; private set; }
    public int SubmissionNumber { get; private set; }
    public Guid? CurrentSubmissionId { get; private set; }
    public int ReviewNumber { get; private set; }
    public Guid? CurrentReviewId { get; private set; }
    protected EducationCorrectionDelivery() { }
    public EducationCorrectionDelivery(EducationCorrectionTaskRevision publication) : base(EducationStudentGrowthSnapshot.StableId($"education-correction-delivery-v1|{publication.Id:D}|{publication.StudentUserId:D}"))
    {
        if (publication.Kind != EducationCorrectionRevisionKind.Published) throw Invalid(); TenantId = publication.TenantId; TaskId = publication.TaskId; PublicationId = publication.Id;
        OwnerUserId = publication.OwnerUserId; StudentUserId = publication.StudentUserId; AssignedAtUtc = publication.RecordedAtUtc;
    }
    public void MarkProvided(DateTime now) { if (EducationRules.RequireUtc(now) < AssignedAtUtc) throw Invalid(); FirstProvidedAtUtc ??= now; }
    public void Submit(EducationCorrectionSubmission submission)
    {
        if (submission.DeliveryId != Id || submission.TenantId != TenantId || submission.StudentUserId != StudentUserId || submission.OwnerUserId != OwnerUserId ||
            submission.SubmissionNumber != SubmissionNumber + 1 || submission.BasedOnSubmissionId != CurrentSubmissionId || submission.ExpectedConcurrencyStamp != ConcurrencyStamp || FirstProvidedAtUtc == null) throw Invalid();
        SubmissionNumber = submission.SubmissionNumber; CurrentSubmissionId = submission.Id;
    }
    public void Review(EducationCorrectionReview review)
    {
        if (review.DeliveryId != Id || review.TenantId != TenantId || review.OwnerUserId != OwnerUserId || review.StudentUserId != StudentUserId || review.SubmissionId != CurrentSubmissionId ||
            review.ReviewNumber != ReviewNumber + 1 || review.BasedOnReviewId != CurrentReviewId || review.ExpectedConcurrencyStamp != ConcurrencyStamp) throw Invalid();
        ReviewNumber = review.ReviewNumber; CurrentReviewId = review.Id;
    }
    private static BusinessException Invalid() => new(EducationCorrectionErrors.Invalid);
}

public class EducationCorrectionSubmission : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid DeliveryId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public int SubmissionNumber { get; private set; }
    public Guid? BasedOnSubmissionId { get; private set; }
    public string ContentJson { get; private set; } = "";
    public string PreviewFingerprint { get; private set; } = "";
    public string RequestFingerprint { get; private set; } = "";
    public string ExpectedConcurrencyStamp { get; private set; } = "";
    public DateTime SubmittedAtUtc { get; private set; }
    protected EducationCorrectionSubmission() { }
    public EducationCorrectionContent Content() => EducationLearningReportJson.Read<EducationCorrectionContent>(ContentJson, 128 * 1024);
    public EducationCorrectionSubmission(Guid id, EducationCorrectionDelivery delivery, EducationCorrectionContent content, bool confirmedSharing,
        string preview, string fingerprint, DateTime now) : base(EducationRules.RequireId(id))
    {
        if (!confirmedSharing || delivery.FirstProvidedAtUtc == null || now < delivery.FirstProvidedAtUtc) throw new BusinessException(EducationCorrectionErrors.Invalid); content.Validate(submitted: true);
        TenantId = delivery.TenantId; DeliveryId = delivery.Id; StudentUserId = delivery.StudentUserId; OwnerUserId = delivery.OwnerUserId;
        SubmissionNumber = checked(delivery.SubmissionNumber + 1); BasedOnSubmissionId = delivery.CurrentSubmissionId; ContentJson = EducationLearningReportJson.Write(content, 128 * 1024);
        PreviewFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(preview); RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint);
        ExpectedConcurrencyStamp = delivery.ConcurrencyStamp; SubmittedAtUtc = EducationRules.RequireUtc(now);
    }
}

public class EducationCorrectionReview : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid DeliveryId { get; private set; }
    public Guid SubmissionId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public int ReviewNumber { get; private set; }
    public Guid? BasedOnReviewId { get; private set; }
    public EducationCorrectionReviewDecision Decision { get; private set; }
    public string StudentFeedback { get; private set; } = "";
    public string RequestFingerprint { get; private set; } = "";
    public string ExpectedConcurrencyStamp { get; private set; } = "";
    public DateTime PublishedAtUtc { get; private set; }
    protected EducationCorrectionReview() { }
    public EducationCorrectionReview(Guid id, EducationCorrectionDelivery delivery, EducationCorrectionSubmission submission, EducationCorrectionReviewDecision decision,
        string feedback, bool confirmedPublishing, string fingerprint, DateTime now) : base(EducationRules.RequireId(id))
    {
        if (!confirmedPublishing || !Enum.IsDefined(decision) || delivery.CurrentSubmissionId != submission.Id || submission.DeliveryId != delivery.Id ||
            submission.TenantId != delivery.TenantId || submission.StudentUserId != delivery.StudentUserId || submission.OwnerUserId != delivery.OwnerUserId || now < submission.SubmittedAtUtc) throw new BusinessException(EducationCorrectionErrors.Invalid);
        TenantId = delivery.TenantId; DeliveryId = delivery.Id; SubmissionId = submission.Id; StudentUserId = delivery.StudentUserId; OwnerUserId = delivery.OwnerUserId;
        ReviewNumber = checked(delivery.ReviewNumber + 1); BasedOnReviewId = delivery.CurrentReviewId; Decision = decision;
        StudentFeedback = Check.NotNullOrWhiteSpace(feedback, nameof(feedback), 2000); RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint);
        ExpectedConcurrencyStamp = delivery.ConcurrencyStamp; PublishedAtUtc = EducationRules.RequireUtc(now);
    }
}
