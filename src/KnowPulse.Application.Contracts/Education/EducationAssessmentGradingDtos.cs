using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.Education;

public class EducationAssessmentGradingOverviewDto
{
    public Guid TaskId { get; set; }
    public Guid PublishedRevisionId { get; set; }
    public string ConcurrencyStamp { get; set; } = string.Empty;
    public int ConfirmedGradeSetVersion { get; set; }
    public int RecipientCount { get; set; }
    public int UnstartedCount { get; set; }
    public int InProgressCount { get; set; }
    public int SubmittedCount { get; set; }
    public int GradingCompleteCount { get; set; }
    public int PendingQuestionCount { get; set; }
    public Guid? ClosureId { get; set; }
    public Guid? CurrentResultReleaseId { get; set; }
    public List<EducationAssessmentSubmissionGradingSummaryDto> Submissions { get; set; } = [];
}

public class EducationAssessmentSubmissionGradingSummaryDto
{
    public Guid SubmissionId { get; set; }
    public Guid StudentUserId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public int QuestionCount { get; set; }
    public int ConfirmedQuestionCount { get; set; }
    public decimal? TotalScore { get; set; }
    public decimal MaxScore { get; set; }
}

public class EducationAssessmentSubmissionForGradingDto
{
    public Guid TaskId { get; set; }
    public int ConfirmedGradeSetVersion { get; set; }
    public Guid StudentUserId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public EducationAssessmentSubmissionDto Submission { get; set; } = new();
    public List<EducationAssessmentQuestionForGradingDto> Questions { get; set; } = [];
}

/// <summary>Teacher-only frozen rubric and submitted answer; no private material provenance.</summary>
public class EducationAssessmentQuestionForGradingDto : EntityDto<Guid>
{
    public QuestionType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Stem { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public decimal MaxScore { get; set; }
    public List<ExamPaperQuestionOptionDto> Options { get; set; } = [];
    public List<string> CorrectAnswers { get; set; } = [];
    public string? Analysis { get; set; }
    public QuestionScoringDefinition? ScoringDefinition { get; set; }
    public EducationAssessmentResponseValue? Response { get; set; }
    public List<EducationAssessmentAnswerImageDto> Images { get; set; } = [];
    public EducationAssessmentQuestionGradeDto Grade { get; set; } = new();
}

public class EducationAssessmentQuestionGradeDto : EntityDto<Guid>
{
    public Guid SubmissionId { get; set; }
    public Guid PaperQuestionId { get; set; }
    public int RevisionNumber { get; set; }
    public string ConcurrencyStamp { get; set; } = string.Empty;
    public Guid? CurrentRevisionId { get; set; }
    public EducationAssessmentGradeRevisionDto? Draft { get; set; }
    public EducationAssessmentGradeRevisionDto? Confirmed { get; set; }
}

public class EducationAssessmentGradeRevisionDto : EntityDto<Guid>
{
    public Guid QuestionGradeId { get; set; }
    public Guid SubmissionId { get; set; }
    public Guid PaperQuestionId { get; set; }
    public int RevisionNumber { get; set; }
    public Guid? BasedOnRevisionId { get; set; }
    public Guid? SupersedesRevisionId { get; set; }
    public Guid? ResponseRevisionId { get; set; }
    public string? ResponseHash { get; set; }
    public Guid QuestionDefinitionRevisionId { get; set; }
    public string ScoringDefinitionHash { get; set; } = string.Empty;
    public string CalculationVersion { get; set; } = string.Empty;
    public EducationAssessmentGradeRevisionKind Kind { get; set; }
    public EducationAssessmentGradingMethod Method { get; set; }
    public EducationAssessmentGradeContent Content { get; set; } = new();
    public decimal MaxScore { get; set; }
    public decimal? ObtainedScore { get; set; }
    public string? Feedback { get; set; }
    public string? Reason { get; set; }
    public Guid? ActorUserId { get; set; }
    public string ExpectedConcurrencyStamp { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public int? ExpectedGradeSetVersion { get; set; }
    public int? ResultGradeSetVersion { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
}

public class SaveEducationAssessmentGradeDraftDto
{
    public Guid OperationId { get; set; }
    public Guid PaperQuestionId { get; set; }
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = string.Empty;
    public Guid? ExpectedGradeRevisionId { get; set; }
    [Required, MinLength(1), MaxLength(100)] public List<EducationAssessmentPartMark> PartMarks { get; set; } = [];
    [StringLength(4000)] public string? Feedback { get; set; }
}

public class ConfirmEducationAssessmentGradeDto
{
    public Guid OperationId { get; set; }
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = string.Empty;
    public Guid ExpectedDraftRevisionId { get; set; }
    [Range(0, int.MaxValue - 1)] public int ExpectedGradeSetVersion { get; set; }
    [StringLength(2048)] public string? Reason { get; set; }
}

public class EducationAssessmentFinalizationPreviewDto
{
    public Guid TaskId { get; set; }
    public Guid PublishedRevisionId { get; set; }
    public string ConcurrencyStamp { get; set; } = string.Empty;
    public int ConfirmedGradeSetVersion { get; set; }
    public Guid? ClosureId { get; set; }
    public int RecipientCount { get; set; }
    public int UnstartedCount { get; set; }
    public int InProgressCount { get; set; }
    public int SubmittedCount { get; set; }
    public int GradingCompleteCount { get; set; }
    public int PendingQuestionCount { get; set; }
    public bool CanClose { get; set; }
    public bool CanPublish { get; set; }
    public string FinalizationFingerprint { get; set; } = string.Empty;
    public DateTime EndsAtUtc { get; set; }
    public DateTime ServerNowUtc { get; set; }
}

public class CloseEducationAssessmentTaskDto
{
    public Guid OperationId { get; set; }
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = string.Empty;
    public Guid ExpectedPublishedRevisionId { get; set; }
    [Required, StringLength(64, MinimumLength = 64)] public string FinalizationFingerprint { get; set; } = string.Empty;
}

public class EducationAssessmentClosureDto : EntityDto<Guid>
{
    public Guid TaskId { get; set; }
    public Guid TaskRevisionId { get; set; }
    public string ExpectedConcurrencyStamp { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public string SubmissionSetFingerprint { get; set; } = string.Empty;
    public int ValidSubmissionCount { get; set; }
    public EducationAssessmentClosureValue Manifest { get; set; } = new();
    public DateTime EffectiveAtUtc { get; set; }
    public DateTime ClosedAtUtc { get; set; }
}

public class PublishEducationAssessmentResultsDto
{
    public Guid OperationId { get; set; }
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = string.Empty;
    public Guid ExpectedClosureId { get; set; }
    [Range(0, int.MaxValue)] public int ExpectedGradeSetVersion { get; set; }
    public ExamResultVisibility ConfiguredVisibility { get; set; } = ExamResultVisibility.Hidden;
    public DateTime? ScoreOpensAtUtc { get; set; }
    public DateTime? ReviewOpensAtUtc { get; set; }
    public bool ConfirmVisibilityChange { get; set; }
    [StringLength(2048)] public string? Reason { get; set; }
}

public class EducationAssessmentResultReleaseDto : EntityDto<Guid>
{
    public Guid TaskId { get; set; }
    public Guid TaskRevisionId { get; set; }
    public Guid ClosureId { get; set; }
    public Guid? PreviousReleaseId { get; set; }
    public int ConfirmedGradeSetVersion { get; set; }
    public string ExpectedConcurrencyStamp { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public string SubmissionSetFingerprint { get; set; } = string.Empty;
    public int ValidSubmissionCount { get; set; }
    public ExamResultVisibility ConfiguredVisibility { get; set; }
    public DateTime? ScoreOpensAtUtc { get; set; }
    public DateTime? ReviewOpensAtUtc { get; set; }
    public Guid ConfirmedByUserId { get; set; }
    public DateTime ReleasedAtUtc { get; set; }
    public string? Reason { get; set; }
    public List<EducationAssessmentResultReleaseItemDto> Items { get; set; } = [];
}

public class EducationAssessmentResultReleaseItemDto : EntityDto<Guid>
{
    public Guid ResultReleaseId { get; set; }
    public Guid RecipientId { get; set; }
    public Guid StudentUserId { get; set; }
    public Guid AttemptId { get; set; }
    public Guid SubmissionId { get; set; }
    public decimal TotalScore { get; set; }
    public decimal MaxScore { get; set; }
    public EducationAssessmentReleasedGradesValue Grades { get; set; } = new();
}

/// <summary>Only a released result; intermediate scores and teacher/source objects cannot be embedded.</summary>
public class EducationAssessmentStudentResultDto
{
    public bool AllowQuestionCollection { get; set; }
    public Guid TaskId { get; set; }
    public Guid RecipientId { get; set; }
    public Guid? AttemptId { get; set; }
    public Guid? SubmissionId { get; set; }
    public EducationAssessmentRecipientOutcome? Outcome { get; set; }
    public ExamResultVisibility CurrentResultVisibility { get; set; } = ExamResultVisibility.Hidden;
    public Guid? ResultReleaseId { get; set; }
    public DateTime? ReleasedAtUtc { get; set; }
    public DateTime? ScoreOpensAtUtc { get; set; }
    public DateTime? ReviewOpensAtUtc { get; set; }
    public decimal? TotalScore { get; set; }
    public decimal? MaxScore { get; set; }
    public List<EducationAssessmentStudentQuestionResultDto> Questions { get; set; } = [];
}

public class EducationAssessmentStudentQuestionResultDto : EntityDto<Guid>
{
    public List<ExamPaperQuestionOptionDto> Options { get; set; } = [];
    public QuestionType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Stem { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public decimal MaxScore { get; set; }
    public decimal ObtainedScore { get; set; }
    public EducationAssessmentResponseValue? Response { get; set; }
    public List<string> CorrectAnswers { get; set; } = [];
    public string? Analysis { get; set; }
    public QuestionScoringDefinition? ScoringDefinition { get; set; }
    public List<EducationAssessmentStudentGradePartDto> Parts { get; set; } = [];
    public string? Feedback { get; set; }
}

public class EducationAssessmentStudentGradePartDto
{
    public Guid PartId { get; set; }
    public decimal MaxScore { get; set; }
    public decimal? ObtainedScore { get; set; }
}

public class RequestEducationAssessmentScoreReviewDto
{
    public Guid OperationId { get; set; }
    public Guid ResultReleaseId { get; set; }
    public Guid? PaperQuestionId { get; set; }
    [Required, StringLength(2048)] public string Reason { get; set; } = string.Empty;
}

public class ResolveEducationAssessmentScoreReviewDto
{
    public Guid OperationId { get; set; }
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = string.Empty;
    public EducationAssessmentScoreReviewDecision Decision { get; set; }
    [Required, StringLength(2048)] public string Reason { get; set; } = string.Empty;
    public Guid? CorrectedGradeRevisionId { get; set; }
    public Guid? ResultingReleaseId { get; set; }
}

public class EducationAssessmentScoreReviewRequestDto : EntityDto<Guid>
{
    public Guid TaskId { get; set; }
    public Guid StudentUserId { get; set; }
    public Guid SubmissionId { get; set; }
    public Guid ResultReleaseId { get; set; }
    public Guid? PaperQuestionId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public DateTime RequestedAtUtc { get; set; }
    public string ConcurrencyStamp { get; set; } = string.Empty;
    public Guid? ResolutionOperationId { get; set; }
    public string? ResolutionRequestFingerprint { get; set; }
    public string? ExpectedResolutionConcurrencyStamp { get; set; }
    public EducationAssessmentScoreReviewDecision? Decision { get; set; }
    public string? ResolutionReason { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public Guid? CorrectedGradeRevisionId { get; set; }
    public Guid? ResultingReleaseId { get; set; }
}
