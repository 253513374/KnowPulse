using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using KnowPulse.Teaching;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class Mvp2ExamAppServiceTests : KnowPulseEntityFrameworkCoreTestBase
{
    private static readonly Guid CurrentUserId = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");

    private readonly IExamAppService _examAppService;
    private readonly IQuestionAppService _questionAppService;
    private readonly IRepository<ExamScore, Guid> _scoreRepository;
    private readonly IRepository<WrongQuestionRecord, Guid> _wrongQuestionRepository;
    private readonly IRepository<ExamAttempt, Guid> _attemptRepository;

    public Mvp2ExamAppServiceTests()
    {
        _examAppService = GetRequiredService<IExamAppService>();
        _questionAppService = GetRequiredService<IQuestionAppService>();
        _scoreRepository = GetRequiredService<IRepository<ExamScore, Guid>>();
        _wrongQuestionRepository = GetRequiredService<IRepository<WrongQuestionRecord, Guid>>();
        _attemptRepository = GetRequiredService<IRepository<ExamAttempt, Guid>>();
    }

    [Fact]
    public async Task New_Papers_Should_Reject_Retired_Curriculum_Scopes()
    {
        var input = new GenerateExamPaperDto
        {
            OperationId = Guid.NewGuid(), Title = "旧课程试卷", BuildMode = ExamPaperBuildMode.Fixed,
            FixedQuestionIds = [Guid.NewGuid()],
            CurriculumScope = new EducationCurriculumScopeDto
            {
                TeacherTextbookSelectionId = Guid.NewGuid(), TextbookKnowledgePointMappingIds = [Guid.NewGuid()]
            }
        };
        (await Should.ThrowAsync<BusinessException>(() => _examAppService.PreviewPaperAsync(input)))
            .Code.ShouldBe("KnowPulse:Education:SchoolWorkflowRetired");
        (await Should.ThrowAsync<BusinessException>(() => _examAppService.SavePaperAsync(input)))
            .Code.ShouldBe("KnowPulse:Education:SchoolWorkflowRetired");
    }

    [Fact]
    public async Task Paper_Generation_Should_Filter_Status_And_Preserve_Question_Snapshot()
    {
        var point = UniquePoint();
        var approved = await CreateApprovedQuestionAsync("snapshot approved", QuestionType.SingleChoice, "A", point);
        await CreateDraftQuestionAsync("snapshot draft", QuestionType.SingleChoice, "A", point);

        await Should.ThrowAsync<BusinessException>(() => _examAppService.PreviewPaperAsync(new GenerateExamPaperDto
        {
            Title = "Insufficient snapshot paper",
            Rules =
            {
                new ExamPaperRuleDto
                {
                    Type = QuestionType.SingleChoice,
                    Difficulty = QuestionDifficulty.Normal,
                    KnowledgePoint = point,
                    Count = 2,
                    ScorePerQuestion = 10
                }
            }
        }));

        var plan = await CreatePlanAsync(point, QuestionType.SingleChoice, count: 1);
        var snapshot = plan.Paper!.Questions.Single();
        snapshot.OriginalQuestionId.ShouldBe(approved.Id);
        snapshot.Stem.ShouldContain("snapshot approved stem");

        await _questionAppService.UpdateAsync(approved.Id, BuildQuestionInput(
            "snapshot approved updated",
            QuestionType.SingleChoice,
            "B",
            point,
            options: ChoiceOptions()));

        var reloaded = await _examAppService.GetPlanAsync(plan.Id);
        reloaded.Paper!.Questions.Single().Stem.ShouldContain("snapshot approved stem");
        reloaded.Paper!.Questions.Single().Stem.ShouldNotContain("updated");
    }

    [Fact]
    public async Task Fixed_Paper_Generation_Should_Reject_Not_Approved_Questions()
    {
        var point = UniquePoint();

        var draft = await CreateDraftQuestionAsync("fixed draft", QuestionType.SingleChoice, "A", point);

        var pending = await CreateDraftQuestionAsync("fixed pending", QuestionType.SingleChoice, "A", point);
        pending = await _questionAppService.SubmitReviewAsync(pending.Id);

        var rejected = await CreateDraftQuestionAsync("fixed rejected", QuestionType.SingleChoice, "A", point);
        await _questionAppService.SubmitReviewAsync(rejected.Id);
        rejected = await _questionAppService.RejectAsync(rejected.Id, new ReviewQuestionDto { Comment = "reject fixed" });

        var disabled = await CreateApprovedQuestionAsync("fixed disabled", QuestionType.SingleChoice, "A", point);
        disabled = await _questionAppService.DisableAsync(disabled.Id);

        var archived = await CreateApprovedQuestionAsync("fixed archived", QuestionType.SingleChoice, "A", point);
        archived = await _questionAppService.ArchiveAsync(archived.Id);

        foreach (var question in new[] { draft, pending, rejected, disabled, archived })
        {
            var exception = await Should.ThrowAsync<BusinessException>(
                () => _examAppService.PreviewPaperAsync(FixedPaper(question.Id)));

            exception.Code.ShouldBe(KnowPulseDomainErrorCodes.ExamPaperQuestionInsufficient);
        }
    }

    [Fact]
    public async Task Attempt_Should_Start_Save_Submit_And_Keep_Submit_Idempotent()
    {
        var point = UniquePoint();
        await CreateApprovedQuestionAsync("flow single", QuestionType.SingleChoice, "A", point);
        var plan = await CreatePlanAsync(point, QuestionType.SingleChoice, count: 1, passingScore: 10);

        var attempt = await _examAppService.StartAttemptAsync(plan.Id);
        attempt.Status.ShouldBe(ExamAttemptStatus.InProgress);
        attempt = await _examAppService.StartAttemptAsync(plan.Id);
        attempt.AttemptNumber.ShouldBe(1);

        var questionId = attempt.Paper!.Questions.Single().Id;
        var saved = await _examAppService.SaveAnswersAsync(attempt.Id, new SaveExamAnswersDto
        {
            IdempotencyKey = "save-flow-1",
            Answers =
            {
                new SaveExamAnswerItemDto
                {
                    PaperQuestionId = questionId,
                    Values = { "A" },
                    ClientVersion = 1
                }
            }
        });
        saved.Answers.Single().SaveVersion.ShouldBe(1);

        var duplicateSave = await _examAppService.SaveAnswersAsync(attempt.Id, new SaveExamAnswersDto
        {
            IdempotencyKey = "save-flow-1",
            Answers =
            {
                new SaveExamAnswerItemDto
                {
                    PaperQuestionId = questionId,
                    Values = { "B" },
                    ClientVersion = 2
                }
            }
        });
        duplicateSave.Answers.Single().Values.Single().ShouldBe("A");

        var submitted = await _examAppService.SubmitAttemptAsync(attempt.Id, new SubmitExamAttemptDto
        {
            IdempotencyKey = "submit-flow-1"
        });
        submitted.Status.ShouldBe(ExamAttemptStatus.Scored);
        submitted.Score.ShouldBeNull();
        (await _examAppService.GradeAttemptAsync(attempt.Id)).IsPassed.ShouldBeTrue();

        var duplicateSubmit = await _examAppService.SubmitAttemptAsync(attempt.Id, new SubmitExamAttemptDto
        {
            IdempotencyKey = "submit-flow-1"
        });
        duplicateSubmit.Status.ShouldBe(ExamAttemptStatus.Scored);
        duplicateSubmit.Score.ShouldBeNull();

        await Should.ThrowAsync<BusinessException>(() => _examAppService.SaveAnswersAsync(attempt.Id, new SaveExamAnswersDto
        {
            IdempotencyKey = "save-after-submit",
            Answers =
            {
                new SaveExamAnswerItemDto
                {
                    PaperQuestionId = questionId,
                    Values = { "B" },
                    ClientVersion = 3
                }
            }
        }));

        var scoreCount = await WithUnitOfWorkAsync(async () =>
        {
            var scores = await _scoreRepository.GetQueryableAsync();
            return scores.Count(x => x.AttemptId == attempt.Id);
        });
        scoreCount.ShouldBe(1);
    }

    [Fact]
    public async Task Scoring_Should_Record_Wrong_Questions_And_Remain_Idempotent()
    {
        var point = UniquePoint();
        await CreateApprovedQuestionAsync("score single", QuestionType.SingleChoice, "A", point);
        await CreateApprovedQuestionAsync("score multi", QuestionType.MultipleChoice, "A,B", point);
        await CreateApprovedQuestionAsync("score truefalse", QuestionType.TrueFalse, "true", point);
        await CreateApprovedQuestionAsync("score fill", QuestionType.FillBlank, "制度", point);

        await EnsureRecipientAsync();
        var plan = await _examAppService.CreatePlanAsync(new CreateExamPlanDto
        {
            Name = "Scoring exam",
            StartTime = DateTime.UtcNow.AddMinutes(-5),
            EndTime = DateTime.UtcNow.AddHours(1),
            DurationMinutes = 30,
            PassingScore = 20,
            MaxAttempts = 1,
            ResultVisibility = ExamResultVisibility.FullReview,
            Paper = new GenerateExamPaperDto
            {
                Title = "Scoring paper",
                BuildMode = ExamPaperBuildMode.Random,
                Rules =
                {
                    Rule(point, QuestionType.SingleChoice, 1, 10),
                    Rule(point, QuestionType.MultipleChoice, 1, 10),
                    Rule(point, QuestionType.TrueFalse, 1, 10),
                    Rule(point, QuestionType.FillBlank, 1, 10)
                }
            },
            Assignments =
            {
                new ExamAssignmentInputDto { ScopeType = ExamAssignmentScopeType.User, TargetId = CurrentUserId }
            }
        });

        var attempt = await _examAppService.StartAttemptAsync(plan.Id);
        var questions = attempt.Paper!.Questions.ToDictionary(x => x.Type);

        await _examAppService.SaveAnswersAsync(attempt.Id, new SaveExamAnswersDto
        {
            IdempotencyKey = "score-save-1",
            Answers =
            {
                Answer(questions[QuestionType.SingleChoice].Id, "A", 1),
                Answer(questions[QuestionType.MultipleChoice].Id, "A", 1),
                Answer(questions[QuestionType.TrueFalse].Id, "false", 1),
                Answer(questions[QuestionType.FillBlank].Id, "制度", 1)
            }
        });

        var submitted = await _examAppService.SubmitAttemptAsync(attempt.Id, new SubmitExamAttemptDto
        {
            IdempotencyKey = "score-submit-1"
        });
        submitted.Status.ShouldBe(ExamAttemptStatus.Scored);
        submitted.Score.ShouldBeNull();

        var gradedAgain = await _examAppService.GradeAttemptAsync(attempt.Id);
        gradedAgain.ObtainedScore.ShouldBe(20);
        gradedAgain.WrongQuestions.Count.ShouldBe(2);

        var wrongCount = await WithUnitOfWorkAsync(async () =>
        {
            var wrongs = await _wrongQuestionRepository.GetQueryableAsync();
            return wrongs.Count(x => x.AttemptId == attempt.Id);
        });
        wrongCount.ShouldBe(2);
    }

    [Theory]
    [InlineData(ExamResultVisibility.Hidden)]
    [InlineData(ExamResultVisibility.ScoreOnly)]
    [InlineData(ExamResultVisibility.FullReview)]
    public async Task Participant_Responses_Should_Hide_Results_During_Exam_Even_For_Administrator(ExamResultVisibility visibility)
    {
        var point = UniquePoint();
        var question = await CreateApprovedQuestionAsync("private review", QuestionType.SingleChoice, "A", point);
        var plan = await CreatePlanAsync(point, QuestionType.SingleChoice, 1, resultVisibility: visibility);

        AssertManagementPaper(plan.Paper!);
        AssertManagementPaper(await _examAppService.PreviewPaperAsync(FixedPaper(question.Id)));
        AssertManagementPaper((await _examAppService.GetPlanAsync(plan.Id)).Paper!);
        var managed = await _examAppService.GetPlanListAsync(new GetExamPlanListInput { Keyword = point });
        AssertManagementPaper(managed.Items.Single().Paper!);
        managed.Items.Single().Assignments.ShouldNotBeEmpty();

        var mine = await _examAppService.GetMyPlanListAsync(new GetExamPlanListInput { Keyword = point });
        AssertParticipantPlanSummary(mine.Items.Single());

        var started = await _examAppService.StartAttemptAsync(plan.Id);
        AssertParticipantAttempt(started);
        var resumed = await _examAppService.StartAttemptAsync(plan.Id);
        resumed.Id.ShouldBe(started.Id);
        AssertParticipantAttempt(resumed);
        AssertParticipantAttempt(await _examAppService.GetAttemptAsync(started.Id));

        var save = new SaveExamAnswersDto
        {
            IdempotencyKey = $"private-save-{point}",
            Answers = { Answer(started.Paper!.Questions.Single().Id, "B", 1) }
        };
        var saved = await _examAppService.SaveAnswersAsync(started.Id, save);
        AssertParticipantAttempt(saved);
        saved.Answers.Single().Values.ShouldBe(new[] { "B" });
        AssertParticipantAttempt(await _examAppService.SaveAnswersAsync(started.Id, save));

        var submit = new SubmitExamAttemptDto { IdempotencyKey = $"private-submit-{point}" };
        var submitted = await _examAppService.SubmitAttemptAsync(started.Id, submit);
        submitted.Status.ShouldBe(ExamAttemptStatus.Scored);
        AssertParticipantAttempt(submitted);
        AssertParticipantAttempt(await _examAppService.SubmitAttemptAsync(started.Id, submit));
        AssertParticipantAttempt(await _examAppService.GetAttemptAsync(started.Id));

        var graded = await _examAppService.GradeAttemptAsync(started.Id);
        graded.ObtainedScore.ShouldBe(0);
        graded.WrongQuestions.Single().CorrectAnswers.ShouldBe(new[] { "A" });
        graded.WrongQuestions.Single().AnalysisSnapshot.ShouldBe("private review analysis");
        var managedScores = await _examAppService.GetScoreListAsync(new GetExamScoreListInput { PlanId = plan.Id });
        managedScores.Items.Single().WrongQuestions.Single().CorrectAnswers.ShouldBe(new[] { "A" });
    }

    [Theory]
    [InlineData(ExamResultVisibility.Hidden, false, false)]
    [InlineData(ExamResultVisibility.ScoreOnly, true, false)]
    [InlineData(ExamResultVisibility.FullReview, true, true)]
    public async Task Ended_Exam_Should_Apply_Result_Visibility_Without_Exposing_Private_Sources(
        ExamResultVisibility visibility,
        bool canViewScore,
        bool canReview)
    {
        var point = UniquePoint();
        await CreateApprovedQuestionAsync("private review", QuestionType.SingleChoice, "A", point);
        var plan = await CreatePlanAsync(point, QuestionType.SingleChoice, 1,
            resultVisibility: visibility, endTime: DateTime.UtcNow.AddHours(-1));
        var attempt = await CreateHistoricalAttemptAsync(plan, CurrentUserId, submitted: true);
        var graded = await _examAppService.GradeAttemptAsync(attempt.Id);
        graded.WrongQuestions.Single().CorrectAnswers.ShouldBe(new[] { "A" });

        var result = await _examAppService.GetAttemptAsync(attempt.Id);
        AssertParticipantAttempt(result, canViewScore, canReview);
        if (canViewScore)
        {
            result.Score!.ObtainedScore.ShouldBe(graded.ObtainedScore);
            result.Score.TotalScore.ShouldBe(graded.TotalScore);
        }

        AssertParticipantAttempt(await _examAppService.SubmitAttemptAsync(attempt.Id, new SubmitExamAttemptDto()),
            canViewScore, canReview);
        var mine = await _examAppService.GetMyPlanListAsync(new GetExamPlanListInput { Keyword = point });
        AssertParticipantPlanSummary(mine.Items.Single());
        AssertManagementPaper((await _examAppService.GetPlanAsync(plan.Id)).Paper!);
    }

    [Fact]
    public async Task Ended_Exam_Should_Not_Expose_Review_Of_Unscored_Attempt()
    {
        var point = UniquePoint();
        await CreateApprovedQuestionAsync("private review", QuestionType.SingleChoice, "A", point);
        var plan = await CreatePlanAsync(point, QuestionType.SingleChoice, 1,
            resultVisibility: ExamResultVisibility.FullReview, endTime: DateTime.UtcNow.AddHours(-1));
        var attempt = await CreateHistoricalAttemptAsync(plan, CurrentUserId, submitted: true);

        AssertParticipantAttempt(await _examAppService.GetAttemptAsync(attempt.Id));
    }

    [Fact]
    public async Task Participant_Endpoints_Should_Reject_Another_Users_Attempt_Even_For_Administrator()
    {
        var point = UniquePoint();
        await CreateApprovedQuestionAsync("private review", QuestionType.SingleChoice, "A", point);
        var plan = await CreatePlanAsync(point, QuestionType.SingleChoice, 1);
        var attempt = await CreateHistoricalAttemptAsync(plan, Guid.NewGuid(), submitted: false);

        await Should.ThrowAsync<AbpAuthorizationException>(() => _examAppService.GetAttemptAsync(attempt.Id));
        await Should.ThrowAsync<AbpAuthorizationException>(() => _examAppService.SaveAnswersAsync(attempt.Id, new SaveExamAnswersDto()));
        await Should.ThrowAsync<AbpAuthorizationException>(() => _examAppService.SubmitAttemptAsync(attempt.Id, new SubmitExamAttemptDto()));
    }

    [Fact]
    public async Task Expired_Attempt_Should_Reject_Save_And_Submit()
    {
        var point = UniquePoint();
        await CreateApprovedQuestionAsync("expired single", QuestionType.SingleChoice, "A", point);
        var plan = await CreatePlanAsync(point, QuestionType.SingleChoice, count: 1);
        var paperQuestionId = plan.Paper!.Questions.Single().Id;

        var expiredAttempt = await _attemptRepository.InsertAsync(
            new ExamAttempt(
                Guid.NewGuid(),
                null,
                plan.Id,
                plan.PaperId,
                CurrentUserId,
                1,
                DateTime.UtcNow.AddMinutes(-10),
                DateTime.UtcNow.AddMinutes(-1)),
            autoSave: true);

        await Should.ThrowAsync<BusinessException>(() => _examAppService.SaveAnswersAsync(expiredAttempt.Id, new SaveExamAnswersDto
        {
            IdempotencyKey = "expired-save",
            Answers =
            {
                new SaveExamAnswerItemDto
                {
                    PaperQuestionId = paperQuestionId,
                    Values = { "A" },
                    ClientVersion = 1
                }
            }
        }));

        await Should.ThrowAsync<BusinessException>(() => _examAppService.SubmitAttemptAsync(expiredAttempt.Id, new SubmitExamAttemptDto
        {
            IdempotencyKey = "expired-submit"
        }));
    }

    private async Task<ExamPlanDto> CreatePlanAsync(
        string point,
        QuestionType type,
        int count,
        decimal passingScore = 0,
        ExamResultVisibility resultVisibility = ExamResultVisibility.ScoreOnly,
        DateTime? endTime = null)
    {
        await EnsureRecipientAsync();

        var now = DateTime.UtcNow;
        var end = endTime ?? now.AddHours(1);
        return await _examAppService.CreatePlanAsync(new CreateExamPlanDto
        {
            Name = $"Exam {point}",
            StartTime = end < now ? end.AddHours(-1) : now.AddMinutes(-5),
            EndTime = end,
            DurationMinutes = 30,
            PassingScore = passingScore,
            MaxAttempts = 1,
            ResultVisibility = resultVisibility,
            Paper = new GenerateExamPaperDto
            {
                Title = $"Paper {point}",
                BuildMode = ExamPaperBuildMode.Random,
                Rules =
                {
                    Rule(point, type, count, 10)
                }
            },
            Assignments =
            {
                new ExamAssignmentInputDto { ScopeType = ExamAssignmentScopeType.User, TargetId = CurrentUserId }
            }
        });
    }

    private Task EnsureRecipientAsync() => WithUnitOfWorkAsync(async () =>
    {
        var repository = GetRequiredService<IRepository<TeachingRelationship, Guid>>();
        if (await repository.AnyAsync(x => x.TeacherId == CurrentUserId && x.StudentId == CurrentUserId)) return;
        var relationship = new TeachingRelationship(Guid.NewGuid(), null, CurrentUserId, CurrentUserId, "评分测试学生");
        relationship.Confirm();
        await repository.InsertAsync(relationship, autoSave: true);
    });

    private async Task<ExamAttempt> CreateHistoricalAttemptAsync(ExamPlanDto plan, Guid userId, bool submitted)
    {
        plan.StartTime.ShouldNotBeNull();
        var startTime = plan.StartTime.Value;
        var attempt = new ExamAttempt(Guid.NewGuid(), null, plan.Id, plan.PaperId, userId, 1,
            startTime, plan.EndTime);
        if (submitted)
        {
            attempt.Submit(startTime.AddMinutes(1), $"historical-{attempt.Id:N}");
        }

        return await _attemptRepository.InsertAsync(attempt, autoSave: true);
    }

    private static void AssertManagementPaper(ExamPaperDto paper)
    {
        var question = paper.Questions.Single();
        question.CorrectAnswers.ShouldBe(new[] { "A" });
        question.Analysis.ShouldBe("private review analysis");
        question.SourceSnapshot.ShouldBe("private review source");
    }

    private static void AssertParticipantPlanSummary(ExamPlanDto plan)
    {
        plan.Assignments.ShouldBeEmpty();
        plan.Paper.ShouldNotBeNull();
        plan.Paper!.QuestionCount.ShouldBe(1);
        plan.Paper.TotalScore.ShouldBe(10);
        plan.Paper.Questions.ShouldBeEmpty();
    }

    private static void AssertParticipantAttempt(ExamAttemptDto attempt, bool canViewScore = false, bool canReview = false)
    {
        attempt.Plan.ShouldNotBeNull();
        AssertParticipantPlanSummary(attempt.Plan!);
        attempt.Paper.ShouldNotBeNull();
        var question = attempt.Paper!.Questions.Single();
        question.Stem.ShouldBe("private review stem");
        question.Options.Count.ShouldBe(3);
        question.SourceSnapshot.ShouldBeNull();
        if (canReview)
        {
            question.CorrectAnswers.ShouldBe(new[] { "A" });
            question.Analysis.ShouldBe("private review analysis");
        }
        else
        {
            question.CorrectAnswers.ShouldBeEmpty();
            question.Analysis.ShouldBeNull();
        }

        if (!canViewScore)
        {
            attempt.Score.ShouldBeNull();
            return;
        }

        attempt.Score.ShouldNotBeNull();
        if (canReview)
        {
            attempt.Score!.WrongQuestions.Single().CorrectAnswers.ShouldBe(new[] { "A" });
            attempt.Score.WrongQuestions.Single().AnalysisSnapshot.ShouldBe("private review analysis");
        }
        else
        {
            attempt.Score!.WrongQuestions.ShouldBeEmpty();
        }
    }

    private static GenerateExamPaperDto FixedPaper(Guid questionId)
    {
        return new GenerateExamPaperDto
        {
            Title = $"Fixed paper {questionId:N}",
            BuildMode = ExamPaperBuildMode.Fixed,
            FixedQuestionIds = { questionId },
            Rules =
            {
                new ExamPaperRuleDto
                {
                    ScorePerQuestion = 10,
                    MultipleChoiceScoringMode = MultipleChoiceScoringMode.AllOrNothing
                }
            }
        };
    }

    private static ExamPaperRuleDto Rule(string point, QuestionType type, int count, decimal score)
    {
        return new ExamPaperRuleDto
        {
            Type = type,
            Difficulty = QuestionDifficulty.Normal,
            KnowledgePoint = point,
            Count = count,
            ScorePerQuestion = score
        };
    }

    private static SaveExamAnswerItemDto Answer(Guid paperQuestionId, string values, int version)
    {
        return new SaveExamAnswerItemDto
        {
            PaperQuestionId = paperQuestionId,
            ClientVersion = version,
            Values = values.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
        };
    }

    private async Task<QuestionDto> CreateApprovedQuestionAsync(
        string title,
        QuestionType type,
        string answers,
        string point)
    {
        var question = await _questionAppService.CreateAsync(BuildQuestionInput(title, type, answers, point, ChoiceOptions(type)));
        await _questionAppService.SubmitReviewAsync(question.Id);
        return await _questionAppService.ApproveAsync(question.Id, new ReviewQuestionDto { Comment = "test approved" });
    }

    private async Task<QuestionDto> CreateDraftQuestionAsync(
        string title,
        QuestionType type,
        string answers,
        string point)
    {
        return await _questionAppService.CreateAsync(BuildQuestionInput(title, type, answers, point, ChoiceOptions(type)));
    }

    private static UpdateQuestionDto BuildQuestionInput(
        string title,
        QuestionType type,
        string answers,
        string point,
        CreateUpdateQuestionOptionDto[] options)
    {
        return new UpdateQuestionDto
        {
            Type = type,
            Difficulty = QuestionDifficulty.Normal,
            CreationSource = QuestionCreationSource.Manual,
            Title = title,
            Stem = $"{title} stem",
            SecurityLevel = 1,
            Tags = "mvp2, exam",
            Analysis = $"{title} analysis",
            Options = options.ToList(),
            Answers = answers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select((x, index) => new CreateUpdateQuestionAnswerDto { Value = x, DisplayOrder = index + 1 })
                .ToList(),
            KnowledgePointNames = { point },
            Sources =
            {
                new CreateUpdateQuestionSourceDto { ManualSourceDescription = $"{title} source" }
            }
        };
    }

    private static CreateUpdateQuestionOptionDto[] ChoiceOptions(QuestionType type = QuestionType.SingleChoice)
    {
        if (type is QuestionType.SingleChoice or QuestionType.MultipleChoice)
        {
            return new[]
            {
                new CreateUpdateQuestionOptionDto { OptionKey = "A", Content = "A option", DisplayOrder = 1 },
                new CreateUpdateQuestionOptionDto { OptionKey = "B", Content = "B option", DisplayOrder = 2 },
                new CreateUpdateQuestionOptionDto { OptionKey = "C", Content = "C option", DisplayOrder = 3 }
            };
        }

        return Array.Empty<CreateUpdateQuestionOptionDto>();
    }

    private static string UniquePoint()
    {
        return $"exam-point-{Guid.NewGuid():N}";
    }
}
