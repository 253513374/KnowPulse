namespace KnowPulse.Education;

public enum EducationTeachingActivityAction { Started = 0, Completed = 1, Difficulty = 2, TeacherFollowUp = 3 }
public enum EducationTeachingActivityState { NotStarted = 0, InProgress = 1, Blocked = 2, ReportedComplete = 3, AssessmentSealed = 4 }
public enum EducationTeachingActivityDifficulty { Unclear = 0, InsufficientTime = 1, MaterialUnavailable = 2, Other = 3 }
