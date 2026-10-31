using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;

namespace KnowPulse.Education;

public enum EducationStudyTaskRevisionKind { Draft = 0, Published = 1, Withdrawn = 2 }
public enum EducationStudyTaskAvailability { Waiting = 0, Available = 1, Overdue = 2, Superseded = 3, Withdrawn = 4, Unavailable = 5 }
public static class EducationStudyTaskErrors
{
    public const string Invalid = "KnowPulse:StudyTask:Invalid";
    public const string Changed = "KnowPulse:StudyTask:Changed";
    public const string OperationConflict = "KnowPulse:StudyTask:OperationConflict";
    public const string RosterChanged = "KnowPulse:StudyTask:RosterChanged";
    public const string Closed = "KnowPulse:StudyTask:Closed";
}
public sealed record EducationStudyTaskContent
{
    public string Title { get; init; } = "";
    public string Instructions { get; init; } = "";
    public DateTime StartsAtUtc { get; init; }
    public DateTime EndsAtUtc { get; init; }
    public int EstimatedMinutes { get; init; }
    public EducationAssessmentRecipientScope RecipientScope { get; init; }
    public List<Guid> StudentUserIds { get; init; } = [];
    public List<EducationStudyMaterial> Materials { get; init; } = [];
    public void Validate(bool published = false)
    {
        if (string.IsNullOrWhiteSpace(Title) || Title.Length > 256 || string.IsNullOrWhiteSpace(Instructions) || Instructions.Length > 8192 ||
            StartsAtUtc.Kind != DateTimeKind.Utc || EndsAtUtc.Kind != DateTimeKind.Utc || StartsAtUtc >= EndsAtUtc || EstimatedMinutes is < 1 or > 1440 ||
            !Enum.IsDefined(RecipientScope) || StudentUserIds == null || StudentUserIds.Count > 3000 || StudentUserIds.Contains(Guid.Empty) ||
            StudentUserIds.Distinct().Count() != StudentUserIds.Count || RecipientScope == EducationAssessmentRecipientScope.FullClass && StudentUserIds.Count != 0 ||
            RecipientScope == EducationAssessmentRecipientScope.SelectedStudents && StudentUserIds.Count == 0 || Materials == null || Materials.Count > 10 ||
            published && Materials.Count == 0 || Materials.Any(x => x == null || x.Id == Guid.Empty || x.KnowledgeItemId == Guid.Empty || x.KnowledgeVersionId == Guid.Empty ||
                x.SchoolKnowledgeShareId == Guid.Empty || string.IsNullOrWhiteSpace(x.Title) || x.Title.Length > 256 || x.AttachmentIds == null ||
                x.AttachmentIds.Count > 10 || x.AttachmentIds.Contains(Guid.Empty) || x.AttachmentIds.Distinct().Count() != x.AttachmentIds.Count) ||
            Materials.Select(x => x.Id).Distinct().Count() != Materials.Count) throw new BusinessException(EducationStudyTaskErrors.Invalid);
    }
}
