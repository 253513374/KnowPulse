namespace KnowPulse.Education;

public enum TextbookVersionStatus
{
    Draft = 0,
    Published = 1,
    Retired = 2
}

public static class EducationCurriculumErrorCodes
{
    public const string ImmutableVersion = "KnowPulse:EducationCurriculum:ImmutableVersion";
    public const string InvalidStructure = "KnowPulse:EducationCurriculum:InvalidStructure";
    public const string ReferenceMismatch = "KnowPulse:EducationCurriculum:ReferenceMismatch";
    public const string ConflictingOperation = "KnowPulse:EducationCurriculum:ConflictingOperation";
    public const string PublishedVersionRequired = "KnowPulse:EducationCurriculum:PublishedVersionRequired";
    public const string CapacityExceeded = "KnowPulse:EducationCurriculum:CapacityExceeded";
}
