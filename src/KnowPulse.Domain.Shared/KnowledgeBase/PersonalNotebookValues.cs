namespace KnowPulse.KnowledgeBase;

public enum PersonalNotebookCommandKind { Created = 0, SavedVersion = 1, UploadedAttachment = 2, Deleted = 3 }

public static class PersonalNotebookErrors
{
    public const string Invalid = "KnowPulse:PersonalNotebook:Invalid";
    public const string Changed = "KnowPulse:PersonalNotebook:Changed";
    public const string OperationConflict = "KnowPulse:PersonalNotebook:OperationConflict";
}
