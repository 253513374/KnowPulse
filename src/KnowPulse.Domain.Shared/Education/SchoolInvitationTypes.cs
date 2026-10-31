namespace KnowPulse.Education;

public enum SchoolInvitationStatus { Pending = 0, Accepted = 1, Declined = 2, Revoked = 3, Expired = 4 }
public enum SchoolInvitationResponse { Accept = 0, Decline = 1 }
public enum SchoolInvitationDeclineReason { IdentityMismatch = 0, SchoolOrRoleMismatch = 1, DoNotJoin = 2 }

public static class SchoolInvitationErrors
{
    public const string Invalid = "KnowPulse:SchoolInvitation:Invalid";
    public const string Unavailable = "KnowPulse:SchoolInvitation:Unavailable";
    public const string OperationConflict = "KnowPulse:SchoolInvitation:OperationConflict";
    public const string PendingExists = "KnowPulse:SchoolInvitation:PendingExists";
}
