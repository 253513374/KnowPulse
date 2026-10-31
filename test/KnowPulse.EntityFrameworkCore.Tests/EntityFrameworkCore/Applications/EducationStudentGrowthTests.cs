using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.Exam;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    private IEducationStudentGrowthAppService Growth => GetRequiredService<IEducationStudentGrowthAppService>();
    private static GetEducationStudentGrowthInput GrowthWindow() => new() { WindowStartsAtUtc = DateTime.UtcNow.AddDays(-1), WindowEndsAtUtc = DateTime.UtcNow };

    [Fact]
    public async Task Student_Growth_Should_Use_Only_Open_Official_Scores_And_Gate_Question_Basis_By_Review_Without_Requiring_A_Teacher_Report()
    {
        var f = await GradingFixtureAsync(false); var grade = await EvidenceGradeAsync(f);
        using (User(f.Source.School.Reader)) await Evidence.SaveEvidenceQualificationAsync(grade, IndependentEvidence());
        using (User(f.Source.Student.UserId)) (await Growth.GetMyContextsAsync()).Items.ShouldBeEmpty();
        var hidden = await ExposureReleaseAsync(f, ExamResultVisibility.Hidden);
        using (User(f.Source.Student.UserId)) (await Growth.GetMyContextsAsync()).Items.ShouldBeEmpty();
        using (User(f.Source.School.Reader)) {
            var input = ReleaseInput(await Grades.GetGradingOverviewAsync(f.TaskId), hidden.ClosureId, ExamResultVisibility.ScoreOnly);
            input.Reason = "开放本人正式成绩"; await Grades.PublishResultsAsync(f.TaskId, input);
        }
        var window = GrowthWindow(); Guid contextId; EducationStudentGrowthValue first;
        using (User(f.Source.Student.UserId))
        {
            contextId = (await Growth.GetMyContextsAsync()).Items.Single().Id;
            first = await Growth.GetMyGrowthAsync(contextId, window);
            first.Assessments.Single().TotalScore.ShouldBe(10); first.Points.Count.ShouldBe(2);
            first.Points.All(x => x.Ratio == 1 && x.State == EducationMasteryState.InsufficientEvidence).ShouldBeTrue();
            var locked = await Growth.GetMyPointEvidenceAsync(contextId, first.SnapshotId, first.Points[0].Id, new());
            locked.Items.ShouldBeEmpty(); locked.TotalCount.ShouldBe(0); locked.HasRestrictedEvidence.ShouldBeTrue();
            var replay = await Growth.GetMyGrowthAsync(contextId, window); replay.SnapshotId.ShouldBe(first.SnapshotId); replay.CalculatedAtUtc.ShouldBe(first.CalculatedAtUtc);
            var json = JsonSerializer.Serialize(first); json.ShouldNotContain("正确甲"); json.ShouldNotContain("CorrectAnswers"); json.ShouldNotContain("GradeRevisionId"); json.ShouldNotContain("MappingId"); json.ShouldNotContain("TeacherUserId");
        }
        using (User(f.Source.School.Reader)) {
            var overview = await Grades.GetGradingOverviewAsync(f.TaskId);
            var input = ReleaseInput(overview, hidden.ClosureId, ExamResultVisibility.FullReview); input.Reason = "开放完整讲评";
            await Grades.PublishResultsAsync(f.TaskId, input);
        }
        using (User(f.Source.Student.UserId))
        {
            await Should.ThrowAsync<BusinessException>(() => Growth.GetMyPointEvidenceAsync(contextId, first.SnapshotId, first.Points[0].Id, new()));
            var reviewed = await Growth.GetMyGrowthAsync(contextId, window); reviewed.SnapshotId.ShouldNotBe(first.SnapshotId);
            var basis = await Growth.GetMyPointEvidenceAsync(contextId, reviewed.SnapshotId, reviewed.Points[0].Id, new());
            basis.Items.Single().PaperQuestionId.ShouldBe(f.Source.QuestionId); basis.HasRestrictedEvidence.ShouldBeFalse();
        }
        await WithUnitOfWorkAsync(async () => { (await Repo<EducationLearningReport>().GetCountAsync()).ShouldBe(0); (await Repo<EducationStudentGrowthSnapshot>().GetCountAsync()).ShouldBe(2); (await Repo<EducationQuestionExposure>().GetCountAsync()).ShouldBe(0); });
    }

    [Fact]
    public async Task Student_Growth_Should_Ignore_Hidden_Grade_And_Qualification_Changes_In_Both_Value_And_Public_Version()
    {
        var visible = await GradingFixtureAsync(false); var a = await EvidenceGradeAsync(visible);
        using (User(visible.Source.School.Reader)) await Evidence.SaveEvidenceQualificationAsync(a, IndependentEvidence());
        await ExposureReleaseAsync(visible, ExamResultVisibility.ScoreOnly);
        var window = GrowthWindow(); Guid contextId; EducationStudentGrowthValue first;
        using (User(visible.Source.Student.UserId)) { contextId = (await Growth.GetMyContextsAsync()).Items.Single().Id; first = await Growth.GetMyGrowthAsync(contextId, window); }
        var hidden = await GradingFixtureAsync(false, sourceOverride: visible.Source); var b = await EvidenceGradeAsync(hidden);
        using (User(visible.Source.School.Reader)) { await Evidence.SaveEvidenceQualificationAsync(b, IndependentEvidence()); await ConfirmManualGradeAsync(hidden.SubmissionId, 1, "仅教师可见的改分"); }
        await ExposureReleaseAsync(hidden, ExamResultVisibility.Hidden);
        using (User(visible.Source.Student.UserId))
        {
            (await Growth.GetMyContextsAsync()).Items.Count.ShouldBe(1);
            var after = await Growth.GetMyGrowthAsync(contextId, window); after.SnapshotId.ShouldBe(first.SnapshotId);
            JsonSerializer.Serialize(after).ShouldBe(JsonSerializer.Serialize(first));
        }
        await WithUnitOfWorkAsync(async () => (await Repo<EducationStudentGrowthSnapshot>().GetCountAsync()).ShouldBe(1));
    }

    [Fact]
    public async Task Student_Growth_Should_Replace_Corrected_Scores_And_Immediately_Block_A_Rehidden_Release_And_Cached_Version()
    {
        var f = await GradingFixtureAsync(false); var grade = await EvidenceGradeAsync(f);
        using (User(f.Source.School.Reader)) await Evidence.SaveEvidenceQualificationAsync(grade, IndependentEvidence());
        var released = await ExposureReleaseAsync(f, ExamResultVisibility.FullReview); var window = GrowthWindow(); Guid context; EducationStudentGrowthValue first;
        using (User(f.Source.Student.UserId)) { context = (await Growth.GetMyContextsAsync()).Items.Single().Id; first = await Growth.GetMyGrowthAsync(context, window); }
        using (User(f.Source.School.Reader)) await ConfirmManualGradeAsync(f.SubmissionId, 1, "正式更正需同步成长" );
        EducationStudentGrowthValue corrected;
        using (User(f.Source.Student.UserId))
        {
            corrected = await Growth.GetMyGrowthAsync(context, window); corrected.SnapshotId.ShouldNotBe(first.SnapshotId);
            corrected.Assessments.Single().TotalScore.ShouldBe(8); corrected.Points.Sum(x => x.ObtainedScore).ShouldBe(8); corrected.Points.Sum(x => x.MaxScore).ShouldBe(10);
            await Should.ThrowAsync<BusinessException>(() => Growth.GetMyPointEvidenceAsync(context, first.SnapshotId, first.Points[0].Id, new()));
        }
        using (User(f.Source.School.Reader)) {
            var input = ReleaseInput(await Grades.GetGradingOverviewAsync(f.TaskId), released.ClosureId, ExamResultVisibility.Hidden); input.Reason = "暂缓结果开放";
            await Grades.PublishResultsAsync(f.TaskId, input);
        }
        using (User(f.Source.Student.UserId)) {
            (await Growth.GetMyContextsAsync()).Items.ShouldBeEmpty(); await Should.ThrowAsync<AbpAuthorizationException>(() => Growth.GetMyGrowthAsync(context, window));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Growth.GetMyPointEvidenceAsync(context, corrected.SnapshotId, corrected.Points[0].Id, new()));
        }
        await WithUnitOfWorkAsync(async () => (await Repo<EducationStudentGrowthSnapshot>().GetCountAsync()).ShouldBe(2));
    }

    [Fact]
    public async Task Student_Growth_Should_Keep_Absence_Unassessed_And_Reject_Other_Students_Teachers_Tenants_And_Future_Windows()
    {
        var f = await GradingFixtureAsync(false); await ExposureReleaseAsync(f, ExamResultVisibility.FullReview);
        var window = GrowthWindow(); Guid context; EducationStudentGrowthValue first;
        using (User(f.Source.Student.UserId)) { context = (await Growth.GetMyContextsAsync()).Items.Single().Id; first = await Growth.GetMyGrowthAsync(context, window); }
        using (User(f.Absent.UserId)) {
            var ownContext = (await Growth.GetMyContextsAsync()).Items.Single().Id; ownContext.ShouldNotBe(context);
            var absent = await Growth.GetMyGrowthAsync(ownContext, window); absent.Assessments.Single().IsAbsent.ShouldBeTrue(); absent.Assessments.Single().TotalScore.ShouldBeNull();
            absent.Points.All(x => x.Ratio == null && x.State == EducationMasteryState.Unassessed).ShouldBeTrue();
            await Should.ThrowAsync<AbpAuthorizationException>(() => Growth.GetMyGrowthAsync(context, window));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Growth.GetMyPointEvidenceAsync(context, first.SnapshotId, first.Points[0].Id, new()));
        }
        using (User(f.Source.School.Reader)) await Should.ThrowAsync<AbpAuthorizationException>(() => Growth.GetMyGrowthAsync(context, window));
        using (User(f.Source.Student.UserId)) {
            var future = GrowthWindow(); future.WindowEndsAtUtc = DateTime.UtcNow.AddMinutes(1);
            await Should.ThrowAsync<BusinessException>(() => Growth.GetMyGrowthAsync(context, future));
            using (GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid())) await Should.ThrowAsync<AbpAuthorizationException>(() => Growth.GetMyPointEvidenceAsync(context, first.SnapshotId, first.Points[0].Id, new()));
        }
    }
}
