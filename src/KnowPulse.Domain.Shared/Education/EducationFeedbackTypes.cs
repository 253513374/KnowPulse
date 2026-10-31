using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Volo.Abp;

namespace KnowPulse.Education;

public enum EducationFeedbackKind { Daily = 0, Assessment = 1, Period = 2 }
public enum EducationFeedbackRevisionKind { Draft = 0, Published = 1, Withdrawn = 2 }
public enum EducationFeedbackVisibility { Draft = 0, WaitingForReview = 1, Open = 2, Withdrawn = 3 }

public static class EducationFeedbackErrorCodes
{
    public const string Invalid = "KnowPulse:EducationFeedback:Invalid";
    public const string OperationConflict = "KnowPulse:EducationFeedback:OperationConflict";
    public const string PreviewChanged = "KnowPulse:EducationFeedback:PreviewChanged";
    public const string NotOpen = "KnowPulse:EducationFeedback:NotOpen";
}

public sealed class EducationFeedbackContent
{
    public EducationFeedbackKind Kind { get; set; }
    public string Context { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public DateTime? PeriodEndsAtUtc { get; set; }
    public string Text { get; set; } = string.Empty;
    public List<Guid> AssessmentTaskIds { get; set; } = [];
    public Guid? SelectedObservationId { get; set; }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { MaxDepth = 4, AllowDuplicateProperties = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public string ToCanonicalJson()
    {
        if (!Enum.IsDefined(Kind) || string.IsNullOrWhiteSpace(Context) || Context.Length > 256 ||
            string.IsNullOrWhiteSpace(Text) || Text.Length > 4000 || OccurredAtUtc.Kind != DateTimeKind.Utc ||
            SelectedObservationId == Guid.Empty || AssessmentTaskIds == null || AssessmentTaskIds.Count > 10 ||
            AssessmentTaskIds.Any(x => x == Guid.Empty) || AssessmentTaskIds.Distinct().Count() != AssessmentTaskIds.Count ||
            Kind == EducationFeedbackKind.Assessment && AssessmentTaskIds.Count == 0 ||
            Kind == EducationFeedbackKind.Period && (!PeriodEndsAtUtc.HasValue || PeriodEndsAtUtc <= OccurredAtUtc) ||
            Kind != EducationFeedbackKind.Period && PeriodEndsAtUtc.HasValue ||
            PeriodEndsAtUtc.HasValue && PeriodEndsAtUtc.Value.Kind != DateTimeKind.Utc) throw Invalid();
        return JsonSerializer.Serialize(new EducationFeedbackContent { Kind = Kind, Context = Context, OccurredAtUtc = OccurredAtUtc,
            PeriodEndsAtUtc = PeriodEndsAtUtc, Text = Text, AssessmentTaskIds = AssessmentTaskIds.OrderBy(x => x).ToList(),
            SelectedObservationId = SelectedObservationId }, Json);
    }

    public static EducationFeedbackContent Parse(string json)
    {
        if (json.Length > 32000) throw Invalid();
        try { var value = JsonSerializer.Deserialize<EducationFeedbackContent>(json, Json) ?? throw Invalid(); value.ToCanonicalJson(); return value; }
        catch (JsonException) { throw Invalid(); }
    }
    private static BusinessException Invalid() => new(EducationFeedbackErrorCodes.Invalid);
}
