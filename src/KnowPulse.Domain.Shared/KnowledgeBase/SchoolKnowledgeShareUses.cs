using System;

namespace KnowPulse.KnowledgeBase;

[Flags]
public enum SchoolKnowledgeShareUses
{
    Read = 1,
    Retrieve = 2,
    GenerateQuestions = 4,
    Download = 8,
    Export = 16,
    AssignToStudents = 32
}

public static class SchoolKnowledgeSharingErrorCodes
{
    public const string InvalidUses = "KnowPulse:SchoolKnowledgeSharing:InvalidUses";
    public const string ConflictingShare = "KnowPulse:SchoolKnowledgeSharing:ConflictingShare";
    public const string PreparedVersionRequired = "KnowPulse:SchoolKnowledgeSharing:PreparedVersionRequired";
}
