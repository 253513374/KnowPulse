using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using Shouldly;
using Volo.Abp;
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
        submitted.Score.ShouldNotBeNull();
        submitted.Score!.IsPassed.ShouldBeTrue();

        var duplicateSubmit = await _examAppService.SubmitAttemptAsync(attempt.Id, new SubmitExamAttemptDto
        {
            IdempotencyKey = "submit-flow-1"
        });
        duplicateSubmit.Status.ShouldBe(ExamAttemptStatus.Scored);

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

        var plan = await _examAppService.CreatePlanAsync(new CreateExamPlanDto
        {
            Name = "Scoring exam",
            StartTime = DateTime.UtcNow.AddMinutes(-5),
            EndTime = DateTime.UtcNow.AddHours(1),
            DurationMinutes = 30,
            PassingScore = 20,
            MaxAttempts = 1,
            Paper = new GenerateExamPaperDto
            {
                Title = "Scoring paper",
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
                new ExamAssignmentInputDto { ScopeType = ExamAssignmentScopeType.All }
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
        submitted.Score!.ObtainedScore.ShouldBe(20);
        submitted.Score.WrongQuestions.Count.ShouldBe(2);

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
        decimal passingScore = 0)
    {
        return await _examAppService.CreatePlanAsync(new CreateExamPlanDto
        {
            Name = $"Exam {point}",
            StartTime = DateTime.UtcNow.AddMinutes(-5),
            EndTime = DateTime.UtcNow.AddHours(1),
            DurationMinutes = 30,
            PassingScore = passingScore,
            MaxAttempts = 1,
            Paper = new GenerateExamPaperDto
            {
                Title = $"Paper {point}",
                Rules =
                {
                    Rule(point, type, count, 10)
                }
            },
            Assignments =
            {
                new ExamAssignmentInputDto { ScopeType = ExamAssignmentScopeType.All }
            }
        });
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
