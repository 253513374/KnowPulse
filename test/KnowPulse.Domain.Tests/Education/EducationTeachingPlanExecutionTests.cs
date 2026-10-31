using System;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.Education;

public partial class EducationTeachingPlanTests
{
    private static EducationTeachingPlanExecutionFixture ExecutionPlan()
    {
        var basis = Basis(); var content = Content(basis); var plan = Plan(basis);
        PublishExecutionVersion(plan, basis, content); return new(plan, basis, content);
    }
    private sealed record EducationTeachingPlanExecutionFixture(EducationTeachingPlan Plan, EducationTeachingPlanBasis Basis, EducationTeachingPlanContent Content);
    private static void PublishExecutionVersion(EducationTeachingPlan plan, EducationTeachingPlanBasis basis, EducationTeachingPlanContent content)
    {
        var draft = Revision(plan, basis, content, EducationTeachingPlanRevisionKind.Draft); plan.Advance(draft);
        var reason = plan.LastPublishedRevisionId.HasValue ? "经教师核对调整安排" : null;
        var confirmed = Revision(plan, basis, content, EducationTeachingPlanRevisionKind.Confirmed, draft, reason); plan.Advance(confirmed);
        plan.Advance(Revision(plan, basis, content, EducationTeachingPlanRevisionKind.Published, confirmed, reason));
    }
    private static EducationTeachingPlanDecisionRevision ExecutionDecision(EducationTeachingPlan plan, EducationPlanDecision action,
        EducationTeachingPlanDecisionRevision? previous = null, bool fresh = true, bool reviewed = true) => new(Guid.NewGuid(), plan, previous, action, "私有教学反思",
            new() { PublishedRevisionId = plan.PublishedRevisionId!.Value, SourceIsCurrent = fresh }, new string('d', 64), reviewed,
            new string('e', 64), plan.ConcurrencyStamp, Now);
    [Fact]
    public void Execution_Pause_Resume_And_Continue_Should_Preserve_The_Published_Content_And_Private_Review_Time()
    {
        var f = ExecutionPlan(); var publication = f.Plan.PublishedRevisionId; var contentNumber = f.Plan.RevisionNumber;
        var continued = ExecutionDecision(f.Plan, EducationPlanDecision.Continue); f.Plan.AdvanceDecision(continued);
        f.Plan.ExecutionChangedAtUtc.ShouldBeNull();
        var pause = ExecutionDecision(f.Plan, EducationPlanDecision.Pause, continued); f.Plan.AdvanceDecision(pause);
        f.Plan.ExecutionState.ShouldBe(EducationPlanExecutionState.Paused); f.Plan.ExecutionChangedAtUtc.ShouldBe(Now);
        f.Plan.AdvanceDecision(ExecutionDecision(f.Plan, EducationPlanDecision.Resume, pause));
        f.Plan.ExecutionState.ShouldBe(EducationPlanExecutionState.Running); f.Plan.PublishedRevisionId.ShouldBe(publication); f.Plan.RevisionNumber.ShouldBe(contentNumber);
    }
    [Fact]
    public void Adjusting_Should_Require_Explicit_New_Publication_Then_Resume_Without_Resetting_State_On_Publish()
    {
        var f = ExecutionPlan(); var adjust = ExecutionDecision(f.Plan, EducationPlanDecision.Adjust); f.Plan.AdvanceDecision(adjust);
        Should.Throw<BusinessException>(() => ExecutionDecision(f.Plan, EducationPlanDecision.Resume, adjust));
        PublishExecutionVersion(f.Plan, f.Basis, f.Content with { Title = "已确认调整" });
        f.Plan.ExecutionState.ShouldBe(EducationPlanExecutionState.Paused);
        f.Plan.AdvanceDecision(ExecutionDecision(f.Plan, EducationPlanDecision.Resume, adjust)); f.Plan.ExecutionState.ShouldBe(EducationPlanExecutionState.Running);
    }
    [Theory]
    [InlineData(EducationPlanDecision.End, EducationPlanExecutionState.Ended)]
    [InlineData(EducationPlanDecision.Terminate, EducationPlanExecutionState.Terminated)]
    public void Terminal_Decisions_Should_Preserve_Publication_And_Reject_Reopening_Or_Republishing(EducationPlanDecision action, EducationPlanExecutionState state)
    {
        var f = ExecutionPlan(); var publication = f.Plan.PublishedRevisionId; var decision = ExecutionDecision(f.Plan, action); f.Plan.AdvanceDecision(decision);
        f.Plan.ExecutionState.ShouldBe(state); f.Plan.PublishedRevisionId.ShouldBe(publication);
        Should.Throw<BusinessException>(() => ExecutionDecision(f.Plan, EducationPlanDecision.Resume, decision));
        var draft = Revision(f.Plan, f.Basis, f.Content, EducationTeachingPlanRevisionKind.Draft); f.Plan.Advance(draft);
        var confirmed = Revision(f.Plan, f.Basis, f.Content, EducationTeachingPlanRevisionKind.Confirmed, draft, "结束后尝试换版"); f.Plan.Advance(confirmed);
        var next = Revision(f.Plan, f.Basis, f.Content, EducationTeachingPlanRevisionKind.Published, confirmed, "结束后尝试换版");
        Should.Throw<BusinessException>(() => f.Plan.Advance(next)); f.Plan.PublishedRevisionId.ShouldBe(publication);
    }
    [Theory]
    [InlineData(EducationPlanDecision.Continue)]
    [InlineData(EducationPlanDecision.Adjust)]
    [InlineData(EducationPlanDecision.End)]
    public void Teaching_Decisions_Require_Explicit_Review_Of_Evidence_And_Uncertainty(EducationPlanDecision action)
        => Should.Throw<BusinessException>(() => ExecutionDecision(ExecutionPlan().Plan, action, reviewed: false));
    [Fact]
    public void Stale_Evidence_May_Be_Stopped_But_Cannot_Resume_Or_Continue()
    {
        var f = ExecutionPlan(); Should.Throw<BusinessException>(() => ExecutionDecision(f.Plan, EducationPlanDecision.Continue, fresh: false));
        var pause = ExecutionDecision(f.Plan, EducationPlanDecision.Pause, fresh: false); f.Plan.AdvanceDecision(pause);
        Should.Throw<BusinessException>(() => ExecutionDecision(f.Plan, EducationPlanDecision.Resume, pause, fresh: false));
        f.Plan.AdvanceDecision(ExecutionDecision(f.Plan, EducationPlanDecision.End, pause, fresh: false));
    }
    [Fact]
    public void Concurrent_Decision_And_Foreign_Predecessor_Must_Not_Overwrite_The_Current_Control_Head()
    {
        var f = ExecutionPlan(); var old = ExecutionDecision(f.Plan, EducationPlanDecision.Continue); var pause = ExecutionDecision(f.Plan, EducationPlanDecision.Pause);
        f.Plan.AdvanceDecision(pause); Should.Throw<BusinessException>(() => f.Plan.AdvanceDecision(old));
        Should.Throw<BusinessException>(() => ExecutionDecision(f.Plan, EducationPlanDecision.Resume)); f.Plan.CurrentDecisionId.ShouldBe(pause.Id);
    }
}
