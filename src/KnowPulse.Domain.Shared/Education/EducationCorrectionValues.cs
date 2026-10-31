using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;

namespace KnowPulse.Education;

public enum EducationCorrectionRevisionKind { Draft = 0, Published = 1, Withdrawn = 2 }
public enum EducationCorrectionAvailability { Available = 0, Waiting = 1, Overdue = 2, Superseded = 3, Withdrawn = 4, ResultUnavailable = 5, SourceChanged = 6, Unavailable = 7 }
public enum EducationCorrectionReviewDecision { NeedsRevision = 0, Accepted = 1 }
public static class EducationCorrectionErrors
{
    public const string Invalid = "KnowPulse:Correction:Invalid";
    public const string Changed = "KnowPulse:Correction:Changed";
    public const string Closed = "KnowPulse:Correction:Closed";
    public const string Conflict = "KnowPulse:Correction:OperationConflict";
    public const string ImageUnavailable = "KnowPulse:Correction:ImageUnavailable";
}
public sealed record EducationCorrectionImageReference(Guid AttachmentId, int RotationDegrees = 0);
public sealed record EducationCorrectionContent
{
    public string OriginalThinking { get; init; } = "";
    public string CurrentSolution { get; init; } = "";
    public string CheckingStrategy { get; init; } = "";
    public List<EducationCorrectionImageReference> Images { get; init; } = [];
    public void Validate(bool submitted = false)
    {
        if (OriginalThinking == null || CurrentSolution == null || CheckingStrategy == null || OriginalThinking.Length > 4000 || CurrentSolution.Length > 8192 || CheckingStrategy.Length > 4000 ||
            Images == null || Images.Count > 5 || Images.Any(x => x == null || x.AttachmentId == Guid.Empty || x.RotationDegrees is not (0 or 90 or 180 or 270)) ||
            Images.Select(x => x.AttachmentId).Distinct().Count() != Images.Count || submitted && (string.IsNullOrWhiteSpace(CheckingStrategy) || string.IsNullOrWhiteSpace(CurrentSolution) && Images.Count == 0))
            throw new BusinessException(EducationCorrectionErrors.Invalid);
    }
}
public sealed record EducationCorrectionInstructions
{
    public string Title { get; init; } = "";
    public string Instructions { get; init; } = "";
    public DateTime StartsAtUtc { get; init; }
    public DateTime DueAtUtc { get; init; }
    public int EstimatedMinutes { get; init; }
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Title) || Title.Length > 256 || string.IsNullOrWhiteSpace(Instructions) || Instructions.Length > 4000 || StartsAtUtc.Kind != DateTimeKind.Utc ||
            DueAtUtc.Kind != DateTimeKind.Utc || StartsAtUtc >= DueAtUtc || EstimatedMinutes is < 1 or > 1440) throw new BusinessException(EducationCorrectionErrors.Invalid);
    }
}
