using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.Education;

public class EducationEvidenceDecisionInput
{
    public Guid OperationId { get; set; }
    public Guid? ExpectedRevisionId { get; set; }
    [StringLength(40)] public string? ConcurrencyStamp { get; set; }
    [Required, StringLength(2048)] public string Reason { get; set; } = string.Empty;
}
public class SaveEducationEvidenceQualificationDto : EducationEvidenceDecisionInput
{
    public EducationLearningEvidenceUse Use { get; set; }
    public bool NoHintsOrAnswersConfirmed { get; set; }
    public bool NewRequirementsConfirmed { get; set; }
    public bool OriginalRelationshipsChecked { get; set; }
}
public class SaveEducationOriginRelationDto : EducationEvidenceDecisionInput
{
    public Guid SecondQuestionGradeId { get; set; }
    public bool IsEnabled { get; set; }
}
public class SaveEducationHintReviewDto : EducationEvidenceDecisionInput
{
    public EducationLearningExposureUse Use { get; set; } = EducationLearningExposureUse.Unverified;
    public bool ConfirmedExactContext { get; set; }
}
public class EducationEvidenceDecisionRevisionDto : EntityDto<Guid>
{
    public int RevisionNumber { get; set; }
    public Guid? BasedOnRevisionId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public DateTime RecordedAtUtc { get; set; }
}
public class EducationEvidenceQualificationRevisionDto : EducationEvidenceDecisionRevisionDto
{
    public Guid QuestionGradeId { get; set; }
    public EducationLearningEvidenceUse Use { get; set; }
    public bool NoHintsOrAnswersConfirmed { get; set; }
    public bool NewRequirementsConfirmed { get; set; }
    public bool OriginalRelationshipsChecked { get; set; }
}
public class EducationOriginRelationRevisionDto : EducationEvidenceDecisionRevisionDto
{
    public Guid RelationId { get; set; }
    public Guid FirstQuestionGradeId { get; set; }
    public Guid SecondQuestionGradeId { get; set; }
    public bool IsEnabled { get; set; }
}
public class EducationHintReviewRevisionDto : EducationEvidenceDecisionRevisionDto
{
    public Guid ExposureId { get; set; }
    public Guid FeedbackRevisionId { get; set; }
    public EducationLearningExposureUse Use { get; set; }
}
public class EducationQuestionEvidenceDto
{
    public Guid QuestionGradeId { get; set; }
    public Guid TaskId { get; set; }
    public Guid SubmissionId { get; set; }
    public string TaskTitle { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string QuestionTitle { get; set; } = string.Empty;
    public string QuestionStem { get; set; } = string.Empty;
    public string? ConcurrencyStamp { get; set; }
    public EducationEvidenceQualificationRevisionDto? Current { get; set; }
}
public class EducationOriginCandidateDto
{
    public Guid QuestionGradeId { get; set; }
    public string TaskTitle { get; set; } = string.Empty;
    public string QuestionTitle { get; set; } = string.Empty;
    public string QuestionStem { get; set; } = string.Empty;
}
public class EducationOriginCandidatesDto
{
    public bool HasMore { get; set; }
    public int NextSkipCount { get; set; }
    public List<EducationOriginCandidateDto> Items { get; set; } = [];
}
public class EducationOriginRelationDto
{
    public Guid RelationId { get; set; }
    public string? ConcurrencyStamp { get; set; }
    public EducationOriginRelationRevisionDto? Current { get; set; }
}
public class EducationEvidenceExposureDto : EntityDto<Guid>
{
    public Guid? ReviewActivityId { get; set; }
    public Guid? ReviewStudyRecipientId { get; set; }
    public Guid? ReviewPaperQuestionId { get; set; }
    public EducationQuestionExposureSource Source { get; set; }
    public DateTime ExposedAtUtc { get; set; }
    public EducationLearningExposureUse Use { get; set; }
    public bool CanReview { get; set; }
}
public class EducationHintContextDto
{
    public Guid ExposureId { get; set; }
    public Guid FeedbackRevisionId { get; set; }
    public int FeedbackRevisionNumber { get; set; }
    public Guid PaperQuestionId { get; set; }
    public Guid QuestionDefinitionRevisionId { get; set; }
    public DateTime ExposedAtUtc { get; set; }
    public string Context { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string QuestionTitle { get; set; } = string.Empty;
    public string QuestionStem { get; set; } = string.Empty;
    public string? ReferenceAnswer { get; set; }
    public List<string> CorrectAnswers { get; set; } = [];
    public List<string> BlankAnswers { get; set; } = [];
    public string? ConcurrencyStamp { get; set; }
    public EducationHintReviewRevisionDto? Current { get; set; }
}
