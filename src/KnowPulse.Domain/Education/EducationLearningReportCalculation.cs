using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;

namespace KnowPulse.Education;

public static class EducationLearningReportCalculation
{
    public static EducationLearningReportStudentResult Student(EducationLearningReportSource source, EducationLearningReportStudentSource student, DateTime now)
    {
        EducationLearningReport.ValidateSource(source);
        if (!source.Students.Contains(student)) throw new BusinessException(EducationLearningReportErrors.Invalid);
        return new() { Student = student.Student, Assessments = student.Assessments,
            Analysis = EducationLearningAnalysis.Calculate(source.StudentScope(student.Student.StudentUserId), student.Evidence, student.Attempts, source.Relationships, student.Exposures, now, source.Rule) };
    }
    public static EducationLearningReportSummary Summary(EducationLearningReportSource source, IReadOnlyCollection<EducationLearningReportStudentResult> students, DateTime now)
    {
        EducationLearningReport.ValidateSource(source);
        if (students.Count != source.Students.Count || students.Select(x => x.Student.StudentUserId).Distinct().Count() != students.Count ||
            students.Any(x => !source.Students.Any(s => s.Student == x.Student) || x.Analysis.CalculatedAtUtc != now)) throw new BusinessException(EducationLearningReportErrors.Invalid);
        var points = source.Curriculum.Mappings.DistinctBy(x => (x.KnowledgePointId, x.KnowledgePointRevisionId)).Select(point =>
        {
            var rows = students.Select(x => x.Analysis.Points.Single(p => p.Point.KnowledgePointId == point.KnowledgePointId && p.Point.KnowledgePointRevisionId == point.KnowledgePointRevisionId)).ToList();
            var maximum = rows.Sum(x => x.MaxScore); var obtained = rows.Sum(x => x.ObtainedScore);
            return new EducationLearningReportPointSummary { Point = new(point.KnowledgePointId, point.KnowledgePointRevisionId, point.KnowledgePointCode, point.KnowledgePointName),
                MaxScore = maximum, ObtainedScore = obtained, Ratio = maximum == 0 ? null : obtained / maximum,
                UnassessedCount = rows.Count(x => x.State == EducationMasteryState.Unassessed), InsufficientEvidenceCount = rows.Count(x => x.State == EducationMasteryState.InsufficientEvidence),
                NeedsConsolidationCount = rows.Count(x => x.State == EducationMasteryState.NeedsConsolidation), CurrentTargetMetCount = rows.Count(x => x.State == EducationMasteryState.CurrentTargetMet) };
        }).OrderBy(x => x.Point.Code).ThenBy(x => x.Point.KnowledgePointRevisionId).ToList();
        return new() { CalculationVersion = EducationLearningAnalysis.AlgorithmVersion, SourceFingerprint = source.Fingerprint, CalculatedAtUtc = now,
            WindowStartsAtUtc = source.WindowStartsAtUtc, WindowEndsAtUtc = source.WindowEndsAtUtc, SchoolClassName = source.SchoolClassName,
            SubjectName = source.Curriculum.SubjectName, TextbookTitle = source.Curriculum.TextbookTitle, RuleMissing = source.Rule == null, RuleRevisionId = source.Rule?.Id, RuleRevisionNumber = source.Rule?.RevisionNumber,
            RuleDecisionRevisionId = source.RuleDecisionRevisionId, RuleDecisionRevisionNumber = source.RuleDecisionRevisionNumber,
            Assessments = source.Assessments, Points = points, Students = students.Select(s => new EducationLearningReportStudentSummary
            { Student = s.Student, AssignedCount = s.Assessments.Count, ScoredCount = s.Assessments.Count(x => x.State == EducationLearningAssessmentState.Scored),
                PendingGradingCount = s.Assessments.Count(x => x.State == EducationLearningAssessmentState.PendingGrading), AbsentCount = s.Assessments.Count(x => x.State == EducationLearningAssessmentState.Absent),
                NotStartedCount = s.Assessments.Count(x => x.State == EducationLearningAssessmentState.NotStarted), InProgressCount = s.Assessments.Count(x => x.State == EducationLearningAssessmentState.InProgress),
                AwaitingSealCount = s.Assessments.Count(x => x.State == EducationLearningAssessmentState.AwaitingSeal), OutsideWindowCount = s.Assessments.Count(x => x.State == EducationLearningAssessmentState.OutsideWindow),
                AssessedPointCount = s.Analysis.AssessedPointCount, TargetPointCount = s.Analysis.TargetPointCount,
                NeedsConsolidationCount = s.Analysis.Points.Count(x => x.State == EducationMasteryState.NeedsConsolidation), InsufficientEvidenceCount = s.Analysis.Points.Count(x => x.State == EducationMasteryState.InsufficientEvidence) })
                .OrderByDescending(x => x.NeedsConsolidationCount).ThenByDescending(x => x.InsufficientEvidenceCount).ThenBy(x => x.Student.StudentUserId).ToList() };
    }
}
