namespace KnowPulse.Education;

/// <summary>只用于一个已核验任教班级，不是组织权限或全平台分配。</summary>
public enum EducationAssessmentRecipientScope
{
    FullClass = 0,
    SelectedStudents = 1
}

public enum EducationAssessmentAttemptStatus
{
    InProgress = 0,
    Submitted = 1
}

public enum EducationAssessmentSubmissionCause
{
    Manual = 0,
    Deadline = 1
}

public static class EducationAssessmentErrorCodes
{
    public const string ReminderUnavailable = "KnowPulse:EducationAssessment:ReminderUnavailable";
    public const string PreparationRequired = "KnowPulse:EducationAssessment:PreparationRequired";
    public const string PreparationChanged = "KnowPulse:EducationAssessment:PreparationChanged";
    public const string InvalidGrade = "KnowPulse:EducationAssessment:InvalidGrade";
    public const string GradeVersionConflict = "KnowPulse:EducationAssessment:GradeVersionConflict";
    public const string GradingIncomplete = "KnowPulse:EducationAssessment:GradingIncomplete";
    public const string TaskNotClosed = "KnowPulse:EducationAssessment:TaskNotClosed";
    public const string InvalidResultRelease = "KnowPulse:EducationAssessment:InvalidResultRelease";
    public const string InvalidScoreReview = "KnowPulse:EducationAssessment:InvalidScoreReview";
    public const string ScoreReviewAlreadyResolved = "KnowPulse:EducationAssessment:ScoreReviewAlreadyResolved";
    public const string InvalidDefinition = "KnowPulse:EducationAssessment:InvalidDefinition";
    public const string InvalidRecipientScope = "KnowPulse:EducationAssessment:InvalidRecipientScope";
    public const string InvalidRevision = "KnowPulse:EducationAssessment:InvalidRevision";
    public const string AlreadyPublished = "KnowPulse:EducationAssessment:AlreadyPublished";
    public const string ConflictingOperation = "KnowPulse:EducationAssessment:ConflictingOperation";
    public const string RosterChanged = "KnowPulse:EducationAssessment:RosterChanged";
    public const string RecipientUnavailable = "KnowPulse:EducationAssessment:RecipientUnavailable";
    public const string OutsideWindow = "KnowPulse:EducationAssessment:OutsideWindow";
    public const string AlreadyStarted = "KnowPulse:EducationAssessment:AlreadyStarted";
    public const string AttemptExpired = "KnowPulse:EducationAssessment:AttemptExpired";
    public const string CapacityExceeded = "KnowPulse:EducationAssessment:CapacityExceeded";
    public const string InvalidResponse = "KnowPulse:EducationAssessment:InvalidResponse";
    public const string ResponseVersionConflict = "KnowPulse:EducationAssessment:ResponseVersionConflict";
    public const string AttemptClosed = "KnowPulse:EducationAssessment:AttemptClosed";
    public const string InvalidSubmission = "KnowPulse:EducationAssessment:InvalidSubmission";
    public const string IncompleteSubmission = "KnowPulse:EducationAssessment:IncompleteSubmission";
    public const string InvalidAttachment = "KnowPulse:EducationAssessment:InvalidAttachment";
    public const string AttachmentNotUploaded = "KnowPulse:EducationAssessment:AttachmentNotUploaded";
}
