namespace KnowPulse.KnowledgeBase;

public enum PersonalKnowledgeExportStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
    Expired = 4,
    Cancelled = 5
}

public static class PersonalKnowledgeExportErrorCodes
{
    public const string NotReady = "KnowPulse:PersonalExport:NotReady";
    public const string CannotRetry = "KnowPulse:PersonalExport:CannotRetry";
    public const string LimitExceeded = "KnowPulse:PersonalExport:LimitExceeded";
    public const string SourceChanged = "KnowPulse:PersonalExport:SourceChanged";
    public const string SourceUnavailable = "KnowPulse:PersonalExport:SourceUnavailable";
    public const string AccountUnavailable = "KnowPulse:PersonalExport:AccountUnavailable";
    public const string StorageFailed = "KnowPulse:PersonalExport:StorageFailed";
    public const string Cancelled = "KnowPulse:PersonalExport:Cancelled";
}
