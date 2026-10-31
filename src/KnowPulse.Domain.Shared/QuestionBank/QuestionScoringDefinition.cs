using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KnowPulse.Education;
using KnowPulse.Exam;
using Volo.Abp;

namespace KnowPulse.QuestionBank;

/// <summary>题目自有的评分规则；课程确认还须传入当前允许映射及实际知识点分配。</summary>
public sealed class QuestionScoringDefinition
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxJsonLength = 64000;
    public const int MaxComponents = 100;
    public const int MaxAllowedAnswers = 20;
    public const int MaxAnswerLength = 512;
    public const int MaxReferenceAnswerLength = 8000;
    public const int MaxDescriptionLength = 1024;
    public const int MaxMarkerLength = 32;
    public const string InvalidDefinitionErrorCode = "KnowPulse:Education:InvalidScoringDefinition";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 8,
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public MultipleChoiceScoringMode? MultipleChoiceScoringMode { get; set; }
    public string? ReferenceAnswer { get; set; }
    public List<QuestionBlankScoringRule> Blanks { get; set; } = [];
    public List<QuestionSubjectiveScoringPoint> ScoringPoints { get; set; } = [];

    /// <summary>保存草稿只检查边界和形状，允许未填写的 ID、顺序、零分和规则。</summary>
    public void ValidateDraft()
    {
        if (SchemaVersion != CurrentSchemaVersion || Blanks == null || ScoringPoints == null ||
            Blanks.Count > MaxComponents || ScoringPoints.Count > MaxComponents ||
            ReferenceAnswer?.Length > MaxReferenceAnswerLength ||
            (MultipleChoiceScoringMode.HasValue && !Enum.IsDefined(MultipleChoiceScoringMode.Value)) ||
            Blanks.Any(x => x == null) || ScoringPoints.Any(x => x == null)) throw Invalid();
        foreach (var blank in Blanks)
        {
            if (blank.StemMarker == null || blank.StemMarker.Length > MaxMarkerLength ||
                blank.FormatInstructions?.Length > MaxDescriptionLength ||
                blank.AllowedAnswers == null || blank.AllowedAnswers.Count > MaxAllowedAnswers ||
                blank.AllowedAnswers.Any(x => x == null || x.Length > MaxAnswerLength) ||
                !DraftScore(blank.Score) || blank.DisplayOrder < 0 || blank.DisplayOrder > MaxComponents ||
                blank.TextbookKnowledgePointMappingId == Guid.Empty ||
                (blank.TextRule.HasValue && !Enum.IsDefined(blank.TextRule.Value))) throw Invalid();
        }
        foreach (var point in ScoringPoints)
        {
            if (point.Description == null || point.Description.Length > MaxDescriptionLength ||
                !DraftScore(point.Score) || point.DisplayOrder < 0 || point.DisplayOrder > MaxComponents ||
                point.TextbookKnowledgePointMappingId == Guid.Empty) throw Invalid();
        }
        if (DuplicateAssignedIds(Blanks.Select(x => x.Id)) || DuplicateAssignedIds(ScoringPoints.Select(x => x.Id)) ||
            DuplicateAssignedOrders(Blanks.Select(x => x.DisplayOrder)) || DuplicateAssignedOrders(ScoringPoints.Select(x => x.DisplayOrder))) throw Invalid();
        EnsureJsonLimit(JsonSerializer.Serialize(this, JsonOptions));
    }

    /// <summary>校验完整定义；候选可只传映射，正式课程确认必须同时传实际 pointWeights。</summary>
    public void ValidateForConfirmation(QuestionType type, string stem, decimal totalScore,
        IReadOnlyCollection<Guid>? allowedMappingIds = null, IReadOnlyCollection<QuestionCurriculumPointWeight>? pointWeights = null)
    {
        ValidateDraft();
        if (!Enum.IsDefined(type) || string.IsNullOrWhiteSpace(stem) || !PositiveScore(totalScore)) throw Invalid();
        if (allowedMappingIds != null && (allowedMappingIds.Count is < 1 or > 3000 || allowedMappingIds.Contains(Guid.Empty) ||
            allowedMappingIds.Distinct().Count() != allowedMappingIds.Count)) throw Invalid();
        var allocations = new List<(Guid? MappingId, decimal Score)>();
        switch (type)
        {
            case QuestionType.SingleChoice:
            case QuestionType.TrueFalse:
                if (MultipleChoiceScoringMode.HasValue || Blanks.Count != 0 || ScoringPoints.Count != 0) throw Invalid();
                break;
            case QuestionType.MultipleChoice:
                if (!MultipleChoiceScoringMode.HasValue || Blanks.Count != 0 || ScoringPoints.Count != 0) throw Invalid();
                break;
            case QuestionType.FillBlank:
                if (MultipleChoiceScoringMode.HasValue || ScoringPoints.Count != 0 || Blanks.Count == 0) throw Invalid();
                CompleteRows(Blanks.Select(x => (x.Id, x.DisplayOrder, x.Score)).ToList());
                var end = 0;
                foreach (var blank in Blanks.OrderBy(x => x.DisplayOrder))
                {
                    if (string.IsNullOrWhiteSpace(blank.StemMarker) || !blank.TextRule.HasValue || blank.AllowedAnswers.Count == 0 ||
                        blank.AllowedAnswers.Any(string.IsNullOrWhiteSpace)) throw Invalid();
                    var position = stem.IndexOf(blank.StemMarker, StringComparison.Ordinal);
                    if (position < end || position < 0 || stem.IndexOf(blank.StemMarker, position + 1, StringComparison.Ordinal) >= 0) throw Invalid();
                    end = position + blank.StemMarker.Length;
                    allocations.Add((blank.TextbookKnowledgePointMappingId, blank.Score));
                }
                break;
            case QuestionType.Subjective:
                if (MultipleChoiceScoringMode.HasValue || Blanks.Count != 0 || ScoringPoints.Count == 0 || string.IsNullOrWhiteSpace(ReferenceAnswer)) throw Invalid();
                CompleteRows(ScoringPoints.Select(x => (x.Id, x.DisplayOrder, x.Score)).ToList());
                if (ScoringPoints.Any(x => string.IsNullOrWhiteSpace(x.Description))) throw Invalid();
                allocations.AddRange(ScoringPoints.Select(x => (x.TextbookKnowledgePointMappingId, x.Score)));
                break;
            default:
                throw Invalid();
        }
        if (allocations.Count > 0 && allocations.Sum(x => x.Score) != totalScore) throw Invalid();
        if (allowedMappingIds != null && allocations.Any(x => !x.MappingId.HasValue || !allowedMappingIds.Contains(x.MappingId.Value))) throw Invalid();
        if (pointWeights != null)
        {
            if (pointWeights.Count is < 1 or > 3000 || pointWeights.Any(x => x == null || x.TextbookKnowledgePointMappingId == Guid.Empty ||
                !PositiveScore(x.Score) || x.Weight <= 0 || x.Weight > 1 ||
                x.Score != decimal.Round(totalScore * x.Weight, 2, MidpointRounding.AwayFromZero) ||
                (allowedMappingIds != null && !allowedMappingIds.Contains(x.TextbookKnowledgePointMappingId))) ||
                pointWeights.Select(x => x.TextbookKnowledgePointMappingId).Distinct().Count() != pointWeights.Count ||
                pointWeights.Sum(x => x.Score) != totalScore || pointWeights.Sum(x => x.Weight) != 1) throw Invalid();
            if (allocations.Count > 0)
            {
                if (allocations.Any(x => !x.MappingId.HasValue)) throw Invalid();
                var byMapping = allocations.GroupBy(x => x.MappingId!.Value).ToDictionary(x => x.Key, x => x.Sum(row => row.Score));
                if (byMapping.Count != pointWeights.Count || pointWeights.Any(x => !byMapping.TryGetValue(x.TextbookKnowledgePointMappingId, out var score) || score != x.Score)) throw Invalid();
            }
        }
    }

    /// <summary>稳定 JSON 用于持久化和请求指纹；不修改源对象、不清洗 ID、不舍入分值。</summary>
    public string ToCanonicalJson()
    {
        ValidateDraft();
        var canonical = new QuestionScoringDefinition
        {
            SchemaVersion = SchemaVersion, MultipleChoiceScoringMode = MultipleChoiceScoringMode, ReferenceAnswer = ReferenceAnswer,
            Blanks = Blanks.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).Select(x => new QuestionBlankScoringRule
            {
                Id = x.Id, DisplayOrder = x.DisplayOrder, StemMarker = x.StemMarker, Score = CanonicalNumber(x.Score),
                AllowedAnswers = x.AllowedAnswers.OrderBy(answer => answer, StringComparer.Ordinal).ToList(),
                TextRule = x.TextRule, TrimWhitespace = x.TrimWhitespace, FormatInstructions = x.FormatInstructions,
                TextbookKnowledgePointMappingId = x.TextbookKnowledgePointMappingId
            }).ToList(),
            ScoringPoints = ScoringPoints.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).Select(x => new QuestionSubjectiveScoringPoint
            {
                Id = x.Id, DisplayOrder = x.DisplayOrder, Description = x.Description, Score = CanonicalNumber(x.Score),
                TextbookKnowledgePointMappingId = x.TextbookKnowledgePointMappingId
            }).ToList()
        };
        var json = JsonSerializer.Serialize(canonical, JsonOptions);
        EnsureJsonLimit(json);
        return json;
    }

    public static QuestionScoringDefinition Parse(string json)
    {
        EnsureJsonLimit(json);
        try
        {
            var result = JsonSerializer.Deserialize<QuestionScoringDefinition>(json, JsonOptions) ?? throw Invalid();
            result.ValidateDraft();
            return result;
        }
        catch (JsonException) { throw Invalid(); }
    }

    private static void CompleteRows(List<(Guid Id, int Order, decimal Score)> rows)
    {
        if (rows.Any(x => x.Id == Guid.Empty || !PositiveScore(x.Score)) ||
            !rows.Select(x => x.Order).Order().SequenceEqual(Enumerable.Range(1, rows.Count))) throw Invalid();
    }
    private static bool DraftScore(decimal score) => score >= 0 && score <= 1000 && decimal.Round(score, 2) == score;
    private static bool PositiveScore(decimal score) => score > 0 && DraftScore(score);
    private static bool DuplicateAssignedIds(IEnumerable<Guid> ids) => ids.Where(x => x != Guid.Empty).GroupBy(x => x).Any(x => x.Count() > 1);
    private static bool DuplicateAssignedOrders(IEnumerable<int> orders) => orders.Where(x => x > 0).GroupBy(x => x).Any(x => x.Count() > 1);
    private static decimal CanonicalNumber(decimal value) => decimal.Parse(value.ToString("G29", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
    private static void EnsureJsonLimit(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxJsonLength || Encoding.UTF8.GetByteCount(json) > MaxJsonLength) throw Invalid();
    }
    private static BusinessException Invalid() => new(InvalidDefinitionErrorCode);
}

/// <summary>自动文本规则仅处理明确列出的文本；公式/表达等价由 Manual 显式转人工。</summary>
public enum QuestionBlankTextRule
{
    Exact = 0,
    IgnoreCase = 1,
    Manual = 2
}

public sealed class QuestionBlankScoringRule
{
    public Guid Id { get; set; }
    public int DisplayOrder { get; set; }
    /// <summary>题干中仅出现一次的显式标记，例如【1】；顺序与题干位置一致。</summary>
    public string StemMarker { get; set; } = string.Empty;
    public decimal Score { get; set; }
    public List<string> AllowedAnswers { get; set; } = [];
    public QuestionBlankTextRule? TextRule { get; set; }
    public bool TrimWhitespace { get; set; }
    /// <summary>格式/单位说明；不据此推断未实现的自动等价规则。</summary>
    public string? FormatInstructions { get; set; }
    public Guid? TextbookKnowledgePointMappingId { get; set; }
}

public sealed class QuestionSubjectiveScoringPoint
{
    public Guid Id { get; set; }
    public int DisplayOrder { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Score { get; set; }
    public Guid? TextbookKnowledgePointMappingId { get; set; }
}
