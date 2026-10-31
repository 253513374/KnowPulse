using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Education;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    private IEducationAssessmentReminderAppService Reminders => GetRequiredService<IEducationAssessmentReminderAppService>();

    [Fact]
    public async Task Assessment_Reminders_Should_Stop_For_Paused_And_Withdrawn_Plan_Assessments()
    {
        var f = await FormalFixtureAsync(); using var teacher = User(f.Source.Source.School.Reader);
        var schoolId = await WithUnitOfWorkAsync(async () => (await Repo<EducationTeachingPlan>().GetAsync(f.PlanId)).SchoolId);
        var before = await Reminders.GetReminderPreviewAsync(f.RecipientId, schoolId); before.CanSend.ShouldBeTrue();
        await Execution.DecideExecutionAsync(f.PlanId, ExecutionInput(await Execution.GetExecutionReviewAsync(f.PlanId), EducationPlanDecision.Pause));
        var stopped = await Reminders.GetReminderPreviewAsync(f.RecipientId, schoolId); stopped.BlockReason.ShouldBe("PlanStopped"); stopped.CanSend.ShouldBeFalse();
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => Reminders.SendReminderAsync(f.RecipientId, schoolId, new() { OperationId = Guid.NewGuid(), PreviewFingerprint = before.PreviewFingerprint, ConfirmRecipient = true }));
        await Should.ThrowAsync<BusinessException>(() => Reminders.SendReminderAsync(f.RecipientId, schoolId, new() { OperationId = Guid.NewGuid(), PreviewFingerprint = stopped.PreviewFingerprint, ConfirmRecipient = true }));
        await Execution.DecideExecutionAsync(f.PlanId, ExecutionInput(await Execution.GetExecutionReviewAsync(f.PlanId), EducationPlanDecision.Resume));
        (await Reminders.GetReminderPreviewAsync(f.RecipientId, schoolId)).CanSend.ShouldBeTrue();
        await Plans.WithdrawAsync(f.PlanId, PlanTransition(await Plans.GetAsync(f.PlanId), "撤回后停止催办"));
        (await Reminders.GetReminderPreviewAsync(f.RecipientId, schoolId)).CanSend.ShouldBeFalse();
        await WithUnitOfWorkAsync(async () => (await Repo<EducationAssessmentReminder>().GetListAsync(x => x.Kind == EducationAssessmentReminderKind.Manual)).Count.ShouldBe(0));
    }

    [Fact]
    public async Task Assessment_Reminders_Should_Deduplicate_Throttle_And_Keep_Student_Read_Separate_From_Start()
    {
        var f = await FixtureAsync(); var student = await AssessmentStudentAsync(f); EducationAssessmentTaskRevisionDto published; EducationAssessmentReminderDto sent;
        using (User(f.Reader)) {
            var task = await Assessments.CreateDraftAsync(await AssessmentInputAsync(f)); var publish = AssessmentPublish(await Assessments.PreviewRecipientsAsync(task.Id)); published = await Assessments.PublishAsync(task.Id, publish);
            (await Assessments.PublishAsync(task.Id, publish)).Id.ShouldBe(published.Id);
            var list = await Reminders.GetReminderTargetsAsync(new() { TaskId = task.Id, SchoolId = f.SchoolId, MaxResultCount = 20 }); list.TotalCount.ShouldBe(1);
            var preview = list.Items.Single(); preview.CanSend.ShouldBeTrue(); var input = new SendEducationAssessmentReminderDto { OperationId = Guid.NewGuid(), PreviewFingerprint = preview.PreviewFingerprint, ConfirmRecipient = true };
            sent = await Reminders.SendReminderAsync(preview.Id, f.SchoolId, input); (await Reminders.SendReminderAsync(preview.Id, f.SchoolId, input)).Id.ShouldBe(sent.Id);
            (await Reminders.GetReminderOperationAsync(preview.Id, f.SchoolId, input.OperationId))!.Id.ShouldBe(sent.Id);
            await Should.ThrowAsync<AbpAuthorizationException>(() => Reminders.GetReminderPreviewAsync(preview.Id, Guid.NewGuid()));
            await Should.ThrowAsync<AbpAuthorizationException>(() => Reminders.GetReminderOperationAsync(preview.Id, Guid.NewGuid(), input.OperationId));
            var blocked = await Reminders.GetReminderPreviewAsync(preview.Id, f.SchoolId); blocked.CanSend.ShouldBeFalse(); blocked.BlockReason.ShouldBe("CoolingDown");
            await Should.ThrowAsync<BusinessException>(() => Reminders.SendReminderAsync(preview.Id, f.SchoolId, new() { OperationId = Guid.NewGuid(), PreviewFingerprint = blocked.PreviewFingerprint, ConfirmRecipient = true }));
            input.PreviewFingerprint = new string('b', 64); await Should.ThrowAsync<BusinessException>(() => Reminders.SendReminderAsync(preview.Id, f.SchoolId, input));
            (await Reminders.GetMyRemindersAsync(new() { SchoolId = f.SchoolId })).TotalCount.ShouldBe(0);
        }
        using (User(student.UserId)) {
            (await Reminders.GetMyRemindersAsync(new() { SchoolId = f.SchoolId })).TotalCount.ShouldBe(1);
            var list = await Reminders.GetMyNoticesAsync(new() { SchoolId = f.SchoolId }); list.TotalCount.ShouldBe(2); list.Items.ShouldAllBe(x => x.ReadAtUtc == null);
            list.Items.Single(x => x.Kind == EducationAssessmentReminderKind.Assignment).PublicationId.ShouldBe(published.Id);
            JsonSerializer.Serialize(list).ShouldNotContain("PRIVATE-"); (await Reminders.GetMyRemindersAsync(new() { SchoolId = Guid.NewGuid() })).TotalCount.ShouldBe(0);
            await Should.ThrowAsync<AbpAuthorizationException>(() => Reminders.ReadMyReminderAsync(sent.Id, Guid.NewGuid()));
            var read = await Reminders.ReadMyReminderAsync(sent.Id, f.SchoolId); read.ReadAtUtc.ShouldNotBeNull(); read.WeChatDelivery.ShouldBe("NotConfigured");
            (await Reminders.ReadMyReminderAsync(sent.Id, f.SchoolId)).ReadAtUtc.ShouldBe(read.ReadAtUtc);
            await Should.ThrowAsync<AbpAuthorizationException>(() => Reminders.GetReminderPreviewAsync(published.Recipients.Single().Id, f.SchoolId));
        }
        await WithUnitOfWorkAsync(async () => { (await Repo<EducationAssessmentReminder>().GetCountAsync()).ShouldBe(2); (await Repo<EducationAssessmentAttempt>().GetCountAsync()).ShouldBe(0); });
    }

    [Fact]
    public async Task Assessment_Reminders_Should_Reject_Changed_Preview_And_Submitted_Or_Inactive_Recipients()
    {
        var f = await FixtureAsync(); var student = await AssessmentStudentAsync(f); EducationAssessmentTaskRevisionDto published; EducationAssessmentReminderPreviewDto before;
        using (User(f.Reader)) { var task = await Assessments.CreateDraftAsync(await AssessmentInputAsync(f)); published = await Assessments.PublishAsync(task.Id, AssessmentPublish(await Assessments.PreviewRecipientsAsync(task.Id))); before = await Reminders.GetReminderPreviewAsync(published.Recipients.Single().Id, f.SchoolId); }
        EducationStudentAssessmentAttemptDto attempt;
        using (User(student.UserId)) { attempt = await Assessments.StartOrResumeAsync(before.Id, new() { OperationId = Guid.NewGuid() }); }
        using (User(f.Reader)) { await Should.ThrowAsync<AbpDbConcurrencyException>(() => Reminders.SendReminderAsync(before.Id, f.SchoolId, new() { OperationId = Guid.NewGuid(), PreviewFingerprint = before.PreviewFingerprint, ConfirmRecipient = true })); }
        using (User(student.UserId)) {
            var review = await Assessments.PreviewSubmissionAsync(attempt.Id);
            await Assessments.SubmitAsync(attempt.Id, new() { OperationId = Guid.NewGuid(), PaperSnapshotVersion = attempt.PaperSnapshotVersion, ExpectedResponseSetVersion = attempt.ResponseSetVersion, ConfirmSavedOnly = true, ExpectedPreparationFingerprint = review.Fingerprint });
        }
        using (User(f.Reader)) {
            var completed = await Reminders.GetReminderPreviewAsync(before.Id, f.SchoolId); completed.CanSend.ShouldBeFalse(); completed.BlockReason.ShouldBe("Submitted");
            await Should.ThrowAsync<BusinessException>(() => Reminders.SendReminderAsync(before.Id, f.SchoolId, new() { OperationId = Guid.NewGuid(), PreviewFingerprint = completed.PreviewFingerprint, ConfirmRecipient = true }));
        }
        await WithUnitOfWorkAsync(async () => { var member = await Repo<SchoolMembership>().GetAsync(student.MembershipId); member.EndAt(DateTime.UtcNow); await Repo<SchoolMembership>().UpdateAsync(member, autoSave: true); });
        using (User(f.Reader)) { await Should.ThrowAsync<AbpAuthorizationException>(() => Reminders.GetReminderPreviewAsync(before.Id, f.SchoolId)); }
        await WithUnitOfWorkAsync(async () => (await Repo<EducationAssessmentReminder>().GetListAsync(x => x.Kind == EducationAssessmentReminderKind.Manual)).Count.ShouldBe(0));
    }
}
