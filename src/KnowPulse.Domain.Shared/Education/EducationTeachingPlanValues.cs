using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization.Metadata;
using Volo.Abp;

namespace KnowPulse.Education;

public enum EducationTeachingPlanRevisionKind { Draft = 0, Confirmed = 1, Published = 2, Withdrawn = 3 }
public enum EducationTeachingPlanStepKind { Diagnostic = 0, Explanation = 1, WorkedExample = 2, Practice = 3, Reassessment = 4, Reading = 5 }
public enum EducationTeachingPlanAvailability { Waiting = 0, Available = 1, SourceChanged = 2, Unavailable = 3, Withdrawn = 4 }
public static class EducationTeachingPlanErrors
{
    public const string Invalid = "KnowPulse:TeachingPlan:Invalid";
    public const string SourceChanged = "KnowPulse:TeachingPlan:SourceChanged";
    public const string OperationConflict = "KnowPulse:TeachingPlan:OperationConflict";
    public const string DiagnosticRequired = "KnowPulse:TeachingPlan:DiagnosticRequired";
    public const string AssessmentNotNew = "KnowPulse:TeachingPlan:AssessmentNotNew";
    public const string AssessmentStopped = "KnowPulse:TeachingPlan:AssessmentStopped";
    public const string MaterialUnavailable = "KnowPulse:TeachingPlan:MaterialUnavailable";
}
public sealed record EducationTeachingPlanStep
{
    public Guid Id { get; init; }
    public EducationTeachingPlanStepKind Kind { get; init; }
    public string Title { get; init; } = "";
    public string Instructions { get; init; } = "";
    public int EstimatedMinutes { get; init; }
    public DateTime DueAtUtc { get; init; }
}
public sealed record EducationTeachingPlanContent
{
    public string Title { get; init; } = "";
    public string StudentGoal { get; init; } = "";
    public string TeacherActions { get; init; } = "";
    public string SuccessCriteria { get; init; } = "";
    public DateTime StartsAtUtc { get; init; }
    public DateTime EndsAtUtc { get; init; }
    public DateTime ReassessmentAtUtc { get; init; }
    public List<Guid> TargetPointRevisionIds { get; init; } = [];
    public List<EducationTeachingPlanStep> Steps { get; init; } = [];
    public List<EducationTeachingPlanMaterial> Materials { get; init; } = [];
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Title) || Title.Length > 256 || string.IsNullOrWhiteSpace(StudentGoal) || StudentGoal.Length > 2000 ||
            string.IsNullOrWhiteSpace(TeacherActions) || TeacherActions.Length > 2000 || string.IsNullOrWhiteSpace(SuccessCriteria) || SuccessCriteria.Length > 2000 ||
            StartsAtUtc.Kind != DateTimeKind.Utc || EndsAtUtc.Kind != DateTimeKind.Utc || ReassessmentAtUtc.Kind != DateTimeKind.Utc ||
            StartsAtUtc >= EndsAtUtc || ReassessmentAtUtc < StartsAtUtc || ReassessmentAtUtc > EndsAtUtc ||
            TargetPointRevisionIds == null || TargetPointRevisionIds.Count is < 1 or > 100 || TargetPointRevisionIds.Contains(Guid.Empty) || TargetPointRevisionIds.Distinct().Count() != TargetPointRevisionIds.Count ||
            Steps == null || Steps.Count is < 1 or > 50 || Steps.Any(x => x == null || x.Id == Guid.Empty || !Enum.IsDefined(x.Kind) ||
                string.IsNullOrWhiteSpace(x.Title) || x.Title.Length > 256 || string.IsNullOrWhiteSpace(x.Instructions) || x.Instructions.Length > 4000 ||
                x.EstimatedMinutes is < 1 or > 1440 || x.DueAtUtc.Kind != DateTimeKind.Utc || x.DueAtUtc < StartsAtUtc || x.DueAtUtc > EndsAtUtc) ||
            Steps.Select(x => x.Id).Distinct().Count() != Steps.Count || !Steps.Any(x => x.Kind == EducationTeachingPlanStepKind.Reassessment && x.DueAtUtc == ReassessmentAtUtc))
            throw new BusinessException(EducationTeachingPlanErrors.Invalid);
        if (Materials == null || Materials.Count > 50 || Materials.Any(x => x == null || x.Id == Guid.Empty || x.KnowledgeItemId == Guid.Empty ||
            x.KnowledgeVersionId == Guid.Empty || x.SchoolKnowledgeShareId == Guid.Empty || string.IsNullOrWhiteSpace(x.Title) || x.Title.Length > 256 ||
            !Steps.Any(s => s.Id == x.StepId && s.Kind is not (EducationTeachingPlanStepKind.Diagnostic or EducationTeachingPlanStepKind.Reassessment)) ||
            x.AttachmentIds == null || x.AttachmentIds.Count > 10 || x.AttachmentIds.Contains(Guid.Empty) || x.AttachmentIds.Distinct().Count() != x.AttachmentIds.Count) ||
            Materials.Select(x => x.Id).Distinct().Count() != Materials.Count || Materials.GroupBy(x => x.StepId).Any(x => x.Count() > 10))
            throw new BusinessException(EducationTeachingPlanErrors.Invalid);
    }
}
public sealed record EducationTeachingPlanMaterial : IEducationLearningMaterial
{
    public Guid Id { get; init; }
    public Guid StepId { get; init; }
    public string Title { get; init; } = "";
    public Guid KnowledgeItemId { get; init; }
    public Guid KnowledgeVersionId { get; init; }
    public Guid? SchoolKnowledgeShareId { get; init; }
    public List<Guid> AttachmentIds { get; init; } = [];
}
public static class EducationTeachingPlanJson
{
    public static void PreserveEmptyMaterialCompatibility(JsonTypeInfo info)
    {
        if (info.Type != typeof(EducationTeachingPlanContent)) return;
        // Empty references retain historical content bytes and committed command fingerprints.
        var property = info.Properties.Single(x => x.Name.Equals(nameof(EducationTeachingPlanContent.Materials), StringComparison.OrdinalIgnoreCase));
        property.ShouldSerialize = (_, value) => value is not List<EducationTeachingPlanMaterial> { Count: 0 };
    }
}
public sealed record EducationStudentTeachingMaterial(Guid Id, Guid StepId, string Title) : IEducationMaterialDisplay;
public sealed record EducationTeachingPlanQuestion(Guid TaskId, Guid TaskRevisionId, Guid RecipientId, Guid ExamPaperId,
    Guid PaperQuestionId, Guid OriginalQuestionId, Guid DefinitionRevisionId);
public sealed record EducationTeachingPlanBasis
{
    public Guid ReportId { get; init; }
    public Guid ReportSnapshotId { get; init; }
    public EducationCurriculumSnapshot Curriculum { get; init; } = new();
    public EducationLearningReportStudent Student { get; init; } = new();
    public DateTime WindowStartsAtUtc { get; init; }
    public DateTime WindowEndsAtUtc { get; init; }
    public string Fingerprint { get; init; } = "";
    public List<Guid> TaskIds { get; init; } = [];
    public List<EducationLearningPointEstimate> Points { get; init; } = [];
    public List<EducationLearningQuestionEvidence> Evidence { get; init; } = [];
    public List<EducationTeachingPlanQuestion> Questions { get; init; } = [];
}
public sealed record EducationStudentTeachingPlanPoint(string Code, string Name, int RevisionNumber);
public sealed record EducationStudentTeachingPlanContent
{
    public string Title { get; init; } = "";
    public string StudentGoal { get; init; } = "";
    public string TeacherActions { get; init; } = "";
    public string SuccessCriteria { get; init; } = "";
    public DateTime StartsAtUtc { get; init; }
    public DateTime EndsAtUtc { get; init; }
    public DateTime ReassessmentAtUtc { get; init; }
    public List<EducationStudentTeachingPlanPoint> Targets { get; init; } = [];
    public List<EducationTeachingPlanStep> Steps { get; init; } = [];
    public List<EducationStudentTeachingMaterial> Materials { get; init; } = [];
}
