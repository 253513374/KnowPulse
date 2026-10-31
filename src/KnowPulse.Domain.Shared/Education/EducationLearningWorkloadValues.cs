namespace KnowPulse.Education;

public enum EducationLearningWorkloadKind { PlanActivity = 0, StudyTask = 1, Correction = 2, Assessment = 3 }
public enum EducationLearningWorkloadState { Ready = 0, Scheduled = 1, Blocked = 2, Overdue = 3 }
public enum EducationCorrectionWorkState { NotSubmitted = 0, AwaitingReview = 1, NeedsRevision = 2, Accepted = 3 }
public static class EducationLearningWorkloadErrors
{
    public const string InvalidWindow = "KnowPulse:LearningWorkload:InvalidWindow";
    public const string Capacity = "KnowPulse:LearningWorkload:Capacity";
}
