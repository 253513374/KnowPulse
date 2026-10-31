using System;
using System.Collections.Generic;
using System.Linq;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.Education;

public class EducationLearningTrendTests
{
    private static (EducationLearningReportSource Source, List<EducationLearningTrendBasis> Bases) Data()
    {
        var now = DateTime.UtcNow; var teacher = Guid.NewGuid(); var tasks = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var scope = new EducationCurriculumSnapshot { TeacherUserId = teacher, SchoolId = Guid.NewGuid(), TeachingAssignmentId = Guid.NewGuid(), EducationSubjectId = Guid.NewGuid(), TextbookVersionId = Guid.NewGuid(),
            Mappings = [new() { KnowledgePointId = Guid.NewGuid(), KnowledgePointRevisionId = Guid.NewGuid() }] };
        var source = new EducationLearningReportSource { OwnerUserId = teacher, AcademicTermId = Guid.NewGuid(), SchoolClassId = Guid.NewGuid(), Curriculum = scope,
            WindowStartsAtUtc = now.AddDays(-4), WindowEndsAtUtc = now.AddMinutes(-1), CapturedAtUtc = now, Fingerprint = new string('a', 64),
            Assessments = tasks.Select((id, i) => new EducationLearningReportAssessment { TaskId = id, TaskRevisionId = Guid.NewGuid(), ExamPaperId = Guid.NewGuid(), PaperSnapshotVersion = 1, Title = "测评" + i,
                StartsAtUtc = now.AddDays(-3 + i), EndsAtUtc = now.AddDays(-2 + i), AssignedCount = 2, VisibleAssignedCount = 2 }).ToList(),
            Students = Enumerable.Range(0, 2).Select(i => new EducationLearningReportStudentSource { Student = new() { StudentUserId = Guid.NewGuid(), StudentEnrollmentId = Guid.NewGuid(), SchoolMembershipId = Guid.NewGuid() },
                Assessments = tasks.Select((id, t) => new EducationLearningReportAssessmentResult { TaskId = id, RecipientId = Guid.NewGuid(), State = EducationLearningAssessmentState.Scored, QuestionCount = 1, ConfirmedQuestionCount = 1,
                    TotalScore = i == 0 ? t * 5 : 10, MaxScore = 10, EffectiveAtUtc = now.AddDays(-3 + t).AddHours(1) }).ToList() }).ToList() };
        return (source, tasks.Select(t => new EducationLearningTrendBasis(t, 1, 10, "coverage", "difficulty", "scoring")).ToList());
    }
    [Fact]
    public void Complete_Class_Means_Should_Include_Real_Zero_And_Keep_Student_Scores_Separate()
    {
        var f = Data(); var rows = EducationLearningTrend.Calculate(f.Source, f.Bases);
        rows[0].Score.ShouldBe(5); rows[1].Score.ShouldBe(7.5m); rows[1].Ratio.ShouldBe(.75m); rows[1].ComparisonLimit.ShouldBe(EducationLearningTrendLimit.None);
        var person = EducationLearningTrend.Calculate(f.Source, f.Bases, f.Source.Students[0].Student.StudentUserId); person[0].Score.ShouldBe(0); person[1].Score.ShouldBe(5); person[1].AssignedCount.ShouldBe(1);
    }
    [Fact]
    public void Missing_Grades_Should_Not_Be_Zero_Or_Be_Skipped_When_Comparing_Adjacent_Records()
    {
        var f = Data(); var students = f.Source.Students.Select(s => s with { Assessments = s.Assessments.Select((a, i) => i == 0 ? a : a with { State = EducationLearningAssessmentState.PendingGrading, ConfirmedQuestionCount = 0, TotalScore = null }).ToList() }).ToList();
        var rows = EducationLearningTrend.Calculate(f.Source with { Students = students }, f.Bases); rows[1].Score.ShouldBeNull(); rows[1].Ratio.ShouldBeNull(); rows[1].PendingGradingCount.ShouldBe(2);
        rows[1].ComparisonLimit.HasFlag(EducationLearningTrendLimit.MissingScore).ShouldBeTrue(); rows[1].ComparisonLimit.HasFlag(EducationLearningTrendLimit.Cohort).ShouldBeTrue(); rows[1].ComparedWithTaskId.ShouldBe(rows[0].TaskId);
    }
    [Fact]
    public void Assigned_Or_Scored_Cohort_Changes_Should_Be_Explicit_Even_When_Both_Means_Exist()
    {
        var f = Data(); var second = f.Source.Students[1]; var source = f.Source with { Students = [f.Source.Students[0], second with { Assessments = [second.Assessments[0]] }],
            Assessments = [f.Source.Assessments[0], f.Source.Assessments[1] with { AssignedCount = 1, VisibleAssignedCount = 1 }] };
        var rows = EducationLearningTrend.Calculate(source, f.Bases); rows[1].ScoredCount.ShouldBe(1); rows[1].Score.ShouldBe(5); rows[1].ComparisonLimit.ShouldBe(EducationLearningTrendLimit.Cohort);
    }
    [Fact]
    public void All_Changed_Paper_Conditions_Should_Be_Reported_Without_Equating_Different_Papers()
    {
        var f = Data(); var source = f.Source with { Students = f.Source.Students.Select(s => s with { Assessments = [s.Assessments[0], s.Assessments[1] with { MaxScore = 20 }] }).ToList() };
        f.Bases[1] = f.Bases[1] with { Maximum = 20, Coverage = "different", Difficulty = "harder", Scoring = "revised" };
        var rows = EducationLearningTrend.Calculate(source, f.Bases);
        rows[1].ComparisonLimit.ShouldBe(EducationLearningTrendLimit.Coverage | EducationLearningTrendLimit.Maximum | EducationLearningTrendLimit.Difficulty | EducationLearningTrendLimit.Scoring);
    }
    [Fact]
    public void Missing_Frozen_Metadata_Should_Never_Be_Declared_Comparable_And_Does_Not_Erase_Raw_Marks()
    {
        var f = Data(); f.Bases = f.Bases.Select(x => x with { Scoring = null }).ToList(); var rows = EducationLearningTrend.Calculate(f.Source, f.Bases);
        rows[1].ComparisonLimit.ShouldBe(EducationLearningTrendLimit.MetadataMissing); rows[1].Score.ShouldBe(7.5m);
    }
    [Fact]
    public void Inconsistent_Frozen_Maximum_Or_Student_Outside_The_Source_Should_Be_Rejected()
    {
        var f = Data(); Should.Throw<BusinessException>(() => EducationLearningTrend.Calculate(f.Source, f.Bases, Guid.NewGuid()));
        f.Bases[1] = f.Bases[1] with { Maximum = 20 }; Should.Throw<BusinessException>(() => EducationLearningTrend.Calculate(f.Source, f.Bases));
    }
}
