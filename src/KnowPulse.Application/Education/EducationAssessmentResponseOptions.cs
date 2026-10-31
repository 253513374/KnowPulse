using System;

namespace KnowPulse.Education;

public class EducationAssessmentResponseOptions
{
    public const string SectionName = "Education:AssessmentResponses";
    public bool IsDeadlineWorkerEnabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 5;
    public int DeadlineBatchSize { get; set; } = 20;
    // Development protection limits; school commitments and retention remain PRD D04/D06/D09.
    public long MaxImageBytes { get; set; } = 5 * 1024 * 1024;
    public long MaxAttemptImageBytes { get; set; } = 20 * 1024 * 1024;
    public int MaxUploadOperationsPerAttempt { get; set; } = 40;

    public void EnsureValid()
    {
        if (PollIntervalSeconds <= 0 || DeadlineBatchSize <= 0 || MaxImageBytes <= 0 || MaxImageBytes > EducationAssessmentResponseValue.MaxImageFileBytes ||
            MaxAttemptImageBytes < MaxImageBytes || MaxUploadOperationsPerAttempt <= 0)
            throw new InvalidOperationException("Education response limits are invalid.");
    }
}
