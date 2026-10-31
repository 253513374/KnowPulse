using System;

namespace KnowPulse.AiQuestioning;

public class AiQuestionGenerationWorkerOptions
{
    public const string SectionName = "AiQuestionGeneration:Worker";
    public bool IsEnabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 5;
    public int LeaseSeconds { get; set; } = 300;
    public int MaxAttempts { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 15;
    public int MaxRegenerationOperations { get; set; } = 100;

    public void EnsureValid()
    {
        if (PollIntervalSeconds <= 0 || BatchSize <= 0 || LeaseSeconds < 6 || MaxAttempts < 1 || MaxAttempts > 10 ||
            RetryDelaySeconds < 1 || MaxRegenerationOperations < 1)
            throw new InvalidOperationException("AI generation worker limits are invalid.");
    }
}
