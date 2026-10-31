using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.RagAI;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Security.Claims;
using Volo.Abp.Uow;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    private IEducationLearningReportAppService Reports => GetRequiredService<IEducationLearningReportAppService>();
    private EducationLearningReportProcessor ReportProcessor => GetRequiredService<EducationLearningReportProcessor>();
    private IEducationMasteryRuleAppService MasteryRules => GetRequiredService<IEducationMasteryRuleAppService>();
    [Fact]
    public async Task Native_Report_Should_Require_The_Exact_School_And_Teaching_Assignment_And_Keep_Web_Reads_Compatible()
    {
        var f = await GradingFixtureAsync(false); await EvidenceGradeAsync(f); EducationLearningReportJobDto job;
        using (User(f.Source.School.Reader)) job = await Reports.RequestAsync(ReportInput(f));
        await ReportProcessor.ProcessAsync(job.Id);
        var scope = await WithUnitOfWorkAsync(async () => (await Repo<EducationLearningReport>().GetAsync(job.ReportId)).Authority());
        using (User(f.Source.School.Reader)) {
            var list = await Reports.GetListAsync(scope.TeachingAssignmentId, new(), scope.SchoolId);
            list.Items.Single().SchoolId.ShouldBe(scope.SchoolId); list.Items.Single().TeachingAssignmentId.ShouldBe(scope.TeachingAssignmentId);
            await Should.ThrowAsync<AbpAuthorizationException>(() => Reports.GetListAsync(scope.TeachingAssignmentId, new(), Guid.NewGuid()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Reports.GetForSchoolAsync(job.ReportId, Guid.NewGuid(), scope.TeachingAssignmentId));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Reports.GetForSchoolAsync(job.ReportId, scope.SchoolId, Guid.NewGuid()));
            var native = await Reports.GetForSchoolAsync(job.ReportId, scope.SchoolId, scope.TeachingAssignmentId);
            native.CurrentSnapshotId.ShouldBe(job.Id); native.Summary.ShouldNotBeNull(); native.Summary.Students.ShouldBeEmpty();
            native.Coverage!.StudentCount.ShouldBe(2); native.Coverage.ScoredStudentCount.ShouldBe(1); native.Coverage.AssignedCount.ShouldBe(2);
            native.Coverage.ScoredCount.ShouldBe(1); native.Coverage.NotStartedCount.ShouldBe(1);
            var web = await Reports.GetAsync(job.ReportId); web.CurrentSnapshotId.ShouldBe(native.CurrentSnapshotId);
            (await Reports.GetListAsync(scope.TeachingAssignmentId, new())).TotalCount.ShouldBe(1);
        }
        using (User(f.Source.Student.UserId)) await Should.ThrowAsync<AbpAuthorizationException>(() => Reports.GetForSchoolAsync(job.ReportId, scope.SchoolId, scope.TeachingAssignmentId));
    }
    private RequestEducationLearningReportDto ReportInput(GradingFixture f) => new() { OperationId = Guid.NewGuid(),
        Scope = new() { TeacherTextbookSelectionId = f.Source.School.ReaderSelection, TextbookKnowledgePointMappingIds = [f.Source.School.MappingA, f.Source.School.MappingB] }, WindowStartsAtUtc = DateTime.UtcNow.AddDays(-1),
        WindowEndsAtUtc = DateTime.UtcNow, TaskIds = [f.TaskId] };

    [Fact]
    public async Task Learning_Report_Should_Capture_Confirmed_Scoring_Without_Answers_And_Build_One_Atomic_Class_Version_Without_A_User_Principal()
    {
        var f = await GradingFixtureAsync(false); var gradeId = await EvidenceGradeAsync(f); EducationLearningReportJobDto requested;
        using (User(f.Source.School.Reader))
        {
            await Evidence.SaveEvidenceQualificationAsync(gradeId, IndependentEvidence());
            var input = ReportInput(f); requested = await Reports.RequestAsync(input);
            (await Reports.RequestAsync(input)).Id.ShouldBe(requested.Id);
            requested.Status.ShouldBe(EducationLearningReportJobStatus.Queued);
            (await Reports.GetAsync(requested.ReportId)).Summary.ShouldBeNull();
        }
        using (GetRequiredService<ICurrentPrincipalAccessor>().Change(new ClaimsPrincipal(new ClaimsIdentity())))
            await ReportProcessor.ProcessAsync(requested.Id);
        using (User(f.Source.School.Reader))
        {
            var report = await Reports.GetAsync(requested.ReportId);
            report.CurrentJob!.Status.ShouldBe(EducationLearningReportJobStatus.Completed); report.SourceIsCurrent.ShouldBeTrue();
            report.CurrentSnapshotId.ShouldBe(requested.Id); report.SnapshotGeneration.ShouldBe(1);
            report.Summary!.RuleMissing.ShouldBeTrue(); report.Summary.Points.All(x => x.Ratio == 1 && x.InsufficientEvidenceCount == 1 && x.UnassessedCount == 1).ShouldBeTrue();
            var people = await Reports.GetStudentsAsync(report.Id, requested.Id, new()); people.TotalCount.ShouldBe(2);
            people.Items.Single(x => x.Student.StudentUserId == f.Absent.UserId).NotStartedCount.ShouldBe(1);
            var detail = await Reports.GetStudentAsync(report.Id, requested.Id, f.Source.Student.UserId);
            detail.Analysis.Points.All(x => x.State == EducationMasteryState.InsufficientEvidence && x.Ratio == 1).ShouldBeTrue();
            detail.Assessments.Single().TotalScore.ShouldBe(10);
            var basis = await Reports.GetPointEvidenceAsync(report.Id, requested.Id, f.Source.Student.UserId, detail.Analysis.Points[0].Point.KnowledgePointRevisionId, new());
            basis.Items.Single().QuestionGradeId.ShouldBe(gradeId); basis.Items.Single().ExclusionReason.ShouldBe(EducationLearningExclusionReason.None);
            var absent = await Reports.GetStudentAsync(report.Id, requested.Id, f.Absent.UserId);
            absent.Analysis.Points.All(x => x.Ratio == null && x.State == EducationMasteryState.Unassessed).ShouldBeTrue();
            await ReportProcessor.ProcessAsync(requested.Id);
        }
        await WithUnitOfWorkAsync(async () =>
        {
            (await Repo<EducationLearningReportSnapshot>().GetCountAsync()).ShouldBe(1); (await Repo<EducationLearningReportStudentSnapshot>().GetCountAsync()).ShouldBe(2);
            var job = await Repo<EducationLearningReportJob>().GetAsync(requested.Id);
            job.SourceJson.ShouldNotContain("正确甲"); job.SourceJson.ShouldNotContain("correctAnswers"); job.SourceJson.ShouldNotContain("responseJson");
            job.SourceJson.ShouldNotContain("reason\""); job.SourceJson.ShouldNotContain("PRIVATE-REFERENCE");
        });
    }

    [Fact]
    public async Task Learning_Report_Should_Hide_Outdated_Results_And_Recompute_A_Corrected_Grade_Exactly_Once()
    {
        var f = await GradingFixtureAsync(false); var gradeId = await EvidenceGradeAsync(f); EducationLearningReportJobDto first;
        using (User(f.Source.School.Reader)) { await Evidence.SaveEvidenceQualificationAsync(gradeId, IndependentEvidence()); first = await Reports.RequestAsync(ReportInput(f)); }
        await ReportProcessor.ProcessAsync(first.Id);
        using (User(f.Source.School.Reader))
        {
            await ConfirmManualGradeAsync(f.SubmissionId, 1, "核对原始作答后更正得分");
            var stale = await Reports.GetAsync(first.ReportId); stale.SourceIsCurrent.ShouldBeFalse(); stale.Summary.ShouldBeNull(); stale.Coverage.ShouldBeNull(); stale.CurrentSnapshotId.ShouldBeNull();
            (await Should.ThrowAsync<BusinessException>(() => Reports.GetStudentAsync(first.ReportId, first.Id, f.Source.Student.UserId))).Code.ShouldBe(EducationLearningReportErrors.SourceChanged);
        }
        (await ReportProcessor.RefreshChangedAsync(first.ReportId)).ShouldBeTrue();
        (await ReportProcessor.RefreshChangedAsync(first.ReportId)).ShouldBeFalse();
        var next = await WithUnitOfWorkAsync(async () => { var root = await Repo<EducationLearningReport>().GetAsync(first.ReportId); root.Generation.ShouldBe(2); return root.CurrentJobId!.Value; });
        await ReportProcessor.ProcessAsync(next);
        using (User(f.Source.School.Reader))
        {
            var report = await Reports.GetAsync(first.ReportId); report.SourceIsCurrent.ShouldBeTrue(); report.SnapshotGeneration.ShouldBe(2);
            var detail = await Reports.GetStudentAsync(first.ReportId, next, f.Source.Student.UserId); detail.Assessments.Single().TotalScore.ShouldBe(8);
            detail.Analysis.Points.Sum(x => x.ObtainedScore).ShouldBe(8); detail.Analysis.Points.Sum(x => x.MaxScore).ShouldBe(10);
            await Should.ThrowAsync<BusinessException>(() => Reports.GetStudentsAsync(first.ReportId, first.Id, new()));
            (await Reports.GetOperationAsync(first.ReportId, first.Id))!.Id.ShouldBe(first.Id);
        }
        await WithUnitOfWorkAsync(async () => (await Repo<EducationLearningReportSnapshot>().GetCountAsync()).ShouldBe(2));
    }

    [Fact]
    public async Task Learning_Report_Should_Reject_Other_Teachers_Students_And_Stale_Refresh_Commands()
    {
        var f = await GradingFixtureAsync(false); await EvidenceGradeAsync(f); EducationLearningReportJobDto job; EducationLearningReportDto before;
        using (User(f.Source.School.Reader)) { job = await Reports.RequestAsync(ReportInput(f)); before = await Reports.GetAsync(job.ReportId); }
        using (User(f.Source.School.Owner)) await Should.ThrowAsync<AbpAuthorizationException>(() => Reports.GetAsync(job.ReportId));
        using (User(f.Source.Student.UserId)) await Should.ThrowAsync<AbpAuthorizationException>(() => Reports.GetOperationAsync(job.ReportId, job.Id));
        using (User(f.Source.School.Reader))
        {
            var input = new RefreshEducationLearningReportDto { OperationId = Guid.NewGuid(), ConcurrencyStamp = before.ConcurrencyStamp, ExpectedCurrentJobId = job.Id, Scope = before.Scope };
            var newer = await Reports.RefreshAsync(job.ReportId, input); (await Reports.RefreshAsync(job.ReportId, input)).Id.ShouldBe(newer.Id);
            input.OperationId = Guid.NewGuid(); await Should.ThrowAsync<Volo.Abp.Data.AbpDbConcurrencyException>(() => Reports.RefreshAsync(job.ReportId, input));
            await ReportProcessor.ProcessAsync(job.Id);
            (await Reports.GetOperationAsync(job.ReportId, job.Id))!.Status.ShouldBe(EducationLearningReportJobStatus.Superseded);
        }
    }

    [Fact]
    public async Task Learning_Report_Should_Keep_Unconfirmed_Subjective_Grades_Null_And_Never_Turn_Pending_Into_Zero()
    {
        var f = await GradingFixtureAsync(true); EducationLearningReportJobDto job;
        using (User(f.Source.School.Reader)) job = await Reports.RequestAsync(ReportInput(f));
        await ReportProcessor.ProcessAsync(job.Id);
        using (User(f.Source.School.Reader))
        {
            var result = await Reports.GetStudentAsync(job.ReportId, job.Id, f.Source.Student.UserId);
            result.Assessments.Single().State.ShouldBe(EducationLearningAssessmentState.PendingGrading);
            result.Assessments.Single().TotalScore.ShouldBeNull();
            result.Analysis.Points.All(x => x.Ratio == null && x.State == EducationMasteryState.Unassessed).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Learning_Report_Should_Use_Minimal_Earlier_Attempts_Outside_Selected_Tasks_For_Known_Original_Groups()
    {
        var first = await GradingFixtureAsync(false); var second = await GradingFixtureAsync(false, sourceOverride: first.Source);
        var a = await EvidenceGradeAsync(first); var b = await EvidenceGradeAsync(second); EducationLearningReportJobDto job;
        using (User(first.Source.School.Reader))
        {
            await Evidence.SaveEvidenceQualificationAsync(b, IndependentEvidence());
            await Evidence.SaveOriginRelationAsync(a, new() { OperationId = Guid.NewGuid(), SecondQuestionGradeId = b, IsEnabled = true, Reason = "两份冻结题属于同一原题" });
            job = await Reports.RequestAsync(ReportInput(second));
        }
        await ReportProcessor.ProcessAsync(job.Id);
        using (User(first.Source.School.Reader))
        {
            var result = await Reports.GetStudentAsync(job.ReportId, job.Id, first.Source.Student.UserId);
            result.Assessments.Single().TaskId.ShouldBe(second.TaskId);
            result.Analysis.Evidence.Single().ExclusionReason.ShouldBe(EducationLearningExclusionReason.EarlierAttempt);
            result.Analysis.Points.All(x => x.Ratio == null && x.IndependentQuestionCount == 0).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Learning_Report_Should_Recover_A_Failed_Captured_Source_With_A_New_Linked_Job_And_Reject_Expired_Run_Ownership()
    {
        var f = await GradingFixtureAsync(false); await EvidenceGradeAsync(f); EducationLearningReportJobDto first;
        using (User(f.Source.School.Reader)) first = await Reports.RequestAsync(ReportInput(f));
        await WithUnitOfWorkAsync(async () =>
        {
            var job = await Repo<EducationLearningReportJob>().GetAsync(first.Id); var run = Guid.NewGuid(); var now = DateTime.UtcNow;
            job.Claim(run, now);
            Should.Throw<BusinessException>(() => job.Heartbeat(Guid.NewGuid(), 0, now));
            Should.Throw<BusinessException>(() => job.Heartbeat(run, 0, now.AddMinutes(2)));
            job.Fail(run, EducationLearningReportFailureStage.Calculate, false, now);
            Should.Throw<BusinessException>(() => job.Claim(Guid.NewGuid(), now));
            await Repo<EducationLearningReportJob>().UpdateManyAsync([job], autoSave: true);
        });
        EducationLearningReportJobDto recovered;
        using (User(f.Source.School.Reader))
        {
            var report = await Reports.GetAsync(first.ReportId);
            var input = new RecoverEducationLearningReportDto { OperationId = Guid.NewGuid(), ConcurrencyStamp = report.ConcurrencyStamp, ExpectedCurrentJobId = first.Id };
            recovered = await Reports.RecoverAsync(report.Id, input); recovered.RetryOfJobId.ShouldBe(first.Id);
            recovered.SourceFingerprint.ShouldBe(first.SourceFingerprint); recovered.Generation.ShouldBe(2);
            (await Reports.RecoverAsync(report.Id, input)).Id.ShouldBe(recovered.Id);
        }
        await ReportProcessor.ProcessAsync(first.Id); await ReportProcessor.ProcessAsync(recovered.Id);
        using (User(f.Source.School.Reader)) (await Reports.GetAsync(first.ReportId)).CurrentSnapshotId.ShouldBe(recovered.Id);
        await WithUnitOfWorkAsync(async () => (await Repo<EducationLearningReportSnapshot>().GetCountAsync()).ShouldBe(1));
    }

    [Fact]
    public async Task Learning_Report_Rule_Should_Require_Real_Manager_Explicit_Policy_And_Produce_Versioned_Reclassification()
    {
        var f = await GradingFixtureAsync(false); var school = f.Source.School; var grade = await EvidenceGradeAsync(f);
        var subjectId = await WithUnitOfWorkAsync(async () => (await Repo<TextbookVersion>().GetAsync(school.BookId)).EducationSubjectId);
        using (User(school.Reader)) await Should.ThrowAsync<AbpAuthorizationException>(() => MasteryRules.GetAsync(school.SchoolId, subjectId));
        await WithUnitOfWorkAsync(async () => await Repo<SchoolMembership>().InsertAsync(new(Guid.NewGuid(), null, school.SchoolId, school.Owner, SchoolMemberRole.Manager, DateTime.UtcNow.AddMinutes(-1)), autoSave: true));
        var input = new SaveEducationMasteryRuleDto { OperationId = Guid.NewGuid(), IsEnabled = true, WindowDays = 30, MinimumIndependentQuestionCount = 1,
            MinimumAssessmentCount = 1, MaximumLatestEvidenceAgeDays = 30, TargetRatio = 0.8m, Reason = "学校确认采用的试点学科规则" };
        EducationMasteryRuleRevisionDto first;
        using (User(school.Owner))
        {
            (await MasteryRules.GetAsync(school.SchoolId, subjectId)).Current.ShouldBeNull();
            await Should.ThrowAsync<BusinessException>(() => MasteryRules.SaveAsync(school.SchoolId, subjectId, input));
            input.ConfirmSchoolPolicy = true; input.TargetRatio = 0.80001m;
            await Should.ThrowAsync<BusinessException>(() => MasteryRules.SaveAsync(school.SchoolId, subjectId, input));
            input.TargetRatio = 0.8m; first = await MasteryRules.SaveAsync(school.SchoolId, subjectId, input);
            (await MasteryRules.SaveAsync(school.SchoolId, subjectId, input)).Id.ShouldBe(first.Id);
        }
        EducationLearningReportJobDto job;
        using (User(school.Reader)) { await Evidence.SaveEvidenceQualificationAsync(grade, IndependentEvidence()); job = await Reports.RequestAsync(ReportInput(f)); }
        await ReportProcessor.ProcessAsync(job.Id);
        using (User(school.Reader)) {
            var result = await Reports.GetStudentAsync(job.ReportId, job.Id, f.Source.Student.UserId);
            result.Analysis.Points.All(x => x.State == EducationMasteryState.CurrentTargetMet).ShouldBeTrue(); result.Analysis.RuleRevisionId.ShouldBe(first.Id);
        }
        using (User(school.Owner))
        {
            var before = await MasteryRules.GetAsync(school.SchoolId, subjectId);
            var disabled = new SaveEducationMasteryRuleDto { OperationId = Guid.NewGuid(), ConcurrencyStamp = before.ConcurrencyStamp, ExpectedRevisionId = first.Id,
                IsEnabled = false, WindowDays = 30, MinimumIndependentQuestionCount = 1, MinimumAssessmentCount = 1, MaximumLatestEvidenceAgeDays = 30,
                TargetRatio = .8m, ConfirmSchoolPolicy = true, Reason = "暂停试点等待重新校准" };
            (await MasteryRules.SaveAsync(school.SchoolId, subjectId, disabled)).RevisionNumber.ShouldBe(2);
            (await MasteryRules.GetOperationAsync(school.SchoolId, subjectId, first.Id))!.IsEnabled.ShouldBeTrue();
            disabled.OperationId = Guid.NewGuid(); await Should.ThrowAsync<Volo.Abp.Data.AbpDbConcurrencyException>(() => MasteryRules.SaveAsync(school.SchoolId, subjectId, disabled));
        }
        using (User(school.Reader)) (await Reports.GetAsync(job.ReportId)).Summary.ShouldBeNull();
        (await ReportProcessor.RefreshChangedAsync(job.ReportId)).ShouldBeTrue();
        var next = await WithUnitOfWorkAsync(async () => (await Repo<EducationLearningReport>().GetAsync(job.ReportId)).CurrentJobId!.Value);
        await ReportProcessor.ProcessAsync(next);
        using (User(school.Reader)) {
            var report = await Reports.GetAsync(job.ReportId); report.Summary!.RuleMissing.ShouldBeTrue(); report.Summary.RuleDecisionRevisionNumber.ShouldBe(2);
            (await Reports.GetStudentAsync(report.Id, next, f.Source.Student.UserId)).Analysis.Points.All(x => x.State == EducationMasteryState.InsufficientEvidence).ShouldBeTrue();
        }
    }
}
