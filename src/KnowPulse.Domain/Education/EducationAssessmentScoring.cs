using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using Volo.Abp;

namespace KnowPulse.Education;

/// <summary>Calculates from the frozen paper only; never normalizes or changes the student's response.</summary>
public static class EducationAssessmentScoring
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 8,
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict
    };

    public static EducationAssessmentQuestionScore Calculate(ExamPaperQuestion question, EducationAssessmentResponseValue? response)
    {
        if (question == null || question.Id == Guid.Empty || !Enum.IsDefined(question.Type) || question.Score <= 0 ||
            question.Score > 1000 || decimal.Round(question.Score, 2) != question.Score) throw InvalidDefinition();
        var definition = question.ScoringDefinitionJson == null ? null : QuestionScoringDefinition.Parse(question.ScoringDefinitionJson);
        if (definition == null && (question.Type is QuestionType.MultipleChoice or QuestionType.FillBlank or QuestionType.Subjective))
            throw new BusinessException(QuestionScoringDefinition.InvalidDefinitionErrorCode);
        definition?.ValidateForConfirmation(question.Type, question.Stem, question.Score);
        if (question.Type == QuestionType.MultipleChoice && definition!.MultipleChoiceScoringMode != question.MultipleChoiceScoringMode)
            throw InvalidDefinition();

        var options = Read<List<FrozenOption>>(question.OptionsJson);
        var correct = Read<List<string>>(question.CorrectAnswersJson);
        if (options.Count > EducationAssessmentResponseValue.MaxOptions || options.Any(x => x == null || string.IsNullOrWhiteSpace(x.OptionKey) ||
            x.OptionKey.Length > QuestionOption.MaxOptionKeyLength || string.IsNullOrWhiteSpace(x.Content) || x.Content.Length > QuestionOption.MaxContentLength) ||
            options.Select(x => x.OptionKey).Distinct(StringComparer.Ordinal).Count() != options.Count ||
            correct.Count > EducationAssessmentResponseValue.MaxOptions || correct.Any(string.IsNullOrWhiteSpace) ||
            correct.Distinct(StringComparer.Ordinal).Count() != correct.Count) throw InvalidDefinition();
        var keys = options.Select(x => x.OptionKey).ToList();
        switch (question.Type)
        {
            case QuestionType.SingleChoice:
            case QuestionType.MultipleChoice:
                if (keys.Count < 2 || keys.Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != keys.Count ||
                    (question.Type == QuestionType.SingleChoice ? correct.Count != 1 : correct.Count < 2)) throw InvalidDefinition();
                // Approval historically compares answer values with trimmed, case-insensitive
                // option keys. Resolve that frozen evidence to the actual stable key; the
                // student's selected key remains an exact Ordinal identity below.
                correct = correct.Select(value => ResolveCorrectKey(value, keys)).ToList();
                if (correct.Distinct(StringComparer.Ordinal).Count() != correct.Count) throw InvalidDefinition();
                break;
            case QuestionType.TrueFalse:
                keys = ["true", "false"];
                // Approved true/false questions may retain display options. They do
                // not change the two stable boolean response identities.
                if (correct.Count != 1) throw InvalidDefinition();
                var boolean = correct[0].Trim();
                correct = [boolean.Equals("true", StringComparison.OrdinalIgnoreCase) || boolean == "对" ? "true" :
                    boolean.Equals("false", StringComparison.OrdinalIgnoreCase) || boolean == "错" ? "false" : throw InvalidDefinition()];
                break;
            default:
                if (options.Count != 0 || correct.Count != 0) throw InvalidDefinition();
                break;
        }

        var answer = response ?? new EducationAssessmentResponseValue { Type = question.Type };
        // Shape validation precedes reading image IDs. Their actual uploaded ownership is
        // established by the sealed submission; this calculation cannot authorize a Blob.
        answer.ToCanonicalJson();
        answer.ValidateForQuestion(question.Type, keys, definition?.Blanks.Select(x => x.Id).ToList() ?? [],
            answer.Images.Select(x => x.AttachmentId).ToList());
        var parts = new List<EducationAssessmentPartScore>();
        switch (question.Type)
        {
            case QuestionType.SingleChoice:
            case QuestionType.TrueFalse:
                parts.Add(new(question.Id, question.Score,
                    answer.SelectedOptionKeys.Count == 1 && answer.SelectedOptionKeys[0] == correct[0] ? question.Score : 0, null));
                break;
            case QuestionType.MultipleChoice:
                var wrong = answer.SelectedOptionKeys.Any(x => !correct.Contains(x, StringComparer.Ordinal));
                var earned = wrong ? 0 : question.MultipleChoiceScoringMode == MultipleChoiceScoringMode.PartialWithoutWrong
                    ? decimal.Round(question.Score * answer.SelectedOptionKeys.Count / correct.Count, 2, MidpointRounding.AwayFromZero)
                    : answer.SelectedOptionKeys.Count == correct.Count ? question.Score : 0;
                parts.Add(new(question.Id, question.Score, earned, null));
                break;
            case QuestionType.FillBlank:
                foreach (var blank in definition!.Blanks.OrderBy(x => x.DisplayOrder))
                {
                    var text = answer.Blanks.FirstOrDefault(x => x.BlankId == blank.Id)?.Text;
                    decimal? score = blank.TextRule == QuestionBlankTextRule.Manual ? null : Matches(blank, text) ? blank.Score : 0;
                    parts.Add(new(blank.Id, blank.Score, score, blank.TextbookKnowledgePointMappingId));
                }
                break;
            case QuestionType.Subjective:
                parts.AddRange(definition!.ScoringPoints.OrderBy(x => x.DisplayOrder)
                    .Select(x => new EducationAssessmentPartScore(x.Id, x.Score, null, x.TextbookKnowledgePointMappingId)));
                break;
        }
        return new EducationAssessmentQuestionScore(question.Score,
            parts.Any(x => !x.ObtainedScore.HasValue) ? null : parts.Sum(x => x.ObtainedScore!.Value), parts.AsReadOnly());
    }

    private static bool Matches(QuestionBlankScoringRule rule, string? original)
    {
        if (string.IsNullOrWhiteSpace(original)) return false;
        var text = rule.TrimWhitespace ? original.Trim() : original;
        var comparison = rule.TextRule == QuestionBlankTextRule.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return rule.AllowedAnswers.Any(allowed => string.Equals(text, rule.TrimWhitespace ? allowed.Trim() : allowed, comparison));
    }

    private static string ResolveCorrectKey(string answer, IReadOnlyList<string> options)
    {
        var matches = options.Where(key => string.Equals(key.Trim(), answer.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count == 1 ? matches[0] : throw InvalidDefinition();
    }

    private static T Read<T>(string json) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(json, Json) ?? throw InvalidDefinition(); }
        catch (JsonException) { throw InvalidDefinition(); }
        catch (NotSupportedException) { throw InvalidDefinition(); }
    }

    // Matches the existing frozen option JSON shape without depending on application DTOs.
    private sealed record FrozenOption(string OptionKey, string Content, int DisplayOrder);
    private static BusinessException InvalidDefinition() => new(EducationAssessmentErrorCodes.InvalidDefinition);
}

public sealed record EducationAssessmentQuestionScore(decimal MaxScore, decimal? ObtainedScore, IReadOnlyList<EducationAssessmentPartScore> Parts);

/// <summary>Choice questions use the paper question ID with no inferred curriculum mapping.</summary>
public sealed record EducationAssessmentPartScore(Guid PartId, decimal MaxScore, decimal? ObtainedScore, Guid? TextbookKnowledgePointMappingId);
