namespace KnowPulse.Blazor.Client.Menus;

public class KnowPulseMenus
{
    private const string Prefix = "KnowPulse";
    public const string Home = Prefix + ".Home";

    public const string Organization = Prefix + ".Organization";
    public const string Departments = Organization + ".Departments";
    public const string Positions = Organization + ".Positions";
    public const string JobLevels = Organization + ".JobLevels";
    public const string UserAssignments = Organization + ".UserAssignments";

    public const string KnowledgeBase = Prefix + ".KnowledgeBase";
    public const string KnowledgeItems = KnowledgeBase + ".Items";
    public const string KnowledgeCategories = KnowledgeBase + ".Categories";
    public const string KnowledgeTags = KnowledgeBase + ".Tags";
    public const string KnowledgePermissionScopes = KnowledgeBase + ".PermissionScopes";

}
