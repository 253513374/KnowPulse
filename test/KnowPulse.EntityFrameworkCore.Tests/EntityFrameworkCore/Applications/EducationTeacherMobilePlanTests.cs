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
    [Fact]
    public async Task Teacher_Mobile_Plans_Should_Filter_Before_Paging_And_Reject_Mixed_Read_Scope_Without_Providing_Activities()
    {
        var (f, job) = await PlanSourceAsync(); var id = Guid.NewGuid(); using var teacher = User(f.Source.School.Reader);
        await Plans.SaveDraftAsync(id, await PlanDraftAsync(f, job)); var published = await ConfirmAndPublishPlanAsync(id);
        var root = await WithUnitOfWorkAsync(() => Repo<EducationTeachingPlan>().GetAsync(id));
        var input = new GetEducationTeacherPlansInput { SchoolId = root.SchoolId, TeachingAssignmentId = root.TeachingAssignmentId, MaxResultCount = 1 };
        var list = await Activities.GetTeacherPlansAsync(input); list.TotalCount.ShouldBe(1); list.Items.Single().PublishedRevisionId.ShouldBe(published.Id);
        JsonSerializer.Serialize(list).ShouldNotContain("PRIVATE-PLAN-NOTES");
        input.SkipCount = 1; (await Activities.GetTeacherPlansAsync(input)).Items.ShouldBeEmpty();
        input.SkipCount = 0; input.SchoolId = Guid.NewGuid(); (await Activities.GetTeacherPlansAsync(input)).TotalCount.ShouldBe(0);
        await Should.ThrowAsync<AbpAuthorizationException>(() => Activities.GetActivityProgressForSchoolAsync(id, published.Id, input.SchoolId, root.TeachingAssignmentId));
        await Should.ThrowAsync<AbpAuthorizationException>(() => Activities.GetActivityProgressForSchoolAsync(id, published.Id, root.SchoolId, Guid.NewGuid()));
        var view = await Activities.GetActivityProgressForSchoolAsync(id, published.Id, root.SchoolId, root.TeachingAssignmentId);
        view.SchoolId.ShouldBe(root.SchoolId); view.TeachingAssignmentId.ShouldBe(root.TeachingAssignmentId); view.Activities.Count.ShouldBe(3);
        await WithUnitOfWorkAsync(async () => { (await Repo<EducationTeachingActivity>().GetListAsync(x => x.PlanId == id)).All(x => x.FirstProvidedAtUtc == null).ShouldBeTrue(); (await Repo<EducationTeachingActivityRevision>().GetCountAsync()).ShouldBe(0); });
        using (User(f.Source.Student.UserId)) await Should.ThrowAsync<AbpAuthorizationException>(() => Activities.GetActivityProgressForSchoolAsync(id, published.Id, root.SchoolId, root.TeachingAssignmentId));
    }
    [Fact]
    public async Task Teacher_Mobile_Follow_Up_Should_Reuse_Web_Receipts_And_Keep_Private_Text_Out_Of_Student_Views()
    {
        var (f, job) = await PlanSourceAsync(); var id = Guid.NewGuid(); EducationTeachingPlanRevisionDto published; EducationTeachingPlan root;
        using (User(f.Source.School.Reader)) { await Plans.SaveDraftAsync(id, await PlanDraftAsync(f, job)); published = await ConfirmAndPublishPlanAsync(id); root = await WithUnitOfWorkAsync(() => Repo<EducationTeachingPlan>().GetAsync(id)); }
        Guid activityId;
        using (User(f.Source.Student.UserId)) { var row = (await Activities.GetMyActivitiesAsync(id, published.Id)).Activities.Single(x => x.StepOrder == 2); activityId = row.Id; await Activities.SaveMyActivityAsync(row.Id, ActivityInput(row, EducationTeachingActivityAction.Difficulty, "需要核对这一节")); }
        using (User(f.Source.School.Reader)) {
            var row = (await Activities.GetActivityProgressForSchoolAsync(id, published.Id, root.SchoolId, root.TeachingAssignmentId)).Activities.Single(x => x.Id == activityId); var input = FollowInput(row);
            await Should.ThrowAsync<AbpAuthorizationException>(() => Activities.SaveActivityFollowUpForSchoolAsync(activityId, Guid.NewGuid(), root.TeachingAssignmentId, input));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Activities.SaveActivityFollowUpForSchoolAsync(activityId, root.SchoolId, Guid.NewGuid(), input));
            var saved = await Activities.SaveActivityFollowUpForSchoolAsync(activityId, root.SchoolId, root.TeachingAssignmentId, input);
            (await Activities.SaveActivityFollowUpAsync(activityId, input)).Id.ShouldBe(saved.Id);
            (await Activities.GetActivityFollowUpOperationForSchoolAsync(id, input.OperationId, root.SchoolId, root.TeachingAssignmentId))!.Id.ShouldBe(saved.Id);
            await Should.ThrowAsync<AbpAuthorizationException>(() => Activities.GetActivityFollowUpOperationForSchoolAsync(id, input.OperationId, Guid.NewGuid(), root.TeachingAssignmentId));
        }
        using (User(f.Source.Student.UserId)) { var view = await Activities.GetMyActivitiesAsync(id, published.Id); var row = view.Activities.Single(x => x.Id == activityId); row.TeacherHandledAtUtc.ShouldNotBeNull(); row.State.ShouldBe(EducationTeachingActivityState.Blocked); row.StudentRevisionNumber.ShouldBe(1); JsonSerializer.Serialize(view).ShouldNotContain("PRIVATE-HANDLING"); }
    }
}
