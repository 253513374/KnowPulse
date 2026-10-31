using System;
using System.Collections.Generic;

namespace KnowPulse.Education;

public enum EducationPlanExecutionState { Running = 0, Paused = 1, Ended = 2, Terminated = 3 }
public enum EducationPlanDecision { Pause = 0, Resume = 1, Continue = 2, Adjust = 3, End = 4, Terminate = 5 }

public sealed record EducationPlanExecutionEvidence
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public Guid PublishedRevisionId { get; init; }
    public bool SourceIsCurrent { get; init; }
    public bool RequiresAssessmentPermission { get; init; }
    public List<EducationPlanExecutionActivityEvidence> Activities { get; init; } = [];
}
public sealed record EducationPlanExecutionActivityEvidence
{
    public Guid ActivityId { get; init; }
    public Guid StepId { get; init; }
    public EducationTeachingPlanStepKind Kind { get; init; }
    public Guid? StudentEventId { get; init; }
    public EducationTeachingActivityAction? StudentAction { get; init; }
    public Guid? FollowUpEventId { get; init; }
    public bool MaterialsAvailable { get; init; }
    public Guid? AssessmentRecipientId { get; init; }
    public Guid? AssessmentTaskId { get; init; }
    public Guid? AttemptId { get; init; }
    public DateTime? SealedAtUtc { get; init; }
    public bool AssessmentStopped { get; init; }
    public Guid? BindingReviewId { get; init; }
    public int? ConfirmedQuestionCount { get; init; }
    public string? ConfirmedGradeSetHash { get; init; }
    public List<Guid> ConfirmedGradeRevisionIds { get; init; } = [];
}
