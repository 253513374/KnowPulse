using System;
using System.Collections.Generic;
using System.Linq;
using KnowPulse.Education;
using KnowPulse.Exam;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.QuestionBank;

public class QuestionScoringDefinitionTests
{
    private static readonly Guid FirstMapping = Guid.Parse("a2edaaeb-814b-40b9-a26a-7c697d6370a1");
    private static readonly Guid SecondMapping = Guid.Parse("a2edaaeb-814b-40b9-a26a-7c697d6370a2");
    private static readonly Guid FirstBlank = Guid.Parse("a2edaaeb-814b-40b9-a26a-7c697d6370b1");
    private static readonly Guid SecondBlank = Guid.Parse("a2edaaeb-814b-40b9-a26a-7c697d6370b2");
    private const string Stem = "第一空【1】，第二空【2】。";

    [Fact]
    public void Incomplete_Draft_Can_Roundtrip_But_Cannot_Be_Confirmed()
    {
        var definition = new QuestionScoringDefinition { Blanks = [new(), new()] };

        definition.ValidateDraft();
        var restored = QuestionScoringDefinition.Parse(definition.ToCanonicalJson());
        restored.Blanks.Count.ShouldBe(2);
        restored.Blanks.ShouldAllBe(x => x.Id == Guid.Empty && x.Score == 0 && x.TextRule == null);
        Invalid(() => restored.ValidateForConfirmation(QuestionType.FillBlank, Stem, 5));
    }

    [Fact]
    public void Two_Blanks_With_Same_Answer_Keep_Separate_Stable_Ids_And_Allocations()
    {
        var definition = FillDefinition();

        definition.ValidateForConfirmation(QuestionType.FillBlank, Stem, 5, Mappings(), Weights(2, 3));
        var restored = QuestionScoringDefinition.Parse(definition.ToCanonicalJson());

        restored.Blanks.Select(x => x.Id).ShouldBe(new[] { FirstBlank, SecondBlank });
        restored.Blanks.Select(x => x.AllowedAnswers.Single()).ShouldBe(new[] { "相同答案", "相同答案" });
        restored.Blanks.Select(x => x.Score).ShouldBe(new[] { 2m, 3m });
        restored.Blanks.Select(x => x.TextbookKnowledgePointMappingId).ShouldBe(new Guid?[] { FirstMapping, SecondMapping });
    }

    [Theory]
    [InlineData("第一空【1】，第二空不存在。")]
    [InlineData("第一空【1】【1】，第二空【2】。")]
    [InlineData("第二空【2】，第一空【1】。")]
    public void Blank_Markers_Must_Be_Present_Unique_And_In_Display_Order(string stem)
    {
        Invalid(() => FillDefinition().ValidateForConfirmation(QuestionType.FillBlank, stem, 5));
    }

    [Fact]
    public void Overlapping_Marker_Occurrences_Are_Not_A_Unique_Blank_Position()
    {
        var definition = FillDefinition();
        definition.Blanks[0].StemMarker = "aa";
        Invalid(() => definition.ValidateForConfirmation(QuestionType.FillBlank, "aaa【2】", 5));

        definition.Blanks[0].StemMarker = "abc";
        definition.Blanks[1].StemMarker = "bc";
        Invalid(() => definition.ValidateForConfirmation(QuestionType.FillBlank, "abc", 5));
    }

    [Fact]
    public void Full_Rules_Require_Stable_Ids_Contiguous_Order_And_Explicit_Text_Rule()
    {
        var definition = FillDefinition();
        definition.Blanks[0].Id = Guid.Empty;
        Invalid(() => definition.ValidateForConfirmation(QuestionType.FillBlank, Stem, 5));
        definition = FillDefinition();
        definition.Blanks[1].DisplayOrder = 3;
        Invalid(() => definition.ValidateForConfirmation(QuestionType.FillBlank, Stem, 5));
        definition = FillDefinition();
        definition.Blanks[0].TextRule = null;
        Invalid(() => definition.ValidateForConfirmation(QuestionType.FillBlank, Stem, 5));
        definition = FillDefinition();
        definition.Blanks[0].AllowedAnswers = [" "];
        Invalid(() => definition.ValidateForConfirmation(QuestionType.FillBlank, Stem, 5));
    }

    [Fact]
    public void Equal_Total_Does_Not_Hide_Wrong_Per_Point_Allocation()
    {
        var definition = FillDefinition();
        Invalid(() => definition.ValidateForConfirmation(QuestionType.FillBlank, Stem, 6));
        Invalid(() => definition.ValidateForConfirmation(QuestionType.FillBlank, Stem, 5, Mappings(), Weights(3, 2)));

        definition.Blanks[1].TextbookKnowledgePointMappingId = FirstMapping;
        definition.ValidateForConfirmation(QuestionType.FillBlank, Stem, 5, Mappings(),
            [new() { TextbookKnowledgePointMappingId = FirstMapping, Score = 5, Weight = 1 }]);
    }

    [Fact]
    public void Course_Components_Cannot_Use_Missing_Or_Out_Of_Scope_Mapping()
    {
        var definition = FillDefinition();
        definition.Blanks[0].TextbookKnowledgePointMappingId = null;
        Invalid(() => definition.ValidateForConfirmation(QuestionType.FillBlank, Stem, 5, Mappings()));
        definition.Blanks[0].TextbookKnowledgePointMappingId = Guid.NewGuid();
        Invalid(() => definition.ValidateForConfirmation(QuestionType.FillBlank, Stem, 5, Mappings()));
        Invalid(() => FillDefinition().ValidateForConfirmation(QuestionType.FillBlank, Stem, 5, [FirstMapping, FirstMapping]));
    }

    [Fact]
    public void Seven_Point_Question_Accepts_Three_Plus_Four_With_Decimal_Weight_Remainder()
    {
        var definition = FillDefinition();
        definition.Blanks[0].Score = 3;
        definition.Blanks[1].Score = 4;
        var weights = Weights(3, 4);
        definition.ValidateForConfirmation(QuestionType.FillBlank, Stem, 7, Mappings(), weights);

        weights[1].Weight -= 0.01m;
        Invalid(() => definition.ValidateForConfirmation(QuestionType.FillBlank, Stem, 7, Mappings(), weights));
    }

    [Fact]
    public void Manual_Formula_Rule_Preserves_Examples_And_Format_Instructions()
    {
        var definition = FillDefinition();
        definition.Blanks[0].TextRule = QuestionBlankTextRule.Manual;
        definition.Blanks[0].AllowedAnswers = ["x²", "x*x"];
        definition.Blanks[0].FormatInstructions = "等价表达由教师人工判断，不使用文本匹配推断。";
        definition.Blanks[0].TrimWhitespace = true;
        definition.Blanks[1].TextRule = QuestionBlankTextRule.IgnoreCase;
        definition.ValidateForConfirmation(QuestionType.FillBlank, Stem, 5, Mappings(), Weights(2, 3));

        var restored = QuestionScoringDefinition.Parse(definition.ToCanonicalJson());
        restored.Blanks[0].TextRule.ShouldBe(QuestionBlankTextRule.Manual);
        restored.Blanks[0].AllowedAnswers.Count.ShouldBe(2);
        restored.Blanks[0].FormatInstructions.ShouldBe(definition.Blanks[0].FormatInstructions);
        restored.Blanks[0].TrimWhitespace.ShouldBeTrue();
        restored.Blanks[1].TextRule.ShouldBe(QuestionBlankTextRule.IgnoreCase);
    }

    [Fact]
    public void Subjective_Requires_Reference_Answer_And_Individual_Described_Scoring_Points()
    {
        var definition = new QuestionScoringDefinition
        {
            ReferenceAnswer = "说明结论，并展示推导过程。",
            ScoringPoints =
            [
                new() { Id = FirstBlank, DisplayOrder = 1, Description = "结论正确", Score = 2, TextbookKnowledgePointMappingId = FirstMapping },
                new() { Id = SecondBlank, DisplayOrder = 2, Description = "推导过程合理", Score = 3, TextbookKnowledgePointMappingId = SecondMapping }
            ]
        };
        definition.ValidateForConfirmation(QuestionType.Subjective, "请说明理由。", 5, Mappings(), Weights(2, 3));
        definition.ReferenceAnswer = " ";
        Invalid(() => definition.ValidateForConfirmation(QuestionType.Subjective, "请说明理由。", 5));
        definition.ReferenceAnswer = "参考解答";
        definition.ScoringPoints[0].Description = "";
        Invalid(() => definition.ValidateForConfirmation(QuestionType.Subjective, "请说明理由。", 5));
    }

    [Theory]
    [InlineData(MultipleChoiceScoringMode.AllOrNothing)]
    [InlineData(MultipleChoiceScoringMode.PartialWithoutWrong)]
    public void Multiple_Choice_Freezes_An_Explicit_Mode_And_Rejects_Other_Type_Payload(MultipleChoiceScoringMode mode)
    {
        var definition = new QuestionScoringDefinition { MultipleChoiceScoringMode = mode };
        definition.ValidateForConfirmation(QuestionType.MultipleChoice, "请选择所有正确选项。", 5, Mappings(), Weights(2, 3));
        QuestionScoringDefinition.Parse(definition.ToCanonicalJson()).MultipleChoiceScoringMode.ShouldBe(mode);
        Invalid(() => definition.ValidateForConfirmation(QuestionType.SingleChoice, "请选择。", 5));
        definition.Blanks.Add(FillDefinition().Blanks[0]);
        Invalid(() => definition.ValidateForConfirmation(QuestionType.MultipleChoice, "请选择。", 5));
        Invalid(() => new QuestionScoringDefinition().ValidateForConfirmation(QuestionType.MultipleChoice, "请选择。", 5));
    }

    [Fact]
    public void Canonical_Json_Is_Stable_Without_Mutating_Or_Cleaning_The_Draft()
    {
        var first = FillDefinition();
        first.Blanks[0].AllowedAnswers = [" z ", "a", "a"];
        first.Blanks[0].Score = 2.00m;
        first.Blanks.Reverse();
        var second = FillDefinition();
        second.Blanks[0].AllowedAnswers = ["a", " z ", "a"];

        first.ToCanonicalJson().ShouldBe(second.ToCanonicalJson());
        first.Blanks[0].Id.ShouldBe(SecondBlank);
        first.Blanks[1].AllowedAnswers.ShouldBe(new[] { " z ", "a", "a" });
        var restored = QuestionScoringDefinition.Parse(first.ToCanonicalJson());
        restored.Blanks[0].AllowedAnswers.ShouldBe(new[] { " z ", "a", "a" });
    }

    [Fact]
    public void Changing_Reviewed_Scoring_Mode_Returns_Question_To_Draft()
    {
        var question = new Question(Guid.NewGuid(), null, QuestionType.MultipleChoice, QuestionDifficulty.Normal,
            QuestionCreationSource.Manual, "多选题", "请选择全部正确答案。");
        question.SetScoringDefinitionDraft(new() { MultipleChoiceScoringMode = MultipleChoiceScoringMode.AllOrNothing });
        question.SubmitForReview(
            [new(Guid.NewGuid(), null, question.Id, "A", "选项A", 1), new(Guid.NewGuid(), null, question.Id, "B", "选项B", 2)],
            [new(Guid.NewGuid(), null, question.Id, "A", 1), new(Guid.NewGuid(), null, question.Id, "B", 2)], DateTime.UtcNow);
        question.Approve(Guid.NewGuid(), DateTime.UtcNow);

        question.SetScoringDefinitionDraft(new() { MultipleChoiceScoringMode = MultipleChoiceScoringMode.PartialWithoutWrong });

        question.Status.ShouldBe(QuestionStatus.Draft);
        question.IsUsableForExam.ShouldBeFalse();
        question.ReviewedTime.ShouldBeNull();
        question.ReviewedByUserId.ShouldBeNull();
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(0.001)]
    [InlineData(1000.01)]
    public void Draft_Does_Not_Round_Negative_Over_Limit_Or_Extra_Precision_Scores(double score)
    {
        var definition = FillDefinition();
        definition.Blanks[0].Score = (decimal)score;
        Invalid(definition.ValidateDraft);
    }

    [Fact]
    public void Draft_Rejects_Unknown_Enums_Duplicate_Assigned_Ids_And_Empty_Mapping_Ids()
    {
        var definition = FillDefinition();
        definition.Blanks[0].TextRule = (QuestionBlankTextRule)50;
        Invalid(definition.ValidateDraft);
        definition = FillDefinition();
        definition.MultipleChoiceScoringMode = (MultipleChoiceScoringMode)50;
        Invalid(definition.ValidateDraft);
        definition = FillDefinition();
        definition.Blanks[1].Id = definition.Blanks[0].Id;
        Invalid(definition.ValidateDraft);
        definition = FillDefinition();
        definition.Blanks[1].DisplayOrder = 1;
        Invalid(definition.ValidateDraft);
        definition = FillDefinition();
        definition.Blanks[0].TextbookKnowledgePointMappingId = Guid.Empty;
        Invalid(definition.ValidateDraft);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"schemaVersion\":2}")]
    [InlineData("{\"blanks\":null}")]
    [InlineData("{\"scoringPoints\":[null]}")]
    [InlineData("{\"multipleChoiceScoringMode\":99}")]
    [InlineData("{\"unexpectedProperty\":true}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1}")]
    [InlineData("{\"blanks\":[{\"textRule\":99}]}")]
    public void Invalid_Json_Fails_With_A_Controlled_Code(string json)
    {
        Invalid(() => QuestionScoringDefinition.Parse(json));
    }

    [Fact]
    public void Bounded_Json_Rejects_Too_Many_Components_Answers_And_Aggregate_Size()
    {
        var definition = new QuestionScoringDefinition { Blanks = Enumerable.Range(0, 101).Select(_ => new QuestionBlankScoringRule()).ToList() };
        Invalid(definition.ValidateDraft);
        definition = FillDefinition();
        definition.Blanks[0].AllowedAnswers = Enumerable.Repeat("答案", 21).ToList();
        Invalid(definition.ValidateDraft);
        definition.Blanks[0].AllowedAnswers = [new string('x', 513)];
        Invalid(definition.ValidateDraft);
        definition = new QuestionScoringDefinition
        {
            Blanks = Enumerable.Range(0, 100).Select(_ => new QuestionBlankScoringRule
            { AllowedAnswers = Enumerable.Repeat(new string('x', 512), 20).ToList() }).ToList()
        };
        Invalid(definition.ValidateDraft);
        Invalid(() => QuestionScoringDefinition.Parse(new string(' ', QuestionScoringDefinition.MaxJsonLength + 1)));
        Invalid(() => QuestionScoringDefinition.Parse("{\"referenceAnswer\":\"" + new string('教', 22000) + "\"}"));
    }

    private static QuestionScoringDefinition FillDefinition() => new()
    {
        Blanks =
        [
            new() { Id = FirstBlank, DisplayOrder = 1, StemMarker = "【1】", Score = 2, AllowedAnswers = ["相同答案"], TextRule = QuestionBlankTextRule.Exact, TextbookKnowledgePointMappingId = FirstMapping },
            new() { Id = SecondBlank, DisplayOrder = 2, StemMarker = "【2】", Score = 3, AllowedAnswers = ["相同答案"], TextRule = QuestionBlankTextRule.Exact, TextbookKnowledgePointMappingId = SecondMapping }
        ]
    };

    private static Guid[] Mappings() => [FirstMapping, SecondMapping];
    private static List<QuestionCurriculumPointWeight> Weights(decimal first, decimal second) =>
    [
        new() { TextbookKnowledgePointMappingId = FirstMapping, Score = first, Weight = first / (first + second) },
        new() { TextbookKnowledgePointMappingId = SecondMapping, Score = second, Weight = 1 - first / (first + second) }
    ];

    private static void Invalid(Action action) => Should.Throw<BusinessException>(action).Code.ShouldBe(QuestionScoringDefinition.InvalidDefinitionErrorCode);
}
