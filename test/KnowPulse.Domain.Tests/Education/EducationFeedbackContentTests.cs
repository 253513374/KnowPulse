using System;
using System.Linq;
using KnowPulse.Education;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.Education;

public class EducationFeedbackContentTests
{
    [Fact]
    public void Phrase_Revisions_Should_Preserve_Text_And_Reject_Wrong_Root_Or_First_Archived_State()
    {
        var owner = Guid.NewGuid(); var root = new EducationFeedbackPhrase(Guid.NewGuid(), null, owner);
        var revision = new EducationFeedbackPhraseRevision(Guid.NewGuid(), root, " 原文\n第二行 ", false, new string('a', 64), DateTime.UtcNow);
        root.Advance(revision); root.RevisionNumber.ShouldBe(1); revision.Text.ShouldBe(" 原文\n第二行 ");
        Should.Throw<BusinessException>(() => root.Advance(revision));
        var other = new EducationFeedbackPhrase(Guid.NewGuid(), null, owner);
        Should.Throw<BusinessException>(() => other.Advance(revision));
        var archived = new EducationFeedbackPhraseRevision(Guid.NewGuid(), other, "归档", true, new string('b', 64), DateTime.UtcNow);
        Should.Throw<BusinessException>(() => other.Advance(archived));
    }

    private static EducationFeedbackContent Valid() => new()
    { Kind = EducationFeedbackKind.Daily, Context = "课堂", Text = " 保留明确选择的反馈\n及换行 ", OccurredAtUtc = new(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc) };

    [Fact]
    public void Feedback_Content_Should_Preserve_Selected_Text_And_Canonicalize_Only_Reference_Order()
    {
        var content = Valid(); content.AssessmentTaskIds = [Guid.NewGuid(), Guid.NewGuid()];
        var restored = EducationFeedbackContent.Parse(content.ToCanonicalJson());
        restored.Text.ShouldBe(content.Text); restored.AssessmentTaskIds.ShouldBe(content.AssessmentTaskIds.OrderBy(x => x));
        restored.OccurredAtUtc.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("length")]
    [InlineData("local-time")]
    [InlineData("period-end")]
    [InlineData("missing-task")]
    [InlineData("duplicate-task")]
    [InlineData("too-many-tasks")]
    [InlineData("empty-observation")]
    public void Invalid_Public_Feedback_Should_Be_Rejected(string change)
    {
        var content = Valid();
        if (change == "empty") content.Text = "  ";
        if (change == "length") content.Text = new string('a', 4001);
        if (change == "local-time") content.OccurredAtUtc = DateTime.SpecifyKind(content.OccurredAtUtc, DateTimeKind.Unspecified);
        if (change == "period-end") { content.Kind = EducationFeedbackKind.Period; content.PeriodEndsAtUtc = content.OccurredAtUtc; }
        if (change == "missing-task") content.Kind = EducationFeedbackKind.Assessment;
        if (change == "duplicate-task") { var id = Guid.NewGuid(); content.AssessmentTaskIds = [id, id]; }
        if (change == "too-many-tasks") content.AssessmentTaskIds = Enumerable.Range(0, 11).Select(_ => Guid.NewGuid()).ToList();
        if (change == "empty-observation") content.SelectedObservationId = Guid.Empty;
        Should.Throw<BusinessException>(() => content.ToCanonicalJson()).Code.ShouldBe(EducationFeedbackErrorCodes.Invalid);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"kind\":0,\"kind\":1}")]
    [InlineData("{\"unknown\":true}")]
    public void Malformed_Stored_Feedback_Should_Not_Be_Interpreted_As_Public_Text(string json) =>
        Should.Throw<BusinessException>(() => EducationFeedbackContent.Parse(json)).Code.ShouldBe(EducationFeedbackErrorCodes.Invalid);
}
