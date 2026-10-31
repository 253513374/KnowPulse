using System;
using System.Collections.Generic;
using KnowPulse.Exam;

namespace KnowPulse.Education;

[Flags]
public enum EducationGrowthComparisonLimit { None = 0, First = 1, Coverage = 2, Maximum = 4, Difficulty = 8, Scoring = 16, MissingScore = 32 }

public sealed record EducationStudentGrowthContext
{
    public Guid Id { get; init; }
    public string SchoolName { get; init; } = "";
    public string ClassName { get; init; } = "";
    public string TermName { get; init; } = "";
    public string TeacherName { get; init; } = "";
    public string SubjectName { get; init; } = "";
    public string TextbookTitle { get; init; } = "";
    public int MaximumWindowDays { get; init; }
}

public sealed record EducationStudentGrowthPoint
{
    public Guid Id { get; init; }
    public int RevisionNumber { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
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
    public int MissingIndependentQuestionCount { get; init; }
    public int MissingAssessmentCount { get; init; }
    public bool RecentEvidenceMissing { get; init; }
}

public sealed record EducationStudentGrowthAssessment
{
    public Guid TaskId { get; init; }
    public Guid RecipientId { get; init; }
    public Guid ResultReleaseId { get; init; }
    public string Title { get; init; } = "";
    public DateTime EffectiveAtUtc { get; init; }
    public bool IsAbsent { get; init; }
    public decimal? TotalScore { get; init; }
    public decimal? MaxScore { get; init; }
    public ExamResultVisibility Visibility { get; init; }
    public EducationGrowthComparisonLimit ComparisonLimit { get; init; }
}

public sealed record EducationStudentGrowthRule(int RevisionNumber, int WindowDays, int MinimumIndependentQuestionCount,
    int MinimumAssessmentCount, int MaximumLatestEvidenceAgeDays, decimal TargetRatio);

public sealed record EducationStudentGrowthValue
{
    public Guid SnapshotId { get; init; }
    public EducationStudentGrowthContext Context { get; init; } = new();
    public DateTime WindowStartsAtUtc { get; init; }
    public DateTime WindowEndsAtUtc { get; init; }
    public DateTime CalculatedAtUtc { get; init; }
    public string CalculationVersion { get; init; } = "";
    public bool RuleMissing { get; init; }
    public EducationStudentGrowthRule? Rule { get; init; }
    public int? RuleDecisionRevisionNumber { get; init; }
    public List<EducationStudentGrowthPoint> Points { get; init; } = [];
    public List<EducationStudentGrowthAssessment> Assessments { get; init; } = [];
}
