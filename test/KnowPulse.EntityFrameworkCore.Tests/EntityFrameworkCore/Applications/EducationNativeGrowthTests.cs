using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.Exam;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    [Fact]
    public async Task Native_Growth_Should_Filter_School_Before_Projection_And_Preserve_Exact_Legacy_Snapshot()
    {
        var f = await GradingFixtureAsync(false); await EvidenceGradeAsync(f); await ExposureReleaseAsync(f, ExamResultVisibility.ScoreOnly);
        var otherSchool = new School(Guid.NewGuid(), null, "NATIVE-GROWTH-OTHER", "学生另一个学校");
        await WithUnitOfWorkAsync(async () => {
            await Repo<School>().InsertAsync(otherSchool, autoSave: true);
            await Repo<SchoolMembership>().InsertAsync(new(Guid.NewGuid(), null, otherSchool.Id, f.Source.Student.UserId, SchoolMemberRole.Student, DateTime.UtcNow.AddMinutes(-1)), autoSave: true);
        });
        using (User(f.Source.Student.UserId))
        {
            var contexts = await Growth.GetMyContextsForSchoolAsync(f.Source.School.SchoolId); contexts.SchoolId.ShouldBe(f.Source.School.SchoolId);
            var context = contexts.Items.Single().Id; var window = GrowthWindow(); var original = await Growth.GetMyGrowthAsync(context, window);
            var scoped = await Growth.GetMyGrowthForSchoolAsync(context, f.Source.School.SchoolId, window);
            JsonSerializer.Serialize(scoped.Value).ShouldBe(JsonSerializer.Serialize(original));
            (await Growth.GetMyContextsForSchoolAsync(otherSchool.Id)).Items.ShouldBeEmpty();
            await Should.ThrowAsync<AbpAuthorizationException>(() => Growth.GetMyGrowthForSchoolAsync(context, otherSchool.Id, window));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Growth.GetMyPointEvidenceForSchoolAsync(context, otherSchool.Id, scoped.Value.SnapshotId, scoped.Value.Points[0].Id, new()));
            var basis = await Growth.GetMyPointEvidenceForSchoolAsync(context, f.Source.School.SchoolId, scoped.Value.SnapshotId, scoped.Value.Points[0].Id, new());
            basis.Evidence.Items.ShouldBeEmpty(); basis.Evidence.HasRestrictedEvidence.ShouldBeTrue();
        }
        (await WithUnitOfWorkAsync(() => Repo<EducationStudentGrowthSnapshot>().GetCountAsync())).ShouldBe(1);
        (await WithUnitOfWorkAsync(() => Repo<EducationQuestionExposure>().GetCountAsync())).ShouldBe(0);
    }
    [Fact]
    public async Task Native_Growth_And_Original_Result_Should_Reject_Changed_Snapshots_And_Release_Before_Exposure()
    {
        var f = await GradingFixtureAsync(false); await EvidenceGradeAsync(f); var release = await ExposureReleaseAsync(f, ExamResultVisibility.FullReview);
        var window = GrowthWindow(); Guid context; EducationStudentGrowthValue first;
        using (User(f.Source.Student.UserId))
        {
            context = (await Growth.GetMyContextsForSchoolAsync(f.Source.School.SchoolId)).Items.Single().Id;
            first = (await Growth.GetMyGrowthForSchoolAsync(context, f.Source.School.SchoolId, window)).Value;
            (await Growth.GetMyPointEvidenceForSchoolAsync(context, f.Source.School.SchoolId, first.SnapshotId, first.Points[0].Id, new())).Evidence.Items.Count.ShouldBe(1);
            (await Should.ThrowAsync<BusinessException>(() => Grades.GetMyResultForSchoolAsync(f.RecipientId, f.Source.School.SchoolId, expectedResultReleaseId: Guid.NewGuid())))
                .Code.ShouldBe(EducationLearningReportErrors.SourceChanged);
        }
        (await WithUnitOfWorkAsync(() => Repo<EducationQuestionExposure>().GetCountAsync())).ShouldBe(0);
        using (User(f.Source.School.Reader))
        {
            var changed = ReleaseInput(await Grades.GetGradingOverviewAsync(f.TaskId), release.ClosureId, ExamResultVisibility.ScoreOnly);
            changed.Reason = "关闭本次逐题讲评，保留已开放成绩";
            await Grades.PublishResultsAsync(f.TaskId, changed);
        }
        using (User(f.Source.Student.UserId))
        {
            (await Should.ThrowAsync<BusinessException>(() => Growth.GetMyPointEvidenceForSchoolAsync(context, f.Source.School.SchoolId, first.SnapshotId, first.Points[0].Id, new())))
                .Code.ShouldBe(EducationLearningReportErrors.SourceChanged);
            (await Should.ThrowAsync<BusinessException>(() => Grades.GetMyResultForSchoolAsync(f.RecipientId, f.Source.School.SchoolId, expectedResultReleaseId: release.Id)))
                .Code.ShouldBe(EducationLearningReportErrors.SourceChanged);
        }
        (await WithUnitOfWorkAsync(() => Repo<EducationQuestionExposure>().GetCountAsync())).ShouldBe(0);
    }
}
