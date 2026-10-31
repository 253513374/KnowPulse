using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.Exam;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    private IEducationFeedbackAppService Feedback => GetRequiredService<IEducationFeedbackAppService>();

    [Fact]
    public async Task Feedback_Target_Context_Should_Resolve_Exact_Teaching_And_Student_Without_Creating_Feedback()
    {
        var f = await GradingFixtureAsync(false);
        using var actor = User(f.Source.School.Reader);
        var input = FeedbackInput(f);
        var target = await Feedback.GetTargetAsync(input);
        target.SchoolId.ShouldBe(f.Source.Revision.SchoolId);
        target.Teaching.Id.ShouldBe(input.TeachingAssignmentId);
        target.Student.StudentEnrollmentId.ShouldBe(input.StudentEnrollmentId);
        target.Student.SchoolMembershipId.ShouldBe(input.SchoolMembershipId);
        target.Student.StudentUserId.ShouldBe(f.Source.Student.UserId);
        var wrong = FeedbackInput(f); wrong.SchoolMembershipId = f.Absent.MembershipId;
        await Should.ThrowAsync<AbpAuthorizationException>(() => Feedback.GetTargetAsync(wrong));
    }

    [Fact]
    public async Task First_Feedback_Save_Receipt_Should_Be_Null_Before_Commit_And_Not_Grant_A_Peer_Ownership()
    {
        var f = await GradingFixtureAsync(false); var root = Guid.NewGuid(); var input = FeedbackInput(f);
        using (User(f.Source.School.Reader))
        {
            (await Feedback.GetMyWorkspaceAsync(root)).ShouldBeNull();
            (await Feedback.GetOperationAsync(root, input.OperationId)).ShouldBeNull();
            var revision = await Feedback.SaveDraftAsync(root, input);
            (await Feedback.GetOperationAsync(root, input.OperationId))!.Id.ShouldBe(revision.Id);
        }
        using (User(f.Absent.UserId))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => Feedback.GetOperationAsync(root, input.OperationId));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Feedback.GetMyWorkspaceAsync(root));
        }
    }

    [Fact]
    public async Task Student_Feedback_School_Filter_Should_Precede_Count_And_Paging_And_Not_Mark_Reading()
    {
        var f = await GradingFixtureAsync(false); var id = Guid.NewGuid();
        using (User(f.Source.School.Reader)) { await Feedback.SaveDraftAsync(id, FeedbackInput(f)); await PublishFeedbackAsync(id); }
        using var student = User(f.Source.Student.UserId);
        var school = f.Source.Revision.SchoolId;
        var own = await Feedback.GetMyListAsync(new(), school);
        own.TotalCount.ShouldBe(1); own.Items.Single().SchoolId.ShouldBe(school); own.Items.Single().Text.ShouldBeNull(); own.Items.Single().OpenedAtUtc.ShouldBeNull();
        (await Feedback.GetMyNoticesAsync(new(), school)).TotalCount.ShouldBe(1);
        (await Feedback.GetMyListAsync(new(), Guid.NewGuid())).TotalCount.ShouldBe(0);
        (await Feedback.GetMyNoticesAsync(new(), Guid.NewGuid())).TotalCount.ShouldBe(0);
    }
    private static SaveEducationFeedbackDraftDto FeedbackInput(GradingFixture f, string text = "公开的学习建议") => new()
    {
        OperationId = Guid.NewGuid(), TeachingAssignmentId = f.Source.Revision.TeachingAssignmentId,
        StudentEnrollmentId = f.Source.Student.EnrollmentId, SchoolMembershipId = f.Source.Student.MembershipId,
        Content = new() { Kind = EducationFeedbackKind.Daily, Context = "课堂学习", OccurredAtUtc = DateTime.UtcNow, Text = text }
    };
    private async Task<EducationFeedbackRevisionDto> PublishFeedbackAsync(Guid id, string? reason = null)
    {
        var preview = await Feedback.PreviewPublishAsync(id);
        var input = new PublishEducationFeedbackDto { OperationId = Guid.NewGuid(), ConcurrencyStamp = preview.Feedback.ConcurrencyStamp,
            ExpectedDraftRevisionId = preview.Feedback.Draft!.Id, PreviewFingerprint = preview.PreviewFingerprint, ConfirmRecipientAndContent = true, Reason = reason };
        var result = await Feedback.PublishAsync(id, input);
        (await Feedback.PublishAsync(id, input)).Id.ShouldBe(result.Id);
        return result;
    }

    [Fact]
    public async Task Feedback_Should_Keep_Observations_Private_And_Only_Expose_Explicitly_Selected_Public_Text()
    {
        var f = await GradingFixtureAsync(false); var id = Guid.NewGuid(); Guid observationId; Guid versionId;
        var target = FeedbackInput(f);
        using (User(f.Source.School.Reader))
        {
            var input = new CreateEducationTeacherObservationDto { OperationId = Guid.NewGuid(), TeachingAssignmentId = target.TeachingAssignmentId,
                StudentEnrollmentId = target.StudentEnrollmentId, SchoolMembershipId = target.SchoolMembershipId,
                ObservedAtUtc = DateTime.UtcNow, Text = "作者私有观察-SENTINEL" };
            observationId = (await Feedback.CreateObservationAsync(input)).Id;
            (await Feedback.CreateObservationAsync(input)).Id.ShouldBe(observationId);
            (await Feedback.GetObservationsAsync(target, new())).TotalCount.ShouldBe(1);
            target.Content.SelectedObservationId = observationId;
            await Feedback.SaveDraftAsync(id, target); versionId = (await PublishFeedbackAsync(id)).Id;
        }
        using (User(f.Source.Student.UserId))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => Feedback.GetObservationsAsync(target, new()));
            var list = await Feedback.GetMyListAsync(new()); list.TotalCount.ShouldBe(1); list.Items.Single().Text.ShouldBeNull();
            var value = await Feedback.OpenMyAsync(id, versionId); value.Text.ShouldBe(target.Content.Text);
            JsonSerializer.Serialize(value).ShouldNotContain("SelectedObservationId");
            JsonSerializer.Serialize(value).ShouldNotContain("作者私有观察");
            JsonSerializer.Serialize(await Feedback.GetMyNoticesAsync(new())).ShouldNotContain("公开的学习建议");
        }
        using (User(f.Source.School.Owner))
            await Should.ThrowAsync<AbpAuthorizationException>(() => Feedback.GetObservationsAsync(target, new()));
        await WithUnitOfWorkAsync(async () => (await Repo<EducationTeacherObservation>().GetListAsync(x => x.Id == observationId)).Count.ShouldBe(1));
    }

    [Fact]
    public async Task Feedback_Should_Keep_Drafts_Hidden_And_Record_An_Actual_Open_Once_Per_Published_Version()
    {
        var f = await GradingFixtureAsync(false); var id = Guid.NewGuid(); var draft = FeedbackInput(f); Guid version;
        using (User(f.Source.School.Reader)) await Feedback.SaveDraftAsync(id, draft);
        using (User(f.Source.Student.UserId))
        {
            (await Feedback.GetMyListAsync(new())).TotalCount.ShouldBe(0);
            (await Feedback.GetMyNoticesAsync(new())).TotalCount.ShouldBe(0);
            await Should.ThrowAsync<BusinessException>(() => Feedback.OpenMyAsync(id, draft.OperationId));
        }
        using (User(f.Source.School.Reader)) version = (await PublishFeedbackAsync(id)).Id;
        using (User(f.Source.Student.UserId))
        {
            var metadata = (await Feedback.GetMyListAsync(new())).Items.Single(); metadata.Text.ShouldBeNull(); metadata.OpenedAtUtc.ShouldBeNull();
            var opened = await Feedback.OpenMyAsync(id, version); opened.Text.ShouldBe(draft.Content.Text); opened.OpenedAtUtc.ShouldNotBeNull();
            (await Feedback.OpenMyAsync(id, version)).OpenedAtUtc.ShouldBe(opened.OpenedAtUtc);
            (await Feedback.GetMyNoticesAsync(new())).Items.Single().IsRead.ShouldBeTrue();
        }
        using (User(f.Absent.UserId)) await Should.ThrowAsync<AbpAuthorizationException>(() => Feedback.OpenMyAsync(id, version));
        await WithUnitOfWorkAsync(async () => (await Repo<EducationFeedbackRead>().GetListAsync(x => x.FeedbackId == id)).Count.ShouldBe(1));
    }

    [Fact]
    public async Task Feedback_Should_Preserve_The_Official_Text_During_Correction_And_Require_Reading_The_New_Version()
    {
        var f = await GradingFixtureAsync(false); var id = Guid.NewGuid(); Guid oldVersion; Guid newVersion;
        using (User(f.Source.School.Reader)) { await Feedback.SaveDraftAsync(id, FeedbackInput(f, "原正式评语")); oldVersion = (await PublishFeedbackAsync(id)).Id; }
        using (User(f.Source.Student.UserId)) await Feedback.OpenMyAsync(id, oldVersion);
        using (User(f.Source.School.Reader))
        {
            var current = await Feedback.GetAsync(id); var input = FeedbackInput(f, "更正后的评语");
            input.ConcurrencyStamp = current.ConcurrencyStamp; input.ExpectedRevisionId = current.CurrentRevision.Id;
            await Feedback.SaveDraftAsync(id, input);
            using (User(f.Source.Student.UserId)) (await Feedback.OpenMyAsync(id, oldVersion)).Text.ShouldBe("原正式评语");
            await Should.ThrowAsync<BusinessException>(() => PublishFeedbackAsync(id));
            newVersion = (await PublishFeedbackAsync(id, "补充具体可执行建议")).Id;
            (await Feedback.GetOperationAsync(id, oldVersion))!.Content!.Text.ShouldBe("原正式评语");
            (await Feedback.GetAsync(id)).PublishedVersionOpenedAtUtc.ShouldBeNull();
        }
        using (User(f.Source.Student.UserId))
        {
            var latest = (await Feedback.GetMyListAsync(new())).Items.Single(); latest.VersionId.ShouldBe(newVersion); latest.OpenedAtUtc.ShouldBeNull();
            await Should.ThrowAsync<AbpDbConcurrencyException>(() => Feedback.OpenMyAsync(id, oldVersion));
            (await Feedback.OpenMyAsync(id, newVersion)).Text.ShouldBe("更正后的评语");
            (await Feedback.GetMyNoticesAsync(new())).TotalCount.ShouldBe(2);
        }
    }

    [Fact]
    public async Task Feedback_Should_Withdraw_Without_Returning_Old_Text_Or_Internal_Reason()
    {
        var f = await GradingFixtureAsync(false); var id = Guid.NewGuid(); Guid published; Guid withdrawn;
        using (User(f.Source.School.Reader))
        {
            await Feedback.SaveDraftAsync(id, FeedbackInput(f)); published = (await PublishFeedbackAsync(id)).Id;
            var current = await Feedback.GetAsync(id);
            var input = new WithdrawEducationFeedbackDto { OperationId = Guid.NewGuid(), ConcurrencyStamp = current.ConcurrencyStamp,
                ExpectedPublishedRevisionId = published, Reason = "内部撤回原因-SENTINEL" };
            withdrawn = (await Feedback.WithdrawAsync(id, input)).Id;
            (await Feedback.WithdrawAsync(id, input)).Id.ShouldBe(withdrawn);
            (await Feedback.GetOperationAsync(id, published))!.Content.ShouldNotBeNull();
            var afterWithdrawal = await Feedback.GetAsync(id); var nextDraft = FeedbackInput(f, "撤回后仍未发布的内部草稿");
            nextDraft.ConcurrencyStamp = afterWithdrawal.ConcurrencyStamp; nextDraft.ExpectedRevisionId = afterWithdrawal.CurrentRevision.Id;
            await Feedback.SaveDraftAsync(id, nextDraft);
        }
        using (User(f.Source.Student.UserId))
        {
            var item = (await Feedback.GetMyListAsync(new())).Items.Single(); item.Visibility.ShouldBe(EducationFeedbackVisibility.Withdrawn);
            item.VersionId.ShouldBe(withdrawn); item.Text.ShouldBeNull();
            await Should.ThrowAsync<AbpDbConcurrencyException>(() => Feedback.OpenMyAsync(id, published));
            var opened = await Feedback.OpenMyAsync(id, withdrawn); opened.Text.ShouldBeNull(); opened.OpenedAtUtc.ShouldNotBeNull();
            JsonSerializer.Serialize(opened).ShouldNotContain("内部撤回原因");
            (await Feedback.GetMyNoticesAsync(new())).Items.Single(x => x.Id == withdrawn).IsCurrentVersion.ShouldBeTrue();
        }
    }

    [Theory]
    [InlineData(EducationFeedbackKind.Assessment)]
    [InlineData(EducationFeedbackKind.Daily)]
    public async Task Feedback_Should_Gate_Every_Assessment_Reference_Until_Full_Review_Even_For_A_Daily_Comment(EducationFeedbackKind kind)
    {
        var f = await GradingFixtureAsync(false); var secondTask = await FeedbackSecondAssessmentAsync(f); var id = Guid.NewGuid(); Guid feedbackVersion;
        using (User(f.Source.School.Reader))
        {
            var input = FeedbackInput(f, "未开放测评的具体解答-SENTINEL"); input.Content.Kind = kind; input.Content.AssessmentTaskIds = [f.TaskId, secondTask.TaskId];
            (await Feedback.GetAssessmentListAsync(input)).Select(x => x.TaskId).ShouldContain(secondTask.TaskId);
            await Feedback.SaveDraftAsync(id, input); feedbackVersion = (await PublishFeedbackAsync(id)).Id;
            (await Feedback.GetAsync(id)).Visibility.ShouldBe(EducationFeedbackVisibility.WaitingForReview);
        }
        using (User(f.Source.Student.UserId))
        {
            (await Feedback.GetMyListAsync(new())).TotalCount.ShouldBe(0); (await Feedback.GetMyNoticesAsync(new())).TotalCount.ShouldBe(0);
            (await Should.ThrowAsync<BusinessException>(() => Feedback.OpenMyAsync(id, feedbackVersion))).Code.ShouldBe(EducationFeedbackErrorCodes.NotOpen);
        }
        using (User(f.Source.School.Reader))
        {
            await Grades.GetSubmissionForGradingAsync(f.SubmissionId);
            var closure = await Grades.CloseTaskAsync(f.TaskId, CloseInput(await Grades.PreviewFinalizationAsync(f.TaskId)));
            await Grades.PublishResultsAsync(f.TaskId, ReleaseInput(await Grades.GetGradingOverviewAsync(f.TaskId), closure.Id, ExamResultVisibility.ScoreOnly));
            using (User(f.Source.Student.UserId)) (await Feedback.GetMyListAsync(new())).TotalCount.ShouldBe(0);
            var release = ReleaseInput(await Grades.GetGradingOverviewAsync(f.TaskId), closure.Id, ExamResultVisibility.FullReview); release.Reason = "全任务批改完毕，开放讲评";
            await Grades.PublishResultsAsync(f.TaskId, release);
            using (User(f.Source.Student.UserId)) (await Feedback.GetMyListAsync(new())).TotalCount.ShouldBe(0);
            var secondClosure = await Grades.CloseTaskAsync(secondTask.TaskId, CloseInput(await Grades.PreviewFinalizationAsync(secondTask.TaskId)));
            await Grades.PublishResultsAsync(secondTask.TaskId, ReleaseInput(await Grades.GetGradingOverviewAsync(secondTask.TaskId), secondClosure.Id, ExamResultVisibility.FullReview));
        }
        using (User(f.Source.Student.UserId))
        {
            var opened = await Feedback.OpenMyAsync(id, feedbackVersion); opened.Text!.ShouldContain("具体解答");
            opened.AssessmentRecipientIds.Order().ShouldBe(new[] { f.RecipientId, secondTask.RecipientId }.Order());
            (await Feedback.GetMyNoticesAsync(new())).TotalCount.ShouldBe(1);
        }
    }

    [Fact]
    public async Task Feedback_Should_Reject_Stale_Editors_And_Conflicting_Operation_Replays_Without_Changing_The_Recipient()
    {
        var f = await GradingFixtureAsync(false); var id = Guid.NewGuid(); using var teacher = User(f.Source.School.Reader);
        var first = FeedbackInput(f); var saved = await Feedback.SaveDraftAsync(id, first);
        (await Feedback.SaveDraftAsync(id, first)).Id.ShouldBe(saved.Id);
        first.Content.Text = "同一操作被篡改";
        (await Should.ThrowAsync<BusinessException>(() => Feedback.SaveDraftAsync(id, first))).Code.ShouldBe(EducationFeedbackErrorCodes.OperationConflict);
        var old = await Feedback.GetAsync(id); var second = FeedbackInput(f, "后一次确认的草稿"); second.ConcurrencyStamp = old.ConcurrencyStamp; second.ExpectedRevisionId = old.CurrentRevision.Id;
        var stale = FeedbackInput(f, "旧页面覆盖"); stale.ConcurrencyStamp = old.ConcurrencyStamp; stale.ExpectedRevisionId = old.CurrentRevision.Id;
        await Feedback.SaveDraftAsync(id, second);
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => Feedback.SaveDraftAsync(id, stale));
        (await Feedback.GetAsync(id)).Draft!.Content!.Text.ShouldBe(second.Content.Text);
        (await Feedback.GetOperationAsync(id, saved.Id))!.Content!.Text.ShouldBe("公开的学习建议");
        var otherTarget = FeedbackInput(f); otherTarget.StudentEnrollmentId = f.Absent.EnrollmentId; otherTarget.SchoolMembershipId = f.Absent.MembershipId;
        await Should.ThrowAsync<AbpAuthorizationException>(() => Feedback.SaveDraftAsync(id, otherTarget));
    }

    private async Task<(Guid TaskId, Guid RecipientId)> FeedbackSecondAssessmentAsync(GradingFixture f) => await WithUnitOfWorkAsync(async () =>
    {
        var originalTask = await Repo<EducationAssessmentTask>().GetAsync(f.TaskId);
        var original = await Repo<EducationAssessmentTaskRevision>().GetAsync(originalTask.PublishedRevisionId!.Value);
        var time = DateTime.UtcNow.AddMinutes(-40);
        var task = new EducationAssessmentTask(Guid.NewGuid(), null, original.OwnerUserId, new string('a', 64), time);
        await Repo<EducationAssessmentTask>().InsertAsync(task, autoSave: true);
        EducationAssessmentTaskRevision Revision(int number, Guid? previous, bool published) => new(Guid.NewGuid(), null, original.OwnerUserId, task.Id,
            number, previous, "另一关联测评", original.Instructions, original.SchoolId, original.AcademicTermId, original.SchoolClassId, original.SchoolClassName,
            original.TeachingAssignmentId, original.ExamPaperId, original.CurriculumSnapshotJson, EducationAssessmentRecipientScope.FullClass, [],
            time.AddMinutes(-20), time.AddMinutes(30), 60, 1, ExamResultVisibility.Hidden, false, new string('b', 64), time.AddMinutes(number),
            published ? time.AddMinutes(number) : null, published ? new string('c', 64) : null);
        var draft = Revision(1, null, false); await Repo<EducationAssessmentTaskRevision>().InsertAsync(draft, autoSave: true); task.AdvanceDraft(draft);
        await Repo<EducationAssessmentTask>().UpdateManyAsync([task], autoSave: true);
        var publication = Revision(2, draft.Id, true); await Repo<EducationAssessmentTaskRevision>().InsertAsync(publication, autoSave: true);
        var recipient = new EducationAssessmentRecipient(Guid.NewGuid(), publication, f.Source.Student.UserId, f.Source.Student.MembershipId, f.Source.Student.EnrollmentId, "关联测评学生", "same-student");
        await Repo<EducationAssessmentRecipient>().InsertAsync(recipient, autoSave: true); task.Publish(publication);
        await Repo<EducationAssessmentTask>().UpdateManyAsync([task], autoSave: true);
        return (task.Id, recipient.Id);
    });
}
