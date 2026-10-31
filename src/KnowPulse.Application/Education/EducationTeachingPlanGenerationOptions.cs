using System;

namespace KnowPulse.Education;

public class EducationTeachingPlanGenerationOptions
{
    public const string SectionName = "TeachingPlanGeneration";
    public int MaximumRequestsPerOwnerPerDay { get; set; } = 20;
    public int ModelTimeoutSeconds { get; set; } = 90;
    public int LeaseSeconds { get; set; } = 120;
    public void EnsureValid()
    {
        if (MaximumRequestsPerOwnerPerDay is < 1 or > 10000 || ModelTimeoutSeconds is < 5 or > 300 || LeaseSeconds < ModelTimeoutSeconds + 10 || LeaseSeconds > 600) throw new InvalidOperationException("Invalid TeachingPlanGeneration limits.");
    }
}
