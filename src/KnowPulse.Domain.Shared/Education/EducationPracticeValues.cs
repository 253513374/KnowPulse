using System;
using System.Collections.Generic;

namespace KnowPulse.Education;

public static class EducationPracticeErrors
{
    public const string Invalid = "KnowPulse:Practice:Invalid";
    public const string SourceUnavailable = "KnowPulse:Practice:SourceUnavailable";
    public const string Conflict = "KnowPulse:Practice:Conflict";
    public const string ImageUnavailable = "KnowPulse:Practice:ImageUnavailable";
}
public sealed record EducationPracticePartResult(Guid PartId, decimal MaxScore, decimal? ObtainedScore);
public sealed record EducationPracticeEvaluation(decimal MaxScore, decimal? ObtainedScore, IReadOnlyList<EducationPracticePartResult> Parts);
