using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using KnowPulse.Exam;
using KnowPulse.KnowledgeBase;
using KnowPulse.KnowledgeIndexing;
using KnowPulse.QuestionBank;
using KnowPulse.RagAI;
using KnowPulse.Teaching;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Security.Claims;
using Volo.Abp.Uow;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
[Trait("Category", "PostgreSQL")]
public class TeachingPostgresVerificationTests : KnowPulseTestBase<TeachingPostgresVerificationModule>
{
    private readonly Guid _teacher = Guid.NewGuid();
    private readonly IExamAppService _exams;
    private readonly ITeachingAppService _teaching;
    private readonly ICurrentPrincipalAccessor _principal;

    public TeachingPostgresVerificationTests()
    {
        _exams = GetRequiredService<IExamAppService>();
        _teaching = GetRequiredService<ITeachingAppService>();
        _principal = GetRequiredService<ICurrentPrincipalAccessor>();
    }

    [TeachingPostgresFact]
    public async Task Should_Persist_Only_Explicit_Recipients_Across_Independent_Transactions()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await ConnectAsync(first);
        await ConnectAsync(second);
        var questionId = await SeedQuestionAsync();
        ExamPlanDto plan;
        using (As(_teacher)) plan = await _exams.CreatePlanAsync(Input(questionId, first, second));
        var late = Guid.NewGuid();
        await ConnectAsync(late);
        var actual = await ReadAsync(async () => await GetRequiredService<IRepository<ExamAssignment, Guid>>()
            .GetListAsync(x => x.PlanId == plan.Id));
        actual.Select(x => x.TargetId).OrderBy(x => x).ShouldBe(new Guid?[] { first, second }.OrderBy(x => x));
        using (As(late, "student")) (await _exams.GetMyPlanListAsync(new())).Items.ShouldNotContain(x => x.Id == plan.Id);
        using (As(first, "student")) (await _exams.GetMyPlanAsync(plan.Id)).Id.ShouldBe(plan.Id);
    }

    [TeachingPostgresFact]
    public async Task Should_Roll_Back_Task_Snapshot_And_First_Recipient_If_Second_Recipient_Fails()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await ConnectAsync(first);
        await ConnectAsync(second);
        var questionId = await SeedQuestionAsync();
        var input = Input(questionId, first, second);
        var fault = GetRequiredService<TeachingPublicationFaultInterceptor>();
        fault.FailStudentId = second;
        try
        {
            using (As(_teacher)) await Should.ThrowAsync<InvalidOperationException>(() => _exams.CreatePlanAsync(input));
        }
        finally { fault.FailStudentId = null; }
        fault.SawTransaction.ShouldBeTrue();
        fault.FailedPlanId.ShouldNotBeNull();
        fault.FailedPaperId.ShouldNotBeNull();
        await ReadAsync(async () =>
        {
            (await GetRequiredService<IRepository<ExamPlan, Guid>>().CountAsync(x => x.Id == fault.FailedPlanId)).ShouldBe(0);
            (await GetRequiredService<IRepository<ExamPaper, Guid>>().CountAsync(x => x.Id == fault.FailedPaperId)).ShouldBe(0);
            (await GetRequiredService<IRepository<ExamPaperQuestion, Guid>>().CountAsync(x => x.PaperId == fault.FailedPaperId)).ShouldBe(0);
            (await GetRequiredService<IRepository<ExamAssignment, Guid>>().CountAsync(x => x.PlanId == fault.FailedPlanId)).ShouldBe(0);
            return true;
        });
    }

    [TeachingPostgresFact]
    public async Task Concurrent_Submit_With_One_Key_Should_Return_The_Same_Durable_Result()
    {
        var student = Guid.NewGuid();
        await ConnectAsync(student);
        var questionId = await SeedQuestionAsync();
        ExamPlanDto plan;
        using (As(_teacher)) plan = await _exams.CreatePlanAsync(Input(questionId, student));
        Guid attemptId;
        using (As(student, "student"))
        {
            var attempt = await _exams.StartAttemptAsync(plan.Id);
            attemptId = attempt.Id;
            await _exams.SaveAnswersAsync(attempt.Id, new()
            {
                IdempotencyKey = "pg-save", Answers = { new() { PaperQuestionId = attempt.Paper!.Questions.Single().Id, Values = { "A" }, ClientVersion = 1 } }
            });
        }
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<ExamAttemptDto> SubmitAsync()
        {
            await gate.Task;
            using var scope = ServiceProvider.CreateScope();
            using var actor = scope.ServiceProvider.GetRequiredService<ICurrentPrincipalAccessor>().Change(Principal(student, "student"));
            return await scope.ServiceProvider.GetRequiredService<IExamAppService>()
                .SubmitAttemptAsync(attemptId, new() { IdempotencyKey = "same-pg-submit" });
        }
        var first = Task.Run(SubmitAsync);
        var second = Task.Run(SubmitAsync);
        gate.SetResult();
        var results = await Task.WhenAll(first, second);
        results.ShouldAllBe(x => x.Status == ExamAttemptStatus.Scored && x.Score != null && x.Score.ObtainedScore == 10);
        results[0].Score!.Id.ShouldBe(results[1].Score!.Id);
        var persisted = await ReadAsync(() => GetRequiredService<IRepository<ExamScore, Guid>>().GetListAsync(x => x.AttemptId == attemptId));
        persisted.Count.ShouldBe(1);
        persisted.Single().Id.ShouldBe(results[0].Score!.Id);
    }

    [TeachingPostgresFact]
    public async Task Same_Index_Job_Retry_Should_Reuse_The_First_Active_Chunk()
    {
        var fixture = await ReadAsync(async () =>
        {
            var item = new KnowledgeItem(Guid.NewGuid(), null, "PG same-job ingestion retry");
            item.AssignTeacher(_teacher);
            await GetRequiredService<IRepository<KnowledgeItem, Guid>>().InsertAsync(item, autoSave: true);
            var version = new KnowledgeVersion(Guid.NewGuid(), null, item.Id, 1,
                "PG retry source", "This text must keep its persisted chunk when the same job retries embedding.");
            version.Publish(DateTime.UtcNow);
            await GetRequiredService<IRepository<KnowledgeVersion, Guid>>().InsertAsync(version, autoSave: true);
            item.SetCurrentPublishedVersion(version.Id);
            await GetRequiredService<IRepository<KnowledgeItem, Guid>>().UpdateAsync(item, autoSave: true);
            var job = new KnowledgeIndexJob(Guid.NewGuid(), null, item.Id, version.Id, KnowledgeIndexJobTriggerType.Publish);
            await GetRequiredService<IRepository<KnowledgeIndexJob, Guid>>().InsertAsync(job, autoSave: true);
            return (VersionId: version.Id, JobId: job.Id);
        });
        var ingestion = GetRequiredService<IKnowledgeIngestionPipeline>();
        using (As(_teacher))
        {
            (await ingestion.IngestAsync(fixture.VersionId, fixture.JobId, completeIndexJob: false)).Succeeded.ShouldBeTrue();
            var first = await ReadAsync(() => GetRequiredService<IRepository<KnowledgeChunk, Guid>>()
                .GetListAsync(x => x.KnowledgeIndexJobId == fixture.JobId));
            first.Count.ShouldBe(1);
            first.Single().IsActive.ShouldBeTrue();
            await ReadAsync(async () =>
            {
                var repository = GetRequiredService<IRepository<KnowledgeIndexJob, Guid>>();
                var job = await repository.GetAsync(fixture.JobId);
                job.MarkFailed("embedding", "Injected verification failure after chunks persisted.", DateTime.UtcNow);
                await repository.UpdateAsync(job, autoSave: true);
                return true;
            });
            (await ingestion.IngestAsync(fixture.VersionId, fixture.JobId, completeIndexJob: false)).Succeeded.ShouldBeTrue();
            var retried = await ReadAsync(() => GetRequiredService<IRepository<KnowledgeChunk, Guid>>()
                .GetListAsync(x => x.KnowledgeIndexJobId == fixture.JobId));
            retried.Count.ShouldBe(first.Count);
            retried.Single().Id.ShouldBe(first.Single().Id);
            retried.Single().IsActive.ShouldBeTrue();
        }
    }

    private async Task ConnectAsync(Guid student)
    {
        TeacherInvitationDto invitation;
        using (As(_teacher)) invitation = await _teaching.GetInvitationAsync();
        TeachingStudentDto relation;
        using (As(student, "student")) relation = await _teaching.ApplyAsync(new() { InvitationCode = invitation.InvitationCode, DisplayName = "PG 隔离验证学生" });
        using (As(_teacher)) await _teaching.ConfirmAsync(relation.Id);
    }

    private Task<Guid> SeedQuestionAsync() => ReadAsync(async () =>
    {
        var id = Guid.NewGuid();
        var question = new Question(id, null, QuestionType.SingleChoice, QuestionDifficulty.Normal,
            QuestionCreationSource.Manual, "PG isolated verification", "Choose A.");
        question.AssignTeacher(_teacher);
        var options = new[]
        {
            new QuestionOption(Guid.NewGuid(), null, id, "A", "Correct", 1),
            new QuestionOption(Guid.NewGuid(), null, id, "B", "Incorrect", 2)
        };
        var answer = new QuestionAnswer(Guid.NewGuid(), null, id, "A", 1);
        question.SubmitForReview(options, new[] { answer }, DateTime.UtcNow);
        question.Approve(_teacher, DateTime.UtcNow);
        await GetRequiredService<IRepository<Question, Guid>>().InsertAsync(question, autoSave: true);
        foreach (var option in options) await GetRequiredService<IRepository<QuestionOption, Guid>>().InsertAsync(option, autoSave: true);
        await GetRequiredService<IRepository<QuestionAnswer, Guid>>().InsertAsync(answer, autoSave: true);
        return id;
    });

    private static CreateExamPlanDto Input(Guid questionId, params Guid[] students) => new()
    {
        Name = "PG verification " + Guid.NewGuid().ToString("N"), StudentIds = students.ToList(),
        Paper = new() { Title = "PG verification " + Guid.NewGuid().ToString("N"), FixedQuestions = { new() { QuestionId = questionId, Score = 10 } } }
    };

    private Task<T> ReadAsync<T>(Func<Task<T>> action) => WithUnitOfWorkAsync(
        new AbpUnitOfWorkOptions { IsTransactional = true }, action);

    private IDisposable As(Guid id, string role = "teacher") => _principal.Change(Principal(id, role));

    private static ClaimsPrincipal Principal(Guid id, string role) => new(new ClaimsIdentity(new[]
    {
        new Claim(AbpClaimTypes.UserId, id.ToString()), new Claim(AbpClaimTypes.UserName, role), new Claim(AbpClaimTypes.Role, role)
    }, "Test"));
}
