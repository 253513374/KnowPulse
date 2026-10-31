using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.Education;

public class EducationAssessmentGradeEvidenceTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Guid FirstMapping = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondMapping = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid FirstRevision = Guid.NewGuid();
    private static readonly Guid SecondRevision = Guid.NewGuid();
    private static readonly Guid FirstPart = Guid.NewGuid();
    private static readonly Guid SecondPart = Guid.NewGuid();
    private static readonly Guid ThirdPart = Guid.NewGuid();

    [Fact]
    public void Seven_Point_Full_Score_Preserves_The_Confirmed_Three_And_Four_Point_Maxima()
    {
        var question = Freeze(QuestionType.SingleChoice, 7, Weights(7, 3));
        var score = EducationAssessmentScoring.Calculate(question, Choice(QuestionType.SingleChoice, "A"));
        var original = question.CurriculumPointWeightsJson;
        var evidence = EducationAssessmentGradeEvidence.Build(question, score);
        evidence.Select(x => (x.TextbookKnowledgePointMappingId, x.KnowledgePointRevisionId, x.MaxScore, x.ObtainedScore))
            .ShouldBe(new[] { (FirstMapping, FirstRevision, 3m, 3m), (SecondMapping, SecondRevision, 4m, 4m) });
        evidence.All(x => x.AlgorithmVersion == EducationAssessmentGradeEvidence.AlgorithmVersion).ShouldBeTrue();
        question.CurriculumPointWeightsJson.ShouldBe(original);
    }

    [Fact]
    public void Multiple_Choice_Partial_Score_Distributes_The_Last_Cent_Without_Duplicating_Whole_Question_Scores()
    {
        var question = Freeze(QuestionType.MultipleChoice, 7, Weights(7, 3), Partial(), correctKeys: ["A", "B", "C"]);
        var score = EducationAssessmentScoring.Calculate(question, Choice(QuestionType.MultipleChoice, "A"));
        score.ObtainedScore.ShouldBe(2.33m);
        var evidence = EducationAssessmentGradeEvidence.Build(question, score);
        evidence.Select(x => (x.MaxScore, x.ObtainedScore)).ShouldBe(new[] { (3m, 1m), (4m, 1.33m) });
        evidence.Sum(x => x.MaxScore).ShouldBe(7);
        evidence.Sum(x => x.ObtainedScore).ShouldBe(2.33m);
    }

    [Fact]
    public void Equal_Remainders_Are_Resolved_By_Stable_Mapping_Id_Regardless_Of_Input_Order()
    {
        var weights = Weights(0.02m, 0.01m);
        weights.Reverse();
        var question = Freeze(QuestionType.MultipleChoice, 0.02m, weights, Partial());
        var score = EducationAssessmentScoring.Calculate(question, Choice(QuestionType.MultipleChoice, "A"));
        var evidence = EducationAssessmentGradeEvidence.Build(question, score);
        evidence.Select(x => (x.TextbookKnowledgePointMappingId, x.ObtainedScore))
            .ShouldBe(new[] { (FirstMapping, 0.01m), (SecondMapping, 0m) });
    }

    [Fact]
    public void Capped_Mappings_Do_Not_Prevent_Another_Round_Of_Distributing_The_Remaining_Cents()
    {
        decimal[] fractions = [0.3055m, 0.3055m, 0.3055m, 0.0245m, 0.0245m, 0.0345m];
        decimal[] maxima = [0.31m, 0.31m, 0.31m, 0.02m, 0.02m, 0.03m];
        var weights = fractions.Select((fraction, index) => new QuestionCurriculumPointWeight
        {
            TextbookKnowledgePointMappingId = Guid.Parse($"00000000-0000-0000-0000-{index + 1:D12}"),
            Weight = fraction, Score = maxima[index]
        }).ToList();
        var correct = Enumerable.Range(1, 50).Select(x => $"K{x:D2}").ToList();
        var question = Freeze(QuestionType.MultipleChoice, 1, weights, Partial(), correctKeys: correct);
        var score = EducationAssessmentScoring.Calculate(question, Choice(QuestionType.MultipleChoice, correct.Take(49).ToArray()));
        score.ObtainedScore.ShouldBe(0.98m);
        var evidence = EducationAssessmentGradeEvidence.Build(question, score);
        evidence.Select(x => x.ObtainedScore).ShouldBe(new decimal[] { 0.31m, 0.30m, 0.30m, 0.02m, 0.02m, 0.03m });
        evidence.Sum(x => x.ObtainedScore).ShouldBe(0.98m);
        evidence.Sum(x => x.MaxScore).ShouldBe(1);
        evidence.All(x => x.ObtainedScore <= x.MaxScore).ShouldBeTrue();
        weights.Reverse();
        var reversedQuestion = Freeze(QuestionType.MultipleChoice, 1, weights, Partial(), correctKeys: correct);
        var reversedScore = EducationAssessmentScoring.Calculate(reversedQuestion, Choice(QuestionType.MultipleChoice, correct.Take(49).ToArray()));
        EducationAssessmentGradeEvidence.Build(reversedQuestion, reversedScore).ShouldBe(evidence);
    }

    [Fact]
    public void Incorrect_And_Unanswered_Objective_Questions_Still_Have_Evidence_For_Every_Mapping()
    {
        var question = Freeze(QuestionType.SingleChoice, 7, Weights(7, 3));
        foreach (var response in new EducationAssessmentResponseValue?[] { null, Choice(QuestionType.SingleChoice, "B") })
        {
            var evidence = EducationAssessmentGradeEvidence.Build(question, EducationAssessmentScoring.Calculate(question, response));
            evidence.Count.ShouldBe(2);
            evidence.Select(x => x.MaxScore).ShouldBe(new decimal[] { 3, 4 });
            evidence.All(x => x.ObtainedScore == 0).ShouldBeTrue();
        }
    }

    [Fact]
    public void Several_Stable_Blanks_On_One_Mapping_Aggregate_Their_Actual_Point_Scores()
    {
        var definition = Blanks();
        var question = Freeze(QuestionType.FillBlank, 6, Weights(6, 3), definition);
        var response = new EducationAssessmentResponseValue
        {
            Type = QuestionType.FillBlank,
            Blanks = [new() { BlankId = ThirdPart, Text = "third" }, new() { BlankId = FirstPart, Text = "first" },
                new() { BlankId = SecondPart, Text = "wrong" }]
        };
        var original = response.ToCanonicalJson();
        var score = EducationAssessmentScoring.Calculate(question, response);
        var evidence = EducationAssessmentGradeEvidence.Build(question, score with { Parts = score.Parts.Reverse().ToList() });
        evidence.Select(x => (x.MaxScore, x.ObtainedScore)).ShouldBe(new[] { (3m, 1m), (3m, 3m) });
        evidence.Sum(x => x.ObtainedScore).ShouldBe(4);
        response.ToCanonicalJson().ShouldBe(original);
    }

    [Fact]
    public void Confirmed_Subjective_Points_Aggregate_Their_Actual_Scores_Without_Proportional_Reallocation()
    {
        var question = Freeze(QuestionType.Subjective, 6, Weights(6, 3), Subjective());
        var pending = EducationAssessmentScoring.Calculate(question, null);
        var parts = pending.Parts.Select(x => x with
        {
            ObtainedScore = x.PartId == FirstPart ? 0.5m : x.PartId == SecondPart ? 1.5m : 2m
        }).ToList();
        var evidence = EducationAssessmentGradeEvidence.Build(question, pending with { ObtainedScore = 4, Parts = parts });
        evidence.Select(x => (x.MaxScore, x.ObtainedScore)).ShouldBe(new[] { (3m, 2m), (3m, 2m) });
    }

    [Theory]
    [InlineData(QuestionType.FillBlank)]
    [InlineData(QuestionType.Subjective)]
    public void Manual_Parts_Require_Explicit_Confirmation_And_Can_Be_Confirmed_As_Zero(QuestionType type)
    {
        var definition = type == QuestionType.FillBlank ? Blanks() : Subjective();
        if (type == QuestionType.FillBlank) definition.Blanks.ForEach(x => x.TextRule = QuestionBlankTextRule.Manual);
        var question = Freeze(type, 6, Weights(6, 3), definition);
        var pending = EducationAssessmentScoring.Calculate(question, null);
        Should.Throw<BusinessException>(() => EducationAssessmentGradeEvidence.Build(question, pending));
        var parts = pending.Parts.Select(x => x with { ObtainedScore = 0m }).ToList();
        var confirmed = pending with { ObtainedScore = 0m, Parts = parts };
        var evidence = EducationAssessmentGradeEvidence.Build(question, confirmed);
        evidence.Count.ShouldBe(2);
        evidence.All(x => x.ObtainedScore == 0).ShouldBeTrue();
        parts[0] = parts[0] with { ObtainedScore = null };
        Should.Throw<BusinessException>(() => EducationAssessmentGradeEvidence.Build(question, confirmed));
    }

    [Theory]
    [InlineData("unknown-part")]
    [InlineData("changed-mapping")]
    [InlineData("unknown-mapping")]
    [InlineData("changed-maxima")]
    [InlineData("missing-part")]
    [InlineData("duplicate-part")]
    public void Changed_Or_Incomplete_Part_Identities_And_Mapping_Attribution_Are_Rejected(string change)
    {
        var question = Freeze(QuestionType.FillBlank, 6, Weights(6, 3), Blanks());
        var original = EducationAssessmentScoring.Calculate(question, null);
        var parts = original.Parts.ToList();
        switch (change)
        {
            case "unknown-part": parts[0] = parts[0] with { PartId = Guid.NewGuid() }; break;
            case "changed-mapping": parts[0] = parts[0] with { TextbookKnowledgePointMappingId = SecondMapping }; break;
            case "unknown-mapping": parts[0] = parts[0] with { TextbookKnowledgePointMappingId = Guid.NewGuid() }; break;
            case "changed-maxima": parts[0] = parts[0] with { MaxScore = 2 }; parts[1] = parts[1] with { MaxScore = 1 }; break;
            case "missing-part": parts.RemoveAt(0); break;
            case "duplicate-part": parts[1] = parts[1] with { PartId = parts[0].PartId }; break;
        }
        Should.Throw<BusinessException>(() => EducationAssessmentGradeEvidence.Build(question, original with { Parts = parts }));
    }

    [Theory]
    [InlineData("negative")]
    [InlineData("over-max")]
    [InlineData("three-decimals")]
    [InlineData("changed-total")]
    [InlineData("inconsistent-sum")]
    [InlineData("null-part")]
    public void Illegal_Scores_And_Inconsistent_Totals_Are_Rejected(string change)
    {
        var question = Freeze(QuestionType.SingleChoice, 7, Weights(7, 3));
        var score = EducationAssessmentScoring.Calculate(question, Choice(QuestionType.SingleChoice, "A"));
        switch (change)
        {
            case "negative": score = score with { ObtainedScore = -1, Parts = [score.Parts[0] with { ObtainedScore = -1 }] }; break;
            case "over-max": score = score with { ObtainedScore = 8, Parts = [score.Parts[0] with { ObtainedScore = 8 }] }; break;
            case "three-decimals": score = score with { ObtainedScore = 1.001m, Parts = [score.Parts[0] with { ObtainedScore = 1.001m }] }; break;
            case "changed-total": score = score with { MaxScore = 8 }; break;
            case "inconsistent-sum": score = score with { ObtainedScore = 6 }; break;
            case "null-part": score = score with { Parts = [null!] }; break;
        }
        Should.Throw<BusinessException>(() => EducationAssessmentGradeEvidence.Build(question, score));
    }

    [Theory]
    [InlineData("missing-point-revision")]
    [InlineData("duplicate-mapping")]
    [InlineData("wrong-tenant")]
    public void Incomplete_Or_Ambiguous_Frozen_Curriculum_Cannot_Create_Evidence(string change)
    {
        var question = Freeze(QuestionType.SingleChoice, 7, Weights(7, 3), modifySnapshot: snapshot =>
        {
            switch (change)
            {
                case "missing-point-revision": snapshot.Mappings[0].KnowledgePointRevisionId = Guid.Empty; break;
                case "duplicate-mapping": snapshot.Mappings.Add(snapshot.Mappings[0]); break;
                case "wrong-tenant": snapshot.TenantId = Guid.NewGuid(); break;
            }
        });
        Should.Throw<BusinessException>(() => EducationAssessmentGradeEvidence.Build(question,
            EducationAssessmentScoring.Calculate(question, null)));
    }

    [Theory]
    [InlineData("unknown-property")]
    [InlineData("duplicate-property")]
    public void Frozen_Curriculum_Is_Parsed_Strictly_Without_Disclosing_Invalid_Json(string change)
    {
        var question = Freeze(QuestionType.SingleChoice, 7, Weights(7, 3), modifySnapshotJson: json =>
            json.Insert(1, change == "unknown-property" ? "\"unknown\":\"SECRET-BODY\"," : "\"textbookTitle\":\"SECRET-BODY\","));
        var error = Should.Throw<BusinessException>(() => EducationAssessmentGradeEvidence.Build(question,
            EducationAssessmentScoring.Calculate(question, null)));
        error.Code.ShouldBe(EducationAssessmentErrorCodes.InvalidDefinition);
        error.ToString().ShouldNotContain("SECRET-BODY");
    }

    private static QuestionScoringDefinition Partial() => new() { MultipleChoiceScoringMode = MultipleChoiceScoringMode.PartialWithoutWrong };
    private static EducationAssessmentResponseValue Choice(QuestionType type, params string[] keys) => new() { Type = type, SelectedOptionKeys = keys.ToList() };
    private static List<QuestionCurriculumPointWeight> Weights(decimal total, decimal firstScore)
    {
        var firstWeight = firstScore / total;
        return [new() { TextbookKnowledgePointMappingId = FirstMapping, Weight = firstWeight, Score = firstScore },
            new() { TextbookKnowledgePointMappingId = SecondMapping, Weight = 1 - firstWeight, Score = total - firstScore }];
    }
    private static QuestionScoringDefinition Blanks() => new()
    {
        Blanks = [new() { Id = FirstPart, DisplayOrder = 1, StemMarker = "【1】", Score = 1, AllowedAnswers = ["first"],
                TextRule = QuestionBlankTextRule.Exact, TextbookKnowledgePointMappingId = FirstMapping },
            new() { Id = SecondPart, DisplayOrder = 2, StemMarker = "【2】", Score = 2, AllowedAnswers = ["second"],
                TextRule = QuestionBlankTextRule.Exact, TextbookKnowledgePointMappingId = FirstMapping },
            new() { Id = ThirdPart, DisplayOrder = 3, StemMarker = "【3】", Score = 3, AllowedAnswers = ["third"],
                TextRule = QuestionBlankTextRule.Exact, TextbookKnowledgePointMappingId = SecondMapping }]
    };
    private static QuestionScoringDefinition Subjective() => new()
    {
        ReferenceAnswer = "完整推理。",
        ScoringPoints = [new() { Id = FirstPart, DisplayOrder = 1, Description = "第一步", Score = 1, TextbookKnowledgePointMappingId = FirstMapping },
            new() { Id = SecondPart, DisplayOrder = 2, Description = "第二步", Score = 2, TextbookKnowledgePointMappingId = FirstMapping },
            new() { Id = ThirdPart, DisplayOrder = 3, Description = "第三步", Score = 3, TextbookKnowledgePointMappingId = SecondMapping }]
    };

    private static ExamPaperQuestion Freeze(QuestionType type, decimal score, List<QuestionCurriculumPointWeight> weights,
        QuestionScoringDefinition? definition = null, List<string>? correctKeys = null, Action<EducationCurriculumSnapshot>? modifySnapshot = null,
        Func<string, string>? modifySnapshotJson = null)
    {
        var owner = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var snapshot = new EducationCurriculumSnapshot
        {
            TeacherUserId = owner, SchoolId = Guid.NewGuid(), TeachingAssignmentId = Guid.NewGuid(), TeacherTextbookSelectionId = Guid.NewGuid(),
            TextbookVersionId = Guid.NewGuid(), EducationSubjectId = Guid.NewGuid(), SubjectName = "数学", Grade = 7,
            CatalogCode = "math", CatalogRevision = 1, TextbookTitle = "七年级数学", Publisher = "出版社", Edition = "2026", Volume = "上册", CapturedAtUtc = now,
            Mappings = weights.Select((weight, index) => new EducationCurriculumMappingSnapshot
            {
                MappingId = weight.TextbookKnowledgePointMappingId, ChapterId = Guid.NewGuid(), ChapterCode = (index + 1).ToString(), ChapterTitle = "章节",
                KnowledgePointId = Guid.Parse("10000000" + weight.TextbookKnowledgePointMappingId.ToString("D")[8..]),
                KnowledgePointRevisionId = weight.TextbookKnowledgePointMappingId == FirstMapping ? FirstRevision :
                    weight.TextbookKnowledgePointMappingId == SecondMapping ? SecondRevision : Guid.Parse("20000000" + weight.TextbookKnowledgePointMappingId.ToString("D")[8..]),
                KnowledgePointCode = (index + 1).ToString(), KnowledgePointName = "知识点", RevisionNumber = 1
            }).ToList()
        };
        modifySnapshot?.Invoke(snapshot);
        var stem = type == QuestionType.FillBlank ? "填空【1】、【2】、【3】。" : "请回答。";
        var source = new Question(Guid.NewGuid(), null, type, QuestionDifficulty.Normal, QuestionCreationSource.Manual, "冻结题", stem);
        source.RestrictToPersonalOwner(owner);
        source.SetScoringDefinitionDraft(definition);
        var snapshotJson = JsonSerializer.Serialize(snapshot, Json);
        source.SetCurriculumDraft(modifySnapshotJson?.Invoke(snapshotJson) ?? snapshotJson,
            JsonSerializer.Serialize(weights.Select(x => x.TextbookKnowledgePointMappingId), Json), "[]", score);
        source.ConfirmCurriculum(JsonSerializer.Serialize(weights, Json), score, owner, now);
        correctKeys ??= type == QuestionType.SingleChoice ? ["A"] : type == QuestionType.MultipleChoice ? ["A", "B"] : [];
        List<QuestionOption> options = type is QuestionType.SingleChoice or QuestionType.MultipleChoice
            ? new[] { "A", "B", "C", "D" }.Concat(correctKeys).Distinct(StringComparer.Ordinal)
                .Select((key, index) => new QuestionOption(Guid.NewGuid(), null, source.Id, key, $"选项{index + 1}", index + 1)).ToList() : [];
        source.SubmitForReview(options, correctKeys.Select((x, index) => new QuestionAnswer(Guid.NewGuid(), null, source.Id, x, index + 1)).ToList(), now);
        source.Approve(owner, now);
        source.RecordApprovedDefinitionRevision(Guid.NewGuid());
        var row = new ExamPaperQuestion(Guid.NewGuid(), null, Guid.NewGuid(), source.Id, type, QuestionDifficulty.Normal,
            QuestionCreationSource.Manual, source.Title, source.Stem, JsonSerializer.Serialize(options.Select(x => new { x.OptionKey, x.Content, x.DisplayOrder }), Json),
            JsonSerializer.Serialize(correctKeys, Json), null, null, null, score, definition?.MultipleChoiceScoringMode ?? MultipleChoiceScoringMode.AllOrNothing, 1);
        row.FreezeDefinition(source);
        row.FreezeCurriculum(source);
        return row;
    }
}
