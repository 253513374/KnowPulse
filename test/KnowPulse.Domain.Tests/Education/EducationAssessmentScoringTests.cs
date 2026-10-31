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

public class EducationAssessmentScoringTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Guid FirstBlank = Guid.NewGuid();
    private static readonly Guid SecondBlank = Guid.NewGuid();
    private static readonly Guid FirstMapping = Guid.NewGuid();
    private static readonly Guid SecondMapping = Guid.NewGuid();

    [Fact]
    public void Repeated_Text_Is_Scored_At_Each_Stable_Blank_Without_Reordering_The_Response()
    {
        var definition = Blanks();
        definition.Blanks[0].AllowedAnswers = ["same"];
        definition.Blanks[1].AllowedAnswers = ["same"];
        var question = Paper(QuestionType.FillBlank, definition);
        var response = BlankResponse((SecondBlank, "same"), (FirstBlank, "same"));
        var original = response.ToCanonicalJson();
        definition.Blanks[0].AllowedAnswers = ["later source edit"];
        var score = EducationAssessmentScoring.Calculate(question, response);
        score.MaxScore.ShouldBe(10);
        score.ObtainedScore.ShouldBe(10);
        score.Parts.Select(x => (x.PartId, x.ObtainedScore, x.TextbookKnowledgePointMappingId))
            .ShouldBe(new[] { (FirstBlank, (decimal?)4m, (Guid?)FirstMapping), (SecondBlank, (decimal?)6m, (Guid?)SecondMapping) });
        response.ToCanonicalJson().ShouldBe(original);
    }

    [Fact]
    public void Exchanging_Text_Between_Blanks_Does_Not_Produce_An_Unordered_Match()
    {
        var score = EducationAssessmentScoring.Calculate(Paper(QuestionType.FillBlank, Blanks()),
            BlankResponse((SecondBlank, "first"), (FirstBlank, "second")));
        score.ObtainedScore.ShouldBe(0);
        score.Parts.Select(x => x.ObtainedScore).ShouldBe(new decimal?[] { 0, 0 });
    }

    [Fact]
    public void An_Automatic_Blank_And_A_Missing_Manual_Blank_Keep_Their_Distinct_Facts_And_Original_Text()
    {
        var definition = Blanks();
        definition.Blanks[0].AllowedAnswers = ["AbC"];
        definition.Blanks[0].TextRule = QuestionBlankTextRule.IgnoreCase;
        definition.Blanks[0].TrimWhitespace = true;
        definition.Blanks[1].TextRule = QuestionBlankTextRule.Manual;
        var question = Paper(QuestionType.FillBlank, definition);
        var response = BlankResponse((FirstBlank, " \tabc\n "));
        var original = response.ToCanonicalJson();
        var score = EducationAssessmentScoring.Calculate(question, response);
        score.Parts[0].ObtainedScore.ShouldBe(4);
        score.Parts[1].ObtainedScore.ShouldBeNull();
        score.ObtainedScore.ShouldBeNull();
        response.ToCanonicalJson().ShouldBe(original);
        var missing = EducationAssessmentScoring.Calculate(question, null);
        missing.Parts[0].ObtainedScore.ShouldBe(0);
        missing.Parts[1].ObtainedScore.ShouldBeNull();
        missing.ObtainedScore.ShouldBeNull();
        var answeredManual = EducationAssessmentScoring.Calculate(question, BlankResponse((FirstBlank, "AbC"), (SecondBlank, "second")));
        answeredManual.Parts[1].ObtainedScore.ShouldBeNull();
        answeredManual.ObtainedScore.ShouldBeNull();
    }

    [Theory]
    [InlineData(QuestionBlankTextRule.Exact, false, "AbC", 4)]
    [InlineData(QuestionBlankTextRule.Exact, false, "abc", 0)]
    [InlineData(QuestionBlankTextRule.Exact, false, " AbC ", 0)]
    [InlineData(QuestionBlankTextRule.Exact, true, " AbC ", 4)]
    [InlineData(QuestionBlankTextRule.IgnoreCase, false, "abc", 4)]
    [InlineData(QuestionBlankTextRule.IgnoreCase, false, " abc ", 0)]
    public void Only_The_Frozen_Text_Rule_Authorizes_Case_And_Whitespace_Comparison(QuestionBlankTextRule rule, bool trim, string text, int expected)
    {
        var definition = Blanks();
        definition.Blanks[0].AllowedAnswers = ["AbC"];
        definition.Blanks[0].TextRule = rule;
        definition.Blanks[0].TrimWhitespace = trim;
        var response = BlankResponse((FirstBlank, text));
        var score = EducationAssessmentScoring.Calculate(Paper(QuestionType.FillBlank, definition), response);
        score.Parts[0].ObtainedScore.ShouldBe(expected);
        score.Parts[1].ObtainedScore.ShouldBe(0);
        response.Blanks.Single().Text.ShouldBe(text);
    }

    [Theory]
    [InlineData(MultipleChoiceScoringMode.AllOrNothing)]
    [InlineData(MultipleChoiceScoringMode.PartialWithoutWrong)]
    public void Multiple_Choice_Uses_Its_Explicit_Mode_Rejects_Wrong_Choices_And_Leaves_Order_Intact(MultipleChoiceScoringMode mode)
    {
        var question = Paper(QuestionType.MultipleChoice, new() { MultipleChoiceScoringMode = mode }, 1.01m);
        var response = Choice(QuestionType.MultipleChoice, "B");
        var partial = EducationAssessmentScoring.Calculate(question, response);
        partial.ObtainedScore.ShouldBe(mode == MultipleChoiceScoringMode.PartialWithoutWrong ? 0.51m : 0m);
        partial.Parts.Single().PartId.ShouldBe(question.Id);
        partial.Parts.Single().MaxScore.ShouldBe(1.01m);
        partial.Parts.Single().TextbookKnowledgePointMappingId.ShouldBeNull();
        EducationAssessmentScoring.Calculate(question, Choice(QuestionType.MultipleChoice, "A", "C")).ObtainedScore.ShouldBe(0);
        var full = Choice(QuestionType.MultipleChoice, "B", "A");
        EducationAssessmentScoring.Calculate(question, full).ObtainedScore.ShouldBe(1.01m);
        full.SelectedOptionKeys.ShouldBe(new[] { "B", "A" });
        EducationAssessmentScoring.Calculate(question, null).ObtainedScore.ShouldBe(0);
    }

    [Theory]
    [InlineData(QuestionType.SingleChoice, "A")]
    [InlineData(QuestionType.TrueFalse, "true")]
    public void Single_Choice_And_True_False_Have_Explicit_Whole_Question_Scores_Using_Ordinal_Keys(QuestionType type, string correct)
    {
        var question = Paper(type);
        var score = EducationAssessmentScoring.Calculate(question, Choice(type, correct));
        score.ObtainedScore.ShouldBe(10);
        score.Parts.Single().PartId.ShouldBe(question.Id);
        score.Parts.Single().ObtainedScore.ShouldBe(10);
        EducationAssessmentScoring.Calculate(question, null).ObtainedScore.ShouldBe(0);
        Should.Throw<BusinessException>(() => EducationAssessmentScoring.Calculate(question, Choice(type, correct.ToUpperInvariant() == correct ? "a" : "TRUE")))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidResponse);
    }

    [Fact]
    public void An_Approved_Case_Insensitive_Correct_Answer_Maps_To_The_Actual_Ordinal_Option_Key()
    {
        var question = Paper(QuestionType.SingleChoice, correctJson: "[\"a\"]",
            optionsJson: "[{\"optionKey\":\"A\",\"content\":\"first\",\"displayOrder\":1},{\"optionKey\":\"B\",\"content\":\"second\",\"displayOrder\":2}]");
        var source = new Question(question.OriginalQuestionId, null, QuestionType.SingleChoice, QuestionDifficulty.Normal,
            QuestionCreationSource.Manual, question.Title, question.Stem);
        source.SubmitForReview([new(Guid.NewGuid(), null, source.Id, "A", "first", 1), new(Guid.NewGuid(), null, source.Id, "B", "second", 2)],
            [new(Guid.NewGuid(), null, source.Id, "a", 1)], DateTime.UtcNow);
        source.Approve(Guid.NewGuid(), DateTime.UtcNow);
        source.RecordApprovedDefinitionRevision(Guid.NewGuid());
        question.FreezeDefinition(source);
        var original = question.CorrectAnswersJson;
        EducationAssessmentScoring.Calculate(question, Choice(QuestionType.SingleChoice, "A")).ObtainedScore.ShouldBe(10);
        Should.Throw<BusinessException>(() => EducationAssessmentScoring.Calculate(question, Choice(QuestionType.SingleChoice, "a")))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidResponse);
        question.CorrectAnswersJson.ShouldBe(original);
    }

    [Theory]
    [InlineData("对", "true")]
    [InlineData("错", "false")]
    [InlineData("TRUE", "true")]
    [InlineData("FALSE", "false")]
    public void Approved_Boolean_Aliases_Map_To_The_Stable_Boolean_Response_Key(string correct, string selected)
    {
        var question = Paper(QuestionType.TrueFalse, correctJson: JsonSerializer.Serialize(new[] { correct }));
        EducationAssessmentScoring.Calculate(question, Choice(QuestionType.TrueFalse, selected)).ObtainedScore.ShouldBe(10);
        EducationAssessmentScoring.Calculate(question, Choice(QuestionType.TrueFalse, selected == "true" ? "false" : "true")).ObtainedScore.ShouldBe(0);
        question.CorrectAnswersJson.ShouldBe(JsonSerializer.Serialize(new[] { correct }));
    }

    [Fact]
    public void An_Approved_True_False_Question_Can_Keep_Its_Frozen_Display_Options()
    {
        var source = new Question(Guid.NewGuid(), null, QuestionType.TrueFalse, QuestionDifficulty.Normal,
            QuestionCreationSource.Manual, "判断题", "请判断陈述。");
        var options = new List<QuestionOption> { new(Guid.NewGuid(), null, source.Id, "true", "正确", 1),
            new(Guid.NewGuid(), null, source.Id, "false", "错误", 2) };
        source.SubmitForReview(options, [new(Guid.NewGuid(), null, source.Id, "对", 1)], DateTime.UtcNow);
        source.Approve(Guid.NewGuid(), DateTime.UtcNow);
        source.RecordApprovedDefinitionRevision(Guid.NewGuid());
        var frozenOptions = JsonSerializer.Serialize(options.Select(x => new { x.OptionKey, x.Content, x.DisplayOrder }), Json);
        var question = new ExamPaperQuestion(Guid.NewGuid(), null, Guid.NewGuid(), source.Id, source.Type, source.Difficulty,
            source.CreationSource, source.Title, source.Stem, frozenOptions, "[\"对\"]", null, null, null, 10,
            MultipleChoiceScoringMode.AllOrNothing, 1);
        question.FreezeDefinition(source);
        EducationAssessmentScoring.Calculate(question, Choice(QuestionType.TrueFalse, "true")).ObtainedScore.ShouldBe(10);
        EducationAssessmentScoring.Calculate(question, Choice(QuestionType.TrueFalse, "false")).ObtainedScore.ShouldBe(0);
        Should.Throw<BusinessException>(() => EducationAssessmentScoring.Calculate(question, Choice(QuestionType.TrueFalse, "对")))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidResponse);
        question.OptionsJson.ShouldBe(frozenOptions);
        question.CorrectAnswersJson.ShouldBe("[\"对\"]");
    }

    [Fact]
    public void Every_Subjective_Point_Is_Pending_Manual_Even_When_The_Whole_Question_Is_Unanswered()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var question = Paper(QuestionType.Subjective, new()
        {
            ReferenceAnswer = "teacher's reference",
            ScoringPoints = [new() { Id = first, DisplayOrder = 1, Description = "first reasoning point", Score = 4, TextbookKnowledgePointMappingId = FirstMapping },
                new() { Id = second, DisplayOrder = 2, Description = "second reasoning point", Score = 6, TextbookKnowledgePointMappingId = SecondMapping }]
        });
        var response = new EducationAssessmentResponseValue { Type = QuestionType.Subjective, Text = " 原始推理\n ",
            Images = [new() { AttachmentId = Guid.NewGuid(), RotationDegrees = 90 }] };
        var original = response.ToCanonicalJson();
        foreach (var value in new EducationAssessmentResponseValue?[] { null, response })
        {
            var score = EducationAssessmentScoring.Calculate(question, value);
            score.ObtainedScore.ShouldBeNull();
            score.Parts.Select(x => (x.PartId, x.MaxScore, x.ObtainedScore))
                .ShouldBe(new[] { (first, 4m, (decimal?)null), (second, 6m, (decimal?)null) });
        }
        response.ToCanonicalJson().ShouldBe(original);
    }

    [Theory]
    [InlineData("unknown-blank")]
    [InlineData("duplicate-blank")]
    [InlineData("wrong-type")]
    [InlineData("incompatible-field")]
    [InlineData("unknown-option")]
    [InlineData("duplicate-option")]
    public void Unknown_Or_Duplicate_Identities_And_Incompatible_Response_Shapes_Are_Rejected(string change)
    {
        var question = Paper(QuestionType.FillBlank, Blanks());
        var response = BlankResponse((FirstBlank, "first"));
        switch (change)
        {
            case "unknown-blank": response.Blanks[0].BlankId = Guid.NewGuid(); break;
            case "duplicate-blank": response.Blanks.Add(new() { BlankId = FirstBlank, Text = "second" }); break;
            case "wrong-type": response = Choice(QuestionType.SingleChoice, "A"); break;
            case "incompatible-field": response.Text = "not a blank response"; break;
            case "unknown-option": question = Paper(QuestionType.SingleChoice); response = Choice(QuestionType.SingleChoice, "unknown"); break;
            case "duplicate-option": question = Paper(QuestionType.MultipleChoice, new() { MultipleChoiceScoringMode = MultipleChoiceScoringMode.AllOrNothing });
                response = Choice(QuestionType.MultipleChoice, "A", "A"); break;
        }
        Should.Throw<BusinessException>(() => EducationAssessmentScoring.Calculate(question, response))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidResponse);
    }

    [Theory]
    [InlineData(QuestionType.FillBlank)]
    [InlineData(QuestionType.MultipleChoice)]
    [InlineData(QuestionType.Subjective)]
    public void Missing_Required_Frozen_Rules_Do_Not_Fall_Back_To_Legacy_Scoring(QuestionType type)
    {
        Should.Throw<BusinessException>(() => EducationAssessmentScoring.Calculate(Paper(type), null))
            .Code.ShouldBe(QuestionScoringDefinition.InvalidDefinitionErrorCode);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("malformed")]
    [InlineData("ambiguous-options")]
    public void Corrupt_Frozen_Correct_Keys_Are_Rejected_With_No_Raw_Body_In_The_Error(string change)
    {
        var question = Paper(QuestionType.SingleChoice, correctJson: change == "duplicate" ? "[\"A\",\"A\"]" :
            change == "unknown" ? "[\"SECRET-INVALID-KEY\"]" : change == "ambiguous-options" ? "[\"A\"]" : "SECRET-MALFORMED-BODY",
            optionsJson: change == "ambiguous-options" ? "[{\"optionKey\":\"A\",\"content\":\"first\",\"displayOrder\":1},{\"optionKey\":\"a\",\"content\":\"second\",\"displayOrder\":2}]" : null);
        var error = Should.Throw<BusinessException>(() => EducationAssessmentScoring.Calculate(question, null));
        error.Code.ShouldBe(EducationAssessmentErrorCodes.InvalidDefinition);
        error.ToString().ShouldNotContain("SECRET-");
    }

    private static EducationAssessmentResponseValue Choice(QuestionType type, params string[] keys) => new()
    { Type = type, SelectedOptionKeys = keys.ToList() };
    private static EducationAssessmentResponseValue BlankResponse(params (Guid Id, string Text)[] blanks) => new()
    { Type = QuestionType.FillBlank, Blanks = blanks.Select(x => new EducationAssessmentBlankResponse { BlankId = x.Id, Text = x.Text }).ToList() };
    private static QuestionScoringDefinition Blanks() => new()
    {
        Blanks = [new() { Id = FirstBlank, DisplayOrder = 1, StemMarker = "【1】", Score = 4, AllowedAnswers = ["first"],
            TextRule = QuestionBlankTextRule.Exact, TextbookKnowledgePointMappingId = FirstMapping },
            new() { Id = SecondBlank, DisplayOrder = 2, StemMarker = "【2】", Score = 6, AllowedAnswers = ["second"],
                TextRule = QuestionBlankTextRule.Exact, TextbookKnowledgePointMappingId = SecondMapping }]
    };

    private static ExamPaperQuestion Paper(QuestionType type, QuestionScoringDefinition? definition = null, decimal score = 10, string? correctJson = null, string? optionsJson = null)
    {
        var questionId = Guid.NewGuid();
        var stem = type == QuestionType.FillBlank ? "第一空【1】，第二空【2】。" : "请作答。";
        List<QuestionOption> options = type is QuestionType.SingleChoice or QuestionType.MultipleChoice
            ? [new(Guid.NewGuid(), null, questionId, "A", "first option", 1), new(Guid.NewGuid(), null, questionId, "B", "second option", 2),
                new(Guid.NewGuid(), null, questionId, "C", "wrong option", 3)] : [];
        List<string> correct = type == QuestionType.SingleChoice ? ["A"] : type == QuestionType.MultipleChoice ? ["A", "B"] :
            type == QuestionType.TrueFalse ? ["true"] : [];
        var row = new ExamPaperQuestion(Guid.NewGuid(), null, Guid.NewGuid(), questionId, type, QuestionDifficulty.Normal,
            QuestionCreationSource.Manual, "frozen question", stem, optionsJson ?? JsonSerializer.Serialize(options.Select(x => new { x.OptionKey, x.Content, x.DisplayOrder }), Json),
            correctJson ?? JsonSerializer.Serialize(correct, Json), null, null, null, score,
            definition?.MultipleChoiceScoringMode ?? MultipleChoiceScoringMode.AllOrNothing, 1);
        if (definition != null)
        {
            var source = new Question(questionId, null, type, QuestionDifficulty.Normal, QuestionCreationSource.Manual, "frozen question", stem);
            source.SetScoringDefinitionDraft(definition);
            source.SubmitForReview(options, correct.Select((answer, index) => new QuestionAnswer(Guid.NewGuid(), null, questionId, answer, index + 1)).ToList(), DateTime.UtcNow);
            source.Approve(Guid.NewGuid(), DateTime.UtcNow);
            source.RecordApprovedDefinitionRevision(Guid.NewGuid());
            row.FreezeDefinition(source);
        }
        return row;
    }
}
