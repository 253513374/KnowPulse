using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

public partial class EducationCurriculumRetrievalTests
{
    private IEducationTeachingPlanExecutionAppService Execution => GetRequiredService<IEducationTeachingPlanExecutionAppService>();
    private static DecideEducationPlanExecutionDto ExecutionInput(EducationPlanExecutionReviewDto view, EducationPlanDecision decision) => new() {
        OperationId = Guid.NewGuid(), ExpectedPublishedRevisionId = view.Evidence.PublishedRevisionId, ExpectedDecisionId = view.CurrentDecisionId,
        ConcurrencyStamp = view.ConcurrencyStamp, EvidenceFingerprint = view.EvidenceFingerprint, Decision = decision,
        Reason = "PRIVATE-EXECUTION-REASON 核对当前事实及证据不足后作出安排，不代表掌握", ConfirmReviewedEvidenceAndUncertainty = true };
    [Fact]
    public async Task Withdrawn_Publication_Should_Keep_Authorized_Private_Execution_History_Without_Enabling_New_Decisions()
    {
        var (f, report) = await PlanSourceAsync(); var id = Guid.NewGuid(); using var teacher = User(f.Source.School.Reader);
        await Plans.SaveDraftAsync(id, await PlanDraftAsync(f, report)); var publication = await ConfirmAndPublishPlanAsync(id);
        var receipt = await Execution.DecideExecutionAsync(id, ExecutionInput(await Execution.GetExecutionReviewAsync(id), EducationPlanDecision.Pause));
        await Plans.WithdrawAsync(id, PlanTransition(await Plans.GetAsync(id), "撤回后保留本人复盘历史"));
        var review = await Execution.GetExecutionReviewAsync(id); review.IsCurrentPublication.ShouldBeFalse(); review.Evidence.PublishedRevisionId.ShouldBe(publication.Id);
        (await Execution.GetExecutionHistoryAsync(id, review.CurrentDecisionId, new())).Items.Single().Id.ShouldBe(receipt.Id);
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => Execution.DecideExecutionAsync(id, ExecutionInput(review, EducationPlanDecision.Resume)));
        using (User(f.Source.Student.UserId)) { var view = await Plans.OpenMyAsync(id, (await Plans.GetMyListAsync(new())).Items.Single().VersionId); view.Content.ShouldBeNull(); }
    }
    [Fact]
    public async Task Execution_Pause_Should_Stop_New_Activities_Keep_Started_Completion_And_Recover_Old_Receipts_Without_Rollback()
    {
        var (f, report) = await PlanSourceAsync(); var id = Guid.NewGuid(); EducationTeachingPlanRevisionDto published;
        using (User(f.Source.School.Reader)) { await Plans.SaveDraftAsync(id, await PlanDraftAsync(f, report)); published = await ConfirmAndPublishPlanAsync(id); }
        Guid started;
        using (User(f.Source.Student.UserId)) {
            var view = await Activities.GetMyActivitiesAsync(id, published.Id); var row = view.Activities.Single(x => x.StepOrder == 2); started = row.Id;
            await Activities.SaveMyActivityAsync(row.Id, ActivityInput(row, EducationTeachingActivityAction.Started));
        }
        DecideEducationPlanExecutionDto pause;
        using (User(f.Source.School.Reader)) { pause = ExecutionInput(await Execution.GetExecutionReviewAsync(id), EducationPlanDecision.Pause); await Execution.DecideExecutionAsync(id, pause); }
        using (User(f.Source.Student.UserId)) {
            var view = await Activities.GetMyActivitiesAsync(id, published.Id); view.Plan.ExecutionState.ShouldBe(EducationPlanExecutionState.Paused); view.Plan.Content.ShouldNotBeNull();
            view.Activities.All(x => !x.CanStart).ShouldBeTrue(); view.Activities.Single(x => x.Id == started).CanReadMaterials.ShouldBeTrue();
            var unstarted = view.Activities.Single(x => x.StepOrder == 1); unstarted.CanReadMaterials.ShouldBeFalse();
            await Should.ThrowAsync<BusinessException>(() => Activities.SaveMyActivityAsync(unstarted.Id, ActivityInput(unstarted, EducationTeachingActivityAction.Started)));
            var complete = await Activities.SaveMyActivityAsync(started, ActivityInput(view.Activities.Single(x => x.Id == started), EducationTeachingActivityAction.Completed)); complete.Action.ShouldBe(EducationTeachingActivityAction.Completed);
            (await Activities.GetMyActivityWorkloadAsync(WorkloadWindow())).Days.Sum(x => x.EstimatedMinutes).ShouldBe(0);
            (await GetRequiredService<IEducationLearningWorkloadAppService>().GetMyLearningWorkloadAsync(WorkloadWindow())).Items.ShouldNotContain(x => x.Kind == EducationLearningWorkloadKind.PlanActivity && x.SourceId == id);
            JsonSerializer.Serialize(view).ShouldNotContain("PRIVATE-EXECUTION-REASON");
            await Should.ThrowAsync<AbpAuthorizationException>(() => Execution.GetExecutionReviewAsync(id));
        }
        using (User(f.Source.School.Reader)) {
            await Execution.DecideExecutionAsync(id, ExecutionInput(await Execution.GetExecutionReviewAsync(id), EducationPlanDecision.Resume));
            (await Execution.DecideExecutionAsync(id, pause)).After.ShouldBe(EducationPlanExecutionState.Paused);
            (await Execution.GetExecutionReviewAsync(id)).State.ShouldBe(EducationPlanExecutionState.Running);
            (await Execution.GetExecutionHistoryAsync(id, (await Execution.GetExecutionReviewAsync(id)).CurrentDecisionId, new())).TotalCount.ShouldBe(2);
        }
        using (User(f.Source.Student.UserId)) {
            var view = await Activities.GetMyActivitiesAsync(id, published.Id); var row = view.Activities.Single(x => x.StepOrder == 1);
            row.CanStart.ShouldBeTrue(); await Activities.SaveMyActivityAsync(row.Id, ActivityInput(row, EducationTeachingActivityAction.Started));
            (await Grades.GetMyResultAsync(f.RecipientId)).TotalScore.ShouldBe(10);
        }
    }
    [Fact]
    public async Task Execution_Review_Should_Reject_Changed_Student_Evidence_And_Keep_Private_Continue_Decisions_Out_Of_Public_State()
    {
        var (f, report) = await PlanSourceAsync(); var id = Guid.NewGuid(); EducationTeachingPlanRevisionDto published; DecideEducationPlanExecutionDto old;
        using (User(f.Source.School.Reader)) { await Plans.SaveDraftAsync(id, await PlanDraftAsync(f, report)); published = await ConfirmAndPublishPlanAsync(id); old = ExecutionInput(await Execution.GetExecutionReviewAsync(id), EducationPlanDecision.Pause); }
        using (User(f.Source.Student.UserId)) { var row = (await Activities.GetMyActivitiesAsync(id, published.Id)).Activities.Single(x => x.StepOrder == 1); await Activities.SaveMyActivityAsync(row.Id, ActivityInput(row, EducationTeachingActivityAction.Difficulty, "需要核对步骤")); }
        using (User(f.Source.School.Reader)) {
            await Should.ThrowAsync<AbpDbConcurrencyException>(() => Execution.DecideExecutionAsync(id, old));
            var view = await Execution.GetExecutionReviewAsync(id); view.Evidence.Activities.First().StudentAction.ShouldBe(EducationTeachingActivityAction.Difficulty);
            var command = ExecutionInput(view, EducationPlanDecision.Continue); var receipt = await Execution.DecideExecutionAsync(id, command);
            (await Execution.GetExecutionOperationAsync(id, command.OperationId))!.Id.ShouldBe(receipt.Id);
            command.Reason += "不同正文"; await Should.ThrowAsync<BusinessException>(() => Execution.DecideExecutionAsync(id, command));
            await Should.ThrowAsync<AbpDbConcurrencyException>(() => Execution.GetExecutionHistoryAsync(id, null, new()));
        }
        using (User(f.Source.Student.UserId)) { var view = await Plans.OpenMyAsync(id, published.Id); view.ExecutionChangedAtUtc.ShouldBeNull(); view.ExecutionState.ShouldBe(EducationPlanExecutionState.Running); JsonSerializer.Serialize(view).ShouldNotContain("PRIVATE-EXECUTION-REASON"); }
    }
    [Fact]
    public async Task Execution_Adjustment_Should_Require_A_New_Confirmed_Publication_And_Explicit_Resume()
    {
        var (f, report) = await PlanSourceAsync(); var id = Guid.NewGuid(); using var teacher = User(f.Source.School.Reader);
        await Plans.SaveDraftAsync(id, await PlanDraftAsync(f, report)); var first = await ConfirmAndPublishPlanAsync(id);
        await Execution.DecideExecutionAsync(id, ExecutionInput(await Execution.GetExecutionReviewAsync(id), EducationPlanDecision.Adjust));
        (await Execution.GetExecutionReviewAsync(id)).AdjustmentNeedsPublication.ShouldBeTrue();
        var blockedResume = ExecutionInput(await Execution.GetExecutionReviewAsync(id), EducationPlanDecision.Resume);
        await Should.ThrowAsync<BusinessException>(() => Execution.DecideExecutionAsync(id, blockedResume));
        var current = await Plans.GetAsync(id); var draft = await PlanDraftAsync(f, report); draft.ExpectedRevisionId = current.Current.Id; draft.ConcurrencyStamp = current.ConcurrencyStamp;
        draft.Content = draft.Content with { Title = "经教师明确调整的安排" }; await Plans.SaveDraftAsync(id, draft); var second = await ConfirmAndPublishPlanAsync(id, "调整任务安排");
        second.Id.ShouldNotBe(first.Id); var view = await Execution.GetExecutionReviewAsync(id); view.State.ShouldBe(EducationPlanExecutionState.Paused); view.AdjustmentNeedsPublication.ShouldBeFalse();
        await Execution.DecideExecutionAsync(id, ExecutionInput(view, EducationPlanDecision.Resume)); (await Execution.GetExecutionReviewAsync(id)).State.ShouldBe(EducationPlanExecutionState.Running);
        (await Activities.GetActivityProgressAsync(id, first.Id)).Activities.Count.ShouldBe(3);
    }
    [Theory]
    [InlineData(EducationPlanDecision.Pause, EducationPlanExecutionState.Paused)]
    [InlineData(EducationPlanDecision.End, EducationPlanExecutionState.Ended)]
    [InlineData(EducationPlanDecision.Terminate, EducationPlanExecutionState.Terminated)]
    public async Task Execution_Control_Should_Block_New_Formal_Starts_Through_Original_Assessment_And_Preserve_Existing_Attempts(EducationPlanDecision decision, EducationPlanExecutionState state)
    {
        var f = await FormalFixtureAsync();
        using (User(f.Source.Source.School.Reader)) await Execution.DecideExecutionAsync(f.PlanId, ExecutionInput(await Execution.GetExecutionReviewAsync(f.PlanId), decision));
        using (User(f.Source.Source.Student.UserId)) {
            (await Assessments.GetMyStudentTaskAsync(f.RecipientId)).CanStart.ShouldBeFalse();
            await Should.ThrowAsync<BusinessException>(() => Assessments.StartOrResumeAsync(f.RecipientId, new() { OperationId = Guid.NewGuid() }));
            (await Activities.GetMyActivitiesAsync(f.PlanId, f.PublicationId)).Plan.ExecutionState.ShouldBe(state);
            (await GetRequiredService<IEducationLearningWorkloadAppService>().GetMyLearningWorkloadAsync(WorkloadWindow())).Items.ShouldNotContain(x => x.SourceId == f.PlanId || x.Id == f.RecipientId);
        }
        var other = await FormalFixtureAsync(); EducationStudentAssessmentAttemptDto attempt;
        using (User(other.Source.Source.Student.UserId)) attempt = await Assessments.StartOrResumeAsync(other.RecipientId, new() { OperationId = Guid.NewGuid() });
        using (User(other.Source.Source.School.Reader)) await Execution.DecideExecutionAsync(other.PlanId, ExecutionInput(await Execution.GetExecutionReviewAsync(other.PlanId), decision));
        using (User(other.Source.Source.Student.UserId)) {
            var resumed = await Assessments.StartOrResumeAsync(other.RecipientId, new() { OperationId = Guid.NewGuid() }); resumed.Id.ShouldBe(attempt.Id); resumed.ExpiresAtUtc.ShouldBe(attempt.ExpiresAtUtc);
            var question = resumed.Paper.Questions.Single(); var saved = await Assessments.SaveResponseAsync(resumed.Id, new() { OperationId = Guid.NewGuid(), PaperQuestionId = question.Id,
                PaperSnapshotVersion = resumed.PaperSnapshotVersion, ExpectedResponseSetVersion = resumed.ResponseSetVersion, Response = new() { Type = QuestionType.SingleChoice, SelectedOptionKeys = ["A"] } });
            var submitted = await SubmitReviewedAsync(resumed.Id, new() { OperationId = Guid.NewGuid(), PaperSnapshotVersion = resumed.PaperSnapshotVersion, ExpectedResponseSetVersion = saved.ResultResponseSetVersion, ConfirmSavedOnly = true });
            submitted.SealedAtUtc.ShouldNotBe(default(DateTime));
        }
        using (User(other.Source.Source.School.Reader)) {
            var evidence = await Execution.GetExecutionReviewAsync(other.PlanId); evidence.Evidence.Activities.Single(x => x.Kind == EducationTeachingPlanStepKind.Reassessment).SealedAtUtc.ShouldNotBeNull();
            if (state != EducationPlanExecutionState.Paused) await Should.ThrowAsync<BusinessException>(() => Execution.DecideExecutionAsync(other.PlanId, ExecutionInput(evidence, EducationPlanDecision.Resume)));
        }
    }

    [Fact]
    public async Task Execution_Review_Should_Freeze_Exact_Confirmed_Reassessment_Versions_And_Reject_A_Concurrent_Correction()
    {
        var f = await FormalFixtureAsync(); Guid submissionId;
        using (User(f.Source.Source.Student.UserId)) {
            var attempt = await Assessments.StartOrResumeAsync(f.RecipientId, new() { OperationId = Guid.NewGuid() }); var question = attempt.Paper.Questions.Single();
            var saved = await Assessments.SaveResponseAsync(attempt.Id, new() { OperationId = Guid.NewGuid(), PaperQuestionId = question.Id, PaperSnapshotVersion = attempt.PaperSnapshotVersion,
                ExpectedResponseSetVersion = attempt.ResponseSetVersion, Response = new() { Type = QuestionType.SingleChoice, SelectedOptionKeys = ["A"] } });
            submissionId = (await SubmitReviewedAsync(attempt.Id, new() { OperationId = Guid.NewGuid(), PaperSnapshotVersion = attempt.PaperSnapshotVersion, ExpectedResponseSetVersion = saved.ResultResponseSetVersion, ConfirmSavedOnly = true })).Id;
        }
        using (User(f.Source.Source.School.Reader)) {
            var graded = await Grades.GetSubmissionForGradingAsync(submissionId); var initialId = graded.Questions.Single().Grade.Confirmed!.Id;
            var initial = await Execution.GetExecutionReviewAsync(f.PlanId); var first = initial.Evidence.Activities.Single(x => x.Kind == EducationTeachingPlanStepKind.Reassessment);
            first.ConfirmedQuestionCount.ShouldBe(1); first.ConfirmedGradeRevisionIds.ShouldBe([initialId]);
            var pending = ExecutionInput(initial, EducationPlanDecision.End); await ConfirmManualGradeAsync(submissionId, 1, "复核新题答卷后更正评分");
            await Should.ThrowAsync<AbpDbConcurrencyException>(() => Execution.DecideExecutionAsync(f.PlanId, pending));
            var current = await Execution.GetExecutionReviewAsync(f.PlanId); var next = current.Evidence.Activities.Single(x => x.Kind == EducationTeachingPlanStepKind.Reassessment);
            next.ConfirmedGradeRevisionIds.Single().ShouldNotBe(initialId); next.ConfirmedGradeSetHash.ShouldNotBe(first.ConfirmedGradeSetHash);
            var ended = await Execution.DecideExecutionAsync(f.PlanId, ExecutionInput(current, EducationPlanDecision.End));
            ended.Evidence.Activities.Single(x => x.Kind == EducationTeachingPlanStepKind.Reassessment).ConfirmedGradeRevisionIds.ShouldBe(next.ConfirmedGradeRevisionIds);
        }
        using (User(f.Source.Source.Student.UserId)) { var result = await Grades.GetMyResultAsync(f.RecipientId); result.CurrentResultVisibility.ShouldBe(ExamResultVisibility.Hidden); result.Questions.ShouldBeEmpty(); }
    }
}
