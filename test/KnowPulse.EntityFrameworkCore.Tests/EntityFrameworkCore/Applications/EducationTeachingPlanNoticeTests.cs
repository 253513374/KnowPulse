using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Education;
using Shouldly;
using Volo.Abp.Authorization;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    private IEducationTeachingPlanNoticeAppService PlanNotices => GetRequiredService<IEducationTeachingPlanNoticeAppService>();

    [Fact]
    public async Task Plan_Notices_Should_Follow_Public_Changes_Without_Providing_Content_Or_Leaking_Private_Events()
    {
        var (f, job) = await PlanSourceAsync(); var planId = Guid.NewGuid(); EducationTeachingPlanRevisionDto published;
        using (User(f.Source.School.Reader)) {
            await Plans.SaveDraftAsync(planId, await PlanDraftAsync(f, job));
            await Plans.ConfirmAsync(planId, PlanTransition(await Plans.GetAsync(planId)));
            (await WithUnitOfWorkAsync(() => Repo<EducationTeachingPlanNotice>().GetCountAsync())).ShouldBe(0);
            var input = PlanTransition(await Plans.GetAsync(planId)); published = await Plans.PublishAsync(planId, input);
            (await Plans.PublishAsync(planId, input)).Id.ShouldBe(published.Id);
            var continuation = ExecutionInput(await Execution.GetExecutionReviewAsync(planId), EducationPlanDecision.Continue);
            await Execution.DecideExecutionAsync(planId, continuation);
        }
        Guid messageId; DateTime? firstRead;
        using (User(f.Source.Student.UserId)) {
            var page = await Inbox.GetMyListAsync(new() { SchoolId = f.Source.School.SchoolId, Source = EducationInboxSource.TeachingPlan });
            var message = page.Items.Single(); message.Kind.ShouldBe(EducationInboxMessageKind.PlanPublished); message.PublicationId.ShouldBe(published.Id); message.CanOpen.ShouldBeTrue(); messageId = message.Id;
            JsonSerializer.Serialize(page).ShouldNotContain("PRIVATE-"); message.Id.ShouldNotBe(published.Id); message.StepId.ShouldBeNull();
            await Should.ThrowAsync<AbpAuthorizationException>(() => PlanNotices.ReadMyPlanNoticeAsync(messageId, Guid.NewGuid()));
            var read = await Inbox.ReadMyAsync(new() { SchoolId = f.Source.School.SchoolId, Source = EducationInboxSource.TeachingPlan, MessageId = messageId });
            firstRead = read.ReadAtUtc; firstRead.ShouldNotBeNull();
            (await PlanNotices.ReadMyPlanNoticeAsync(messageId, f.Source.School.SchoolId)).ReadAtUtc.ShouldBe(firstRead);
        }
        await WithUnitOfWorkAsync(async () => {
            (await Repo<EducationTeachingActivity>().GetListAsync(x => x.PlanId == planId)).ShouldAllBe(x => x.FirstProvidedAtUtc == null && x.CurrentStudentRevisionId == null);
            (await Repo<EducationTeachingPlanQuestionExposure>().GetListAsync(x => x.PlanId == planId)).ShouldBeEmpty();
        });
        using (User(f.Absent.UserId)) { await Should.ThrowAsync<AbpAuthorizationException>(() => PlanNotices.ReadMyPlanNoticeAsync(messageId, f.Source.School.SchoolId)); }
        using (User(f.Source.School.Reader)) {
            var pause = ExecutionInput(await Execution.GetExecutionReviewAsync(planId), EducationPlanDecision.Pause);
            await Execution.DecideExecutionAsync(planId, pause); await Execution.DecideExecutionAsync(planId, pause);
        }
        using (User(f.Source.Student.UserId)) {
            var page = await PlanNotices.GetMyPlanNoticesAsync(new() { SchoolId = f.Source.School.SchoolId }); page.TotalCount.ShouldBe(2);
            page.Items.Count(x => x.Kind == EducationTeachingPlanNoticeKind.ExecutionChanged).ShouldBe(1); JsonSerializer.Serialize(page).ShouldNotContain("PRIVATE-");
        }
        using (User(f.Source.School.Reader)) { await Plans.WithdrawAsync(planId, PlanTransition(await Plans.GetAsync(planId), "PRIVATE-WITHDRAWAL")); }
        using (User(f.Source.Student.UserId)) {
            var page = await PlanNotices.GetMyPlanNoticesAsync(new() { SchoolId = f.Source.School.SchoolId }); page.TotalCount.ShouldBe(3); page.Items.ShouldAllBe(x => !x.CanOpen);
            var withdrawn = page.Items.Single(x => x.Kind == EducationTeachingPlanNoticeKind.Withdrawn); withdrawn.PublishedRevisionId.ShouldBe(published.Id);
            (await PlanNotices.ReadMyPlanNoticeAsync(withdrawn.Id, f.Source.School.SchoolId)).ReadAtUtc.ShouldNotBeNull();
            (await PlanNotices.ReadMyPlanNoticeAsync(messageId, f.Source.School.SchoolId)).ReadAtUtc.ShouldBe(firstRead);
        }
    }

    [Fact]
    public async Task Difficulty_Notice_Should_Pin_Public_Step_And_Current_Reply_Without_Copying_Teacher_Note()
    {
        var (f, job) = await PlanSourceAsync(); var planId = Guid.NewGuid(); EducationTeachingPlanRevisionDto published; EducationStudentActivityDto activity;
        using (User(f.Source.School.Reader)) { await Plans.SaveDraftAsync(planId, await PlanDraftAsync(f, job)); published = await ConfirmAndPublishPlanAsync(planId); }
        using (User(f.Source.Student.UserId)) {
            activity = (await Activities.GetMyActivitiesAsync(planId, published.Id)).Activities.First();
            await Activities.SaveMyActivityAsync(activity.Id, ActivityInput(activity, EducationTeachingActivityAction.Difficulty, "STUDENT-DIFFICULTY"));
        }
        Guid privateEvent;
        using (User(f.Source.School.Reader)) {
            var progress = await Activities.GetActivityProgressAsync(planId); var input = FollowInput(progress.Activities.Single(x => x.Id == activity.Id));
            privateEvent = (await Activities.SaveActivityFollowUpAsync(activity.Id, input)).Id;
            (await Activities.SaveActivityFollowUpAsync(activity.Id, input)).Id.ShouldBe(privateEvent);
        }
        Guid notice;
        using (User(f.Source.Student.UserId)) {
            var page = await Inbox.GetMyListAsync(new() { SchoolId = f.Source.School.SchoolId, Source = EducationInboxSource.TeachingPlan });
            var row = page.Items.Single(x => x.Kind == EducationInboxMessageKind.DifficultyHandled); row.Id.ShouldNotBe(privateEvent); row.StepId.ShouldBe(activity.StepId); row.CanOpen.ShouldBeTrue(); notice = row.Id;
            var json = JsonSerializer.Serialize(page); json.ShouldNotContain("PRIVATE-"); json.ShouldNotContain(privateEvent.ToString()); json.ShouldNotContain("STUDENT-DIFFICULTY");
            await PlanNotices.ReadMyPlanNoticeAsync(notice, f.Source.School.SchoolId);
            activity = (await Activities.GetMyActivitiesAsync(planId, published.Id)).Activities.Single(x => x.Id == activity.Id);
            await Activities.SaveMyActivityAsync(activity.Id, ActivityInput(activity, EducationTeachingActivityAction.Difficulty, "NEW-DIFFICULTY"));
            (await PlanNotices.ReadMyPlanNoticeAsync(notice, f.Source.School.SchoolId)).CanOpen.ShouldBeFalse();
        }
        await WithUnitOfWorkAsync(async () => { var enrollment = await Repo<StudentEnrollment>().GetAsync(f.Source.Student.EnrollmentId);
            enrollment.EndAt(DateTime.UtcNow); await Repo<StudentEnrollment>().UpdateManyAsync([enrollment], autoSave: true); });
        using (User(f.Source.Student.UserId)) {
            (await PlanNotices.GetMyPlanNoticesAsync(new() { SchoolId = f.Source.School.SchoolId })).TotalCount.ShouldBe(0);
            await Should.ThrowAsync<AbpAuthorizationException>(() => PlanNotices.ReadMyPlanNoticeAsync(notice, f.Source.School.SchoolId));
        }
    }
}
