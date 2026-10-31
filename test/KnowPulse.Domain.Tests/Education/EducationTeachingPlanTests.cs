using System;
using System.Linq;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.Education;

public partial class EducationTeachingPlanTests
{
    private static readonly DateTime Now = new(2026, 10, 17, 10, 0, 0, DateTimeKind.Utc);
    private static EducationTeachingPlanBasis Basis() => new() { ReportId = Guid.NewGuid(), ReportSnapshotId = Guid.NewGuid(),
        Curriculum = new() { TeacherUserId = Guid.NewGuid(), SchoolId = Guid.NewGuid(), TeachingAssignmentId = Guid.NewGuid(), EducationSubjectId = Guid.NewGuid(), TextbookVersionId = Guid.NewGuid(), SubjectName = "数学" },
        Student = new() { StudentUserId = Guid.NewGuid(), StudentEnrollmentId = Guid.NewGuid(), SchoolMembershipId = Guid.NewGuid(), StudentName = "学生" },
        WindowStartsAtUtc = Now.AddDays(-1), WindowEndsAtUtc = Now, Fingerprint = new string('a', 64),
        Points = [new() { Point = new(Guid.NewGuid(), Guid.NewGuid(), "A", "知识点"), State = EducationMasteryState.InsufficientEvidence }] };
    private static EducationTeachingPlanContent Content(EducationTeachingPlanBasis basis) => new() { Title = "教学安排", StudentGoal = "补充证据", TeacherActions = "解释并核验", SuccessCriteria = "由教师复核新题", StartsAtUtc = Now, EndsAtUtc = Now.AddDays(1), ReassessmentAtUtc = Now.AddDays(1), TargetPointRevisionIds = [basis.Points[0].Point.KnowledgePointRevisionId],
        Steps = [new() { Id = Guid.NewGuid(), Kind = EducationTeachingPlanStepKind.Diagnostic, Title = "诊断", Instructions = "先独立作答", EstimatedMinutes = 10, DueAtUtc = Now.AddHours(1) },
            new() { Id = Guid.NewGuid(), Kind = EducationTeachingPlanStepKind.Reassessment, Title = "复测", Instructions = "后续安排新题", EstimatedMinutes = 10, DueAtUtc = Now.AddDays(1) }] };
    private static EducationTeachingPlan Plan(EducationTeachingPlanBasis basis) => new(Guid.NewGuid(), basis, Guid.NewGuid(), Guid.NewGuid(), "教师", "班级");
    private static EducationTeachingPlanRevision Revision(EducationTeachingPlan plan, EducationTeachingPlanBasis basis, EducationTeachingPlanContent content, EducationTeachingPlanRevisionKind kind, EducationTeachingPlanRevision? source = null, string? reason = null)
        => new(Guid.NewGuid(), plan, kind, kind == EducationTeachingPlanRevisionKind.Withdrawn ? null : basis, kind == EducationTeachingPlanRevisionKind.Withdrawn ? null : content,
            kind == EducationTeachingPlanRevisionKind.Withdrawn ? null : "私有记录", reason, source, plan.ConcurrencyStamp, new string('b', 64), Now);
    [Fact]
    public void Teaching_Plan_Should_Preserve_Public_And_Private_Versions_Through_Confirmation_Editing_And_Withdrawal()
    {
        var basis = Basis(); var content = Content(basis); var plan = Plan(basis); var draft = Revision(plan, basis, content, EducationTeachingPlanRevisionKind.Draft); plan.Advance(draft);
        plan.PublicRevisionId.ShouldBeNull(); var confirmed = Revision(plan, basis, content, EducationTeachingPlanRevisionKind.Confirmed, draft); plan.Advance(confirmed);
        plan.PublicRevisionId.ShouldBeNull(); var published = Revision(plan, basis, content, EducationTeachingPlanRevisionKind.Published, confirmed); plan.Advance(published);
        plan.PublicRevisionId.ShouldBe(published.Id); var newDraft = Revision(plan, basis, content with { Title = "尚未发布" }, EducationTeachingPlanRevisionKind.Draft); plan.Advance(newDraft);
        plan.PublicRevisionId.ShouldBe(published.Id); var withdrawn = Revision(plan, basis, content, EducationTeachingPlanRevisionKind.Withdrawn, newDraft, "撤回待复核"); plan.Advance(withdrawn);
        plan.PublicRevisionId.ShouldBe(withdrawn.Id); plan.PublishedRevisionId.ShouldBeNull(); plan.LastPublishedRevisionId.ShouldBe(published.Id);
        var later = Revision(plan, basis, content, EducationTeachingPlanRevisionKind.Draft); plan.Advance(later); plan.PublicRevisionId.ShouldBe(withdrawn.Id);
        published.InternalNotes.ShouldBe("私有记录"); withdrawn.Content().ShouldBeNull();
    }
    [Theory]
    [InlineData("missing-diagnosis")]
    [InlineData("changed-content")]
    [InlineData("changed-notes")]
    [InlineData("wrong-target")]
    [InlineData("publish-draft")]
    public void Teaching_Plan_Confirmation_Should_Reject_Unconfirmed_Or_Changed_Frozen_Content(string invalid)
    {
        var basis = Basis(); var content = Content(basis); var plan = Plan(basis); var draft = Revision(plan, basis, content, EducationTeachingPlanRevisionKind.Draft); plan.Advance(draft);
        var next = invalid switch { "missing-diagnosis" => content with { Steps = content.Steps.Select((x, i) => i == 0 ? x with { Kind = EducationTeachingPlanStepKind.Practice } : x).ToList() },
            "wrong-target" => content with { TargetPointRevisionIds = [Guid.NewGuid()] }, _ => content with { Title = "变更内容" } };
        if (invalid == "changed-notes") Should.Throw<BusinessException>(() => new EducationTeachingPlanRevision(Guid.NewGuid(), plan, EducationTeachingPlanRevisionKind.Confirmed, basis, content, "不同的私有记录", null, draft, plan.ConcurrencyStamp, new string('b', 64), Now));
        else Should.Throw<BusinessException>(() => Revision(plan, basis, next, invalid == "publish-draft" ? EducationTeachingPlanRevisionKind.Published : EducationTeachingPlanRevisionKind.Confirmed, draft));
    }
    [Theory]
    [InlineData("minutes")]
    [InlineData("duplicate")]
    [InlineData("time")]
    [InlineData("reassessment")]
    public void Teaching_Plan_Content_Should_Require_Concrete_Safe_Steps_And_Consistent_Dates(string invalid)
    {
        var content = Content(Basis()); content = invalid switch { "minutes" => content with { Steps = [content.Steps[0] with { EstimatedMinutes = 0 }, content.Steps[1]] },
            "duplicate" => content with { Steps = [content.Steps[1], content.Steps[1]] }, "time" => content with { EndsAtUtc = Now.AddDays(-1) }, _ => content with { ReassessmentAtUtc = Now.AddHours(2) } };
        Should.Throw<BusinessException>(content.Validate);
    }
    [Fact]
    public void Teaching_Plan_Context_Should_Bind_Exact_Publication_And_Question_And_Remain_Unverified()
    {
        var original = Basis(); var q = new EducationTeachingPlanQuestion(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var basis = original with { Questions = [q] }; var content = Content(basis); var plan = Plan(basis);
        var draft = Revision(plan, basis, content, EducationTeachingPlanRevisionKind.Draft); plan.Advance(draft);
        var confirmed = Revision(plan, basis, content, EducationTeachingPlanRevisionKind.Confirmed, draft); plan.Advance(confirmed);
        var published = Revision(plan, basis, content, EducationTeachingPlanRevisionKind.Published, confirmed); plan.Advance(published);
        var delivered = new EducationTeachingPlanQuestionExposure(plan, published, q, Now.AddMinutes(1));
        new EducationTeachingPlanQuestionExposure(plan, published, q, Now.AddMinutes(2)).Id.ShouldBe(delivered.Id); delivered.Evidence().Use.ShouldBe(EducationLearningExposureUse.Unverified);
        Should.Throw<BusinessException>(() => new EducationTeachingPlanQuestionExposure(plan, published, q with { PaperQuestionId = Guid.NewGuid() }, Now));
        Should.Throw<BusinessException>(() => new EducationTeachingPlanQuestionExposure(plan, draft, q, Now));
    }
}
