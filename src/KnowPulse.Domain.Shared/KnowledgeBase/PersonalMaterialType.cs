namespace KnowPulse.KnowledgeBase;

public enum PersonalMaterialType
{
    Unspecified = 0,
    LessonPlan = 1,
    Courseware = 2,
    Question = 3,
    TeachingCase = 4,
    Reflection = 5,
    TeachingMethod = 6,
    Note = 7,
    Reference = 8
}

public static class PersonalKnowledgeOrganizationErrorCodes
{
    public const string InvalidOrganization = "KnowPulse:PersonalKnowledgeOrganization:InvalidOrganization";
    public const string OrganizationChanged = "KnowPulse:PersonalKnowledgeOrganization:OrganizationChanged";
}
