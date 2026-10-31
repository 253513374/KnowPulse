using System;
using KnowPulse.Education;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.Education;

public partial class EducationTeachingPlanTests
{
    private static EducationTeachingActivity Activity(EducationTeachingPlanStepKind kind = EducationTeachingPlanStepKind.Reading)
    {
        var basis = Basis(); var original = Content(basis); var step = new EducationTeachingPlanStep { Id = Guid.NewGuid(), Kind = kind, Title = "独立学习活动", Instructions = "阅读并解释思路", EstimatedMinutes = 15, DueAtUtc = Now.AddHours(2) };
        var content = original with { Steps = [original.Steps[0], step, original.Steps[1]] }; var plan = Plan(basis);
        var draft = Revision(plan, basis, content, EducationTeachingPlanRevisionKind.Draft); plan.Advance(draft);
        var confirmed = Revision(plan, basis, content, EducationTeachingPlanRevisionKind.Confirmed, draft); plan.Advance(confirmed);
        var published = Revision(plan, basis, content, EducationTeachingPlanRevisionKind.Published, confirmed); plan.Advance(published);
        return new(published, step.Id, Now.AddMinutes(1));
    }
    private static EducationTeachingActivityRevision Act(EducationTeachingActivity root, EducationTeachingActivityRevision? current, EducationTeachingActivityAction action,
        string? text = null, bool consent = true, bool confirmed = true, EducationTeachingActivityDifficulty? difficulty = null, int? minutes = null)
        => new(Guid.NewGuid(), root, current, action, difficulty, text, minutes, consent, confirmed, action == EducationTeachingActivityAction.TeacherFollowUp ? "私有处理记录" : null,
            action == EducationTeachingActivityAction.TeacherFollowUp ? current?.Id : null, new string('c',64), Now.AddMinutes(10));
    [Fact]
    public void Activity_Should_Preserve_Delivery_And_Separate_Student_Progress_From_Teacher_Handling()
    {
        var root = Activity(); root.StepOrder.ShouldBe(2); root.MarkProvided(Now.AddMinutes(2)); root.MarkProvided(Now.AddMinutes(3)); root.FirstProvidedAtUtc.ShouldBe(Now.AddMinutes(2));
        var started = Act(root, null, EducationTeachingActivityAction.Started); root.Advance(started);
        var complete = Act(root, started, EducationTeachingActivityAction.Completed); root.Advance(complete);
        var restarted = Act(root, complete, EducationTeachingActivityAction.Started, "重新核对理解"); root.Advance(restarted);
        var blocked = Act(root, restarted, EducationTeachingActivityAction.Difficulty, "看不懂", difficulty: EducationTeachingActivityDifficulty.Unclear); root.Advance(blocked);
        var handled = Act(root, blocked, EducationTeachingActivityAction.TeacherFollowUp); root.Advance(handled);
        root.RevisionNumber.ShouldBe(5); root.StudentRevisionNumber.ShouldBe(4); root.CurrentStudentRevisionId.ShouldBe(blocked.Id);
        handled.StudentRevisionNumber.ShouldBeNull(); handled.ActorUserId.ShouldBe(root.OwnerUserId); handled.SharedText.ShouldBeNull(); handled.InternalTeacherNote.ShouldNotBeNull();
        complete.Action.ShouldBe(EducationTeachingActivityAction.Completed); root.CurrentFollowUpRevisionId.ShouldBe(handled.Id);
        Should.Throw<BusinessException>(() => root.Advance(blocked));
    }
    [Theory]
    [InlineData("undelivered")]
    [InlineData("complete-before-start")]
    [InlineData("not-consented")]
    [InlineData("not-confirmed")]
    [InlineData("negative-minutes")]
    [InlineData("diagnostic")]
    [InlineData("reassessment")]
    [InlineData("teacher-without-difficulty")]
    public void Activity_Should_Reject_Unprovided_Unconfirmed_Or_Invalid_Progress(string invalid)
    {
        var root = Activity(invalid == "diagnostic" ? EducationTeachingPlanStepKind.Diagnostic : invalid == "reassessment" ? EducationTeachingPlanStepKind.Reassessment : EducationTeachingPlanStepKind.Reading);
        if (invalid != "undelivered") root.MarkProvided(Now.AddMinutes(2));
        EducationTeachingActivityRevision? current = null;
        if (invalid is "not-confirmed" or "negative-minutes" or "diagnostic" or "reassessment") { current = Act(root, null, EducationTeachingActivityAction.Started); root.Advance(current); }
        Should.Throw<BusinessException>(() => Act(root, current,
            invalid == "undelivered" ? EducationTeachingActivityAction.Started : invalid == "not-consented" ? EducationTeachingActivityAction.Difficulty :
            invalid == "teacher-without-difficulty" ? EducationTeachingActivityAction.TeacherFollowUp : EducationTeachingActivityAction.Completed,
            invalid == "not-consented" ? "不应私自上交" : null, invalid != "not-consented", invalid != "not-confirmed",
            invalid == "not-consented" ? EducationTeachingActivityDifficulty.Unclear : null, invalid == "negative-minutes" ? -1 : null));
    }
}
