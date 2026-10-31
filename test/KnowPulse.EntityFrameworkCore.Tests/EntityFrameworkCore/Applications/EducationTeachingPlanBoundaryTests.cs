using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.Exam;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Volo.Abp.Users;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    [Fact]
    public async Task Teaching_Plan_Should_Require_Fresh_Saved_Draft_Explicit_Confirmation_And_Original_Operation_Receipts()
    {
        var (f, job) = await PlanSourceAsync(); var id = Guid.NewGuid(); var draft = await PlanDraftAsync(f, job);
        using var user = User(f.Source.School.Reader);
        var saved = await Plans.SaveDraftAsync(id, draft); var current = await Plans.GetAsync(id); var wrong = PlanTransition(current); wrong.ConfirmStudentSafeText = false;
        await Should.ThrowAsync<BusinessException>(() => Plans.ConfirmAsync(id, wrong));
        await Should.ThrowAsync<BusinessException>(() => Plans.PublishAsync(id, PlanTransition(current)));
        var confirm = PlanTransition(current); var confirmed = await Plans.ConfirmAsync(id, confirm); (await Plans.ConfirmAsync(id, confirm)).Id.ShouldBe(confirmed.Id);
        var publish = PlanTransition(await Plans.GetAsync(id)); var published = await Plans.PublishAsync(id, publish); (await Plans.PublishAsync(id, publish)).Id.ShouldBe(published.Id);
        (await Plans.SaveDraftAsync(id, draft)).Id.ShouldBe(saved.Id);
        (await Plans.GetOperationAsync(id, saved.Id))!.Kind.ShouldBe(EducationTeachingPlanRevisionKind.Draft);
        draft.OperationId = Guid.NewGuid(); draft.ExpectedRevisionId = saved.Id; draft.ConcurrencyStamp = current.ConcurrencyStamp;
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => Plans.SaveDraftAsync(id, draft));
        draft.OperationId = saved.Id; draft.Content = draft.Content with { Title = "重用操作号不能改变内容" };
        (await Should.ThrowAsync<BusinessException>(() => Plans.SaveDraftAsync(id, draft))).Code.ShouldBe(EducationTeachingPlanErrors.OperationConflict);
    }

    [Fact]
    public async Task Teaching_Plan_Should_Block_An_Outdated_Students_Basis_And_Require_A_New_Report_And_New_Public_Version()
    {
        var (f, job) = await PlanSourceAsync(); var id = Guid.NewGuid(); EducationTeachingPlanRevisionDto published;
        using (User(f.Source.School.Reader)) { await Plans.SaveDraftAsync(id, await PlanDraftAsync(f, job)); published = await ConfirmAndPublishPlanAsync(id); await ConfirmManualGradeAsync(f.SubmissionId, 1, "方案依据评分已更正"); }
        using (User(f.Source.Student.UserId)) { var stale = await Plans.OpenMyAsync(id, published.Id); stale.Availability.ShouldBe(EducationTeachingPlanAvailability.SourceChanged); stale.Content.ShouldBeNull(); }
        await WithUnitOfWorkAsync(async () => (await Repo<EducationTeachingPlanQuestionExposure>().GetCountAsync()).ShouldBe(0));
        (await ReportProcessor.RefreshChangedAsync(job.ReportId)).ShouldBeTrue();
        var nextId = await WithUnitOfWorkAsync(async () => (await Repo<EducationLearningReport>().GetAsync(job.ReportId)).CurrentJobId!.Value); await ReportProcessor.ProcessAsync(nextId);
        EducationTeachingPlanRevisionDto replacement;
        using (User(f.Source.School.Reader)) {
            var current = await Plans.GetAsync(id); current.SourceIsCurrent.ShouldBeFalse();
            var sourceJob = (await Reports.GetAsync(job.ReportId)).CurrentJob!; var input = await PlanDraftAsync(f, sourceJob); input.ExpectedRevisionId = current.Current.Id; input.ConcurrencyStamp = current.ConcurrencyStamp;
            await Plans.SaveDraftAsync(id, input); replacement = await ConfirmAndPublishPlanAsync(id, "依据更正后的分值调整安排");
            (await Plans.GetRevisionAsync(id, published.Id)).Basis!.Evidence.Single().ObtainedScore.ShouldBe(10);
        }
        using (User(f.Source.Student.UserId)) { await Should.ThrowAsync<AbpDbConcurrencyException>(() => Plans.OpenMyAsync(id, published.Id)); (await Plans.OpenMyAsync(id, replacement.Id)).Content.ShouldNotBeNull(); }
    }

    [Fact]
    public async Task Teaching_Plan_Should_Not_Invalidate_A_Students_Published_Plan_When_Another_Students_Grade_Changes()
    {
        var f = await GradingFixtureAsync(false, secondStarted: true); EducationLearningReportJobDto job; Guid otherSubmission;
        using (User(f.Source.School.Reader)) {
            await Grades.GetSubmissionForGradingAsync(f.SubmissionId);
            var closure = await Grades.CloseTaskAsync(f.TaskId, CloseInput(await Grades.PreviewFinalizationAsync(f.TaskId)));
            otherSubmission = (await Grades.GetGradingOverviewAsync(f.TaskId)).Submissions.Single(x => x.StudentUserId == f.Absent.UserId).SubmissionId;
            await Grades.GetSubmissionForGradingAsync(otherSubmission);
            await Grades.PublishResultsAsync(f.TaskId, ReleaseInput(await Grades.GetGradingOverviewAsync(f.TaskId), closure.Id, ExamResultVisibility.FullReview));
            job = await Reports.RequestAsync(ReportInput(f));
        }
        await ReportProcessor.ProcessAsync(job.Id); var id = Guid.NewGuid(); EducationTeachingPlanRevisionDto published;
        using (User(f.Source.School.Reader)) { await Plans.SaveDraftAsync(id, await PlanDraftAsync(f, job)); published = await ConfirmAndPublishPlanAsync(id); await ConfirmManualGradeAsync(otherSubmission, 1, "另一位学生的更正不影响本人的安排"); }
        using (User(f.Source.Student.UserId)) (await Plans.OpenMyAsync(id, published.Id)).Availability.ShouldBe(EducationTeachingPlanAvailability.Available);
    }

    [Fact]
    public async Task Teaching_Plan_Should_Reject_Another_Teacher_Student_Tenant_And_An_Unrecognized_Public_Version()
    {
        var (f, job) = await PlanSourceAsync(); var id = Guid.NewGuid(); EducationTeachingPlanRevisionDto published;
        using (User(f.Source.School.Reader)) { await Plans.SaveDraftAsync(id, await PlanDraftAsync(f, job)); published = await ConfirmAndPublishPlanAsync(id); }
        using (User(f.Source.School.Owner)) await Should.ThrowAsync<AbpAuthorizationException>(() => Plans.GetAsync(id));
        using (User(f.Absent.UserId)) await Should.ThrowAsync<AbpAuthorizationException>(() => Plans.OpenMyAsync(id, published.Id));
        using (User(f.Source.Student.UserId)) {
            await Should.ThrowAsync<AbpDbConcurrencyException>(() => Plans.OpenMyAsync(id, Guid.NewGuid()));
            using (GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid())) await Should.ThrowAsync<AbpAuthorizationException>(() => Plans.OpenMyAsync(id, published.Id));
        }
        await WithUnitOfWorkAsync(async () => { var enrollment = await Repo<StudentEnrollment>().GetAsync(f.Source.Student.EnrollmentId); enrollment.EndAt(DateTime.UtcNow); await Repo<StudentEnrollment>().UpdateManyAsync([enrollment], autoSave: true); });
        using (User(f.Source.Student.UserId)) { (await Plans.GetMyListAsync(new())).Items.ShouldBeEmpty(); await Should.ThrowAsync<AbpAuthorizationException>(() => Plans.OpenMyAsync(id, published.Id)); }
    }

    [Fact]
    public async Task Teaching_Plan_Should_Prioritize_Diagnosis_When_Evidence_Is_Insufficient()
    {
        var (f, job) = await PlanSourceAsync(); var id = Guid.NewGuid(); var input = await PlanDraftAsync(f, job);
        input.Content = input.Content with { Steps = input.Content.Steps.Select((x, i) => i == 0 ? x with { Kind = EducationTeachingPlanStepKind.Practice } : x).ToList() };
        using var user = User(f.Source.School.Reader); await Plans.SaveDraftAsync(id, input);
        (await Should.ThrowAsync<BusinessException>(async () => await Plans.ConfirmAsync(id, PlanTransition(await Plans.GetAsync(id))))).Code.ShouldBe(EducationTeachingPlanErrors.DiagnosticRequired);
    }

    [Fact]
    public async Task Teaching_Plan_Actual_Context_Should_Enter_Later_Source_Capture_Without_Invalidating_An_Earlier_Measurement()
    {
        var f = await GradingFixtureAsync(false); var grade = await EvidenceGradeAsync(f);
        using (User(f.Source.School.Reader)) await Evidence.SaveEvidenceQualificationAsync(grade, IndependentEvidence());
        await ExposureReleaseAsync(f, ExamResultVisibility.FullReview); EducationLearningReportJobDto first;
        using (User(f.Source.School.Reader)) first = await Reports.RequestAsync(ReportInput(f)); await ReportProcessor.ProcessAsync(first.Id);
        var id = Guid.NewGuid(); EducationTeachingPlanRevisionDto published;
        using (User(f.Source.School.Reader)) { await Plans.SaveDraftAsync(id, await PlanDraftAsync(f, first)); published = await ConfirmAndPublishPlanAsync(id); }
        using (User(f.Source.Student.UserId)) await Plans.OpenMyAsync(id, published.Id);
        EducationLearningReportJobDto later;
        using (User(f.Source.School.Reader)) later = await Reports.RequestAsync(ReportInput(f)); await ReportProcessor.ProcessAsync(later.Id);
        await WithUnitOfWorkAsync(async () => {
            var source = (await Repo<EducationLearningReportJob>().GetAsync(later.Id)).Source();
            var context = source.Students.Single(x => x.Student.StudentUserId == f.Source.Student.UserId).Exposures.Single();
            context.Use.ShouldBe(EducationLearningExposureUse.Unverified); context.Id.ShouldBe((await Repo<EducationTeachingPlanQuestionExposure>().GetListAsync()).Single().Id);
        });
        using (User(f.Source.School.Reader)) (await Reports.GetStudentAsync(later.ReportId, later.Id, f.Source.Student.UserId)).Analysis.Points.All(x => x.Ratio == 1 && x.IndependentQuestionCount == 1).ShouldBeTrue();
    }

    [Theory]
    [InlineData(EducationPermissions.TeachingPlanManage)]
    [InlineData(EducationPermissions.AnalysisManage)]
    [InlineData(EducationPermissions.TeachingPlanView)]
    [InlineData(EducationPermissions.AssessmentManage)]
    public async Task Teaching_Plan_Should_Reject_Denied_Functional_Permissions(string denied)
    {
        var checker = Substitute.For<IPermissionChecker>(); checker.IsGrantedAsync(Arg.Any<string>()).Returns(true); checker.IsGrantedAsync(denied).Returns(false);
        var lazy = Substitute.For<IAbpLazyServiceProvider>(); lazy.LazyGetRequiredService<IPermissionChecker>().Returns(checker);
        lazy.LazyGetRequiredService<ICurrentUser>().Returns(GetRequiredService<ICurrentUser>()); lazy.LazyGetRequiredService<IUnitOfWorkManager>().Returns(GetRequiredService<IUnitOfWorkManager>());
        var service = new EducationTeachingPlanAppService { LazyServiceProvider = lazy }; var id = Guid.NewGuid();
        Func<Task>[] calls = denied == EducationPermissions.TeachingPlanView ? [() => service.GetMyListAsync(new()), () => service.OpenMyAsync(id, id)] :
            denied == EducationPermissions.AssessmentManage ? [() => service.GetHintContextAsync(id), () => service.SaveHintReviewAsync(id, new()), () => service.GetHintReviewOperationAsync(id, id)] :
            [() => service.SaveDraftAsync(id, new()), () => service.GetAsync(id), () => service.GetListAsync(new()), () => service.GetRevisionAsync(id, id), () => service.GetOperationAsync(id, id), () => service.ConfirmAsync(id, new()), () => service.PublishAsync(id, new()), () => service.WithdrawAsync(id, new()),
                () => service.GetHintContextAsync(id), () => service.SaveHintReviewAsync(id, new()), () => service.GetHintReviewOperationAsync(id, id),
                () => service.GetExecutionReviewAsync(id), () => service.DecideExecutionAsync(id, new()), () => service.GetExecutionOperationAsync(id, id), () => service.GetExecutionHistoryAsync(id, null, new()),
                () => service.GetTeacherPlansAsync(new()), () => service.GetActivityProgressForSchoolAsync(id, id, id, id), () => service.SaveActivityFollowUpForSchoolAsync(id, id, id, new()), () => service.GetActivityFollowUpOperationForSchoolAsync(id, id, id, id)];
        foreach (var call in calls) await Should.ThrowAsync<AbpAuthorizationException>(call);
    }
}
