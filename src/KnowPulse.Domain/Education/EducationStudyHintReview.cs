using System;
using KnowPulse.Exam;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

public class EducationStudyHintReview : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid RecipientId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid PaperQuestionId { get; private set; }
    public Guid ExamPaperId { get; private set; }
    public DateTime ExposedAtUtc { get; private set; }
    public int RevisionNumber { get; private set; }
    public Guid? CurrentRevisionId { get; private set; }
    protected EducationStudyHintReview() { }
    public static Guid Identity(Guid recipientId, Guid paperQuestionId) => EducationStudentGrowthSnapshot.StableId($"education-study-hint-v1|{recipientId:D}|{paperQuestionId:D}");
    public EducationStudyHintReview(EducationStudyRecipient recipient, EducationStudyTaskRevision publication, ExamPaperQuestion question) : base(Identity(recipient.Id, question.Id))
    {
        if (recipient.FirstProvidedAtUtc == null || recipient.TaskRevisionId != publication.Id || publication.Kind != EducationStudyTaskRevisionKind.Published ||
            recipient.TaskId != publication.TaskId || recipient.OwnerUserId != publication.OwnerUserId || recipient.TenantId != publication.TenantId ||
            question.TenantId != recipient.TenantId || question.QuestionDefinitionRevisionId == null || question.CurriculumSnapshotJson == null || question.CurriculumPointWeightsJson == null)
            throw Invalid();
        TenantId = recipient.TenantId; RecipientId = recipient.Id; StudentUserId = recipient.StudentUserId; OwnerUserId = recipient.OwnerUserId;
        PaperQuestionId = question.Id; ExamPaperId = question.PaperId; ExposedAtUtc = recipient.FirstProvidedAtUtc.Value;
    }
    public void Advance(EducationStudyHintReviewRevision revision)
    {
        if (revision.ReviewId != Id || revision.RecipientId != RecipientId || revision.PaperQuestionId != PaperQuestionId || revision.OwnerUserId != OwnerUserId ||
            revision.StudentUserId != StudentUserId || revision.TenantId != TenantId || revision.RevisionNumber != RevisionNumber + 1 || revision.BasedOnRevisionId != CurrentRevisionId ||
            revision.ExpectedConcurrencyStamp != ConcurrencyStamp) throw Invalid();
        CurrentRevisionId = revision.Id; RevisionNumber = revision.RevisionNumber;
    }
    private static BusinessException Invalid() => new(EducationLearningErrorCodes.InvalidEvidence);
}

public class EducationStudyHintReviewRevision : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid ReviewId { get; private set; }
    public Guid RecipientId { get; private set; }
    public Guid PaperQuestionId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public int RevisionNumber { get; private set; }
    public Guid? BasedOnRevisionId { get; private set; }
    public EducationLearningExposureUse Use { get; private set; }
    public string Reason { get; private set; } = "";
    public string ExpectedConcurrencyStamp { get; private set; } = "";
    public string RequestFingerprint { get; private set; } = "";
    public DateTime RecordedAtUtc { get; private set; }
    protected EducationStudyHintReviewRevision() { }
    public EducationStudyHintReviewRevision(Guid id, EducationStudyHintReview root, EducationLearningExposureUse use, bool confirmedExactContext,
        string reason, string fingerprint, DateTime now) : base(EducationRules.RequireId(id))
    {
        if (!Enum.IsDefined(use) || !confirmedExactContext || now < root.ExposedAtUtc) throw new BusinessException(EducationLearningErrorCodes.InvalidEvidence);
        TenantId = root.TenantId; ReviewId = root.Id; RecipientId = root.RecipientId; PaperQuestionId = root.PaperQuestionId; StudentUserId = root.StudentUserId; OwnerUserId = root.OwnerUserId;
        RevisionNumber = checked(root.RevisionNumber + 1); BasedOnRevisionId = root.CurrentRevisionId; Use = use;
        Reason = Check.NotNullOrWhiteSpace(reason, nameof(reason), 2048); ExpectedConcurrencyStamp = root.ConcurrencyStamp;
        RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint); RecordedAtUtc = EducationRules.RequireUtc(now);
    }
}
