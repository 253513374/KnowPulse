using System;
using KnowPulse.Exam;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.Education;

public class EducationStudentGrowthSnapshotTests
{
    private static readonly DateTime Now = new(2026, 10, 17, 10, 0, 0, DateTimeKind.Utc);
    private readonly Guid _student = Guid.NewGuid(), _enrollment = Guid.NewGuid(), _member = Guid.NewGuid(), _term = Guid.NewGuid(), _class = Guid.NewGuid();
    private readonly EducationCurriculumSnapshot _scope = new() { SchoolId = Guid.NewGuid(), TeachingAssignmentId = Guid.NewGuid(), TeacherUserId = Guid.NewGuid(), EducationSubjectId = Guid.NewGuid(), TextbookVersionId = Guid.NewGuid() };
    private EducationStudentGrowthValue Value()
    {
        var context = EducationStudentGrowthSnapshot.ContextFor(null, _student, _enrollment, _scope.TeachingAssignmentId, _scope.TextbookVersionId);
        return new() { Context = new() { Id = context, MaximumWindowDays = 730 }, WindowStartsAtUtc = Now.AddDays(-1), WindowEndsAtUtc = Now, CalculatedAtUtc = Now,
            CalculationVersion = EducationLearningAnalysis.AlgorithmVersion, RuleMissing = true,
            Points = [new() { Id = EducationStudentGrowthSnapshot.PointFor(context, Guid.NewGuid()), RevisionNumber = 1, Code = "A", Name = "知识点", State = EducationMasteryState.InsufficientEvidence, ObtainedScore = 8, MaxScore = 10, Ratio = .8m, IndependentQuestionCount = 1, AssessmentCount = 1 }],
            Assessments = [new() { TaskId = Guid.NewGuid(), RecipientId = Guid.NewGuid(), ResultReleaseId = Guid.NewGuid(), EffectiveAtUtc = Now.AddHours(-1), Visibility = ExamResultVisibility.ScoreOnly, TotalScore = 8, MaxScore = 10 }] };
    }
    private EducationStudentGrowthSnapshot Create(EducationStudentGrowthValue value) => new(null, _student, _enrollment, _member, _term, _class, _scope, new string('a', 64), value);

    [Fact]
    public void Growth_Snapshot_Should_Preserve_One_Version_And_Distinguish_Real_Zero_From_No_Evidence()
    {
        var value = Value(); var first = Create(value); var replay = Create(value with { CalculatedAtUtc = Now.AddMinutes(1) });
        first.Id.ShouldBe(replay.Id); first.Result().SnapshotId.ShouldBe(first.Id); first.Result().Points[0].Ratio.ShouldBe(.8m);
        Create(value with { Points = [value.Points[0] with { ObtainedScore = 0, Ratio = 0 }] }).Result().Points[0].Ratio.ShouldBe(0);
        Create(value with { Points = [value.Points[0] with { State = EducationMasteryState.Unassessed, ObtainedScore = 0, MaxScore = 0, Ratio = null, IndependentQuestionCount = 0, AssessmentCount = 0 }] }).Result().Points[0].Ratio.ShouldBeNull();
        EducationStudentGrowthSnapshot.ContextFor(null, Guid.NewGuid(), _enrollment, _scope.TeachingAssignmentId, _scope.TextbookVersionId).ShouldNotBe(value.Context.Id);
    }
    [Theory]
    [InlineData("hidden")]
    [InlineData("absence-score")]
    [InlineData("fake-zero")]
    [InlineData("ratio")]
    [InlineData("future")]
    [InlineData("point-duplicate")]
    [InlineData("context")]
    [InlineData("assessment-duplicate")]
    [InlineData("counts")]
    public void Growth_Snapshot_Should_Reject_Unsafe_Or_Inconsistent_Public_Values(string invalid)
    {
        var value = Value();
        value = invalid switch {
            "hidden" => value with { Assessments = [value.Assessments[0] with { Visibility = ExamResultVisibility.Hidden }] },
            "absence-score" => value with { Assessments = [value.Assessments[0] with { IsAbsent = true }] },
            "fake-zero" => value with { Points = [value.Points[0] with { State = EducationMasteryState.Unassessed, ObtainedScore = 0, MaxScore = 0, Ratio = 0 }] },
            "ratio" => value with { Points = [value.Points[0] with { Ratio = .9m }] },
            "future" => value with { WindowEndsAtUtc = Now.AddDays(1) },
            "point-duplicate" => value with { Points = [value.Points[0], value.Points[0]] },
            "context" => value with { Context = value.Context with { Id = Guid.NewGuid() } },
            "counts" => value with { Points = [value.Points[0] with { IndependentQuestionCount = 0 }] },
            _ => value with { Assessments = [value.Assessments[0], value.Assessments[0]] }
        };
        Should.Throw<BusinessException>(() => Create(value)).Code.ShouldBe(EducationLearningReportErrors.Invalid);
    }
}
