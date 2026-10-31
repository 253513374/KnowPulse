using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Volo.Abp;

namespace KnowPulse.Education;

public static class EducationTeachingPlanGenerationOutput
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 16, PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter<EducationTeachingPlanStepKind>(allowIntegerValues: false) } };
    private sealed class Proposal
    {
        [JsonRequired] public string Title { get; set; } = "";
        [JsonRequired] public string StudentGoal { get; set; } = "";
        [JsonRequired] public string TeacherActions { get; set; } = "";
        [JsonRequired] public string SuccessCriteria { get; set; } = "";
        [JsonRequired] public List<Step> Steps { get; set; } = [];
    }
    private sealed class Step
    {
        [JsonRequired] public EducationTeachingPlanStepKind Kind { get; set; }
        [JsonRequired] public string Title { get; set; } = "";
        [JsonRequired] public string Instructions { get; set; } = "";
        [JsonRequired] public int EstimatedMinutes { get; set; }
        [JsonRequired] public DateTime DueAtUtc { get; set; }
    }
    public static EducationTeachingPlanContent Parse(Guid jobId, string text, EducationPlanGenerationRequest request, EducationTeachingPlanBasis basis)
    {
        if (text == null || text.Length > 128 * 1024) throw Invalid();
        var value = text.Trim(); if (value.StartsWith("```json", StringComparison.Ordinal) && value.EndsWith("```", StringComparison.Ordinal)) value = value[7..^3].Trim();
        Proposal proposal;
        try {
            using var document = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object || Duplicates(document.RootElement)) throw Invalid();
            if (document.RootElement.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array && steps.EnumerateArray().Any(s => s.ValueKind == JsonValueKind.Object && Duplicates(s))) throw Invalid();
            proposal = JsonSerializer.Deserialize<Proposal>(value, Json) ?? throw Invalid();
        } catch (JsonException) { throw Invalid(); }
        if (proposal.Steps == null || proposal.Steps.Any(x => x == null)) throw Invalid();
        var content = new EducationTeachingPlanContent { Title = proposal.Title, StudentGoal = proposal.StudentGoal, TeacherActions = proposal.TeacherActions, SuccessCriteria = proposal.SuccessCriteria,
            StartsAtUtc = request.StartsAtUtc, EndsAtUtc = request.EndsAtUtc, ReassessmentAtUtc = request.ReassessmentAtUtc, TargetPointRevisionIds = request.TargetPointRevisionIds.ToList(),
            Steps = proposal.Steps.Select((s, i) => new EducationTeachingPlanStep { Id = EducationStudentGrowthSnapshot.StableId($"plan-generation-step-v1|{jobId:D}|{i}"), Kind = s.Kind,
                Title = s.Title, Instructions = s.Instructions, EstimatedMinutes = s.EstimatedMinutes, DueAtUtc = s.DueAtUtc }).ToList() };
        Validate(content, request, basis); EducationLearningReportJson.Write(content, 256 * 1024); return content;
    }
    public static void Validate(EducationTeachingPlanContent content, EducationPlanGenerationRequest request, EducationTeachingPlanBasis basis)
    {
        request.Validate(basis); content.Validate();
        if (!content.TargetPointRevisionIds.SequenceEqual(request.TargetPointRevisionIds) || content.StartsAtUtc != request.StartsAtUtc || content.EndsAtUtc != request.EndsAtUtc || content.ReassessmentAtUtc != request.ReassessmentAtUtc ||
            content.Materials.Count != 0 || content.Steps.Count is < 4 or > 20 || content.Steps.Sum(x => x.EstimatedMinutes) > request.MaximumEstimatedMinutes ||
            !content.Steps.Any(x => x.Kind == EducationTeachingPlanStepKind.Explanation) || !content.Steps.Any(x => x.Kind is EducationTeachingPlanStepKind.WorkedExample or EducationTeachingPlanStepKind.Reading) || !content.Steps.Any(x => x.Kind == EducationTeachingPlanStepKind.Practice) ||
            content.Steps[^1].Kind != EducationTeachingPlanStepKind.Reassessment || content.Steps[^1].DueAtUtc != request.ReassessmentAtUtc || content.Steps.Zip(content.Steps.Skip(1)).Any(x => x.First.DueAtUtc > x.Second.DueAtUtc) ||
            request.TargetPointRevisionIds.Any(id => basis.Points.Single(p => p.Point.KnowledgePointRevisionId == id).State is EducationMasteryState.Unassessed or EducationMasteryState.InsufficientEvidence) && content.Steps[0].Kind != EducationTeachingPlanStepKind.Diagnostic) throw Invalid();
    }
    private static BusinessException Invalid() => new(EducationPlanGenerationErrors.Invalid);
    private static bool Duplicates(JsonElement element) => element.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != element.EnumerateObject().Count();
}
