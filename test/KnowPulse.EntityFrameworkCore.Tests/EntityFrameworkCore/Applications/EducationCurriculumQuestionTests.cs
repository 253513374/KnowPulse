using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.AiQuestioning;
using KnowPulse.Education;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using KnowPulse.RagAI;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    [Fact]
    public async Task Generated_Candidate_Should_Keep_Full_Source_Scope_And_Require_Teacher_Point_Confirmation_After_Adoption()
    {
        var f = await FixtureAsync();
        var source = await SourceAsync(f.Owner, "获准教材依据");
        using (User(f.Owner))
        {
            await BindAsync(source, f.MappingA);
            await BindAsync(source, f.MappingB);
            await _sharing.ShareVersionAsync(Share(f, source));
        }
        _vectors.SearchResults.Add(Hit(source));
        var chat = GetRequiredService<TestRagChatClient>();
        chat.Reset();
        chat.ResponseContent = $$"""
            {"questions":[{"type":"SingleChoice","difficulty":"Normal","title":"教材题","stem":"请选择正确答案",
            "options":[{"optionKey":"A","content":"甲"},{"optionKey":"B","content":"乙"}],"answers":["A"],
            "analysis":"教材依据","knowledgePoints":["同名知识点"],"textbookKnowledgePointMappingIds":["{{f.MappingA:D}}"],"citations":[{"chunkId":"{{source.Id:D}}"}]}]}
            """;
        using var reader = User(f.Reader);
        GetRequiredService<IOptions<AiQuestionGenerationWorkerOptions>>().Value.MaxAttempts = 1;
        var generation = GetRequiredService<IAiQuestionGenerationAppService>();
        var task = await generation.CreateAsync(new CreateAiQuestionGenerationTaskDto
        {
            IdempotencyKey = Guid.NewGuid(), KnowledgeScope = "教材依据", Scope = KnowledgeSearchScope.SchoolShared,
            CurriculumScope = Both(f), Score = 2
        });
        await GetRequiredService<AiQuestionGenerationProcessor>().ProcessAsync(task.Id);
        task = await generation.GetAsync(task.Id);
        var candidate = task.Candidates.Single();
        candidate.HasValidatedCurriculumMapping.ShouldBeTrue();
        var questions = GetRequiredService<IQuestionWorkflowAppService>();
        var question = await questions.AdoptAiCandidateAsync(candidate.Id);
        question.Curriculum!.Mappings.Count.ShouldBe(2);
        question.CurriculumSuggestedMappingIds.ShouldBe(new[] { f.MappingA });
        question.CurriculumConfirmed.ShouldBeFalse();
        question.Sources.Single().CurriculumBindingIds.Count.ShouldBe(2);
        question.Sources.Single().TextbookKnowledgePointMappingIds.OrderBy(x => x).ShouldBe(new[] { f.MappingA, f.MappingB }.OrderBy(x => x));
        (await WithUnitOfWorkAsync(() => Repo<Question>().GetAsync(question.Id))).PersonalOwnerUserId.ShouldBe(f.Reader);
        await Should.ThrowAsync<BusinessException>(() => questions.SubmitAsync(question.Id));
        var confirmed = await GetRequiredService<IQuestionAppService>().ConfirmCurriculumAsync(question.Id, new ConfirmQuestionCurriculumDto
        {
            Scope = Both(f), ConcurrencyStamp = question.ConcurrencyStamp, Score = 2,
            PointWeights = [new() { TextbookKnowledgePointMappingId = f.MappingA, Weight = 1, Score = 2 }]
        });
        confirmed.CurriculumConfirmed.ShouldBeTrue();
        confirmed.CurriculumConfirmedByUserId.ShouldBe(f.Reader);
        question = await questions.SubmitAsync(question.Id);
        (await GetRequiredService<IQuestionAppService>().ApproveAsync(question.Id, Review(question))).Status.ShouldBe(QuestionStatus.Approved);
    }

    [Fact]
    public async Task Manual_Curriculum_Weights_Should_Not_Duplicate_Full_Marks_Or_Accept_Foreign_Mappings_Or_Stale_Confirmation()
    {
        var f = await FixtureAsync();
        using var reader = User(f.Reader);
        var questions = GetRequiredService<IQuestionAppService>();
        var question = await questions.CreateAsync(Manual(f));
        var invalid = Confirm(f, question);
        invalid.PointWeights = [new() { TextbookKnowledgePointMappingId = f.MappingA, Weight = 1, Score = 10 }, new() { TextbookKnowledgePointMappingId = f.MappingB, Weight = 1, Score = 10 }];
        await Should.ThrowAsync<BusinessException>(() => questions.ConfirmCurriculumAsync(question.Id, invalid));
        invalid = Confirm(f, question);
        invalid.PointWeights[0].Score = 10;
        await Should.ThrowAsync<BusinessException>(() => questions.ConfirmCurriculumAsync(question.Id, invalid));
        invalid = Confirm(f, question);
        invalid.PointWeights[0].TextbookKnowledgePointMappingId = Guid.NewGuid();
        await Should.ThrowAsync<BusinessException>(() => questions.ConfirmCurriculumAsync(question.Id, invalid));
        invalid = Confirm(f, question);
        invalid.ConcurrencyStamp = "stale";
        await Should.ThrowAsync<Volo.Abp.Data.AbpDbConcurrencyException>(() => questions.ConfirmCurriculumAsync(question.Id, invalid));
        var confirmed = await questions.ConfirmCurriculumAsync(question.Id, Confirm(f, question));
        confirmed.CurriculumPointWeights.Sum(x => x.Score).ShouldBe(10);
        confirmed.CurriculumPointWeights.Sum(x => x.Weight).ShouldBe(1);
        using (User(f.Owner)) await Should.ThrowAsync<AbpAuthorizationException>(() => questions.ConfirmCurriculumAsync(question.Id, Confirm(f, confirmed)));
    }

    [Fact]
    public async Task Actual_Content_And_Weight_Edits_Should_Invalidate_Confirmation_While_Unchanged_Save_Keeps_It()
    {
        var f = await FixtureAsync();
        using var reader = User(f.Reader);
        var questions = GetRequiredService<IQuestionAppService>();
        var question = await questions.CreateAsync(Manual(f));
        question = await questions.ConfirmCurriculumAsync(question.Id, Confirm(f, question));
        var unchanged = Draft(question);
        unchanged.CurriculumScope = Both(f);
        unchanged.CurriculumScore = question.CurriculumScore!.Value;
        unchanged.CurriculumPointWeights = question.CurriculumPointWeights;
        question = await GetRequiredService<IQuestionWorkflowAppService>().SaveDraftAsync(question.Id, unchanged);
        question.CurriculumConfirmed.ShouldBeTrue();
        unchanged.Analysis = "修改了解析";
        unchanged.ConcurrencyStamp = question.ConcurrencyStamp;
        question = await GetRequiredService<IQuestionWorkflowAppService>().SaveDraftAsync(question.Id, unchanged);
        question.CurriculumConfirmed.ShouldBeFalse();
        await Should.ThrowAsync<BusinessException>(() => questions.SubmitReviewAsync(question.Id));
        question = await questions.ConfirmCurriculumAsync(question.Id, Confirm(f, question));
        var changedWeights = Draft(question);
        changedWeights.CurriculumPointWeights = [new() { TextbookKnowledgePointMappingId = f.MappingA, Weight = 0.5m, Score = 5 }, new() { TextbookKnowledgePointMappingId = f.MappingB, Weight = 0.5m, Score = 5 }];
        question = await GetRequiredService<IQuestionWorkflowAppService>().SaveDraftAsync(question.Id, changedWeights);
        question.CurriculumConfirmed.ShouldBeFalse();
        question.CurriculumPointWeights.Select(x => x.Weight).ShouldBe(new[] { 0.5m, 0.5m });
    }

    [Fact]
    public async Task Saved_Paper_Should_Freeze_Weights_And_Curriculum_And_Allow_Only_Owner_History_After_Selection_Ends()
    {
        var f = await FixtureAsync();
        using var reader = User(f.Reader);
        var question = await ApprovedManualAsync(f);
        var exams = GetRequiredService<IExamAppService>();
        var input = Paper(f, question.Id);
        var wrongScore = Paper(f, question.Id);
        wrongScore.Rules.Single().ScorePerQuestion = 20;
        // Fixed curriculum papers always retain each question's confirmed marks rather than the legacy uniform rule score.
        (await exams.PreviewPaperAsync(wrongScore)).Questions.Single().Score.ShouldBe(10);
        var saved = await exams.SavePaperAsync(input);
        saved.Curriculum!.TextbookVersionId.ShouldBe(f.BookId);
        saved.Questions.Single().CurriculumPointWeights.Sum(x => x.Score).ShouldBe(saved.Questions.Single().Score);
        var frozen = await WithUnitOfWorkAsync(() => Repo<ExamPaperQuestion>().GetAsync(saved.Questions.Single().Id));
        var originalWeights = frozen.CurriculumPointWeightsJson;
        var originalScope = frozen.CurriculumSnapshotJson;
        await WithUnitOfWorkAsync(async () =>
        {
            var selection = await Repo<TeacherTextbookSelection>().GetAsync(f.ReaderSelection);
            selection.End(DateTime.UtcNow);
            await Repo<TeacherTextbookSelection>().UpdateManyAsync([selection], autoSave: true);
        });
        (await exams.GetSavedPaperAsync(saved.Id)).Questions.Single().Stem.ShouldBe(question.Stem);
        (await exams.SavePaperAsync(input)).Id.ShouldBe(saved.Id);
        input.OperationId = Guid.NewGuid();
        await Should.ThrowAsync<AbpAuthorizationException>(() => exams.SavePaperAsync(input));
        var history = await WithUnitOfWorkAsync(() => Repo<ExamPaperQuestion>().GetAsync(frozen.Id));
        history.CurriculumPointWeightsJson.ShouldBe(originalWeights);
        history.CurriculumSnapshotJson.ShouldBe(originalScope);
        using (User(f.Owner)) await Should.ThrowAsync<AbpAuthorizationException>(() => exams.GetSavedPaperAsync(saved.Id));
    }

    [Fact]
    public async Task Clearing_Visible_Sources_Should_Not_Remove_Original_Curriculum_Authorization_Before_New_Use()
    {
        var f = await FixtureAsync();
        var source = await SourceAsync(f.Owner, "原共享依据");
        Guid binding;
        Guid share;
        using (User(f.Owner))
        {
            binding = await BindAsync(source, f.MappingA);
            share = (await _sharing.ShareVersionAsync(Share(f, source))).Id;
        }
        using var reader = User(f.Reader);
        var input = Manual(f);
        input.Sources = [new() { KnowledgeItemId = source.KnowledgeItemId, KnowledgeVersionId = source.KnowledgeVersionId, ChunkId = source.Id,
            SchoolKnowledgeShareId = share, CurriculumBindingIds = [binding], TextbookKnowledgePointMappingIds = [f.MappingA] }];
        var questions = GetRequiredService<IQuestionAppService>();
        var question = await questions.CreateAsync(input);
        question = await questions.ConfirmCurriculumAsync(question.Id, Confirm(f, question));
        question = await questions.SubmitReviewAsync(question.Id);
        question = await questions.ApproveAsync(question.Id, Review(question));
        var saved = await GetRequiredService<IExamAppService>().SavePaperAsync(Paper(f, question.Id));
        var originalRefs = (await WithUnitOfWorkAsync(() => Repo<Question>().GetAsync(question.Id))).CurriculumSourceReferencesJson;
        var workflow = GetRequiredService<IQuestionWorkflowAppService>();
        // Saving a paper protected the question with its concurrency token. A stale editor must still fail.
        await Should.ThrowAsync<Volo.Abp.Data.AbpDbConcurrencyException>(() => workflow.SaveDraftAsync(question.Id, Draft(question)));
        question = await questions.GetAsync(question.Id);
        question = await workflow.SaveDraftAsync(question.Id, Draft(question)); // Sources defaults empty.
        question.Sources.Single().CurriculumBindingIds.ShouldBe(new[] { binding });
        (await WithUnitOfWorkAsync(() => Repo<Question>().GetAsync(question.Id))).CurriculumSourceReferencesJson.ShouldBe(originalRefs);
        using (User(f.Owner)) await EndBindingAsync(binding);
        await Should.ThrowAsync<AbpAuthorizationException>(() => questions.SubmitReviewAsync(question.Id));
        await Should.ThrowAsync<AbpAuthorizationException>(() => questions.ConfirmCurriculumAsync(question.Id, Confirm(f, question)));
        (await GetRequiredService<IExamAppService>().GetSavedPaperAsync(saved.Id)).Questions.Single().CurriculumSources.Single().CurriculumBindingIds.ShouldBe(new[] { binding });
    }

    [Fact]
    public async Task Curriculum_Paper_Should_Reject_Mixed_Legacy_Questions_And_Not_Publish_Through_Enterprise_Assignments()
    {
        var f = await FixtureAsync();
        using var reader = User(f.Reader);
        var question = await ApprovedManualAsync(f);
        var plain = Manual(f);
        plain.CurriculumScope = null;
        var legacy = await GetRequiredService<IQuestionAppService>().CreateAsync(plain);
        await GetRequiredService<IQuestionAppService>().SubmitReviewAsync(legacy.Id);
        await GetRequiredService<IQuestionAppService>().ApproveAsync(legacy.Id, new ReviewQuestionDto());
        (await WithUnitOfWorkAsync(() => Repo<Question>().GetAsync(legacy.Id))).PersonalOwnerUserId.ShouldBeNull();
        await Should.ThrowAsync<AbpAuthorizationException>(() => GetRequiredService<IQuestionAppService>().ConfirmCurriculumAsync(legacy.Id, Confirm(f, legacy)));
        var paper = Paper(f, question.Id);
        paper.FixedQuestionIds.Add(legacy.Id);
        await Should.ThrowAsync<BusinessException>(() => GetRequiredService<IExamAppService>().SavePaperAsync(paper));
        await Should.ThrowAsync<BusinessException>(() => GetRequiredService<IExamAppService>().CreatePlanAsync(new CreateExamPlanDto
        {
            Name = "不得借企业分配发布学校测评", StartTime = DateTime.UtcNow, EndTime = DateTime.UtcNow.AddHours(1), Paper = Paper(f, question.Id)
        }));
        (await WithUnitOfWorkAsync(() => Repo<ExamPlan>().GetCountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task Participant_View_Of_Frozen_Curriculum_Paper_Should_Not_Return_Private_Source_Or_Internal_Curriculum_Identifiers()
    {
        var f = await FixtureAsync();
        Guid planId;
        using (User(f.Reader))
        {
            var question = await ApprovedManualAsync(f);
            var saved = await GetRequiredService<IExamAppService>().SavePaperAsync(Paper(f, question.Id));
            planId = await WithUnitOfWorkAsync(async () =>
            {
                // Historical participant projection fixture; creating a new education task through this legacy plan path is blocked.
                var plan = new ExamPlan(Guid.NewGuid(), null, "既有冻结测评", saved.Id, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddHours(1),
                    30, 5, 1, ExamResultVisibility.ScoreOnly);
                await Repo<ExamPlan>().InsertAsync(plan, autoSave: true);
                await Repo<ExamAssignment>().InsertAsync(new ExamAssignment(Guid.NewGuid(), null, plan.Id, ExamAssignmentScopeType.User, f.Owner), autoSave: true);
                return plan.Id;
            });
        }
        using var participant = User(f.Owner);
        var attempt = await GetRequiredService<IExamAppService>().StartAttemptAsync(planId);
        attempt.Paper!.Curriculum.ShouldBeNull();
        var row = attempt.Paper.Questions.Single();
        row.Curriculum.ShouldBeNull();
        row.CurriculumPointWeights.ShouldBeEmpty();
        row.CurriculumSources.ShouldBeEmpty();
        row.CurriculumConfirmedByUserId.ShouldBeNull();
        row.OriginalQuestionId.ShouldBe(Guid.Empty);
        row.SourceSnapshot.ShouldBeNull();
        row.CorrectAnswers.ShouldBeEmpty();
    }

    private static EducationCurriculumScopeDto Both(Fixture f) => new()
    { TeacherTextbookSelectionId = f.ReaderSelection, TextbookKnowledgePointMappingIds = [f.MappingA, f.MappingB] };
    private static CreateQuestionDto Manual(Fixture f) => new()
    {
        OperationId = Guid.NewGuid(),
        Type = QuestionType.SingleChoice, Difficulty = QuestionDifficulty.Normal, Title = "教师手工教材题", Stem = "请选正确答案", Analysis = "原解析",
        CurriculumScope = Both(f), CurriculumScore = 10,
        Options = [new() { OptionKey = "A", Content = "甲", DisplayOrder = 1 }, new() { OptionKey = "B", Content = "乙", DisplayOrder = 2 }],
        Answers = [new() { Value = "A", DisplayOrder = 1 }], KnowledgePointNames = ["同名知识点"]
    };
    private static SaveQuestionDraftDto Draft(QuestionDto question) => new()
    {
        ScoringDefinition = question.ScoringDefinition,
        ConcurrencyStamp = question.ConcurrencyStamp,
        Type = question.Type, Difficulty = question.Difficulty, Title = question.Title, Stem = question.Stem, Analysis = question.Analysis,
        SecurityLevel = question.SecurityLevel, ApplicablePositionId = question.ApplicablePositionId, Tags = question.Tags,
        Options = question.Options.Select(x => new CreateUpdateQuestionOptionDto { OptionKey = x.OptionKey, Content = x.Content, DisplayOrder = x.DisplayOrder }).ToList(),
        Answers = question.Answers.Select(x => new CreateUpdateQuestionAnswerDto { Value = x.Value, DisplayOrder = x.DisplayOrder }).ToList(),
        KnowledgePointNames = question.KnowledgePoints.Select(x => x.Name).ToList()
    };
    private static ConfirmQuestionCurriculumDto Confirm(Fixture f, QuestionDto question) => new()
    {
        Scope = Both(f), ConcurrencyStamp = question.ConcurrencyStamp, Score = 10,
        PointWeights = [new() { TextbookKnowledgePointMappingId = f.MappingA, Weight = 0.6m, Score = 6 },
            new() { TextbookKnowledgePointMappingId = f.MappingB, Weight = 0.4m, Score = 4 }]
    };
    private async Task<QuestionDto> ApprovedManualAsync(Fixture f)
    {
        var service = GetRequiredService<IQuestionAppService>();
        var question = await service.CreateAsync(Manual(f));
        question = await service.ConfirmCurriculumAsync(question.Id, Confirm(f, question));
        question = await service.SubmitReviewAsync(question.Id);
        return await service.ApproveAsync(question.Id, Review(question));
    }
    private static GenerateExamPaperDto Paper(Fixture f, Guid questionId) => new()
    {
        OperationId = Guid.NewGuid(),
        Title = "教材试卷", BuildMode = ExamPaperBuildMode.Fixed, FixedQuestionIds = [questionId], CurriculumScope = Both(f),
        Rules = [new() { ScorePerQuestion = 10 }]
    };
}
