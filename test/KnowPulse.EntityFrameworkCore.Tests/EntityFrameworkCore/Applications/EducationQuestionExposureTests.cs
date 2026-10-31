using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.Exam;
using Shouldly;
using Volo.Abp.Authorization;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    private async Task<EducationAssessmentResultReleaseDto> ExposureReleaseAsync(GradingFixture f, ExamResultVisibility visibility)
    {
        using var teacher = User(f.Source.School.Reader);
        await Grades.GetSubmissionForGradingAsync(f.SubmissionId);
        var closure = await Grades.CloseTaskAsync(f.TaskId, CloseInput(await Grades.PreviewFinalizationAsync(f.TaskId)));
        return await Grades.PublishResultsAsync(f.TaskId, ReleaseInput(await Grades.GetGradingOverviewAsync(f.TaskId), closure.Id, visibility));
    }

    [Theory]
    [InlineData(ExamResultVisibility.Hidden)]
    [InlineData(ExamResultVisibility.ScoreOnly)]
    [InlineData(ExamResultVisibility.FullReview)]
    public async Task Question_Exposure_Should_Record_Only_Authorized_Full_Review_And_Preserve_The_First_Return_Time(ExamResultVisibility visibility)
    {
        var f = await GradingFixtureAsync(false); var release = await ExposureReleaseAsync(f, visibility);
        await WithUnitOfWorkAsync(async () => (await Repo<EducationQuestionExposure>().GetCountAsync()).ShouldBe(0));
        using (User(f.Source.Student.UserId))
        {
            await Grades.GetMyResultAsync(f.RecipientId);
            DateTime? first = await WithUnitOfWorkAsync(async () =>
                (await Repo<EducationQuestionExposure>().GetListAsync(x => x.TaskId == f.TaskId)).SingleOrDefault()?.ExposedAtUtc);
            await Grades.GetMyResultAsync(f.RecipientId);
            await WithUnitOfWorkAsync(async () =>
            {
                var rows = await Repo<EducationQuestionExposure>().GetListAsync(x => x.TaskId == f.TaskId);
                rows.Count.ShouldBe(visibility == ExamResultVisibility.FullReview ? 1 : 0);
                if (rows.Count == 0) return;
                rows.Single().ResultReleaseId.ShouldBe(release.Id); rows.Single().StudentUserId.ShouldBe(f.Source.Student.UserId);
                rows.Single().ExposedAtUtc.ShouldBe(first!.Value); rows.Single().ExposedAtUtc.Kind.ShouldBe(DateTimeKind.Utc);
                rows.Single().Source.ShouldBe(EducationQuestionExposureSource.AssessmentReview); rows.Single().FeedbackRevisionId.ShouldBeNull();
                rows.Single().ToLearningEvidence().Use.ShouldBe(EducationLearningExposureUse.Confirmed);
            });
        }
    }

    [Fact]
    public async Task Question_Exposure_Should_Record_Each_Linked_Assessment_When_The_Student_Actually_Opens_Feedback()
    {
        var f = await GradingFixtureAsync(false); var second = await FeedbackSecondAssessmentAsync(f);
        await ExposureReleaseAsync(f, ExamResultVisibility.FullReview);
        Guid commentId = Guid.NewGuid(); Guid feedbackVersion;
        using (User(f.Source.School.Reader))
        {
            var closure = await Grades.CloseTaskAsync(second.TaskId, CloseInput(await Grades.PreviewFinalizationAsync(second.TaskId)));
            await Grades.PublishResultsAsync(second.TaskId, ReleaseInput(await Grades.GetGradingOverviewAsync(second.TaskId), closure.Id, ExamResultVisibility.FullReview));
            var draft = FeedbackInput(f); draft.Content.AssessmentTaskIds = [f.TaskId, second.TaskId];
            await Feedback.SaveDraftAsync(commentId, draft); feedbackVersion = (await PublishFeedbackAsync(commentId)).Id;
        }
        using (User(f.Source.Student.UserId))
        {
            await Feedback.GetMyListAsync(new()); await Feedback.GetMyNoticesAsync(new());
            await WithUnitOfWorkAsync(async () => (await Repo<EducationQuestionExposure>().GetCountAsync()).ShouldBe(0));
            await Feedback.OpenMyAsync(commentId, feedbackVersion); await Feedback.OpenMyAsync(commentId, feedbackVersion);
        }
        await WithUnitOfWorkAsync(async () =>
        {
            var rows = await Repo<EducationQuestionExposure>().GetListAsync(); rows.Count.ShouldBe(2);
            rows.All(x => x.Source == EducationQuestionExposureSource.StudentFeedback && x.FeedbackId == commentId && x.FeedbackRevisionId == feedbackVersion).ShouldBeTrue();
            rows.All(x => x.ToLearningEvidence().Use == EducationLearningExposureUse.Unverified).ShouldBeTrue();
            rows.Select(x => x.TaskId).Order().ShouldBe(new[] { f.TaskId, second.TaskId }.Order());
        });
    }

    [Fact]
    public async Task Question_Exposure_Should_Preserve_Old_Exposure_When_A_Corrected_Result_Is_Opened()
    {
        var f = await GradingFixtureAsync(false); var release = await ExposureReleaseAsync(f, ExamResultVisibility.FullReview);
        using (User(f.Source.Student.UserId)) await Grades.GetMyResultAsync(f.RecipientId);
        var first = await WithUnitOfWorkAsync(async () => (await Repo<EducationQuestionExposure>().GetListAsync()).Single());
        using (User(f.Source.School.Reader)) await ConfirmManualGradeAsync(f.SubmissionId, 1, "复核扣分后仍保留先前的答案曝光事实");
        using (User(f.Source.Student.UserId)) await Grades.GetMyResultAsync(f.RecipientId);
        await WithUnitOfWorkAsync(async () =>
        {
            var rows = await Repo<EducationQuestionExposure>().GetListAsync(); rows.Count.ShouldBe(2);
            rows.Select(x => x.OriginalQuestionId).Distinct().Count().ShouldBe(1);
            var original = rows.Single(x => x.ResultReleaseId == release.Id); original.Id.ShouldBe(first.Id); original.ExposedAtUtc.ShouldBe(first.ExposedAtUtc);
            rows.Single(x => x.Id != first.Id).ExposedAtUtc.ShouldBeGreaterThanOrEqualTo(first.ExposedAtUtc);
        });
    }

    [Fact]
    public async Task Question_Exposure_Recorder_Should_Not_Attribute_Review_To_Another_Student()
    {
        var f = await GradingFixtureAsync(false); var release = await ExposureReleaseAsync(f, ExamResultVisibility.FullReview);
        using var other = User(f.Absent.UserId);
        await WithUnitOfWorkAsync(async () =>
        {
            var recipient = await Repo<EducationAssessmentRecipient>().GetAsync(f.RecipientId);
            var published = await Repo<EducationAssessmentResultRelease>().GetAsync(release.Id);
            await Should.ThrowAsync<AbpAuthorizationException>(() => GetRequiredService<EducationQuestionExposureRecorder>().RecordAsync(recipient, published));
            (await Repo<EducationQuestionExposure>().GetCountAsync()).ShouldBe(0);
        });
    }
}
