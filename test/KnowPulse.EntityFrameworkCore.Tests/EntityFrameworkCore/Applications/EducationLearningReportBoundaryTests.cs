using System;
using System.Threading.Tasks;
using KnowPulse.Education;
using NSubstitute;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Volo.Abp.Users;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    [Fact]
    public async Task Learning_Report_Should_Reject_Explicitly_Denied_Permissions_Before_Loading_Any_Source()
    {
        var checker = Substitute.For<IPermissionChecker>(); checker.IsGrantedAsync(Arg.Any<string>()).Returns(false);
        var lazy = Substitute.For<IAbpLazyServiceProvider>(); lazy.LazyGetRequiredService<IPermissionChecker>().Returns(checker);
        lazy.LazyGetRequiredService<ICurrentUser>().Returns(GetRequiredService<ICurrentUser>());
        lazy.LazyGetRequiredService<IUnitOfWorkManager>().Returns(GetRequiredService<IUnitOfWorkManager>());
        var reports = new EducationLearningReportAppService { LazyServiceProvider = lazy };
        var rules = new EducationMasteryRuleAppService { LazyServiceProvider = lazy }; var id = Guid.NewGuid();
        Func<Task>[] calls = [() => reports.RequestAsync(new()), () => reports.RefreshAsync(id, new()), () => reports.RecoverAsync(id, new()),
            () => reports.GetOperationAsync(id, id), () => reports.GetListAsync(id, new()), () => reports.GetAsync(id), () => reports.GetForSchoolAsync(id, id, id),
            () => reports.GetStudentsAsync(id, id, new()), () => reports.GetStudentAsync(id, id, id), () => reports.GetPointEvidenceAsync(id, id, id, id, new()), () => reports.GetTrendAsync(id, id, id, id, new()),
            () => rules.GetAsync(id, id), () => rules.SaveAsync(id, id, new()), () => rules.GetOperationAsync(id, id, id) ];
        foreach (var call in calls) await Should.ThrowAsync<AbpAuthorizationException>(call);
        await checker.Received(11).IsGrantedAsync(EducationPermissions.AnalysisManage);
        await checker.Received(3).IsGrantedAsync(EducationPermissions.SchoolManage);
    }

    [Fact]
    public async Task Learning_Report_Should_Stop_An_Outdated_Queued_Source_Without_Publishing_Partial_Results()
    {
        var f = await GradingFixtureAsync(false); await EvidenceGradeAsync(f); EducationLearningReportJobDto job;
        using (User(f.Source.School.Reader)) {
            job = await Reports.RequestAsync(ReportInput(f)); await ConfirmManualGradeAsync(f.SubmissionId, 1, "生成前确认更正评分");
        }
        await ReportProcessor.ProcessAsync(job.Id);
        using (User(f.Source.School.Reader)) {
            var report = await Reports.GetAsync(job.ReportId); report.CurrentJob!.Status.ShouldBe(EducationLearningReportJobStatus.SourceChanged);
            report.Summary.ShouldBeNull(); report.CurrentSnapshotId.ShouldBeNull();
        }
        await WithUnitOfWorkAsync(async () => { (await Repo<EducationLearningReportSnapshot>().GetCountAsync()).ShouldBe(0); (await Repo<EducationLearningReportStudentSnapshot>().GetCountAsync()).ShouldBe(0); });
        (await ReportProcessor.RefreshChangedAsync(job.ReportId)).ShouldBeTrue();
    }

    [Fact]
    public async Task Learning_Report_Should_Reject_Other_Tenants_And_Disabled_Owner_In_Both_Public_And_Background_Access()
    {
        var f = await GradingFixtureAsync(false); await EvidenceGradeAsync(f); EducationLearningReportJobDto job;
        using (User(f.Source.School.Reader)) job = await Reports.RequestAsync(ReportInput(f));
        using (User(f.Source.School.Reader)) using (GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid()))
            await Should.ThrowAsync<AbpAuthorizationException>(() => Reports.GetAsync(job.ReportId));
        await WithUnitOfWorkAsync(async () => { var account = await Repo<IdentityUser>().GetAsync(f.Source.School.Reader); account.SetIsActive(false); await Repo<IdentityUser>().UpdateManyAsync([account], autoSave: true); });
        using (User(f.Source.School.Reader)) await Should.ThrowAsync<AbpAuthorizationException>(() => Reports.GetAsync(job.ReportId));
        await ReportProcessor.ProcessAsync(job.Id);
        await WithUnitOfWorkAsync(async () => { (await Repo<EducationLearningReportJob>().GetAsync(job.Id)).FailureStage.ShouldBe(EducationLearningReportFailureStage.Source); (await Repo<EducationLearningReportSnapshot>().GetCountAsync()).ShouldBe(0); });
    }
}
