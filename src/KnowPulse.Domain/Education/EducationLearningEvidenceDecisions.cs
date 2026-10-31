using System;
using System.Security.Cryptography;
using System.Text;
using KnowPulse.Exam;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

public class EducationEvidenceQualification : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid SubmissionId { get; private set; }
    public Guid AttemptId { get; private set; }
    public Guid PaperQuestionId { get; private set; }
    public Guid OriginalQuestionId { get; private set; }
    public Guid QuestionDefinitionRevisionId { get; private set; }
    public int RevisionNumber { get; private set; }
    public Guid? CurrentRevisionId { get; private set; }
    protected EducationEvidenceQualification() { }
    public EducationEvidenceQualification(EducationAssessmentQuestionGrade grade, ExamPaperQuestion question) : base(grade.Id)
    {
        if (grade.TenantId != question.TenantId || grade.PaperQuestionId != question.Id || grade.ExamPaperId != question.PaperId || !question.QuestionDefinitionRevisionId.HasValue)
            throw new BusinessException(EducationLearningErrorCodes.InvalidEvidence);
        TenantId = grade.TenantId; OwnerUserId = grade.OwnerUserId; StudentUserId = grade.StudentUserId; TaskId = grade.TaskId;
        SubmissionId = grade.SubmissionId; AttemptId = grade.AttemptId; PaperQuestionId = question.Id; OriginalQuestionId = question.OriginalQuestionId;
        QuestionDefinitionRevisionId = question.QuestionDefinitionRevisionId.Value;
    }
    public void Advance(EducationEvidenceQualificationRevision revision)
    {
        if (revision.QuestionGradeId != Id || revision.TenantId != TenantId || revision.OwnerUserId != OwnerUserId || revision.StudentUserId != StudentUserId ||
            revision.QuestionDefinitionRevisionId != QuestionDefinitionRevisionId || revision.RevisionNumber != RevisionNumber + 1 ||
            revision.BasedOnRevisionId != CurrentRevisionId || revision.ExpectedConcurrencyStamp != ConcurrencyStamp) throw new BusinessException(EducationLearningErrorCodes.InvalidEvidence);
        CurrentRevisionId = revision.Id; RevisionNumber = revision.RevisionNumber;
    }
}

public class EducationEvidenceQualificationRevision : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid QuestionGradeId { get; private set; }
    public Guid QuestionDefinitionRevisionId { get; private set; }
    public int RevisionNumber { get; private set; }
    public Guid? BasedOnRevisionId { get; private set; }
    public EducationLearningEvidenceUse Use { get; private set; }
    public bool NoHintsOrAnswersConfirmed { get; private set; }
    public bool NewRequirementsConfirmed { get; private set; }
    public bool OriginalRelationshipsChecked { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string ExpectedConcurrencyStamp { get; private set; } = string.Empty;
    public string RequestFingerprint { get; private set; } = string.Empty;
    public DateTime RecordedAtUtc { get; private set; }
    protected EducationEvidenceQualificationRevision() { }
    public EducationEvidenceQualificationRevision(Guid id, EducationEvidenceQualification root, EducationLearningEvidenceUse use,
        bool noHints, bool newRequirements, bool originsChecked, string reason, string stamp, string fingerprint, DateTime now) : base(EducationRules.RequireId(id))
    {
        if (!Enum.IsDefined(use) || stamp != root.ConcurrencyStamp || use == EducationLearningEvidenceUse.Independent && (!noHints || !newRequirements || !originsChecked))
            throw new BusinessException(EducationLearningErrorCodes.InvalidEvidence);
        TenantId = root.TenantId; OwnerUserId = root.OwnerUserId; StudentUserId = root.StudentUserId; QuestionGradeId = root.Id;
        QuestionDefinitionRevisionId = root.QuestionDefinitionRevisionId; RevisionNumber = checked(root.RevisionNumber + 1); BasedOnRevisionId = root.CurrentRevisionId;
        Use = use; NoHintsOrAnswersConfirmed = noHints; NewRequirementsConfirmed = newRequirements; OriginalRelationshipsChecked = originsChecked;
        Reason = Check.NotNullOrWhiteSpace(reason, nameof(reason), 2048); ExpectedConcurrencyStamp = Check.NotNullOrWhiteSpace(stamp, nameof(stamp), 40);
        RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint); RecordedAtUtc = EducationRules.RequireUtc(now);
    }
    public EducationLearningQuestionEvidence ApplyTo(EducationLearningQuestionEvidence row)
    {
        if (row.QuestionGradeId != QuestionGradeId || row.TenantId != TenantId || row.StudentUserId != StudentUserId || row.QuestionDefinitionRevisionId != QuestionDefinitionRevisionId)
            throw new BusinessException(EducationLearningErrorCodes.InvalidEvidence);
        return row with { Use = Use, QualificationRevisionId = Id, NoHintsOrAnswersConfirmed = NoHintsOrAnswersConfirmed,
            NewRequirementsConfirmed = NewRequirementsConfirmed, OriginalRelationshipsChecked = OriginalRelationshipsChecked };
    }
}

public class EducationExposureHintReview : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid FeedbackRevisionId { get; private set; }
    public int RevisionNumber { get; private set; }
    public Guid? CurrentRevisionId { get; private set; }
    protected EducationExposureHintReview() { }
    public EducationExposureHintReview(EducationQuestionExposure exposure, EducationStudentFeedbackRevision feedback) : base(exposure.Id)
    {
        if (exposure.Source != EducationQuestionExposureSource.StudentFeedback || exposure.FeedbackRevisionId != feedback.Id || exposure.FeedbackId != feedback.FeedbackId ||
            exposure.TenantId != feedback.TenantId || exposure.StudentUserId != feedback.StudentUserId || feedback.Kind != EducationFeedbackRevisionKind.Published)
            throw new BusinessException(EducationLearningErrorCodes.InvalidEvidence);
        TenantId = exposure.TenantId; OwnerUserId = feedback.OwnerUserId; StudentUserId = exposure.StudentUserId; FeedbackRevisionId = feedback.Id;
    }
    public void Advance(EducationExposureHintReviewRevision revision)
    {
        if (revision.ExposureId != Id || revision.TenantId != TenantId || revision.OwnerUserId != OwnerUserId || revision.StudentUserId != StudentUserId ||
            revision.FeedbackRevisionId != FeedbackRevisionId || revision.RevisionNumber != RevisionNumber + 1 || revision.BasedOnRevisionId != CurrentRevisionId ||
            revision.ExpectedConcurrencyStamp != ConcurrencyStamp) throw new BusinessException(EducationLearningErrorCodes.InvalidEvidence);
        CurrentRevisionId = revision.Id; RevisionNumber = revision.RevisionNumber;
    }
}

public class EducationExposureHintReviewRevision : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid ExposureId { get; private set; }
    public Guid FeedbackRevisionId { get; private set; }
    public int RevisionNumber { get; private set; }
    public Guid? BasedOnRevisionId { get; private set; }
    public EducationLearningExposureUse Use { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string ExpectedConcurrencyStamp { get; private set; } = string.Empty;
    public string RequestFingerprint { get; private set; } = string.Empty;
    public DateTime RecordedAtUtc { get; private set; }
    protected EducationExposureHintReviewRevision() { }
    public EducationExposureHintReviewRevision(Guid id, EducationExposureHintReview root, EducationLearningExposureUse use,
        bool confirmedExactContext, string reason, string stamp, string fingerprint, DateTime now) : base(EducationRules.RequireId(id))
    {
        if (!Enum.IsDefined(use) || !confirmedExactContext || stamp != root.ConcurrencyStamp) throw new BusinessException(EducationLearningErrorCodes.InvalidEvidence);
        TenantId = root.TenantId; OwnerUserId = root.OwnerUserId; StudentUserId = root.StudentUserId; ExposureId = root.Id; FeedbackRevisionId = root.FeedbackRevisionId;
        RevisionNumber = checked(root.RevisionNumber + 1); BasedOnRevisionId = root.CurrentRevisionId; Use = use;
        Reason = Check.NotNullOrWhiteSpace(reason, nameof(reason), 2048); ExpectedConcurrencyStamp = Check.NotNullOrWhiteSpace(stamp, nameof(stamp), 40);
        RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint); RecordedAtUtc = EducationRules.RequireUtc(now);
    }
}

public class EducationQuestionOriginRelation : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid SchoolId { get; private set; }
    public Guid EducationSubjectId { get; private set; }
    public Guid FirstOriginalQuestionId { get; private set; }
    public Guid SecondOriginalQuestionId { get; private set; }
    public int RevisionNumber { get; private set; }
    public Guid? CurrentRevisionId { get; private set; }
    protected EducationQuestionOriginRelation() { }
    public EducationQuestionOriginRelation(Guid? tenantId, Guid ownerId, Guid schoolId, Guid subjectId, Guid first, Guid second)
        : base(RelationId(tenantId, ownerId, schoolId, subjectId, first, second))
    {
        if (first.CompareTo(second) >= 0) throw new BusinessException(EducationLearningErrorCodes.InvalidEvidence);
        TenantId = tenantId; OwnerUserId = ownerId; SchoolId = schoolId; EducationSubjectId = subjectId;
        FirstOriginalQuestionId = first; SecondOriginalQuestionId = second;
    }
    public void Advance(EducationQuestionOriginRelationRevision revision)
    {
        if (revision.RelationId != Id || revision.TenantId != TenantId || revision.OwnerUserId != OwnerUserId || revision.FirstOriginalQuestionId != FirstOriginalQuestionId ||
            revision.SecondOriginalQuestionId != SecondOriginalQuestionId || revision.RevisionNumber != RevisionNumber + 1 || revision.BasedOnRevisionId != CurrentRevisionId ||
            revision.ExpectedConcurrencyStamp != ConcurrencyStamp) throw new BusinessException(EducationLearningErrorCodes.InvalidEvidence);
        CurrentRevisionId = revision.Id; RevisionNumber = revision.RevisionNumber;
    }
    public static Guid RelationId(Guid? tenantId, Guid ownerId, Guid schoolId, Guid subjectId, Guid first, Guid second)
    {
        EducationRules.RequireId(ownerId); EducationRules.RequireId(schoolId); EducationRules.RequireId(subjectId); EducationRules.RequireId(first); EducationRules.RequireId(second);
        if (tenantId == Guid.Empty || first == second) throw new BusinessException(EducationLearningErrorCodes.InvalidEvidence);
        if (first.CompareTo(second) > 0) (first, second) = (second, first);
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"education-question-relation-v1|{tenantId:D}|{ownerId:D}|{schoolId:D}|{subjectId:D}|{first:D}|{second:D}")).AsSpan(0, 16));
    }
}

public class EducationQuestionOriginRelationRevision : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid RelationId { get; private set; }
    public Guid FirstOriginalQuestionId { get; private set; }
    public Guid SecondOriginalQuestionId { get; private set; }
    public Guid FirstQuestionGradeId { get; private set; }
    public Guid SecondQuestionGradeId { get; private set; }
    public Guid FirstDefinitionRevisionId { get; private set; }
    public Guid SecondDefinitionRevisionId { get; private set; }
    public int RevisionNumber { get; private set; }
    public Guid? BasedOnRevisionId { get; private set; }
    public bool IsEnabled { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string ExpectedConcurrencyStamp { get; private set; } = string.Empty;
    public string RequestFingerprint { get; private set; } = string.Empty;
    public DateTime RecordedAtUtc { get; private set; }
    protected EducationQuestionOriginRelationRevision() { }
    public EducationQuestionOriginRelationRevision(Guid id, EducationQuestionOriginRelation root, EducationAssessmentQuestionGrade firstGrade, ExamPaperQuestion first,
        EducationAssessmentQuestionGrade secondGrade, ExamPaperQuestion second, bool enabled, string reason, string stamp, string fingerprint, DateTime now) : base(EducationRules.RequireId(id))
    {
        if (stamp != root.ConcurrencyStamp || firstGrade.TenantId != root.TenantId || secondGrade.TenantId != root.TenantId || first.TenantId != root.TenantId || second.TenantId != root.TenantId ||
            firstGrade.OwnerUserId != root.OwnerUserId || secondGrade.OwnerUserId != root.OwnerUserId || firstGrade.PaperQuestionId != first.Id || secondGrade.PaperQuestionId != second.Id ||
            firstGrade.ExamPaperId != first.PaperId || secondGrade.ExamPaperId != second.PaperId ||
            first.OriginalQuestionId != root.FirstOriginalQuestionId || second.OriginalQuestionId != root.SecondOriginalQuestionId ||
            !first.QuestionDefinitionRevisionId.HasValue || !second.QuestionDefinitionRevisionId.HasValue) throw new BusinessException(EducationLearningErrorCodes.InvalidEvidence);
        TenantId = root.TenantId; OwnerUserId = root.OwnerUserId; RelationId = root.Id; FirstOriginalQuestionId = first.OriginalQuestionId; SecondOriginalQuestionId = second.OriginalQuestionId;
        FirstQuestionGradeId = firstGrade.Id; SecondQuestionGradeId = secondGrade.Id; FirstDefinitionRevisionId = first.QuestionDefinitionRevisionId.Value; SecondDefinitionRevisionId = second.QuestionDefinitionRevisionId.Value;
        RevisionNumber = checked(root.RevisionNumber + 1); BasedOnRevisionId = root.CurrentRevisionId; IsEnabled = enabled;
        Reason = Check.NotNullOrWhiteSpace(reason, nameof(reason), 2048); ExpectedConcurrencyStamp = Check.NotNullOrWhiteSpace(stamp, nameof(stamp), 40);
        RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint); RecordedAtUtc = EducationRules.RequireUtc(now);
    }
    public EducationLearningQuestionRelationship? ToLearningRelation() => IsEnabled ? new(Id, FirstOriginalQuestionId, SecondOriginalQuestionId) : null;
}
