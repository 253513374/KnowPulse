using System;
using KnowPulse.Education;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.Education;

public partial class EducationTeachingPlanTests
{
    private static (EducationTeachingPlanQuestionExposure Exposure, EducationTeachingPlanRevision Published) PlanContext()
    {
        var q = new EducationTeachingPlanQuestion(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var basis = Basis() with { Questions = [q] }; var content = Content(basis); var plan = Plan(basis);
        var draft = Revision(plan, basis, content, EducationTeachingPlanRevisionKind.Draft); plan.Advance(draft);
        var confirmed = Revision(plan, basis, content, EducationTeachingPlanRevisionKind.Confirmed, draft); plan.Advance(confirmed);
        var published = Revision(plan, basis, content, EducationTeachingPlanRevisionKind.Published, confirmed); plan.Advance(published);
        return (new(plan, published, q, Now.AddMinutes(1)), published);
    }
    [Fact]
    public void Plan_Context_Review_Should_Keep_Immutable_Decisions_And_Exact_Exposure_Pairing()
    {
        var (exposure, published) = PlanContext(); var root = new EducationTeachingPlanHintReview(exposure, published);
        var first = new EducationTeachingPlanHintReviewRevision(Guid.NewGuid(), root, EducationLearningExposureUse.ConfirmedNoHints, true, "核对原版", root.ConcurrencyStamp, new string('c', 64), Now.AddMinutes(2));
        root.Advance(first); exposure.Evidence(first).Use.ShouldBe(EducationLearningExposureUse.ConfirmedNoHints);
        var next = new EducationTeachingPlanHintReviewRevision(Guid.NewGuid(), root, EducationLearningExposureUse.Confirmed, true, "更正判断", root.ConcurrencyStamp, new string('d', 64), Now.AddMinutes(3));
        root.Advance(next); root.RevisionNumber.ShouldBe(2); next.BasedOnRevisionId.ShouldBe(first.Id);
        root.CurrentRevisionId.ShouldBe(next.Id); exposure.Evidence(first).HintReviewRevisionId.ShouldBe(first.Id);
        exposure.Evidence(next).Use.ShouldBe(EducationLearningExposureUse.Confirmed);
        exposure.Evidence().Use.ShouldBe(EducationLearningExposureUse.Unverified);
        Should.Throw<BusinessException>(() => root.Advance(first));
        var (otherExposure, otherVersion) = PlanContext();
        Should.Throw<BusinessException>(() => new EducationTeachingPlanHintReview(exposure, otherVersion));
        Should.Throw<BusinessException>(() => otherExposure.Evidence(next));
    }
    [Theory]
    [InlineData("not-confirmed")]
    [InlineData("invalid-use")]
    [InlineData("stale-stamp")]
    [InlineData("blank-reason")]
    public void Plan_Context_Review_Should_Reject_An_Unconfirmed_Invalid_Or_Stale_Decision(string invalid)
    {
        var (exposure, published) = PlanContext(); var root = new EducationTeachingPlanHintReview(exposure, published);
        Should.Throw<Exception>(() => new EducationTeachingPlanHintReviewRevision(Guid.NewGuid(), root,
            invalid == "invalid-use" ? (EducationLearningExposureUse)999 : EducationLearningExposureUse.ConfirmedNoHints,
            invalid != "not-confirmed", invalid == "blank-reason" ? " " : "核对实际提供的版本",
            invalid == "stale-stamp" ? "old" : root.ConcurrencyStamp, new string('c', 64), Now.AddMinutes(2)));
        root.CurrentRevisionId.ShouldBeNull(); root.RevisionNumber.ShouldBe(0);
    }
}
