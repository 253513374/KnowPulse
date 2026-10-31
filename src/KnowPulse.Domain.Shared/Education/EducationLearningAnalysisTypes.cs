using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KnowPulse.Education;

public enum EducationLearningEvidenceUse { Unverified = 0, Independent = 1, Practice = 2, Invalid = 3 }
public enum EducationMasteryState { Unassessed = 0, InsufficientEvidence = 1, NeedsConsolidation = 2, CurrentTargetMet = 3 }
public enum EducationLearningExclusionReason { None = 0, Invalid = 1, Unverified = 2, TeacherPractice = 3, EarlierAttempt = 4, EarlierExposure = 5, OutsideWindow = 6, UnverifiedExposure = 7 }
public enum EducationQuestionExposureSource { AssessmentReview = 0, StudentFeedback = 1, TeachingPlan = 2, FormalAssessmentPlan = 3, StudyTask = 4 }
public enum EducationLearningExposureUse { Confirmed = 0, Unverified = 1, ConfirmedNoHints = 2 }

public static class EducationLearningErrorCodes
{
    public const string InvalidScope = "KnowPulse:EducationLearning:InvalidScope";
    public const string InvalidEvidence = "KnowPulse:EducationLearning:InvalidEvidence";
    public const string InvalidRule = "KnowPulse:EducationLearning:InvalidRule";
}

public sealed record EducationLearningPoint(Guid KnowledgePointId, Guid KnowledgePointRevisionId, string Code, string Name);
public sealed record EducationLearningPointScore(Guid MappingId, Guid KnowledgePointId, Guid KnowledgePointRevisionId, decimal MaxScore, decimal ObtainedScore);

public sealed record EducationLearningAnalysisScope
{
    public Guid? TenantId { get; init; }
    public Guid SchoolId { get; init; }
    public Guid TeachingAssignmentId { get; init; }
    public Guid StudentUserId { get; init; }
    public Guid EducationSubjectId { get; init; }
    public Guid TextbookVersionId { get; init; }
    public DateTime WindowStartsAtUtc { get; init; }
    public DateTime WindowEndsAtUtc { get; init; }
    public IReadOnlyList<EducationLearningPoint> Points { get; init; } = [];
}

public sealed record EducationMasteryCalculationRule
{
    public Guid Id { get; init; }
    public Guid? TenantId { get; init; }
    public Guid SchoolId { get; init; }
    public Guid EducationSubjectId { get; init; }
    public int RevisionNumber { get; init; }
    public int WindowDays { get; init; }
    public int MinimumIndependentQuestionCount { get; init; }
    public int MinimumAssessmentCount { get; init; }
    public int MaximumLatestEvidenceAgeDays { get; init; }
    public decimal TargetRatio { get; init; }
}

/// <summary>A current confirmed grade, with qualification separate from the original answer and score.</summary>
public sealed record EducationLearningQuestionEvidence
{
    public Guid QuestionGradeId { get; init; }
    public Guid GradeRevisionId { get; init; }
    public Guid? TenantId { get; init; }
    public Guid SchoolId { get; init; }
    public Guid TeachingAssignmentId { get; init; }
    public Guid StudentUserId { get; init; }
    public Guid EducationSubjectId { get; init; }
    public Guid TextbookVersionId { get; init; }
    public Guid TaskId { get; init; }
    public Guid SubmissionId { get; init; }
    public Guid AttemptId { get; init; }
    public Guid PaperQuestionId { get; init; }
    public Guid OriginalQuestionId { get; init; }
    public Guid QuestionDefinitionRevisionId { get; init; }
    public int DisplayOrder { get; init; }
    public DateTime AttemptStartedAtUtc { get; init; }
    public DateTime EffectiveAtUtc { get; init; }
    public DateTime GradeConfirmedAtUtc { get; init; }
    public decimal MaxScore { get; init; }
    public decimal ObtainedScore { get; init; }
    public EducationLearningEvidenceUse Use { get; init; }
    public Guid? QualificationRevisionId { get; init; }
    public Guid? InvalidationRevisionId { get; init; }
    public bool NoHintsOrAnswersConfirmed { get; init; }
    public bool NewRequirementsConfirmed { get; init; }
    public bool OriginalRelationshipsChecked { get; init; }
    public IReadOnlyList<EducationLearningPointScore> Points { get; init; } = [];
}

public sealed record EducationLearningQuestionRelationship(Guid Id, Guid FirstQuestionId, Guid SecondQuestionId);
public sealed record EducationLearningQuestionExposure(Guid Id, Guid StudentUserId, Guid OriginalQuestionId, DateTime ExposedAtUtc,
    EducationLearningExposureUse Use = EducationLearningExposureUse.Confirmed, Guid? HintReviewRevisionId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? PaperQuestionId = null);
public sealed record EducationLearningQuestionAttempt(Guid? TenantId, Guid SchoolId, Guid EducationSubjectId, Guid StudentUserId,
    Guid TaskId, Guid AttemptId, Guid PaperQuestionId, Guid OriginalQuestionId, int DisplayOrder, DateTime StartedAtUtc);

public sealed record EducationLearningEvidenceDecision(Guid GradeRevisionId, Guid TaskId, Guid SubmissionId, Guid PaperQuestionId,
    Guid OriginalGroupId, EducationLearningExclusionReason ExclusionReason);

public sealed record EducationLearningPointEstimate
{
    public EducationLearningPoint Point { get; init; } = new(Guid.Empty, Guid.Empty, "", "");
    public EducationMasteryState State { get; init; }
    public decimal ObtainedScore { get; init; }
    public decimal MaxScore { get; init; }
    public decimal? Ratio { get; init; }
    public int IndependentQuestionCount { get; init; }
    public int AssessmentCount { get; init; }
    public DateTime? LatestEvidenceAtUtc { get; init; }
    public int UnverifiedQuestionCount { get; init; }
    public int PracticeQuestionCount { get; init; }
    public int InvalidQuestionCount { get; init; }
    public bool RuleMissing { get; init; }
    public int MissingIndependentQuestionCount { get; init; }
    public int MissingAssessmentCount { get; init; }
    public bool RecentEvidenceMissing { get; init; }
}

public sealed record EducationLearningAnalysisResult
{
    public string CalculationVersion { get; init; } = string.Empty;
    public string SourceFingerprint { get; init; } = string.Empty;
    public DateTime CalculatedAtUtc { get; init; }
    public DateTime WindowStartsAtUtc { get; init; }
    public DateTime WindowEndsAtUtc { get; init; }
    public Guid? RuleRevisionId { get; init; }
    public int? RuleRevisionNumber { get; init; }
    public int TargetPointCount { get; init; }
    public int AssessedPointCount { get; init; }
    public IReadOnlyList<EducationLearningPointEstimate> Points { get; init; } = [];
    public IReadOnlyList<EducationLearningEvidenceDecision> Evidence { get; init; } = [];
}
