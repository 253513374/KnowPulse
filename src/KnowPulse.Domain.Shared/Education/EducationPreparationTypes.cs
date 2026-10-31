namespace KnowPulse.Education;

public enum LessonPreparationReferenceKind { Material = 0, Question = 1, ExamPaper = 2 }

public static class EducationPreparationErrorCodes
{
    public const string WholeMaterialConfirmationRequired = "KnowPulse:EducationPreparation:WholeMaterialConfirmationRequired";
    public const string InvalidSource = "KnowPulse:EducationPreparation:InvalidSource";
    public const string CurriculumMismatch = "KnowPulse:EducationPreparation:CurriculumMismatch";
    public const string ConflictingOperation = "KnowPulse:EducationPreparation:ConflictingOperation";
    public const string CapacityExceeded = "KnowPulse:EducationPreparation:CapacityExceeded";
    public const string ReflectionRequired = "KnowPulse:EducationPreparation:ReflectionRequired";
}
