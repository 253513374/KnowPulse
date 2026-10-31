using System;

namespace KnowPulse.KnowledgeBase;

public class PersonalKnowledgeExportOptions
{
    public const string SectionName = "PersonalKnowledge:Exports";
    public bool IsEnabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 10;
    public int BatchSize { get; set; } = 5;
    // Development capacity defaults; deployments set these for their storage and retention policy.
    public int FileLifetimeHours { get; set; } = 24;
    public int LeaseSeconds { get; set; } = 300;
    public int MaxItems { get; set; } = 1000;
    public int MaxVersions { get; set; } = 5000;
    public int MaxAttachments { get; set; } = 5000;
    public long MaxSourceBytes { get; set; } = 512L * 1024 * 1024;
    public long MaxFileBytes { get; set; } = 512L * 1024 * 1024;

    public void EnsureValid()
    {
        if (PollIntervalSeconds <= 0 || BatchSize <= 0 || FileLifetimeHours <= 0 || LeaseSeconds <= 0 ||
            MaxItems <= 0 || MaxVersions <= 0 || MaxAttachments <= 0 || MaxSourceBytes <= 0 || MaxFileBytes <= 0)
            throw new InvalidOperationException("Personal export limits must be positive.");
    }
}
