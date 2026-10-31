using System;
using System.Security.Cryptography;
using System.Text;
using KnowPulse.Exam;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

/// <summary>A body-free fact that an authorized student received review or linked feedback.</summary>
public class EducationQuestionExposure : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid SchoolId { get; private set; }
    public Guid EducationSubjectId { get; private set; }
    public Guid OriginalQuestionId { get; private set; }
    public Guid QuestionDefinitionRevisionId { get; private set; }
    public Guid PaperQuestionId { get; private set; }
    public Guid ExamPaperId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid TaskRevisionId { get; private set; }
    public Guid RecipientId { get; private set; }
    public Guid ResultReleaseId { get; private set; }
    public Guid? FeedbackId { get; private set; }
    public Guid? FeedbackRevisionId { get; private set; }
    public EducationQuestionExposureSource Source { get; private set; }
    public DateTime ExposedAtUtc { get; private set; }
    protected EducationQuestionExposure() { }

    public EducationQuestionExposure(EducationAssessmentTaskRevision task, EducationAssessmentRecipient recipient,
        EducationAssessmentResultRelease release, ExamPaperQuestion question, Guid subjectId, DateTime now,
        EducationStudentFeedbackRevision? feedback = null) : base(ExposureId(recipient.Id, release.Id, question.Id, feedback?.Id))
    {
        if (task.TenantId != recipient.TenantId || task.TenantId != release.TenantId || task.TenantId != question.TenantId ||
            task.Id != recipient.TaskRevisionId || task.TaskId != recipient.TaskId || release.TaskId != task.TaskId || release.TaskRevisionId != task.Id ||
            task.ExamPaperId != question.PaperId || !question.QuestionDefinitionRevisionId.HasValue || subjectId == Guid.Empty ||
            release.VisibilityAt(now) != ExamResultVisibility.FullReview || feedback != null &&
                (feedback.TenantId != task.TenantId || feedback.StudentUserId != recipient.StudentUserId || feedback.OwnerUserId != task.OwnerUserId ||
                 feedback.Kind != EducationFeedbackRevisionKind.Published || feedback.RecordedAtUtc > now || feedback.Content == null || !feedback.Content.AssessmentTaskIds.Contains(task.TaskId)))
            throw new BusinessException(EducationLearningErrorCodes.InvalidEvidence);
        TenantId = task.TenantId; StudentUserId = recipient.StudentUserId; SchoolId = task.SchoolId; EducationSubjectId = subjectId;
        OriginalQuestionId = question.OriginalQuestionId; QuestionDefinitionRevisionId = question.QuestionDefinitionRevisionId.Value;
        PaperQuestionId = question.Id; ExamPaperId = question.PaperId; TaskId = task.TaskId; TaskRevisionId = task.Id;
        RecipientId = recipient.Id; ResultReleaseId = release.Id; FeedbackId = feedback?.FeedbackId; FeedbackRevisionId = feedback?.Id;
        Source = feedback == null ? EducationQuestionExposureSource.AssessmentReview : EducationQuestionExposureSource.StudentFeedback;
        ExposedAtUtc = EducationRules.RequireUtc(now);
    }

    public static Guid ExposureId(Guid recipientId, Guid releaseId, Guid paperQuestionId, Guid? feedbackRevisionId)
    {
        EducationRules.RequireId(recipientId); EducationRules.RequireId(releaseId); EducationRules.RequireId(paperQuestionId);
        if (feedbackRevisionId == Guid.Empty) throw new BusinessException(EducationLearningErrorCodes.InvalidEvidence);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"education-question-exposure-v1|{recipientId:D}|{releaseId:D}|{paperQuestionId:D}|{feedbackRevisionId:D}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    public EducationLearningQuestionExposure ToLearningEvidence(EducationExposureHintReviewRevision? review = null)
    {
        if (review != null && (Source != EducationQuestionExposureSource.StudentFeedback || review.ExposureId != Id || review.TenantId != TenantId ||
            review.StudentUserId != StudentUserId || review.FeedbackRevisionId != FeedbackRevisionId)) throw new BusinessException(EducationLearningErrorCodes.InvalidEvidence);
        return new(Id, StudentUserId, OriginalQuestionId, ExposedAtUtc,
            Source == EducationQuestionExposureSource.AssessmentReview ? EducationLearningExposureUse.Confirmed : review?.Use ?? EducationLearningExposureUse.Unverified, review?.Id);
    }
}
