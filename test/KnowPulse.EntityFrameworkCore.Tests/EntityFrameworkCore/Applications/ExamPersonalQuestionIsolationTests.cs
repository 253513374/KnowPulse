using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using KnowPulse.Exam;
using KnowPulse.KnowledgeBase;
using KnowPulse.QuestionBank;
using KnowPulse.Teaching;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.Users;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class ExamPersonalQuestionIsolationTests : KnowPulseEntityFrameworkCoreTestBase
{
    private static readonly Guid OwnerUserId = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");
    private static readonly Guid OtherUserId = Guid.Parse("6c9a52d8-4ab1-43ec-83dd-860551f5508f");

    private readonly IExamAppService _exams;
    private readonly IQuestionAppService _questions;
    private readonly IRepository<Question, Guid> _questionRepository;
    private readonly IRepository<QuestionSource, Guid> _sources;
    private readonly IRepository<KnowledgeItem, Guid> _items;
    private readonly IRepository<ExamScore, Guid> _scores;
    private readonly IRepository<ExamPaper, Guid> _papers;
    private readonly ICurrentPrincipalAccessor _principalAccessor;

    public ExamPersonalQuestionIsolationTests()
    {
        _exams = GetRequiredService<IExamAppService>();
        _questions = GetRequiredService<IQuestionAppService>();
        _questionRepository = GetRequiredService<IRepository<Question, Guid>>();
        _sources = GetRequiredService<IRepository<QuestionSource, Guid>>();
        _items = GetRequiredService<IRepository<KnowledgeItem, Guid>>();
        _scores = GetRequiredService<IRepository<ExamScore, Guid>>();
        _papers = GetRequiredService<IRepository<ExamPaper, Guid>>();
        _principalAccessor = GetRequiredService<ICurrentPrincipalAccessor>();
    }

    [Fact]
    public async Task Private_Questions_And_Whole_Papers_Should_Remain_Owner_Only_After_Source_References_Are_Removed()
    {
        var point = $"exam-private-{Guid.NewGuid():N}";
        var knowledge = await CreatePersonalKnowledgeAsync();
        var privateQuestion = await CreateApprovedQuestionAsync(point, "Private question", knowledge);
        var legacyQuestion = await CreateApprovedQuestionAsync(point, "Organization question");
        (await _questionRepository.GetAsync(privateQuestion.Id)).PersonalOwnerUserId.ShouldBe(OwnerUserId);
        var privatePlan = await CreatePlanAsync($"{point} private", privateQuestion.Id);
        var legacyPlan = await CreatePlanAsync($"{point} organization", legacyQuestion.Id);
        var mixedPlan = await CreatePlanAsync($"{point} mixed", privateQuestion.Id, legacyQuestion.Id);
        (await _papers.GetAsync(privatePlan.PaperId)).PersonalOwnerUserId.ShouldBe(OwnerUserId);
        (await _papers.GetAsync(mixedPlan.PaperId)).PersonalOwnerUserId.ShouldBe(OwnerUserId);
        (await _papers.GetAsync(legacyPlan.PaperId)).PersonalOwnerUserId.ShouldBeNull();
        privatePlan.Paper!.Questions.Single().SourceSnapshot.ShouldBe(knowledge.Item.Title);
        var attempt = await _exams.StartAttemptAsync(privatePlan.Id);
        await _exams.SubmitAttemptAsync(attempt.Id, new SubmitExamAttemptDto());

        foreach (var removeSources in new[] { false, true })
        {
            if (removeSources)
            {
                foreach (var source in await _sources.GetListAsync(x => x.QuestionId == privateQuestion.Id))
                {
                    await _sources.DeleteAsync(source, autoSave: true);
                }
            }

            using (ChangeUser(OtherUserId))
            {
                await Should.ThrowAsync<AbpAuthorizationException>(() => _exams.PreviewPaperAsync(FixedPaper(privateQuestion.Id)));
                await Should.ThrowAsync<AbpAuthorizationException>(() => CreatePlanAsync($"{point} forbidden", privateQuestion.Id));
                await Should.ThrowAsync<AbpAuthorizationException>(() => _exams.GetPlanAsync(privatePlan.Id));
                await Should.ThrowAsync<AbpAuthorizationException>(() => _exams.GetPlanAsync(mixedPlan.Id));
                await Should.ThrowAsync<AbpAuthorizationException>(() => _exams.GradeAttemptAsync(attempt.Id));

                var plans = await _exams.GetPlanListAsync(new GetExamPlanListInput { Keyword = point, MaxResultCount = 1 });
                plans.TotalCount.ShouldBe(0);
                plans.Items.ShouldBeEmpty();
                await Should.ThrowAsync<AbpAuthorizationException>(() =>
                    _exams.GetScoreListAsync(new GetExamScoreListInput { PlanId = privatePlan.Id }));
                var scores = await _exams.GetScoreListAsync(new GetExamScoreListInput());
                scores.TotalCount.ShouldBe(0);
                scores.Items.ShouldBeEmpty();

                var random = await _exams.PreviewPaperAsync(RulePaper(point));
                random.Questions.Single().OriginalQuestionId.ShouldBe(legacyQuestion.Id);
                var insufficient = await Should.ThrowAsync<BusinessException>(() => _exams.PreviewPaperAsync(RulePaper(point, count: 2)));
                insufficient.Code.ShouldBe(KnowPulseDomainErrorCodes.ExamPaperQuestionInsufficient);
            }
        }

        (await _exams.GetPlanAsync(privatePlan.Id)).Paper!.Questions.Single().SourceSnapshot.ShouldBe(knowledge.Item.Title);
        (await _exams.GetPlanAsync(mixedPlan.Id)).Paper!.Questions.Count.ShouldBe(2);
        (await _exams.PreviewPaperAsync(FixedPaper(privateQuestion.Id))).Questions.Single().OriginalQuestionId.ShouldBe(privateQuestion.Id);
        (await _exams.GradeAttemptAsync(attempt.Id)).WrongQuestions.Single().CorrectAnswers.ShouldBe(new[] { "true" });
        (await _exams.GetScoreListAsync(new GetExamScoreListInput { PlanId = privatePlan.Id })).TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Frozen_Personal_Paper_Should_Survive_Source_And_Original_Question_Deletion()
    {
        var point = $"exam-source-{Guid.NewGuid():N}";
        var knowledge = await CreatePersonalKnowledgeAsync();
        var question = await CreateApprovedQuestionAsync(point, "Private teaching snapshot", knowledge);
        var plan = await CreatePlanAsync(point, question.Id);
        ExamAttemptDto attempt;
        using (ChangeUser(OtherUserId))
        {
            attempt = await _exams.StartAttemptAsync(plan.Id);
            AssertSafeStudentSnapshot(attempt);
        }

        await _items.DeleteAsync(knowledge.Item.Id, autoSave: true);
        await Should.ThrowAsync<AbpAuthorizationException>(() => _exams.PreviewPaperAsync(FixedPaper(question.Id)));
        await Should.ThrowAsync<AbpAuthorizationException>(() => CreatePlanAsync($"{point} forbidden", question.Id));
        var unavailable = await Should.ThrowAsync<BusinessException>(() => _exams.PreviewPaperAsync(RulePaper(point)));
        unavailable.Code.ShouldBe(KnowPulseDomainErrorCodes.ExamPaperQuestionInsufficient);
        (await _exams.GetPlanAsync(plan.Id)).Paper!.Questions.Single().SourceSnapshot.ShouldBe(knowledge.Item.Title);

        foreach (var source in await _sources.GetListAsync(x => x.QuestionId == question.Id))
        {
            await _sources.DeleteAsync(source, autoSave: true);
        }
        await _questionRepository.DeleteAsync(question.Id, autoSave: true);
        (await _questionRepository.FindAsync(question.Id)).ShouldBeNull();
        var missingQuestion = await Should.ThrowAsync<BusinessException>(() => _exams.PreviewPaperAsync(FixedPaper(question.Id)));
        missingQuestion.Code.ShouldBe(KnowPulseDomainErrorCodes.ExamPaperQuestionInsufficient);

        using (ChangeUser(OtherUserId))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _exams.GetPlanAsync(plan.Id));
            (await _exams.GetPlanListAsync(new GetExamPlanListInput { Keyword = point })).Items.ShouldBeEmpty();
            var plans = await _exams.GetMyPlanListAsync(new GetExamPlanListInput { Keyword = point });
            plans.Items.Single().Paper!.Questions.ShouldBeEmpty();
            AssertSafeStudentSnapshot(await _exams.StartAttemptAsync(plan.Id));
            var saved = await _exams.SaveAnswersAsync(attempt.Id, new SaveExamAnswersDto
            {
                IdempotencyKey = $"save-{attempt.Id:N}",
                Answers =
                {
                    new SaveExamAnswerItemDto
                    {
                        PaperQuestionId = attempt.Paper!.Questions.Single().Id,
                        Values = { "true" },
                        ClientVersion = 1
                    }
                }
            });
            AssertSafeStudentSnapshot(saved);
            var submitted = await _exams.SubmitAttemptAsync(attempt.Id, new SubmitExamAttemptDto());
            submitted.Status.ShouldBe(ExamAttemptStatus.Scored);
            AssertSafeStudentSnapshot(submitted);
            AssertSafeStudentSnapshot(await _exams.GetAttemptAsync(attempt.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _exams.GradeAttemptAsync(attempt.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() =>
                _exams.GetScoreListAsync(new GetExamScoreListInput { PlanId = plan.Id }));
            (await _exams.GetScoreListAsync(new GetExamScoreListInput())).Items.ShouldBeEmpty();
        }

        (await _scores.GetListAsync(x => x.AttemptId == attempt.Id)).Single().ObtainedScore.ShouldBe(10);
        var historical = await _exams.GetPlanAsync(plan.Id);
        historical.Paper!.Questions.Single().SourceSnapshot.ShouldBe(knowledge.Item.Title);
        historical.Paper.Questions.Single().CorrectAnswers.ShouldBe(new[] { "true" });
        historical.Paper.Questions.Single().Analysis.ShouldBe(question.Analysis);
        (await _exams.GetPlanListAsync(new GetExamPlanListInput { Keyword = point })).Items.Single().Id.ShouldBe(plan.Id);
        (await _exams.GradeAttemptAsync(attempt.Id)).ObtainedScore.ShouldBe(10);
        (await _exams.GetScoreListAsync(new GetExamScoreListInput { PlanId = plan.Id })).Items.Single().ObtainedScore.ShouldBe(10);

        using (GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid()))
        using (GetRequiredService<IDataFilter>().Disable<IMultiTenant>())
        {
            // Even if the data filter is disabled by a caller, the frozen owner does not cross a tenant boundary.
            await Should.ThrowAsync<AbpAuthorizationException>(() => _exams.GetPlanAsync(plan.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _exams.GradeAttemptAsync(attempt.Id));
        }
    }

    [Fact]
    public async Task New_Paper_Should_Freeze_Personal_Owner_From_An_Unmarked_Questions_Validated_Source()
    {
        var point = $"exam-legacy-source-{Guid.NewGuid():N}";
        var question = await CreateApprovedQuestionAsync(point, "Legacy linked question");
        var knowledge = await CreatePersonalKnowledgeAsync();
        await _sources.InsertAsync(new QuestionSource(Guid.NewGuid(), null, question.Id, knowledge.Item.Id,
            knowledge.Version.Id, null, knowledge.Item.Title, "Restricted source excerpt", null, null), autoSave: true);
        (await _questionRepository.GetAsync(question.Id)).PersonalOwnerUserId.ShouldBeNull();
        var plan = await CreatePlanAsync(point, question.Id);
        (await _papers.GetAsync(plan.PaperId)).PersonalOwnerUserId.ShouldBe(OwnerUserId);

        using (ChangeUser(OtherUserId))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _exams.PreviewPaperAsync(FixedPaper(question.Id)));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _exams.GetPlanAsync(plan.Id));
            (await _exams.GetPlanListAsync(new GetExamPlanListInput { Keyword = point })).Items.ShouldBeEmpty();
            await Should.ThrowAsync<BusinessException>(() => _exams.PreviewPaperAsync(RulePaper(point)));
        }

        (await _exams.GetPlanAsync(plan.Id)).Paper!.Questions.Single().OriginalQuestionId.ShouldBe(question.Id);
        await _questionRepository.DeleteAsync(question.Id, autoSave: true);
        await _items.DeleteAsync(knowledge.Item.Id, autoSave: true);
        (await _exams.GetPlanAsync(plan.Id)).Paper!.Questions.Single().OriginalQuestionId.ShouldBe(question.Id);
        using (ChangeUser(OtherUserId))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _exams.GetPlanAsync(plan.Id));
        }
    }

    [Fact]
    public async Task Legacy_Paper_Without_Frozen_Owner_Should_Still_Require_Readable_Original_Questions()
    {
        var point = $"exam-legacy-paper-{Guid.NewGuid():N}";
        var question = await CreateApprovedQuestionAsync(point, "Legacy paper question");
        var plan = await CreatePlanAsync(point, question.Id);
        (await _papers.GetAsync(plan.PaperId)).PersonalOwnerUserId.ShouldBeNull();

        using (ChangeUser(OtherUserId))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _exams.GetPlanAsync(plan.Id));
        }

        var knowledge = await CreatePersonalKnowledgeAsync();
        await _sources.InsertAsync(new QuestionSource(Guid.NewGuid(), null, question.Id, knowledge.Item.Id,
            knowledge.Version.Id, null, knowledge.Item.Title, null, null, null), autoSave: true);
        using (ChangeUser(OtherUserId))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _exams.GetPlanAsync(plan.Id));
        }
        (await _exams.GetPlanAsync(plan.Id)).Id.ShouldBe(plan.Id);

        await _questionRepository.DeleteAsync(question.Id, autoSave: true);
        await Should.ThrowAsync<AbpAuthorizationException>(() => _exams.GetPlanAsync(plan.Id));
        (await _exams.GetPlanListAsync(new GetExamPlanListInput { Keyword = point })).Items.ShouldBeEmpty();
        using (ChangeUser(OtherUserId))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _exams.GetPlanAsync(plan.Id));
        }
    }

    private async Task<KnowledgeFixture> CreatePersonalKnowledgeAsync()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var accounts = GetRequiredService<IRepository<IdentityUser, Guid>>();
            foreach (var userId in new[] { OwnerUserId, OtherUserId })
                if (await accounts.FindAsync(userId) == null)
                    await accounts.InsertAsync(new IdentityUser(userId, $"exam-{userId:N}", $"{userId:N}@example.invalid"), autoSave: true);
        });
        var libraries = GetRequiredService<IRepository<KnowledgeLibrary, Guid>>();
        var library = await libraries.FindAsync(OwnerUserId);
        library ??= await libraries.InsertAsync(new KnowledgeLibrary(OwnerUserId, null), autoSave: true);
        var item = await _items.InsertAsync(new KnowledgeItem(Guid.NewGuid(), null, $"Private source {Guid.NewGuid():N}",
            libraryId: library.Id, ownerUserId: OwnerUserId), autoSave: true);
        var version = new KnowledgeVersion(Guid.NewGuid(), null, item.Id, 1, item.Title, "Private teaching material");
        version.Publish(DateTime.UtcNow);
        await GetRequiredService<IRepository<KnowledgeVersion, Guid>>().InsertAsync(version, autoSave: true);
        item.SetCurrentPublishedVersion(version.Id);
        await _items.UpdateAsync(item, autoSave: true);
        return new KnowledgeFixture(item, version);
    }

    private async Task<QuestionDto> CreateApprovedQuestionAsync(string point, string title, KnowledgeFixture? knowledge = null)
    {
        // 历史题没有新教师归属；个人来源的衍生归属仍须保留，不能用新建接口模拟旧组织题。
        var question = new Question(Guid.NewGuid(), null, QuestionType.TrueFalse, QuestionDifficulty.Normal,
            QuestionCreationSource.Manual, title, $"{title} stem");
        if (knowledge != null) question.RestrictToPersonalOwner(OwnerUserId);
        await WithUnitOfWorkAsync(async () =>
        {
            await _questionRepository.InsertAsync(question, autoSave: true);
            await GetRequiredService<IRepository<QuestionAnswer, Guid>>().InsertAsync(
                new QuestionAnswer(Guid.NewGuid(), null, question.Id, "true", 1), autoSave: true);
            await GetRequiredService<IRepository<QuestionAnalysis, Guid>>().InsertAsync(
                new QuestionAnalysis(Guid.NewGuid(), null, question.Id, $"{title} explanation"), autoSave: true);
            await GetRequiredService<IRepository<QuestionKnowledgePoint, Guid>>().InsertAsync(
                new QuestionKnowledgePoint(Guid.NewGuid(), null, question.Id, point), autoSave: true);
            await _sources.InsertAsync(new QuestionSource(Guid.NewGuid(), null, question.Id,
                knowledge?.Item.Id, knowledge?.Version.Id, null, knowledge?.Item.Title, null, null,
                knowledge == null ? "Organization source" : null), autoSave: true);
        });
        await _questions.SubmitReviewAsync(question.Id);
        return await _questions.ApproveAsync(question.Id, new ReviewQuestionDto { Comment = "Approved for exam isolation test" });
    }

    private async Task<ExamPlanDto> CreatePlanAsync(string name, params Guid[] questionIds)
    {
        var teacherId = GetRequiredService<ICurrentUser>().GetId();
        var studentIds = new[] { OwnerUserId, OtherUserId };
        await WithUnitOfWorkAsync(async () =>
        {
            var accounts = GetRequiredService<IRepository<IdentityUser, Guid>>();
            var relationships = GetRequiredService<IRepository<TeachingRelationship, Guid>>();
            foreach (var studentId in studentIds)
            {
                if (await accounts.FindAsync(studentId) == null)
                    await accounts.InsertAsync(new IdentityUser(studentId, $"exam-{studentId:N}",
                        $"{studentId:N}@example.invalid"), autoSave: true);
                if (await relationships.FindAsync(x => x.TeacherId == teacherId && x.StudentId == studentId) == null)
                {
                    var relationship = new TeachingRelationship(Guid.NewGuid(), null, teacherId, studentId, "试卷隔离测试学生");
                    relationship.Confirm();
                    await relationships.InsertAsync(relationship, autoSave: true);
                }
            }
        });
        return await _exams.CreatePlanAsync(new CreateExamPlanDto
        {
            Name = name,
            StartTime = DateTime.UtcNow.AddMinutes(-5),
            EndTime = DateTime.UtcNow.AddHours(1),
            DurationMinutes = 30,
            PassingScore = 5,
            ResultVisibility = ExamResultVisibility.FullReview,
            Paper = FixedPaper(questionIds),
            Assignments = studentIds.Select(id => new ExamAssignmentInputDto
                { ScopeType = ExamAssignmentScopeType.User, TargetId = id }).ToList()
        });
    }

    private static GenerateExamPaperDto FixedPaper(params Guid[] questionIds)
    {
        return new GenerateExamPaperDto
        {
            Title = "Isolation exam paper",
            BuildMode = ExamPaperBuildMode.Fixed,
            FixedQuestionIds = questionIds.ToList(),
            Rules = { new ExamPaperRuleDto { ScorePerQuestion = 10 } }
        };
    }

    private static GenerateExamPaperDto RulePaper(string point, int count = 1)
    {
        return new GenerateExamPaperDto
        {
            Title = "Rule-based isolation paper",
            BuildMode = ExamPaperBuildMode.Random,
            Rules = { new ExamPaperRuleDto { KnowledgePoint = point, Count = count, ScorePerQuestion = 10 } }
        };
    }

    private IDisposable ChangeUser(Guid userId)
    {
        return _principalAccessor.Change(new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(AbpClaimTypes.UserId, userId.ToString()),
            new Claim(AbpClaimTypes.UserName, $"test-{userId:N}"),
            new Claim(AbpClaimTypes.Role, "admin")
        ], "Test")));
    }

    private static void AssertSafeStudentSnapshot(ExamAttemptDto attempt)
    {
        attempt.Paper!.Questions.Single().Stem.ShouldBe("Private teaching snapshot stem");
        attempt.Paper.Questions.Single().SourceSnapshot.ShouldBeNull();
        attempt.Paper.Questions.Single().CorrectAnswers.ShouldBeEmpty();
        attempt.Paper.Questions.Single().Analysis.ShouldBeNull();
        attempt.Plan!.Paper!.Questions.ShouldBeEmpty();
        attempt.Plan.Assignments.ShouldBeEmpty();
        attempt.Score.ShouldBeNull();
    }

    private sealed record KnowledgeFixture(KnowledgeItem Item, KnowledgeVersion Version);
}
