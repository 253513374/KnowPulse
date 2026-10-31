using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.AiModelManagement;
using KnowPulse.Education;
using KnowPulse.Exam;
using KnowPulse.RagAI;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.Uow;
using Volo.Abp.Users;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    private EducationTeachingPlanGenerationProcessor PlanGenerator => GetRequiredService<EducationTeachingPlanGenerationProcessor>();
    private TestRagChatClient PlanChat => GetRequiredService<TestRagChatClient>();
    private static string PlanProposal(EducationPlanGenerationRequest request) => JsonSerializer.Serialize(new { title = "有理数学习方案", studentGoal = "核对独立思路并有序练习", teacherActions = "先诊断再解释，检查每步依据", successCriteria = "新题独立完成后由教师复核",
        steps = new[] { "Diagnostic", "Explanation", "WorkedExample", "Practice", "Reassessment" }.Select(kind => new { kind, title = kind, instructions = "说明过程并核对每一步的依据", estimatedMinutes = 10, dueAtUtc = request.ReassessmentAtUtc }).ToArray() });
    private async Task<(GradingFixture Fixture, EducationLearningReportJobDto Report, Guid PlanId, GenerateEducationTeachingPlanDraftDto Input)> PlanGenerationFixtureAsync(bool configureModel = true)
    {
        var (f, report) = await PlanSourceAsync(); var manual = await PlanDraftAsync(f, report);
        using (User(f.Source.School.Reader)) {
            if (configureModel) {
                var catalog = GetRequiredService<IAiModelCatalogAppService>(); var overview = await catalog.GetOverviewAsync(); var deployment = overview.Deployments.First(x => x.IsActive && x.Capability == AiModelCapability.Chat);
                await catalog.SetUsageBindingAsync(new() { Usage = AiModelUsage.TeachingPlanGeneration, DeploymentId = deployment.Id, BusinessModelVersion = "teacher-plan-test-v1", MaxOutputTokens = 4096, Temperature = 0.2 });
            }
        }
        PlanChat.Reset(); var request = new EducationPlanGenerationRequest { ReportId = report.ReportId, ReportSnapshotId = report.Id, StudentUserId = f.Source.Student.UserId,
            TargetPointRevisionIds = manual.Content.TargetPointRevisionIds, StartsAtUtc = manual.Content.StartsAtUtc, EndsAtUtc = manual.Content.EndsAtUtc, ReassessmentAtUtc = manual.Content.ReassessmentAtUtc, MaximumEstimatedMinutes = 60 };
        PlanChat.ResponseContent = PlanProposal(request); return (f, report, Guid.NewGuid(), new() { OperationId = Guid.NewGuid(), Request = request });
    }
    [Fact]
    public async Task Plan_Generation_Should_Recover_One_Request_Send_Only_Anonymous_Point_Facts_And_Require_Separate_Teacher_Confirmation_And_Publication()
    {
        var f = await PlanGenerationFixtureAsync(); EducationTeachingPlanGenerationDto queued;
        using (User(f.Fixture.Source.School.Reader)) {
            queued = await Plans.GenerateDraftAsync(f.PlanId, f.Input); (await Plans.GenerateDraftAsync(f.PlanId, f.Input)).Id.ShouldBe(queued.Id);
            (await Plans.GetGenerationAsync(Guid.NewGuid())).ShouldBeNull(); queued.Status.ShouldBe(EducationPlanGenerationStatus.Pending);
            (await Plans.GetGenerationsAsync(new())).Items.Single().PlanId.ShouldBe(f.PlanId);
        }
        using (User(f.Fixture.Source.Student.UserId)) (await Plans.GetMyListAsync(new())).TotalCount.ShouldBe(0);
        PlanChat.Handler = request => { GetRequiredService<IUnitOfWorkManager>().Current.ShouldBeNull(); return Task.FromResult(RagChatClientResult.Success(PlanChat.ResponseContent, 120, 30, 150)); };
        await PlanGenerator.ProcessAsync(queued.Id); PlanChat.CallCount.ShouldBe(1);
        EducationTeachingPlanGenerationDto generated;
        using (User(f.Fixture.Source.School.Reader)) {
            generated = (await Plans.GetGenerationAsync(queued.Id))!; generated.Status.ShouldBe(EducationPlanGenerationStatus.Completed); generated.Content.ShouldNotBeNull(); generated.Content.Steps.Count.ShouldBe(5); generated.Basis.ShouldNotBeNull(); generated.HasSavedPlan.ShouldBeFalse();
            generated.PromptTokens.ShouldBe(120); generated.CompletionTokens.ShouldBe(30); generated.EstimatedCostUsd.ShouldBeNull();
            var before = generated.ConcurrencyStamp; await PlanGenerator.ProcessAsync(queued.Id); PlanChat.CallCount.ShouldBe(1); (await Plans.GetGenerationAsync(queued.Id))!.ConcurrencyStamp.ShouldBe(before);
            var prompt = PlanChat.Requests.Single().UserPrompt;
            foreach (var secret in new[] { generated.StudentName, f.Fixture.Source.Student.UserId.ToString(), f.Fixture.Source.School.Reader.ToString(), f.Fixture.SubmissionId.ToString(), "正确甲", "正确乙", "PRIVATE-PLAN-NOTES" }) prompt.ShouldNotContain(secret);
            prompt.ShouldContain("independentQuestions"); prompt.ShouldContain("schoolRuleMissing"); prompt.ShouldNotContain("totalScore"); prompt.ShouldNotContain("accountName");
            var input = new SaveEducationTeachingPlanDraftDto { OperationId = Guid.NewGuid(), GenerationJobId = generated.Id, ReportId = f.Input.Request.ReportId, ReportSnapshotId = f.Input.Request.ReportSnapshotId,
                StudentUserId = f.Input.Request.StudentUserId, Content = generated.Content with { Title = "教师核对后的有理数计划" }, InternalNotes = "PRIVATE-GENERATION-REVIEW" };
            var draft = await Plans.SaveDraftAsync(f.PlanId, input); draft.GenerationJobId.ShouldBe(generated.Id); (await Plans.SaveDraftAsync(f.PlanId, input)).Id.ShouldBe(draft.Id);
            (await Plans.GetGenerationAsync(queued.Id))!.HasSavedPlan.ShouldBeTrue();
            await Should.ThrowAsync<BusinessException>(async () => await Plans.PublishAsync(f.PlanId, PlanTransition(await Plans.GetAsync(f.PlanId))));
            var confirm = await Plans.ConfirmAsync(f.PlanId, PlanTransition(await Plans.GetAsync(f.PlanId))); confirm.GenerationJobId.ShouldBe(generated.Id);
        }
        using (User(f.Fixture.Source.Student.UserId)) (await Plans.GetMyListAsync(new())).TotalCount.ShouldBe(0);
        using (User(f.Fixture.Source.School.Reader)) {
            var published = await Plans.PublishAsync(f.PlanId, PlanTransition(await Plans.GetAsync(f.PlanId))); published.GenerationJobId.ShouldBe(generated.Id);
        }
        using (User(f.Fixture.Source.Student.UserId)) {
            var publicPlan = (await Plans.GetMyListAsync(new())).Items.Single(); var opened = await Plans.OpenMyAsync(publicPlan.Id, publicPlan.VersionId); opened.Content!.Title.ShouldBe("教师核对后的有理数计划"); opened.Content.Steps.Count.ShouldBe(5);
            await Should.ThrowAsync<AbpAuthorizationException>(() => Plans.GetGenerationAsync(generated.Id));
        }
    }
    [Fact]
    public async Task Plan_Generation_Invalid_Output_Should_Preserve_The_Failed_Round_And_Require_An_Explicit_Linked_Retry()
    {
        var f = await PlanGenerationFixtureAsync(); EducationTeachingPlanGenerationDto first;
        PlanChat.ResponseContent = "PRIVATE-INVALID-MODEL-RESPONSE";
        using (User(f.Fixture.Source.School.Reader)) first = await Plans.GenerateDraftAsync(f.PlanId, f.Input);
        await PlanGenerator.ProcessAsync(first.Id);
        using (User(f.Fixture.Source.School.Reader)) {
            var failed = (await Plans.GetGenerationAsync(first.Id))!; failed.Status.ShouldBe(EducationPlanGenerationStatus.Failed); failed.Failure.ShouldBe(EducationPlanGenerationFailure.Output); failed.Content.ShouldBeNull(); failed.PromptTokens.ShouldBe(120);
            PlanChat.ResponseContent = PlanProposal(f.Input.Request); var retry = new RetryEducationTeachingPlanGenerationDto { OperationId = Guid.NewGuid(), ConcurrencyStamp = failed.ConcurrencyStamp };
            var next = await Plans.RetryGenerationAsync(failed.Id, retry); next.RetryOfJobId.ShouldBe(failed.Id); (await Plans.RetryGenerationAsync(failed.Id, retry)).Id.ShouldBe(next.Id);
            await PlanGenerator.ProcessAsync(next.Id); PlanChat.CallCount.ShouldBe(2); (await Plans.GetGenerationAsync(next.Id))!.Status.ShouldBe(EducationPlanGenerationStatus.Completed);
            (await Plans.GetGenerationAsync(first.Id))!.Failure.ShouldBe(EducationPlanGenerationFailure.Output);
            var stored = await WithUnitOfWorkAsync(() => Repo<EducationTeachingPlanGenerationJob>().GetAsync(first.Id)); stored.ContentJson.ShouldBeNull(); stored.OutputHash!.Length.ShouldBe(64); stored.RequestJson.ShouldNotContain("PRIVATE-INVALID"); stored.BasisJson.ShouldNotContain("PRIVATE-INVALID");
        }
    }
    [Fact]
    public async Task Plan_Generation_Model_Outage_And_Quota_Should_Not_Block_Manual_Planning_Or_Silently_Retry_The_Provider()
    {
        var f = await PlanGenerationFixtureAsync(false); GetRequiredService<IOptions<EducationTeachingPlanGenerationOptions>>().Value.MaximumRequestsPerOwnerPerDay = 1;
        using var teacher = User(f.Fixture.Source.School.Reader); var first = await Plans.GenerateDraftAsync(f.PlanId, f.Input);
        var another = new GenerateEducationTeachingPlanDraftDto { OperationId = Guid.NewGuid(), Request = f.Input.Request };
        (await Should.ThrowAsync<BusinessException>(() => Plans.GenerateDraftAsync(Guid.NewGuid(), another))).Code.ShouldBe(EducationPlanGenerationErrors.Busy);
        await PlanGenerator.ProcessAsync(first.Id); PlanChat.CallCount.ShouldBe(0); (await Plans.GetGenerationAsync(first.Id))!.Failure.ShouldBe(EducationPlanGenerationFailure.Model);
        (await Should.ThrowAsync<BusinessException>(() => Plans.GenerateDraftAsync(Guid.NewGuid(), another))).Code.ShouldBe(EducationPlanGenerationErrors.Quota);
        var manual = await PlanDraftAsync(f.Fixture, f.Report); (await Plans.SaveDraftAsync(Guid.NewGuid(), manual)).Kind.ShouldBe(EducationTeachingPlanRevisionKind.Draft);
    }
    [Fact]
    public async Task Plan_Generation_Should_Recheck_Own_Evidence_After_The_Model_Returns_And_Withhold_Stale_Output()
    {
        var f = await PlanGenerationFixtureAsync(); EducationTeachingPlanGenerationDto first;
        using (User(f.Fixture.Source.School.Reader)) first = await Plans.GenerateDraftAsync(f.PlanId, f.Input);
        PlanChat.Handler = async _ => { GetRequiredService<IUnitOfWorkManager>().Current.ShouldBeNull(); await ConfirmManualGradeAsync(f.Fixture.SubmissionId, 1, "模型运行期间核对后更正评分"); return RagChatClientResult.Success(PlanProposal(f.Input.Request), 120, 30, 150); };
        await PlanGenerator.ProcessAsync(first.Id);
        using (User(f.Fixture.Source.School.Reader)) {
            var result = (await Plans.GetGenerationAsync(first.Id))!; result.Status.ShouldBe(EducationPlanGenerationStatus.Failed); result.Failure.ShouldBe(EducationPlanGenerationFailure.Source); result.Content.ShouldBeNull(); result.SourceIsCurrent.ShouldBeFalse();
            (await Should.ThrowAsync<BusinessException>(() => Plans.RetryGenerationAsync(first.Id, new() { OperationId = Guid.NewGuid(), ConcurrencyStamp = result.ConcurrencyStamp }))).Code.ShouldBe(EducationPlanGenerationErrors.Changed);
        }
        PlanChat.CallCount.ShouldBe(1); await WithUnitOfWorkAsync(async () => (await Repo<EducationTeachingPlan>().GetCountAsync()).ShouldBe(0));
    }
    [Fact]
    public async Task Plan_Generation_Should_Reject_Other_Actors_And_Still_Adopt_Unchanged_Own_Basis_After_A_New_Class_Report()
    {
        var f = await PlanGenerationFixtureAsync(); EducationTeachingPlanGenerationDto queued;
        using (User(f.Fixture.Source.School.Reader)) queued = await Plans.GenerateDraftAsync(f.PlanId, f.Input);
        foreach (var actor in new[] { f.Fixture.Source.School.Owner, f.Fixture.Source.Student.UserId }) using (User(actor)) await Should.ThrowAsync<AbpAuthorizationException>(() => Plans.GetGenerationAsync(queued.Id));
        using (GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid())) using (User(f.Fixture.Source.School.Reader)) await Should.ThrowAsync<AbpAuthorizationException>(() => Plans.GetGenerationAsync(queued.Id));
        await PlanGenerator.ProcessAsync(queued.Id);
        using (User(f.Fixture.Source.School.Reader)) {
            var report = await Reports.GetAsync(f.Report.ReportId); var newer = await Reports.RefreshAsync(report.Id, new() { OperationId = Guid.NewGuid(), ExpectedCurrentJobId = report.CurrentJob!.Id, ConcurrencyStamp = report.ConcurrencyStamp, Scope = report.Scope }); await ReportProcessor.ProcessAsync(newer.Id);
            var result = (await Plans.GetGenerationAsync(queued.Id))!; result.SourceIsCurrent.ShouldBeTrue();
            var input = new SaveEducationTeachingPlanDraftDto { OperationId = Guid.NewGuid(), GenerationJobId = queued.Id, ReportId = f.Report.ReportId, ReportSnapshotId = f.Report.Id, StudentUserId = f.Fixture.Source.Student.UserId, Content = result.Content! };
            (await Plans.SaveDraftAsync(f.PlanId, input)).GenerationJobId.ShouldBe(queued.Id);
            input.OperationId = Guid.NewGuid(); (await Should.ThrowAsync<BusinessException>(() => Plans.SaveDraftAsync(Guid.NewGuid(), input))).Code.ShouldBe(EducationPlanGenerationErrors.Conflict);
        }
    }
    [Theory]
    [InlineData(EducationPermissions.TeachingPlanGenerate)]
    [InlineData(EducationPermissions.TeachingPlanManage)]
    [InlineData(EducationPermissions.AnalysisManage)]
    public async Task Plan_Generation_Should_Reject_Denied_Functional_Permissions_Before_Reads_Or_Model_Calls(string denied)
    {
        var checker = Substitute.For<IPermissionChecker>(); checker.IsGrantedAsync(Arg.Any<string>()).Returns(true); checker.IsGrantedAsync(denied).Returns(false);
        var lazy = Substitute.For<IAbpLazyServiceProvider>(); lazy.LazyGetRequiredService<IPermissionChecker>().Returns(checker); lazy.LazyGetRequiredService<ICurrentUser>().Returns(GetRequiredService<ICurrentUser>());
        var service = new EducationTeachingPlanAppService { LazyServiceProvider = lazy }; var id = Guid.NewGuid();
        Func<Task>[] calls = [() => service.GenerateDraftAsync(id, new()), () => service.GetGenerationAsync(id), () => service.GetGenerationsAsync(new()), () => service.RetryGenerationAsync(id, new())];
        foreach (var call in calls) await Should.ThrowAsync<AbpAuthorizationException>(call); PlanChat.CallCount.ShouldBe(0);
    }
    [Fact]
    public async Task Plan_Generation_History_Should_Page_Metadata_After_Current_Relation_Filtering_Without_A_Lifetime_Candidate_Cap()
    {
        var f = await PlanGenerationFixtureAsync(); using var teacher = User(f.Fixture.Source.School.Reader); var first = await Plans.GenerateDraftAsync(f.PlanId, f.Input);
        var original = await WithUnitOfWorkAsync(() => Repo<EducationTeachingPlanGenerationJob>().GetAsync(first.Id));
        await WithUnitOfWorkAsync(async () => {
            for (var i = 0; i < 21; i++) {
                var at = DateTime.UtcNow; var job = new EducationTeachingPlanGenerationJob(Guid.NewGuid(), f.PlanId, original.Request(), original.Basis(), original.AcademicTermId, original.SchoolClassId, new string('e', 64), at);
                var run = Guid.NewGuid(); job.Claim(run, at, 120); job.Fail(run, EducationPlanGenerationFailure.Model, at.AddSeconds(1)); await Repo<EducationTeachingPlanGenerationJob>().InsertAsync(job, autoSave: true);
            }
        });
        var page = await Plans.GetGenerationsAsync(new() { SkipCount = 20, MaxResultCount = 20 }); page.TotalCount.ShouldBe(22); page.Items.Count.ShouldBe(2); page.Items.All(x => x.StudentName == original.StudentName).ShouldBeTrue();
        (await Plans.GetGenerationsAsync(new() { PlanId = f.PlanId })).TotalCount.ShouldBe(22); (await Plans.GetGenerationsAsync(new() { PlanId = Guid.NewGuid() })).TotalCount.ShouldBe(0);
        await WithUnitOfWorkAsync(async () => { var assignment = await Repo<TeachingAssignment>().GetAsync(original.TeachingAssignmentId); assignment.EndAt(DateTime.UtcNow); await Repo<TeachingAssignment>().UpdateAsync(assignment, autoSave: true); });
        (await Plans.GetGenerationsAsync(new())).TotalCount.ShouldBe(0); await Should.ThrowAsync<AbpAuthorizationException>(() => Plans.GetGenerationAsync(first.Id)); PlanChat.CallCount.ShouldBe(0);
    }
    [Fact]
    public async Task Plan_Generation_Permission_Revocation_Should_Stop_New_Model_Work_Without_Blocking_Manual_Edits_To_An_Already_Adopted_Plan()
    {
        var f = await PlanGenerationFixtureAsync(); using var teacher = User(f.Fixture.Source.School.Reader); var job = await Plans.GenerateDraftAsync(f.PlanId, f.Input); await PlanGenerator.ProcessAsync(job.Id);
        var generated = (await Plans.GetGenerationAsync(job.Id))!;
        var draft = new SaveEducationTeachingPlanDraftDto { OperationId = Guid.NewGuid(), GenerationJobId = job.Id, ReportId = f.Report.ReportId, ReportSnapshotId = f.Report.Id, StudentUserId = f.Fixture.Source.Student.UserId, Content = generated.Content! };
        await Plans.SaveDraftAsync(f.PlanId, draft); var current = await Plans.GetAsync(f.PlanId);
        var checker = Substitute.For<IPermissionChecker>(); checker.IsGrantedAsync(Arg.Any<string>()).Returns(true); checker.IsGrantedAsync(EducationPermissions.TeachingPlanGenerate).Returns(false);
        var service = PlanServiceWithPermissions(checker);
        await Should.ThrowAsync<AbpAuthorizationException>(() => service.GenerateDraftAsync(Guid.NewGuid(), new())); checker.ClearReceivedCalls();
        draft.OperationId = Guid.NewGuid(); draft.ExpectedRevisionId = current.Current.Id; draft.ConcurrencyStamp = current.ConcurrencyStamp; draft.Content = draft.Content with { Title = "撤回生成权限后的人工修改" };
        var edited = await service.SaveDraftAsync(f.PlanId, draft); edited.GenerationJobId.ShouldBe(job.Id); edited.Content!.Title.ShouldBe(draft.Content.Title);
        await checker.DidNotReceive().IsGrantedAsync(EducationPermissions.TeachingPlanGenerate); PlanChat.CallCount.ShouldBe(1);
    }
    private EducationTeachingPlanAppService PlanServiceWithPermissions(IPermissionChecker checker)
    {
        var original = GetRequiredService<EducationTeachingPlanAppService>().LazyServiceProvider;
        var lazy = Substitute.For<IAbpLazyServiceProvider>(); lazy.LazyGetRequiredService(Arg.Any<Type>()).Returns(call => original.LazyGetRequiredService(call.Arg<Type>())); lazy.LazyGetRequiredService<IPermissionChecker>().Returns(checker);
        void Forward<T>() where T : class => lazy.LazyGetRequiredService<T>().Returns(GetRequiredService<T>());
        Forward<ICurrentUser>(); Forward<ICurrentTenant>(); Forward<IUnitOfWorkManager>(); Forward<IAsyncQueryableExecuter>(); Forward<EducationAssessmentAccessGuard>(); Forward<EducationLearningReportSourceReader>();
        Forward<IRepository<EducationTeachingPlan, Guid>>(); Forward<IRepository<EducationTeachingPlanRevision, Guid>>(); Forward<IRepository<EducationTeachingPlanGenerationJob, Guid>>();
        Forward<IRepository<IdentityUser, Guid>>(); Forward<IRepository<SchoolClass, Guid>>(); Forward<IRepository<TeachingAssignment, Guid>>(); Forward<IRepository<AcademicTerm, Guid>>();
        Forward<IRepository<StudentEnrollment, Guid>>(); Forward<IRepository<SchoolMembership, Guid>>(); Forward<IRepository<EducationLearningReport, Guid>>();
        Forward<IRepository<EducationAssessmentRecipient, Guid>>(); Forward<IRepository<ExamPaperQuestion, Guid>>();
        Forward<IdentityUserManager>(); Forward<ICurrentPrincipalAccessor>(); Forward<IOptions<EducationTeachingPlanGenerationOptions>>(); Forward<IAiModelResolver>(); Forward<IRagChatClient>();
        return new EducationTeachingPlanAppService { LazyServiceProvider = lazy };
    }
    [Fact]
    public async Task Plan_Generation_Should_Rebuild_Owner_Roles_After_The_Model_Returns_Instead_Of_Trusting_The_Original_Principal()
    {
        var f = await PlanGenerationFixtureAsync(); var roleName = "plan-test-" + Guid.NewGuid().ToString("N");
        await WithUnitOfWorkAsync(async () => {
            (await GetRequiredService<IdentityRoleManager>().CreateAsync(new IdentityRole(Guid.NewGuid(), roleName))).Succeeded.ShouldBeTrue();
            var account = await Repo<IdentityUser>().GetAsync(f.Fixture.Source.School.Reader); (await GetRequiredService<IdentityUserManager>().AddToRoleAsync(account, roleName)).Succeeded.ShouldBeTrue();
        });
        EducationTeachingPlanGenerationDto job;
        using (User(f.Fixture.Source.School.Reader)) job = await Plans.GenerateDraftAsync(f.PlanId, f.Input);
        var run = Guid.NewGuid(); await WithUnitOfWorkAsync(async () => { var stored = await Repo<EducationTeachingPlanGenerationJob>().GetAsync(job.Id); stored.Claim(run, DateTime.UtcNow, 120).ShouldBeTrue(); await Repo<EducationTeachingPlanGenerationJob>().UpdateAsync(stored, autoSave: true); });
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new(AbpClaimTypes.UserId, f.Fixture.Source.School.Reader.ToString()), new(AbpClaimTypes.UserName, "plan-test"), new(AbpClaimTypes.Role, roleName)], "PlanTest", AbpClaimTypes.UserName, AbpClaimTypes.Role));
        using var caller = GetRequiredService<ICurrentPrincipalAccessor>().Change(principal);
        var checker = Substitute.For<IPermissionChecker>(); checker.IsGrantedAsync(Arg.Any<string>()).Returns(_ => GetRequiredService<ICurrentUser>().IsInRole(roleName));
        checker.IsGrantedAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<string>()).Returns(call => call.ArgAt<ClaimsPrincipal>(0).IsInRole(roleName));
        PlanChat.Handler = async _ => {
            await WithUnitOfWorkAsync(async () => { var account = await Repo<IdentityUser>().GetAsync(f.Fixture.Source.School.Reader); (await GetRequiredService<IdentityUserManager>().RemoveFromRoleAsync(account, roleName)).Succeeded.ShouldBeTrue(); });
            GetRequiredService<ICurrentUser>().IsInRole(roleName).ShouldBeTrue(); return RagChatClientResult.Success(PlanProposal(f.Input.Request), 120, 30, 150);
        };
        await PlanServiceWithPermissions(checker).ExecuteGenerationAsync(job.Id, run);
        var result = await WithUnitOfWorkAsync(() => Repo<EducationTeachingPlanGenerationJob>().GetAsync(job.Id));
        result.Status.ShouldBe(EducationPlanGenerationStatus.Failed); result.Failure.ShouldBe(EducationPlanGenerationFailure.Authorization); result.ContentJson.ShouldBeNull(); result.PromptTokens.ShouldBe(120); PlanChat.CallCount.ShouldBe(1);
    }
}
