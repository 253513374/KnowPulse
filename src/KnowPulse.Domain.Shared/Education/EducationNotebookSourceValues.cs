using System;
using Volo.Abp;

namespace KnowPulse.Education;

public enum EducationNotebookSourceKind { AssessmentQuestion = 0, StudyMaterial = 1 }
public static class EducationNotebookSourceErrors
{
    public const string Invalid = "KnowPulse:NotebookSource:Invalid";
    public const string Unavailable = "KnowPulse:NotebookSource:Unavailable";
    public const string Conflict = "KnowPulse:NotebookSource:Conflict";
    public const string Limit = "KnowPulse:NotebookSource:Limit";
}
public sealed record EducationNotebookSourcePointer
{
    public EducationNotebookSourceKind Kind { get; init; }
    public Guid RecipientId { get; init; }
    public Guid SourceVersionId { get; init; }
    public Guid ComponentId { get; init; }
    public void Validate()
    {
        if (!Enum.IsDefined(Kind) || RecipientId == Guid.Empty || SourceVersionId == Guid.Empty || ComponentId == Guid.Empty)
            throw new BusinessException(EducationNotebookSourceErrors.Invalid);
    }
}
public sealed record EducationPrivateNotebookSource
{
    public Guid Id { get; init; }
    public Guid NoteVersionId { get; init; }
    public EducationNotebookSourcePointer Source { get; init; } = new();
    public string RequestFingerprint { get; init; } = "";
    public DateTime RecordedAtUtc { get; init; }
    public Guid? RemovalOperationId { get; init; }
    public string? RemovalFingerprint { get; init; }
    public DateTime? RemovedAtUtc { get; init; }
}
