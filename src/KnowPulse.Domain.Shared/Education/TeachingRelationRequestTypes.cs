namespace KnowPulse.Education;

public enum TeachingRelationRequestStatus { Pending = 0, Processed = 1, Withdrawn = 2 }
public enum TeachingRelationResolution { Reject = 0, End = 1, Add = 2, Replace = 3 }
public static class TeachingRelationRequestErrors
{
    public const string Invalid = "KnowPulse:TeachingRelationRequest:Invalid";
    public const string Changed = "KnowPulse:TeachingRelationRequest:Changed";
    public const string Conflict = "KnowPulse:TeachingRelationRequest:Conflict";
}
