using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using KnowPulse.Teaching;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Security.Claims;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class TeachingExamAppServiceTests : KnowPulseEntityFrameworkCoreTestBase
{
    private readonly IExamAppService _exams;
    private readonly ITeachingAppService _teaching;
    private readonly ICurrentPrincipalAccessor _principal;
    private readonly Guid _teacher = Guid.NewGuid();
    private readonly Guid _otherTeacher = Guid.NewGuid();
    private readonly Guid _student = Guid.NewGuid();

    public TeachingExamAppServiceTests()
    {
        _exams = GetRequiredService<IExamAppService>();
        _teaching = GetRequiredService<ITeachingAppService>();
        _principal = GetRequiredService<ICurrentPrincipalAccessor>();
    }

    [Fact]
    public async Task Should_Keep_Multiple_Teachers_And_Idempotent_Applications_Separate()
    {
        TeacherInvitationDto first;
        TeacherInvitationDto second;
        using (As(_teacher))
        {
            first = await _teaching.GetInvitationAsync();
            (await _teaching.GetInvitationAsync()).InvitationCode.ShouldBe(first.InvitationCode);
        }
        using (As(_otherTeacher)) second = await _teaching.GetInvitationAsync();
        TeachingStudentDto relation;
        using (As(_student, "student"))
        {
            (await _teaching.LookupInvitationAsync(first.InvitationCode)).TeacherId.ShouldBe(_teacher);
            relation = await _teaching.ApplyAsync(new() { InvitationCode = first.InvitationCode, DisplayName = "小雨" });
            (await _teaching.ApplyAsync(new() { InvitationCode = first.InvitationCode, DisplayName = "小雨" })).Id.ShouldBe(relation.Id);
            await _teaching.ApplyAsync(new() { InvitationCode = second.InvitationCode, DisplayName = "小雨" });
            (await _teaching.GetMyTeachersAsync()).Items.Count.ShouldBe(2);
        }
        using (As(_otherTeacher))
            await Should.ThrowAsync<AbpAuthorizationException>(() => _teaching.ConfirmAsync(relation.Id));
        using (As(_teacher))
        {
            (await _teaching.ConfirmAsync(relation.Id)).Status.ShouldBe(TeachingRelationshipStatus.Active);
            (await _teaching.ConfirmAsync(relation.Id)).Id.ShouldBe(relation.Id);
            (await _teaching.GetStudentsAsync(new() { Status = TeachingRelationshipStatus.Active })).TotalCount.ShouldBe(1);
            (await _teaching.RemoveAsync(relation.Id)).Status.ShouldBe(TeachingRelationshipStatus.Removed);
        }
        using (As(_student, "student"))
            (await _teaching.ApplyAsync(new() { InvitationCode = first.InvitationCode, DisplayName = "小雨" })).Status
                .ShouldBe(TeachingRelationshipStatus.Pending);
    }

    [Fact]
    public async Task Should_Reject_Broad_Recipients_And_Isolate_Teacher_Content_And_Results()
    {
        var relation = await ConnectAsync(_teacher, _student);
        var question = await SeedQuestionAsync(_teacher);
        var foreign = await SeedQuestionAsync(_otherTeacher);
        ExamPlanDto plan;
        using (As(_teacher))
        {
            await Should.ThrowAsync<UserFriendlyException>(() => _exams.CreatePlanAsync(new() { Name = "空名单", Paper = Paper(question) }));
            await Should.ThrowAsync<UserFriendlyException>(() => _exams.CreatePlanAsync(new()
            {
                Name = "禁止全员", Paper = Paper(question), Assignments = { new() { ScopeType = ExamAssignmentScopeType.All } }
            }));
            await Should.ThrowAsync<UserFriendlyException>(() => _exams.CreatePlanAsync(new()
            {
                Name = "陌生学生", Paper = Paper(question), StudentIds = { Guid.NewGuid() }
            }));
            await Should.ThrowAsync<BusinessException>(() => _exams.PreviewPaperAsync(Paper(foreign)));
            plan = await _exams.CreatePlanAsync(new() { Name = "私有定向任务", Paper = Paper(question), StudentIds = { _student } });
            plan.StartTime.ShouldBeNull();
            plan.EndTime.ShouldBeNull();
            plan.DurationMinutes.ShouldBeNull();
            await _teaching.RemoveAsync(relation.Id);
            await Should.ThrowAsync<UserFriendlyException>(() => _exams.CreatePlanAsync(new()
            {
                Name = "关系已失效的新任务", Paper = Paper(question), StudentIds = { _student }
            }));
            (await _exams.GetPlanListAsync(new())).TotalCount.ShouldBe(1);
        }
        var lateStudent = Guid.NewGuid();
        await ConnectAsync(_teacher, lateStudent);
        using (As(lateStudent, "student"))
        {
            (await _exams.GetMyPlanListAsync(new())).TotalCount.ShouldBe(0);
            await Should.ThrowAsync<BusinessException>(() => _exams.GetMyPlanAsync(plan.Id));
            await Should.ThrowAsync<BusinessException>(() => _exams.StartAttemptAsync(plan.Id));
        }
        using (As(_otherTeacher))
        {
            (await _exams.GetPlanListAsync(new())).TotalCount.ShouldBe(0);
            await Should.ThrowAsync<AbpAuthorizationException>(() => _exams.GetPlanAsync(plan.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _exams.GetPlanResultsAsync(plan.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _exams.GetScoreListAsync(new() { PlanId = plan.Id }));
        }
        using (As(_student, "student"))
        {
            var mine = await _exams.GetMyPlanAsync(plan.Id);
            mine.Assignments.ShouldBeEmpty();
            mine.TeacherId.ShouldBe(_teacher);
            AssertSafePaper(mine.Paper!);
            var attempt = await _exams.StartAttemptAsync(plan.Id);
            attempt.ExpiresTime.ShouldBeNull();
            AssertSafePaper(attempt.Paper!);
            AssertSafePaper(attempt.Plan!.Paper!);
        }
        using (As(_teacher))
        {
            var results = await _exams.GetPlanResultsAsync(plan.Id);
            results.Recipients.Count.ShouldBe(1);
            results.Recipients.Single().StudentId.ShouldBe(_student);
            results.Recipients.Single().DisplayName.ShouldBe("小雨");
        }
    }

    [Theory]
    [InlineData(ExamResultVisibility.Hidden)]
    [InlineData(ExamResultVisibility.ScoreOnly)]
    [InlineData(ExamResultVisibility.FullReview)]
    public async Task Should_Trim_Student_Responses_By_Phase_And_Persist_One_Result(ExamResultVisibility visibility)
    {
        await ConnectAsync(_teacher, _student);
        var question = await SeedQuestionAsync(_teacher);
        ExamPlanDto plan;
        using (As(_teacher)) plan = await _exams.CreatePlanAsync(new()
        {
            Name = "展示策略测试", StudentIds = { _student }, Paper = Paper(question), ResultVisibility = visibility
        });
        Guid attemptId;
        using (As(_student, "student"))
        {
            var attempt = await _exams.StartAttemptAsync(plan.Id);
            attemptId = attempt.Id;
            AssertSafePaper(attempt.Paper!);
            var saved = await _exams.SaveAnswersAsync(attempt.Id, new()
            {
                IdempotencyKey = "save-1", Answers = { new() { PaperQuestionId = attempt.Paper!.Questions.Single().Id, Values = { "B" }, ClientVersion = 1 } }
            });
            saved.Answers.Single().Values.Single().ShouldBe("B");
            var stale = await _exams.SaveAnswersAsync(attempt.Id, new()
            {
                IdempotencyKey = "stale-save", Answers = { new() { PaperQuestionId = attempt.Paper!.Questions.Single().Id, Values = { "A" }, ClientVersion = 1 } }
            });
            stale.Answers.Single().Values.Single().ShouldBe("B");
            using (As(Guid.NewGuid(), "student"))
            {
                await Should.ThrowAsync<AbpAuthorizationException>(() => _exams.GetAttemptAsync(attempt.Id));
                await Should.ThrowAsync<AbpAuthorizationException>(() => _exams.SaveAnswersAsync(attempt.Id, new()));
            }
            AssertSafePaper(saved.Paper!);
            AssertSafePaper((await _exams.GetAttemptAsync(attempt.Id)).Plan!.Paper!);
            var submitted = await _exams.SubmitAttemptAsync(attempt.Id, new() { IdempotencyKey = "submit-1" });
            (await _exams.SubmitAttemptAsync(attempt.Id, new() { IdempotencyKey = "submit-1" })).Id.ShouldBe(attempt.Id);
            (await _exams.StartAttemptAsync(plan.Id)).Id.ShouldBe(attempt.Id);
            if (visibility == ExamResultVisibility.Hidden)
            {
                submitted.Score.ShouldBeNull();
                submitted.Plan!.MyScore.ShouldBeNull();
            }
            else
            {
                submitted.Score!.ObtainedScore.ShouldBe(0);
                submitted.Plan!.MyScore.ShouldBe(0);
                if (visibility == ExamResultVisibility.FullReview)
                {
                    submitted.Paper!.Questions.Single().CorrectAnswers.ShouldBe(new[] { "A" });
                    submitted.Plan.Paper!.Questions.Single().Analysis.ShouldBe("private teacher analysis");
                    submitted.Score.WrongQuestions.Count.ShouldBe(1);
                }
                else
                {
                    AssertSafePaper(submitted.Paper!);
                    AssertSafePaper(submitted.Plan.Paper!);
                    submitted.Score.WrongQuestions.ShouldBeEmpty();
                }
            }
            submitted.Paper!.Questions.Single().SourceSnapshot.ShouldBeNull();
            AssertSafePaper((await _exams.GetMyPlanListAsync(new())).Items.Single().Paper!);
        }
        using (As(_otherTeacher))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => _exams.GetStudentAttemptAsync(attemptId));
            await Should.ThrowAsync<AbpAuthorizationException>(() => _exams.GradeAttemptAsync(attemptId));
        }
        using (As(_teacher))
        {
            var full = await _exams.GetStudentAttemptAsync(attemptId);
            full.Paper!.Questions.Single().SourceSnapshot!.ShouldContain("private teacher source");
            full.Score!.WrongQuestions.Count.ShouldBe(1);
        }
        var scoreCount = await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<ExamScore, Guid>>().CountAsync(x => x.AttemptId == attemptId));
        scoreCount.ShouldBe(1);
    }

    [Fact]
    public async Task Should_Expire_An_Old_Attempt_Before_Starting_A_Remaining_Attempt()
    {
        await ConnectAsync(_teacher, _student);
        var question = await SeedQuestionAsync(_teacher);
        ExamPlanDto plan;
        using (As(_teacher)) plan = await _exams.CreatePlanAsync(new()
        {
            Name = "恢复考试", StudentIds = { _student }, Paper = Paper(question), MaxAttempts = 2, DurationMinutes = 30
        });
        var oldId = Guid.NewGuid();
        await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<ExamAttempt, Guid>>().InsertAsync(new ExamAttempt(
            oldId, null, plan.Id, plan.PaperId, _student, 1, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddMinutes(-1)), autoSave: true));
        using (As(_student, "student"))
        {
            var resumed = await _exams.StartAttemptAsync(plan.Id);
            resumed.Id.ShouldNotBe(oldId);
            resumed.AttemptNumber.ShouldBe(2);
            resumed.ExpiresTime.ShouldNotBeNull();
        }
        (await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<ExamAttempt, Guid>>().GetAsync(oldId))).Status
            .ShouldBe(ExamAttemptStatus.Expired);
    }

    [Fact]
    public async Task Should_Not_Grant_Student_Access_From_Legacy_All_Or_Empty_Assignments()
    {
        await ConnectAsync(_teacher, _student);
        var question = await SeedQuestionAsync(_teacher);
        ExamPlanDto original;
        using (As(_teacher)) original = await _exams.CreatePlanAsync(new()
        {
            Name = "显式任务", StudentIds = { _student }, Paper = Paper(question)
        });
        var legacyIds = new List<Guid>();
        await WithUnitOfWorkAsync(async () =>
        {
            foreach (var all in new[] { true, false })
            {
                var id = Guid.NewGuid();
                await GetRequiredService<IRepository<ExamPlan, Guid>>().InsertAsync(new ExamPlan(
                    id, null, "历史全员或空名单", original.PaperId, null, null, null, null, 1,
                    ExamResultVisibility.FullReview, ownerId: _teacher), autoSave: true);
                if (all) await GetRequiredService<IRepository<ExamAssignment, Guid>>().InsertAsync(new(
                    Guid.NewGuid(), null, id, ExamAssignmentScopeType.All, null), autoSave: true);
                legacyIds.Add(id);
            }
        });
        using (As(_student, "student"))
        {
            (await _exams.GetMyPlanListAsync(new())).Items.Select(x => x.Id).ShouldBe(new[] { original.Id });
            foreach (var id in legacyIds)
            {
                await Should.ThrowAsync<BusinessException>(() => _exams.GetMyPlanAsync(id));
                await Should.ThrowAsync<BusinessException>(() => _exams.StartAttemptAsync(id));
            }
        }
    }

    private IDisposable As(Guid id, string role = "teacher") => _principal.Change(new ClaimsPrincipal(new ClaimsIdentity(new[]
    {
        new Claim(AbpClaimTypes.UserId, id.ToString()), new Claim(AbpClaimTypes.UserName, role), new Claim(AbpClaimTypes.Role, role)
    }, "Test")));

    [Theory]
    [InlineData("对", "true")]
    [InlineData("错", "false")]
    public async Task Should_Score_Chinese_TrueFalse_Answers_Against_Student_Boolean_Keys(string answer, string studentValue)
    {
        await ConnectAsync(_teacher, _student);
        var question = await SeedQuestionAsync(_teacher, QuestionType.TrueFalse, answer);
        ExamPlanDto plan;
        using (As(_teacher)) plan = await _exams.CreatePlanAsync(new()
        {
            Name = "判断题", StudentIds = { _student }, Paper = Paper(question)
        });
        using (As(_student, "student"))
        {
            var attempt = await _exams.StartAttemptAsync(plan.Id);
            await _exams.SaveAnswersAsync(attempt.Id, new()
            {
                Answers = { new() { PaperQuestionId = attempt.Paper!.Questions.Single().Id, Values = { studentValue } } }
            });
            (await _exams.SubmitAttemptAsync(attempt.Id, new())).Score!.ObtainedScore.ShouldBe(10);
        }
    }

    private async Task<TeachingStudentDto> ConnectAsync(Guid teacherId, Guid studentId)
    {
        TeacherInvitationDto invitation;
        using (As(teacherId)) invitation = await _teaching.GetInvitationAsync();
        TeachingStudentDto relation;
        using (As(studentId, "student")) relation = await _teaching.ApplyAsync(new() { InvitationCode = invitation.InvitationCode, DisplayName = "小雨" });
        using (As(teacherId)) return await _teaching.ConfirmAsync(relation.Id);
    }

    private Task<Guid> SeedQuestionAsync(Guid ownerId, QuestionType type = QuestionType.SingleChoice, string answerValue = "A") => WithUnitOfWorkAsync(async () =>
    {
        var id = Guid.NewGuid();
        var question = new Question(id, null, type, QuestionDifficulty.Normal, QuestionCreationSource.Manual,
            "private teacher question", "original question snapshot");
        question.AssignTeacher(ownerId);
        var options = new List<QuestionOption>
        {
            new(Guid.NewGuid(), null, id, "A", "Correct option", 1),
            new(Guid.NewGuid(), null, id, "B", "Wrong option", 2)
        };
        var answer = new QuestionAnswer(Guid.NewGuid(), null, id, answerValue, 1);
        question.SubmitForReview(options, new[] { answer }, DateTime.UtcNow);
        question.Approve(ownerId, DateTime.UtcNow);
        await GetRequiredService<IRepository<Question, Guid>>().InsertAsync(question, autoSave: true);
        foreach (var option in options) await GetRequiredService<IRepository<QuestionOption, Guid>>().InsertAsync(option, autoSave: true);
        await GetRequiredService<IRepository<QuestionAnswer, Guid>>().InsertAsync(answer, autoSave: true);
        await GetRequiredService<IRepository<QuestionAnalysis, Guid>>().InsertAsync(new(Guid.NewGuid(), null, id, "private teacher analysis"), autoSave: true);
        await GetRequiredService<IRepository<QuestionSource, Guid>>().InsertAsync(new(Guid.NewGuid(), null, id,
            null, null, null, null, null, null, "private teacher source"), autoSave: true);
        return id;
    });

    private static GenerateExamPaperDto Paper(Guid questionId) => new()
    {
        Title = "测试快照", BuildMode = ExamPaperBuildMode.Fixed,
        FixedQuestions = { new() { QuestionId = questionId, Score = 10 } }
    };

    private static void AssertSafePaper(ExamPaperDto paper)
    {
        foreach (var question in paper.Questions)
        {
            question.CorrectAnswers.ShouldBeEmpty();
            question.Analysis.ShouldBeNull();
            question.SourceSnapshot.ShouldBeNull();
            question.OriginalQuestionId.ShouldBe(Guid.Empty);
        }
    }
}
