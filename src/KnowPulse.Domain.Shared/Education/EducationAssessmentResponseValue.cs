using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using KnowPulse.QuestionBank;
using Volo.Abp;

namespace KnowPulse.Education;

/// <summary>一题完整的原始响应；原文和选择/图片顺序保留，评分另行记录。</summary>
public sealed class EducationAssessmentResponseValue
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxJsonLength = 200000;
    public const int MaxTextLength = 20000;
    public const int MaxOptions = 100;
    public const int MaxBlanks = 100;
    public const int MaxImages = 20;
    public const long MaxImageFileBytes = 20 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 8,
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict
    };

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public QuestionType Type { get; set; }
    public List<string> SelectedOptionKeys { get; set; } = [];
    public List<EducationAssessmentBlankResponse> Blanks { get; set; } = [];
    public string? Text { get; set; }
    public List<EducationAssessmentImageResponse> Images { get; set; } = [];

    public void ValidateForQuestion(QuestionType type, IReadOnlyCollection<string> allowedOptionKeys,
        IReadOnlyCollection<Guid> allowedBlankIds, IReadOnlyCollection<Guid> allowedAttachmentIds)
    {
        ValidateShape();
        if (type != Type || allowedOptionKeys == null || allowedBlankIds == null || allowedAttachmentIds == null ||
            SelectedOptionKeys.Any(x => !allowedOptionKeys.Contains(x, StringComparer.Ordinal)) ||
            Blanks.Any(x => !allowedBlankIds.Contains(x.BlankId)) ||
            Images.Any(x => !allowedAttachmentIds.Contains(x.AttachmentId))) throw Invalid();
        // Missing blank entries mean unanswered. This is a complete replacement of this
        // question only; it never merges stale fields or changes another question.
        EnsureLength(JsonSerializer.Serialize(this, JsonOptions));
    }

    public string ToCanonicalJson()
    {
        ValidateShape();
        return EnsureLength(JsonSerializer.Serialize(this, JsonOptions));
    }

    public static EducationAssessmentResponseValue Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxJsonLength) throw Invalid();
        try
        {
            var result = JsonSerializer.Deserialize<EducationAssessmentResponseValue>(json, JsonOptions) ?? throw Invalid();
            result.ValidateShape();
            return result;
        }
        catch (JsonException) { throw Invalid(); }
        catch (NotSupportedException) { throw Invalid(); }
    }

    public bool IsUnanswered()
    {
        ValidateShape();
        return SelectedOptionKeys.Count == 0 && string.IsNullOrWhiteSpace(Text) &&
            Blanks.All(x => string.IsNullOrWhiteSpace(x.Text)) && Images.Count == 0;
    }

    private void ValidateShape()
    {
        if (SchemaVersion != CurrentSchemaVersion || !Enum.IsDefined(Type) || SelectedOptionKeys == null ||
            Blanks == null || Images == null || SelectedOptionKeys.Count > MaxOptions || Blanks.Count > MaxBlanks ||
            Images.Count > MaxImages || Text?.Length > MaxTextLength ||
            SelectedOptionKeys.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 64) ||
            SelectedOptionKeys.Distinct(StringComparer.Ordinal).Count() != SelectedOptionKeys.Count ||
            Blanks.Any(x => x == null || x.BlankId == Guid.Empty || x.Text == null || x.Text.Length > MaxTextLength) ||
            Blanks.Select(x => x.BlankId).Distinct().Count() != Blanks.Count ||
            Images.Any(x => x == null || x.AttachmentId == Guid.Empty || x.RotationDegrees is not (0 or 90 or 180 or 270)) ||
            Images.Select(x => x.AttachmentId).Distinct().Count() != Images.Count) throw Invalid();

        switch (Type)
        {
            case QuestionType.SingleChoice:
            case QuestionType.TrueFalse:
                if (SelectedOptionKeys.Count > 1 || Blanks.Count != 0 || Text != null || Images.Count != 0) throw Invalid();
                break;
            case QuestionType.MultipleChoice:
                if (Blanks.Count != 0 || Text != null || Images.Count != 0) throw Invalid();
                break;
            case QuestionType.FillBlank:
                if (SelectedOptionKeys.Count != 0 || Text != null || Images.Count != 0) throw Invalid();
                break;
            case QuestionType.Subjective:
                if (SelectedOptionKeys.Count != 0 || Blanks.Count != 0) throw Invalid();
                break;
            default: throw Invalid();
        }
    }

    private static string EnsureLength(string json) => json.Length <= MaxJsonLength ? json : throw Invalid();
    private static BusinessException Invalid() => new(EducationAssessmentErrorCodes.InvalidResponse);
}

public sealed class EducationAssessmentBlankResponse
{
    public Guid BlankId { get; set; }
    public string Text { get; set; } = string.Empty;
}

public sealed class EducationAssessmentImageResponse
{
    public Guid AttachmentId { get; set; }
    public int RotationDegrees { get; set; }
}

public sealed class EducationAssessmentSubmissionManifestEntry
{
    public Guid PaperQuestionId { get; set; }
    public Guid? ResponseRevisionId { get; set; }
    public string? ResponseHash { get; set; }
}
