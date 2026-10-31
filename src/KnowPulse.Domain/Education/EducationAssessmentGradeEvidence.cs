using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using Volo.Abp;

namespace KnowPulse.Education;

/// <summary>Builds complete mapping evidence from confirmed scores and the frozen curriculum only.</summary>
public static class EducationAssessmentGradeEvidence
{
    public const string AlgorithmVersion = "education-grade-evidence-v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 8,
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict
    };

    public static IReadOnlyList<EducationAssessmentMappingGradeEvidence> Build(
        ExamPaperQuestion frozenQuestion, EducationAssessmentQuestionScore confirmedScore)
    {
        if (frozenQuestion == null || confirmedScore == null || frozenQuestion.Id == Guid.Empty ||
            !frozenQuestion.QuestionDefinitionRevisionId.HasValue || frozenQuestion.QuestionDefinitionRevisionId == Guid.Empty ||
            !frozenQuestion.CurriculumConfirmedByUserId.HasValue || !frozenQuestion.CurriculumConfirmedAtUtc.HasValue ||
            confirmedScore.MaxScore != frozenQuestion.Score || !ValidScore(confirmedScore.MaxScore) || confirmedScore.MaxScore == 0 ||
            !confirmedScore.ObtainedScore.HasValue || !ValidScore(confirmedScore.ObtainedScore.Value) ||
            confirmedScore.ObtainedScore > confirmedScore.MaxScore || confirmedScore.Parts == null ||
            confirmedScore.Parts.Count is < 1 or > QuestionScoringDefinition.MaxComponents ||
            confirmedScore.Parts.Any(x => x == null || x.PartId == Guid.Empty || !ValidScore(x.MaxScore) || x.MaxScore == 0 ||
                !x.ObtainedScore.HasValue || !ValidScore(x.ObtainedScore.Value) || x.ObtainedScore > x.MaxScore) ||
            confirmedScore.Parts.Select(x => x.PartId).Distinct().Count() != confirmedScore.Parts.Count ||
            confirmedScore.Parts.Sum(x => x.MaxScore) != confirmedScore.MaxScore ||
            confirmedScore.Parts.Sum(x => x.ObtainedScore!.Value) != confirmedScore.ObtainedScore.Value) throw Invalid();

        var snapshot = Read<EducationCurriculumSnapshot>(frozenQuestion.CurriculumSnapshotJson);
        var weights = Read<List<QuestionCurriculumPointWeight>>(frozenQuestion.CurriculumPointWeightsJson);
        if (snapshot.TenantId != frozenQuestion.TenantId || snapshot.TeacherUserId == Guid.Empty ||
            snapshot.TeacherUserId != frozenQuestion.CurriculumConfirmedByUserId || snapshot.TextbookVersionId == Guid.Empty ||
            snapshot.Mappings == null || snapshot.Mappings.Count is < 1 or > 3000 ||
            snapshot.Mappings.Any(x => x == null || x.MappingId == Guid.Empty || x.KnowledgePointId == Guid.Empty ||
                x.KnowledgePointRevisionId == Guid.Empty || x.RevisionNumber < 1) ||
            snapshot.Mappings.Select(x => x.MappingId).Distinct().Count() != snapshot.Mappings.Count ||
            weights.Any(x => x == null)) throw Invalid();
        Question.ValidatePointWeights(snapshot, weights, frozenQuestion.Score);
        var definition = frozenQuestion.ScoringDefinitionJson == null ? null : QuestionScoringDefinition.Parse(frozenQuestion.ScoringDefinitionJson);
        definition?.ValidateForConfirmation(frozenQuestion.Type, frozenQuestion.Stem, frozenQuestion.Score,
            snapshot.Mappings.Select(x => x.MappingId).ToList(), weights);

        // Reuse the scorer's frozen part identities and maxima; only obtained scores may be confirmed by a teacher.
        var expectedParts = EducationAssessmentScoring.Calculate(frozenQuestion, null).Parts.ToDictionary(x => x.PartId);
        if (expectedParts.Count != confirmedScore.Parts.Count || confirmedScore.Parts.Any(x =>
            !expectedParts.TryGetValue(x.PartId, out var expected) || x.MaxScore != expected.MaxScore ||
            x.TextbookKnowledgePointMappingId != expected.TextbookKnowledgePointMappingId)) throw Invalid();

        var orderedWeights = weights.OrderBy(x => x.TextbookKnowledgePointMappingId).ToList();
        decimal[] obtained;
        if (frozenQuestion.Type is QuestionType.FillBlank or QuestionType.Subjective)
        {
            var partsByMapping = confirmedScore.Parts.GroupBy(x => x.TextbookKnowledgePointMappingId!.Value)
                .ToDictionary(x => x.Key, x => (Max: x.Sum(part => part.MaxScore), Obtained: x.Sum(part => part.ObtainedScore!.Value)));
            if (partsByMapping.Count != orderedWeights.Count || orderedWeights.Any(x =>
                !partsByMapping.TryGetValue(x.TextbookKnowledgePointMappingId, out var score) || score.Max != x.Score)) throw Invalid();
            obtained = orderedWeights.Select(x => partsByMapping[x.TextbookKnowledgePointMappingId].Obtained).ToArray();
        }
        else
        {
            obtained = Allocate(orderedWeights, confirmedScore.ObtainedScore.Value, frozenQuestion.Score);
        }

        var mappings = snapshot.Mappings.ToDictionary(x => x.MappingId);
        var evidence = orderedWeights.Select((weight, index) => new EducationAssessmentMappingGradeEvidence(
            weight.TextbookKnowledgePointMappingId, mappings[weight.TextbookKnowledgePointMappingId].KnowledgePointId,
            mappings[weight.TextbookKnowledgePointMappingId].KnowledgePointRevisionId, weight.Score, obtained[index], AlgorithmVersion)).ToList();
        if (evidence.Any(x => !ValidScore(x.ObtainedScore) || x.ObtainedScore > x.MaxScore) ||
            evidence.Sum(x => x.MaxScore) != frozenQuestion.Score || evidence.Sum(x => x.ObtainedScore) != confirmedScore.ObtainedScore.Value) throw Invalid();
        return evidence.AsReadOnly();
    }

    private static decimal[] Allocate(IReadOnlyList<QuestionCurriculumPointWeight> weights, decimal obtained, decimal maximum)
    {
        if (obtained == maximum) return weights.Select(x => x.Score).ToArray();
        var obtainedCents = decimal.ToInt32(obtained * 100);
        var exactCents = weights.Select(x => obtainedCents * x.Weight).ToArray();
        var cents = exactCents.Select(x => decimal.ToInt32(decimal.Floor(x))).ToArray();
        var remaining = obtainedCents - cents.Sum();
        if (remaining < 0 || remaining > weights.Count) throw Invalid();
        var order = Enumerable.Range(0, weights.Count).OrderByDescending(i => exactCents[i] - cents[i])
            .ThenBy(i => weights[i].TextbookKnowledgePointMappingId).ToArray();
        // ponytail: at most 3000 remaining cents; scan only active rows if large mapping sets become hot.
        while (remaining > 0)
        {
            var before = remaining;
            foreach (var index in order)
            {
                if (remaining == 0) break;
                if (cents[index] >= weights[index].Score * 100) continue;
                cents[index]++;
                remaining--;
            }
            if (remaining == before) throw Invalid();
        }
        return cents.Select(x => x / 100m).ToArray();
    }

    private static T Read<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > Question.MaxCurriculumJsonLength) throw Invalid();
        try { return JsonSerializer.Deserialize<T>(json, Json) ?? throw Invalid(); }
        catch (JsonException) { throw Invalid(); }
    }

    private static bool ValidScore(decimal score) => score >= 0 && score <= 1000 && decimal.Round(score, 2) == score;
    private static BusinessException Invalid() => new(EducationAssessmentErrorCodes.InvalidDefinition);
}

public sealed record EducationAssessmentMappingGradeEvidence(Guid TextbookKnowledgePointMappingId,
    Guid KnowledgePointId, Guid KnowledgePointRevisionId, decimal MaxScore, decimal ObtainedScore, string AlgorithmVersion);
