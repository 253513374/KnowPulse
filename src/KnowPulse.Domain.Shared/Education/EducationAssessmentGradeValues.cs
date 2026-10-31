using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Volo.Abp;

namespace KnowPulse.Education;

public enum EducationAssessmentGradeRevisionKind { Draft = 0, Confirmed = 1 }
public enum EducationAssessmentGradingMethod { Automatic = 0, Teacher = 1 }
public enum EducationAssessmentRecipientOutcome { Submitted = 0, Absent = 1 }
public enum EducationAssessmentScoreReviewDecision { ConfirmOriginal = 0, Corrected = 1 }

public sealed class EducationAssessmentPartMark
{
    public Guid PartId { get; set; }
    public decimal? ObtainedScore { get; set; }
}

public sealed class EducationAssessmentGradePart
{
    public Guid PartId { get; set; }
    public decimal MaxScore { get; set; }
    public decimal? ObtainedScore { get; set; }
    public Guid? TextbookKnowledgePointMappingId { get; set; }
}

public sealed class EducationAssessmentMappingGradeFact
{
    public Guid TextbookKnowledgePointMappingId { get; set; }
    public Guid KnowledgePointId { get; set; }
    public Guid KnowledgePointRevisionId { get; set; }
    public decimal MaxScore { get; set; }
    public decimal ObtainedScore { get; set; }
    public string AlgorithmVersion { get; set; } = string.Empty;
}

/// <summary>Frozen grading components and complete mapping evidence; drafts never claim confirmed evidence.</summary>
public sealed class EducationAssessmentGradeContent
{
    public const int MaxJsonLength = 1000000;
    public int SchemaVersion { get; set; } = 1;
    public List<EducationAssessmentGradePart> Parts { get; set; } = [];
    public List<EducationAssessmentMappingGradeFact> MappingEvidence { get; set; } = [];
    [JsonIgnore] public decimal MaxScore => Parts.Sum(x => x.MaxScore);
    [JsonIgnore] public decimal? ObtainedScore => Parts.Any(x => !x.ObtainedScore.HasValue) ? null : Parts.Sum(x => x.ObtainedScore!.Value);

    public void Validate(bool confirmed)
    {
        if (SchemaVersion != 1 || Parts == null || MappingEvidence == null || Parts.Count is < 1 or > 100 ||
            Parts.Any(x => x == null || x.PartId == Guid.Empty || !EducationAssessmentGradeJson.Score(x.MaxScore, true) ||
                x.TextbookKnowledgePointMappingId == Guid.Empty || x.ObtainedScore.HasValue &&
                (!EducationAssessmentGradeJson.Score(x.ObtainedScore.Value) || x.ObtainedScore > x.MaxScore)) ||
            Parts.Select(x => x.PartId).Distinct().Count() != Parts.Count || MaxScore > 1000 ||
            MappingEvidence.Count > 3000 || MappingEvidence.Any(x => x == null || x.TextbookKnowledgePointMappingId == Guid.Empty ||
                x.KnowledgePointId == Guid.Empty || x.KnowledgePointRevisionId == Guid.Empty ||
                !EducationAssessmentGradeJson.Score(x.MaxScore, true) || !EducationAssessmentGradeJson.Score(x.ObtainedScore) ||
                x.ObtainedScore > x.MaxScore || string.IsNullOrWhiteSpace(x.AlgorithmVersion) || x.AlgorithmVersion.Length > 64) ||
            MappingEvidence.Select(x => x.TextbookKnowledgePointMappingId).Distinct().Count() != MappingEvidence.Count ||
            (!confirmed && MappingEvidence.Count != 0) || confirmed &&
            (!ObtainedScore.HasValue || MappingEvidence.Count == 0 || MappingEvidence.Sum(x => x.MaxScore) != MaxScore ||
             MappingEvidence.Sum(x => x.ObtainedScore) != ObtainedScore.Value)) throw EducationAssessmentGradeJson.Invalid();
    }

    public string ToCanonicalJson(bool confirmed)
    {
        Validate(confirmed);
        return EducationAssessmentGradeJson.Write(new EducationAssessmentGradeContent
        {
            Parts = Parts.OrderBy(x => x.PartId).Select(x => new EducationAssessmentGradePart
            {
                PartId = x.PartId, MaxScore = EducationAssessmentGradeJson.Number(x.MaxScore),
                ObtainedScore = x.ObtainedScore.HasValue ? EducationAssessmentGradeJson.Number(x.ObtainedScore.Value) : null,
                TextbookKnowledgePointMappingId = x.TextbookKnowledgePointMappingId
            }).ToList(),
            MappingEvidence = MappingEvidence.OrderBy(x => x.TextbookKnowledgePointMappingId).Select(x => new EducationAssessmentMappingGradeFact
            {
                TextbookKnowledgePointMappingId = x.TextbookKnowledgePointMappingId, KnowledgePointId = x.KnowledgePointId,
                KnowledgePointRevisionId = x.KnowledgePointRevisionId, MaxScore = EducationAssessmentGradeJson.Number(x.MaxScore),
                ObtainedScore = EducationAssessmentGradeJson.Number(x.ObtainedScore), AlgorithmVersion = x.AlgorithmVersion
            }).ToList()
        }, MaxJsonLength);
    }

    public static EducationAssessmentGradeContent Parse(string json, bool confirmed)
    {
        var content = EducationAssessmentGradeJson.Read<EducationAssessmentGradeContent>(json, MaxJsonLength);
        content.Validate(confirmed);
        return content;
    }
}

public sealed class EducationAssessmentClosureEntry
{
    public Guid RecipientId { get; set; }
    public Guid StudentUserId { get; set; }
    public EducationAssessmentRecipientOutcome Outcome { get; set; }
    public Guid? AttemptId { get; set; }
    public Guid? SubmissionId { get; set; }
}

public sealed class EducationAssessmentClosureValue
{
    public const int MaxJsonLength = 1000000;
    public int SchemaVersion { get; set; } = 1;
    public List<EducationAssessmentClosureEntry> Entries { get; set; } = [];
    [JsonIgnore] public int ValidSubmissionCount => Entries.Count(x => x.Outcome == EducationAssessmentRecipientOutcome.Submitted);
    [JsonIgnore] public string SubmissionSetFingerprint => SubmissionFingerprint(Entries);

    public void Validate()
    {
        if (SchemaVersion != 1 || Entries == null || Entries.Count is < 1 or > 3000 ||
            Entries.Any(x => x == null || x.RecipientId == Guid.Empty || x.StudentUserId == Guid.Empty || !Enum.IsDefined(x.Outcome) ||
                x.AttemptId == Guid.Empty || x.SubmissionId == Guid.Empty || (x.Outcome == EducationAssessmentRecipientOutcome.Submitted
                    ? !x.AttemptId.HasValue || !x.SubmissionId.HasValue : x.AttemptId.HasValue || x.SubmissionId.HasValue)) ||
            Entries.Select(x => x.RecipientId).Distinct().Count() != Entries.Count ||
            Entries.Select(x => x.StudentUserId).Distinct().Count() != Entries.Count ||
            Entries.Where(x => x.SubmissionId.HasValue).Select(x => x.SubmissionId).Distinct().Count() != ValidSubmissionCount ||
            Entries.Where(x => x.AttemptId.HasValue).Select(x => x.AttemptId).Distinct().Count() != ValidSubmissionCount)
            throw EducationAssessmentGradeJson.Invalid();
    }

    public string ToCanonicalJson()
    {
        Validate();
        return EducationAssessmentGradeJson.Write(new EducationAssessmentClosureValue { Entries = Entries.OrderBy(x => x.RecipientId).ToList() }, MaxJsonLength);
    }
    public static EducationAssessmentClosureValue Parse(string json)
    {
        var value = EducationAssessmentGradeJson.Read<EducationAssessmentClosureValue>(json, MaxJsonLength);
        value.Validate();
        return value;
    }
    public static string SubmissionFingerprint(IEnumerable<EducationAssessmentClosureEntry> entries)
    {
        var json = JsonSerializer.Serialize(entries.Where(x => x.Outcome == EducationAssessmentRecipientOutcome.Submitted)
            .OrderBy(x => x.RecipientId).Select(x => new { x.RecipientId, x.StudentUserId, x.AttemptId, x.SubmissionId }), EducationAssessmentGradeJson.Options);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }
}

public sealed class EducationAssessmentReleasedGradeReference
{
    public Guid PaperQuestionId { get; set; }
    public Guid QuestionGradeId { get; set; }
    public Guid GradeRevisionId { get; set; }
}

public sealed class EducationAssessmentReleasedGradesValue
{
    public const int MaxJsonLength = 1000000;
    public int SchemaVersion { get; set; } = 1;
    public List<EducationAssessmentReleasedGradeReference> Grades { get; set; } = [];
    public void Validate()
    {
        if (SchemaVersion != 1 || Grades == null || Grades.Count is < 1 or > 3000 ||
            Grades.Any(x => x == null || x.PaperQuestionId == Guid.Empty || x.QuestionGradeId == Guid.Empty || x.GradeRevisionId == Guid.Empty) ||
            Grades.Select(x => x.PaperQuestionId).Distinct().Count() != Grades.Count ||
            Grades.Select(x => x.QuestionGradeId).Distinct().Count() != Grades.Count ||
            Grades.Select(x => x.GradeRevisionId).Distinct().Count() != Grades.Count) throw EducationAssessmentGradeJson.Invalid();
    }
    public string ToCanonicalJson()
    {
        Validate();
        return EducationAssessmentGradeJson.Write(new EducationAssessmentReleasedGradesValue { Grades = Grades.OrderBy(x => x.PaperQuestionId).ToList() }, MaxJsonLength);
    }
    public static EducationAssessmentReleasedGradesValue Parse(string json)
    {
        var value = EducationAssessmentGradeJson.Read<EducationAssessmentReleasedGradesValue>(json, MaxJsonLength);
        value.Validate();
        return value;
    }
}

internal static class EducationAssessmentGradeJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 8, AllowDuplicateProperties = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict
    };
    internal static bool Score(decimal score, bool positive = false) => score >= (positive ? 0.01m : 0) && score <= 1000 && decimal.Round(score, 2) == score;
    internal static decimal Number(decimal value) => decimal.Parse(value.ToString("G29", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    internal static BusinessException Invalid() => new(EducationAssessmentErrorCodes.InvalidGrade);
    internal static string Write<T>(T value, int maximum)
    {
        var json = JsonSerializer.Serialize(value, Options);
        if (json.Length > maximum) throw Invalid();
        return json;
    }
    internal static T Read<T>(string json, int maximum) where T : class
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > maximum) throw Invalid();
        try { return JsonSerializer.Deserialize<T>(json, Options) ?? throw Invalid(); }
        catch (JsonException) { throw Invalid(); }
    }
}
