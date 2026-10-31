using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Education;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    [Fact]
    public async Task Teacher_Trend_Should_Page_Whole_Confirmed_Papers_And_Filter_Point_Evidence_To_The_Selected_Assessment()
    {
        var first = await GradingFixtureAsync(false); var second = await GradingFixtureAsync(false, sourceOverride: first.Source);
        await EvidenceGradeAsync(first); await EvidenceGradeAsync(second); EducationLearningReportJobDto job;
        using (User(first.Source.School.Reader)) {
            await ConfirmManualGradeAsync(second.SubmissionId, 1, "按评分点核对后更正");
            var input = ReportInput(first); input.TaskIds.Add(second.TaskId); job = await Reports.RequestAsync(input);
        }
        await ReportProcessor.ProcessAsync(job.Id);
        var scope = await WithUnitOfWorkAsync(async () => (await Repo<EducationLearningReport>().GetAsync(job.ReportId)).Authority());
        using (User(first.Source.School.Reader)) {
            var page = await Reports.GetTrendAsync(job.ReportId, job.Id, scope.SchoolId, scope.TeachingAssignmentId, new() { MaxResultCount = 1 });
            page.TotalCount.ShouldBe(2); page.Items.Single().Score.ShouldBe(10); page.Items.Single().ScoredCount.ShouldBe(1); page.Items.Single().AssignedCount.ShouldBe(2);
            var next = await Reports.GetTrendAsync(job.ReportId, job.Id, scope.SchoolId, scope.TeachingAssignmentId, new() { SkipCount = 1, MaxResultCount = 1 });
            var row = next.Items.Single(); row.Score.ShouldBe(8); row.ComparedWithTaskId.ShouldBe(first.TaskId);
            row.ComparisonLimit.HasFlag(EducationLearningTrendLimit.MissingScore).ShouldBeTrue(); row.ComparisonLimit.HasFlag(EducationLearningTrendLimit.Cohort).ShouldBeTrue(); row.ComparisonLimit.HasFlag(EducationLearningTrendLimit.Scoring).ShouldBeTrue();
            var student = await Reports.GetTrendAsync(job.ReportId, job.Id, scope.SchoolId, scope.TeachingAssignmentId, new() { StudentUserId = first.Source.Student.UserId });
            student.Student!.Id.ShouldBe(first.Source.Student.UserId); student.Items.Select(x => x.Score).ShouldBe([10m, 8m]); student.Items.All(x => x.AssignedCount == 1 && x.ScoredCount == 1).ShouldBeTrue();
            student.Items[1].ComparisonLimit.HasFlag(EducationLearningTrendLimit.Cohort).ShouldBeFalse();
            var person = await Reports.GetStudentAsync(job.ReportId, job.Id, first.Source.Student.UserId); var point = person.Analysis.Points[0].Point.KnowledgePointRevisionId;
            var all = await Reports.GetPointEvidenceAsync(job.ReportId, job.Id, first.Source.Student.UserId, point, new()); all.TotalCount.ShouldBe(2);
            var exact = await Reports.GetPointEvidenceAsync(job.ReportId, job.Id, first.Source.Student.UserId, point, new() { MaxResultCount = 1 }, second.TaskId);
            exact.TotalCount.ShouldBe(1); exact.Items.Single().TaskId.ShouldBe(second.TaskId);
            (await Reports.GetPointEvidenceAsync(job.ReportId, job.Id, first.Source.Student.UserId, point, new() { SkipCount = 1 }, second.TaskId)).Items.ShouldBeEmpty();
            await Should.ThrowAsync<AbpAuthorizationException>(() => Reports.GetPointEvidenceAsync(job.ReportId, job.Id, first.Source.Student.UserId, point, new(), Guid.NewGuid()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Reports.GetTrendAsync(job.ReportId, job.Id, Guid.NewGuid(), scope.TeachingAssignmentId, new()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Reports.GetTrendAsync(job.ReportId, job.Id, scope.SchoolId, Guid.NewGuid(), new()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Reports.GetTrendAsync(job.ReportId, job.Id, scope.SchoolId, scope.TeachingAssignmentId, new() { StudentUserId = Guid.NewGuid() }));
            await Should.ThrowAsync<BusinessException>(() => Reports.GetTrendAsync(job.ReportId, Guid.NewGuid(), scope.SchoolId, scope.TeachingAssignmentId, new()));
            await ConfirmManualGradeAsync(second.SubmissionId, 2, "再次核对后更正");
            (await Should.ThrowAsync<BusinessException>(() => Reports.GetTrendAsync(job.ReportId, job.Id, scope.SchoolId, scope.TeachingAssignmentId, new()))).Code.ShouldBe(EducationLearningReportErrors.SourceChanged);
        }
        using (User(first.Source.School.Owner)) await Should.ThrowAsync<AbpAuthorizationException>(() => Reports.GetTrendAsync(job.ReportId, job.Id, scope.SchoolId, scope.TeachingAssignmentId, new()));
        using (User(first.Source.Student.UserId)) {
            await Should.ThrowAsync<AbpAuthorizationException>(() => Reports.GetTrendAsync(job.ReportId, job.Id, scope.SchoolId, scope.TeachingAssignmentId, new()));
            var hidden = await Grades.GetMyResultAsync(first.RecipientId); hidden.TotalScore.ShouldBeNull(); hidden.CurrentResultVisibility.ShouldBe(KnowPulse.Exam.ExamResultVisibility.Hidden); hidden.Questions.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Teacher_Trend_Should_Keep_Unconfirmed_Subjective_Marks_Missing_And_Read_The_Previous_Current_Snapshot_During_Recalculation()
    {
        var f = await GradingFixtureAsync(true); EducationLearningReportJobDto job;
        using (User(f.Source.School.Reader)) job = await Reports.RequestAsync(ReportInput(f));
        await ReportProcessor.ProcessAsync(job.Id);
        using (User(f.Source.School.Reader)) {
            var report = await Reports.GetAsync(job.ReportId);
            await Reports.RefreshAsync(report.Id, new() { OperationId = Guid.NewGuid(), ConcurrencyStamp = report.ConcurrencyStamp, ExpectedCurrentJobId = job.Id, Scope = report.Scope });
            var trend = await Reports.GetTrendAsync(job.ReportId, job.Id, report.SchoolId, report.TeachingAssignmentId, new() { StudentUserId = f.Source.Student.UserId });
            var row = trend.Items.Single(); row.Score.ShouldBeNull(); row.Ratio.ShouldBeNull(); row.MaxScore.ShouldBe(10); row.PendingGradingCount.ShouldBe(1); row.ScoredCount.ShouldBe(0);
            row.ComparisonLimit.HasFlag(EducationLearningTrendLimit.MissingScore).ShouldBeTrue();
        }
    }
}
