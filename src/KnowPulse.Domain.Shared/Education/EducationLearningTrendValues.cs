using System;

namespace KnowPulse.Education;

[Flags]
public enum EducationLearningTrendLimit { None = 0, First = 1, Coverage = 2, Maximum = 4, Difficulty = 8, Scoring = 16, MissingScore = 32, Cohort = 64, MetadataMissing = 128 }

/// <summary>Transient frozen-paper comparison keys; never a content grant or a public answer/rubric.</summary>
public sealed record EducationLearningTrendBasis(Guid TaskId, int QuestionCount, decimal Maximum, string? Coverage, string? Difficulty, string? Scoring);

public sealed record EducationLearningTrendRow
{
    public Guid TaskId { get; init; }
    public string Title { get; init; } = "";
    public DateTime EffectiveAtUtc { get; init; }
    public DateTime EndsAtUtc { get; init; }
    public int PaperSnapshotVersion { get; init; }
    public int QuestionCount { get; init; }
    public int OriginalAssignedCount { get; init; }
    public int AssignedCount { get; init; }
    public int ScoredCount { get; init; }
    public int PendingGradingCount { get; init; }
    public int AbsentCount { get; init; }
    public int NotStartedCount { get; init; }
    public int InProgressCount { get; init; }
    public int AwaitingSealCount { get; init; }
    public int OutsideWindowCount { get; init; }
    /// <summary>Mean of complete scored papers in class mode; the actual paper mark in student mode.</summary>
    public decimal? Score { get; init; }
    public decimal MaxScore { get; init; }
    public decimal? Ratio { get; init; }
    public Guid? ComparedWithTaskId { get; init; }
    public EducationLearningTrendLimit ComparisonLimit { get; init; }
}
