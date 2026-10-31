using System;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    [Fact]
    public async Task Incomplete_Course_Fill_Draft_Should_Save_But_Not_Confirm_Or_Submit()
    {
        var f = await FixtureAsync();
        using var reader = User(f.Reader);
        var service = GetRequiredService<IQuestionAppService>();
        var input = Manual(f);
        input.Type = QuestionType.FillBlank;
        input.Stem = "甲【1】，乙【2】";
        input.Answers = [];
        input.Options = [];
        var draft = await service.CreateAsync(input);
        draft.Answers.ShouldBeEmpty();
        await Should.ThrowAsync<BusinessException>(() => service.ConfirmCurriculumAsync(draft.Id, Confirm(f, draft)));
        await Should.ThrowAsync<BusinessException>(() => service.SubmitReviewAsync(draft.Id));
        var edit = Draft(draft);
        edit.ScoringDefinition = new() { Blanks = [new() { StemMarker = "【1】" }] };
        draft = await GetRequiredService<IQuestionWorkflowAppService>().SaveDraftAsync(draft.Id, edit);
        draft.ScoringDefinition!.Blanks.Count.ShouldBe(1);
        draft.CurriculumConfirmed.ShouldBeFalse();
        edit.ConcurrencyStamp = null;
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => GetRequiredService<IQuestionWorkflowAppService>().SaveDraftAsync(draft.Id, edit));
    }

    [Fact]
    public async Task Separate_Blanks_With_Same_Answer_Should_Freeze_Their_Rules_And_Not_Change_With_Source_Edit()
    {
        var f = await FixtureAsync();
        using var reader = User(f.Reader);
        var service = GetRequiredService<IQuestionAppService>();
        var input = Manual(f);
        input.Type = QuestionType.FillBlank;
        input.Stem = "甲【1】，乙【2】";
        input.Answers = [];
        input.Options = [];
        input.ScoringDefinition = new()
        {
            Blanks = [new() { Id = Guid.NewGuid(), DisplayOrder = 1, StemMarker = "【1】", Score = 6,
                TextRule = QuestionBlankTextRule.Exact, AllowedAnswers = ["相同"], TextbookKnowledgePointMappingId = f.MappingA },
                new() { Id = Guid.NewGuid(), DisplayOrder = 2, StemMarker = "【2】", Score = 4,
                TextRule = QuestionBlankTextRule.IgnoreCase, AllowedAnswers = ["相同"], TextbookKnowledgePointMappingId = f.MappingB }]
        };
        var question = await service.CreateAsync(input);
        question = await service.ConfirmCurriculumAsync(question.Id, Confirm(f, question));
        question = await service.SubmitReviewAsync(question.Id);
        question = await service.ApproveAsync(question.Id, Review(question));
        var paper = await GetRequiredService<IExamAppService>().SavePaperAsync(Paper(f, question.Id));
        var row = paper.Questions.Single();
        row.QuestionDefinitionRevisionId.ShouldBe(question.ApprovedDefinitionRevisionId);
        row.ScoringDefinition!.Blanks.Select(x => x.Id).Distinct().Count().ShouldBe(2);
        row.ScoringDefinition.Blanks.Select(x => x.AllowedAnswers.Single()).ShouldBe(new[] { "相同", "相同" });
        row.ResponseFields.Select(x => x.StemMarker).ShouldBe(new[] { "【1】", "【2】" });
        var frozenRules = row.ScoringDefinition.ToCanonicalJson();
        var planId = await WithUnitOfWorkAsync(async () =>
        {
            // Direct historical fixture exercises the recipient projection; new education
            // publication through this legacy plan API remains rejected.
            var plan = new ExamPlan(Guid.NewGuid(), null, "历史安全投影", paper.Id,
                DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddHours(1), 30, 5, 1, ExamResultVisibility.ScoreOnly);
            await Repo<ExamPlan>().InsertAsync(plan, autoSave: true);
            await Repo<ExamAssignment>().InsertAsync(new(Guid.NewGuid(), null, plan.Id, ExamAssignmentScopeType.User, f.Owner), autoSave: true);
            return plan.Id;
        });
        using (User(f.Owner))
        {
            var participant = await GetRequiredService<IExamAppService>().StartAttemptAsync(planId);
            var safe = participant.Paper!.Questions.Single();
            safe.ScoringDefinition.ShouldBeNull();
            safe.QuestionDefinitionRevisionId.ShouldBeNull();
            safe.ResponseFields.Select(x => x.Id).ShouldBe(row.ResponseFields.Select(x => x.Id));
            JsonSerializer.Serialize(participant).ShouldNotContain("相同");
            var exams = GetRequiredService<IExamAppService>();
            await Should.ThrowAsync<BusinessException>(() => exams.SaveAnswersAsync(participant.Id, new()));
            await Should.ThrowAsync<BusinessException>(() => exams.SubmitAttemptAsync(participant.Id, new() { IdempotencyKey = "new-rules" }));
            (await exams.GetAttemptAsync(participant.Id)).Status.ShouldBe(ExamAttemptStatus.InProgress);
        }
        question = await service.GetAsync(question.Id);
        var edit = Draft(question);
        edit.ScoringDefinition = QuestionScoringDefinition.Parse(frozenRules);
        edit.ScoringDefinition.Blanks[0].AllowedAnswers = ["新答案"];
        var changed = await GetRequiredService<IQuestionWorkflowAppService>().SaveDraftAsync(question.Id, edit);
        changed.CurriculumConfirmed.ShouldBeFalse();
        changed.Status.ShouldBe(QuestionStatus.Draft);
        (await GetRequiredService<IExamAppService>().GetSavedPaperAsync(paper.Id)).Questions.Single().ScoringDefinition!.ToCanonicalJson().ShouldBe(frozenRules);
        await Should.ThrowAsync<BusinessException>(() => GetRequiredService<IExamAppService>().SavePaperAsync(Paper(f, changed.Id)));
    }

    [Fact]
    public async Task Course_Multiple_Choice_Should_Require_Explicit_Mode_And_Freeze_It_Despite_Paper_Rule()
    {
        var f = await FixtureAsync();
        using var reader = User(f.Reader);
        var service = GetRequiredService<IQuestionAppService>();
        var input = Manual(f);
        input.Type = QuestionType.MultipleChoice;
        input.Answers.Add(new() { Value = "B", DisplayOrder = 2 });
        var question = await service.CreateAsync(input);
        await Should.ThrowAsync<BusinessException>(() => service.ConfirmCurriculumAsync(question.Id, Confirm(f, question)));
        var edit = Draft(question);
        edit.ScoringDefinition = new() { MultipleChoiceScoringMode = MultipleChoiceScoringMode.PartialWithoutWrong };
        question = await GetRequiredService<IQuestionWorkflowAppService>().SaveDraftAsync(question.Id, edit);
        question = await service.ConfirmCurriculumAsync(question.Id, Confirm(f, question));
        question = await service.SubmitReviewAsync(question.Id);
        question = await service.ApproveAsync(question.Id, Review(question));
        var inputPaper = Paper(f, question.Id);
        inputPaper.Rules.Single().MultipleChoiceScoringMode = MultipleChoiceScoringMode.AllOrNothing;
        var paper = await GetRequiredService<IExamAppService>().SavePaperAsync(inputPaper);
        paper.Questions.Single().MultipleChoiceScoringMode.ShouldBe(MultipleChoiceScoringMode.PartialWithoutWrong);
        paper.Questions.Single().ScoringDefinition!.MultipleChoiceScoringMode.ShouldBe(MultipleChoiceScoringMode.PartialWithoutWrong);
    }

    [Fact]
    public async Task Subjective_Should_Use_Manual_Points_Without_Dummy_Answers_And_Reject_Wrong_Mapping_Sum()
    {
        var f = await FixtureAsync();
        using var reader = User(f.Reader);
        var service = GetRequiredService<IQuestionAppService>();
        var input = Manual(f);
        input.Type = QuestionType.Subjective;
        input.Options = [];
        input.Answers = [];
        input.ScoringDefinition = new()
        {
            ReferenceAnswer = "先说明方法，再计算结果。",
            ScoringPoints = [new() { Id = Guid.NewGuid(), DisplayOrder = 1, Description = "方法合理", Score = 6, TextbookKnowledgePointMappingId = f.MappingA },
                new() { Id = Guid.NewGuid(), DisplayOrder = 2, Description = "计算正确", Score = 4, TextbookKnowledgePointMappingId = f.MappingB }]
        };
        var question = await service.CreateAsync(input);
        var wrong = Confirm(f, question);
        wrong.PointWeights = [new() { TextbookKnowledgePointMappingId = f.MappingA, Weight = 1, Score = 10 }];
        await Should.ThrowAsync<BusinessException>(() => service.ConfirmCurriculumAsync(question.Id, wrong));
        question = await service.ConfirmCurriculumAsync(question.Id, Confirm(f, question));
        question = await service.SubmitReviewAsync(question.Id);
        question = await service.ApproveAsync(question.Id, Review(question));
        question.Answers.ShouldBeEmpty();
        var paper = await GetRequiredService<IExamAppService>().SavePaperAsync(Paper(f, question.Id));
        paper.Questions.Single().ScoringDefinition!.ScoringPoints.Sum(x => x.Score).ShouldBe(10);
        await Should.ThrowAsync<BusinessException>(() => GetRequiredService<IExamAppService>().CreatePlanAsync(new()
        {
            Name = "不能落入旧客观评分", StartTime = DateTime.UtcNow, EndTime = DateTime.UtcNow.AddHours(1), Paper = Paper(f, question.Id)
        }));
    }

    private static ReviewQuestionDto Review(QuestionDto question) => new()
        { OperationId = Guid.NewGuid(), ConcurrencyStamp = question.ConcurrencyStamp };
}
