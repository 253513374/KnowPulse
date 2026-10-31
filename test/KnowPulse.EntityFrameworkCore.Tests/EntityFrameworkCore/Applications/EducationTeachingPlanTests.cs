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
    private IEducationTeachingPlanAppService Plans => GetRequiredService<IEducationTeachingPlanAppService>();
    private async Task<(GradingFixture F, EducationLearningReportJobDto Job)> PlanSourceAsync(ExamResultVisibility visibility = ExamResultVisibility.FullReview)
    {
        var f = await GradingFixtureAsync(false); await ExposureReleaseAsync(f, visibility); EducationLearningReportJobDto job;
        using (User(f.Source.School.Reader)) job = await Reports.RequestAsync(ReportInput(f)); await ReportProcessor.ProcessAsync(job.Id); return (f, job);
    }
    private async Task<SaveEducationTeachingPlanDraftDto> PlanDraftAsync(GradingFixture f, EducationLearningReportJobDto job)
    {
        var end = DateTime.UtcNow.AddDays(2);
        return new() { OperationId = Guid.NewGuid(), ReportId = job.ReportId, ReportSnapshotId = job.Id, StudentUserId = f.Source.Student.UserId,
            InternalNotes = "PRIVATE-PLAN-NOTES 不向学生发送", Content = new() { Title = "一次方程巩固安排", StudentGoal = "先补充独立测评证据，再练习解题过程", TeacherActions = "逐步讲解并核对学生理解", SuccessCriteria = "新题独立完成后由教师复核", StartsAtUtc = DateTime.UtcNow, EndsAtUtc = end, ReassessmentAtUtc = end,
                TargetPointRevisionIds = [await WithUnitOfWorkAsync(async () => (await Repo<TextbookKnowledgePointMapping>().GetAsync(f.Source.School.MappingA)).KnowledgePointRevisionId)], Steps = [
                    new() { Id = Guid.NewGuid(), Kind = EducationTeachingPlanStepKind.Diagnostic, Title = "补充诊断", Instructions = "先独立说明解题思路", EstimatedMinutes = 10, DueAtUtc = end.AddDays(-1) },
                    new() { Id = Guid.NewGuid(), Kind = EducationTeachingPlanStepKind.WorkedExample, Title = "教师讲解例题", Instructions = "对照老师给出的例题步骤找出依据", EstimatedMinutes = 15, DueAtUtc = end.AddHours(-1) },
                    new() { Id = Guid.NewGuid(), Kind = EducationTeachingPlanStepKind.Reassessment, Title = "安排新题复测", Instructions = "由老师另外发布同知识点新题测评", EstimatedMinutes = 15, DueAtUtc = end } ] } };
    }
    private static TransitionEducationTeachingPlanDto PlanTransition(EducationTeachingPlanDto current, string? reason = null) => new() { OperationId = Guid.NewGuid(),
        ExpectedRevisionId = current.Current.Id, ConcurrencyStamp = current.ConcurrencyStamp, ConfirmRecipientAndContent = true, ConfirmInlineMaterialRights = true, ConfirmStudentSafeText = true, Reason = reason };
    private async Task<EducationTeachingPlanRevisionDto> ConfirmAndPublishPlanAsync(Guid id, string? reason = null)
    { await Plans.ConfirmAsync(id, PlanTransition(await Plans.GetAsync(id), reason)); return await Plans.PublishAsync(id, PlanTransition(await Plans.GetAsync(id), reason)); }

    [Fact]
    public async Task Teaching_Plan_Should_Keep_Drafts_And_Confirmation_Private_Publish_An_Exact_Version_And_Record_Only_Actual_Body_Context()
    {
        var (f, job) = await PlanSourceAsync(); var id = Guid.NewGuid(); var input = await PlanDraftAsync(f, job); EducationTeachingPlanRevisionDto draft, published;
        using (User(f.Source.School.Reader)) { draft = await Plans.SaveDraftAsync(id, input); (await Plans.SaveDraftAsync(id, input)).Id.ShouldBe(draft.Id); }
        using (User(f.Source.Student.UserId)) { (await Plans.GetMyListAsync(new())).Items.ShouldBeEmpty(); await Should.ThrowAsync<AbpAuthorizationException>(() => Plans.OpenMyAsync(id, draft.Id)); }
        using (User(f.Source.School.Reader)) await Plans.ConfirmAsync(id, PlanTransition(await Plans.GetAsync(id)));
        using (User(f.Source.Student.UserId)) (await Plans.GetMyListAsync(new())).Items.ShouldBeEmpty();
        using (User(f.Source.School.Reader)) published = await Plans.PublishAsync(id, PlanTransition(await Plans.GetAsync(id)));
        using (User(f.Source.Student.UserId)) {
            var card = (await Plans.GetMyListAsync(new())).Items.Single(); card.Content.ShouldBeNull(); card.VersionId.ShouldBe(published.Id); card.Availability.ShouldBe(EducationTeachingPlanAvailability.Available);
            await WithUnitOfWorkAsync(async () => (await Repo<EducationTeachingPlanQuestionExposure>().GetCountAsync()).ShouldBe(0));
            var body = await Plans.OpenMyAsync(id, published.Id); body.Content!.Steps.Count.ShouldBe(3);
            JsonSerializer.Serialize(body).ShouldNotContain("PRIVATE-PLAN-NOTES"); JsonSerializer.Serialize(body).ShouldNotContain("ReportSnapshotId"); JsonSerializer.Serialize(body).ShouldNotContain("TargetPointRevisionIds");
            await Plans.OpenMyAsync(id, published.Id);
        }
        await WithUnitOfWorkAsync(async () => { var rows = await Repo<EducationTeachingPlanQuestionExposure>().GetListAsync(); rows.Count.ShouldBe(1); rows.Single().PlanRevisionId.ShouldBe(published.Id); rows.Single().Evidence().Use.ShouldBe(EducationLearningExposureUse.Unverified); });
        using (User(f.Source.School.Reader)) (await Plans.GetRevisionAsync(id, draft.Id)).InternalNotes.ShouldBe(input.InternalNotes);
    }

    [Fact]
    public async Task Teaching_Plan_Should_Wait_For_All_Linked_Full_Review_Without_Leaking_Content_Or_Recording_Delivery()
    {
        var (f, job) = await PlanSourceAsync(ExamResultVisibility.ScoreOnly); var id = Guid.NewGuid(); EducationTeachingPlanRevisionDto published;
        using (User(f.Source.School.Reader)) { await Plans.SaveDraftAsync(id, await PlanDraftAsync(f, job)); published = await ConfirmAndPublishPlanAsync(id); }
        using (User(f.Source.Student.UserId)) { var closed = await Plans.OpenMyAsync(id, published.Id); closed.Availability.ShouldBe(EducationTeachingPlanAvailability.Waiting); closed.Content.ShouldBeNull(); }
        await WithUnitOfWorkAsync(async () => (await Repo<EducationTeachingPlanQuestionExposure>().GetCountAsync()).ShouldBe(0));
        using (User(f.Source.School.Reader)) {
            var overview = await Grades.GetGradingOverviewAsync(f.TaskId);
            var input = ReleaseInput(overview, overview.ClosureId!.Value, ExamResultVisibility.FullReview); input.Reason = "开放讲评后允许方案正文"; await Grades.PublishResultsAsync(f.TaskId, input);
        }
        using (User(f.Source.Student.UserId)) (await Plans.OpenMyAsync(id, published.Id)).Content.ShouldNotBeNull();
    }

    [Fact]
    public async Task Teaching_Plan_Should_Keep_Old_Published_Content_During_Editing_And_Hide_New_Drafts_After_Withdrawal()
    {
        var (f, job) = await PlanSourceAsync(); var id = Guid.NewGuid(); var input = await PlanDraftAsync(f, job); EducationTeachingPlanRevisionDto published, withdrawal;
        using (User(f.Source.School.Reader)) { await Plans.SaveDraftAsync(id, input); published = await ConfirmAndPublishPlanAsync(id); var current = await Plans.GetAsync(id);
            input.OperationId = Guid.NewGuid(); input.ExpectedRevisionId = current.Current.Id; input.ConcurrencyStamp = current.ConcurrencyStamp; input.Content = input.Content with { Title = "尚未下发的新目标" };
            await Plans.SaveDraftAsync(id, input);
        }
        using (User(f.Source.Student.UserId)) (await Plans.OpenMyAsync(id, published.Id)).Content!.Title.ShouldBe("一次方程巩固安排");
        using (User(f.Source.School.Reader)) { withdrawal = await Plans.WithdrawAsync(id, PlanTransition(await Plans.GetAsync(id), "撤回等待重新安排"));
            var current = await Plans.GetAsync(id); input.OperationId = Guid.NewGuid(); input.ExpectedRevisionId = current.Current.Id; input.ConcurrencyStamp = current.ConcurrencyStamp; await Plans.SaveDraftAsync(id, input); }
        using (User(f.Source.Student.UserId)) {
            var card = (await Plans.GetMyListAsync(new())).Items.Single(); card.VersionId.ShouldBe(withdrawal.Id); card.Availability.ShouldBe(EducationTeachingPlanAvailability.Withdrawn);
            (await Plans.OpenMyAsync(id, withdrawal.Id)).Content.ShouldBeNull(); await Should.ThrowAsync<AbpDbConcurrencyException>(() => Plans.OpenMyAsync(id, published.Id));
        }
    }
}
