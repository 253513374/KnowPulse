using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;

namespace KnowPulse.Education;

public static class EducationLearningTrend
{
    public const string ComparisonVersion = "frozen-paper-observed-score-v1";
    private sealed record Entry(EducationLearningTrendRow Row, EducationLearningTrendBasis Basis, string Cohort);
    private static BusinessException Invalid() => new(EducationLearningReportErrors.Invalid);
    public static List<EducationLearningTrendRow> Calculate(EducationLearningReportSource source, IReadOnlyList<EducationLearningTrendBasis> bases, Guid? studentId = null)
    {
        EducationLearningReport.ValidateSource(source);
        if (source.Assessments == null || source.Assessments.Count > 500 || source.Assessments.Select(x => x.TaskId).Distinct().Count() != source.Assessments.Count ||
            bases.Count != source.Assessments.Count || bases.Select(x => x.TaskId).Distinct().Count() != bases.Count ||
            bases.Any(x => x.TaskId == Guid.Empty || x.QuestionCount < 1 || x.Maximum <= 0 || !source.Assessments.Any(a => a.TaskId == x.TaskId)) ||
            studentId.HasValue && !source.Students.Any(x => x.Student.StudentUserId == studentId)) throw Invalid();
        var students = source.Students.Where(x => !studentId.HasValue || x.Student.StudentUserId == studentId).ToList(); var entries = new List<Entry>();
        var byTask = students.SelectMany(s => s.Assessments.Select(x => (StudentId: s.Student.StudentUserId, Value: x))).GroupBy(x => x.Value.TaskId).ToDictionary(x => x.Key, x => x.ToList());
        foreach (var a in source.Assessments)
        {
            var basis = bases.Single(x => x.TaskId == a.TaskId);
            var rows = byTask.GetValueOrDefault(a.TaskId) ?? [];
            if (studentId.HasValue && rows.Count == 0) continue;
            if (rows.Select(x => x.StudentId).Distinct().Count() != rows.Count || rows.Count != (studentId.HasValue ? 1 : a.VisibleAssignedCount) || rows.Count > a.AssignedCount ||
                rows.Any(x => !Enum.IsDefined(x.Value.State) || x.Value.MaxScore != basis.Maximum || x.Value.QuestionCount != basis.QuestionCount ||
                    (x.Value.State == EducationLearningAssessmentState.Scored ? !x.Value.TotalScore.HasValue || x.Value.TotalScore < 0 || x.Value.TotalScore > basis.Maximum || x.Value.ConfirmedQuestionCount != basis.QuestionCount || !x.Value.EffectiveAtUtc.HasValue : x.Value.TotalScore.HasValue))) throw Invalid();
            var scored = rows.Where(x => x.Value.State == EducationLearningAssessmentState.Scored).ToList();
            decimal? score = scored.Count == 0 ? null : scored.Average(x => x.Value.TotalScore!.Value);
            var at = studentId.HasValue && rows[0].Value.EffectiveAtUtc.HasValue ? rows[0].Value.EffectiveAtUtc!.Value : scored.Count == 0 ? a.EndsAtUtc : scored.Max(x => x.Value.EffectiveAtUtc!.Value);
            var row = new EducationLearningTrendRow { TaskId = a.TaskId, Title = a.Title, EffectiveAtUtc = at, EndsAtUtc = a.EndsAtUtc, PaperSnapshotVersion = a.PaperSnapshotVersion, QuestionCount = basis.QuestionCount,
                OriginalAssignedCount = a.AssignedCount, AssignedCount = rows.Count, ScoredCount = scored.Count, Score = score, MaxScore = basis.Maximum, Ratio = score / basis.Maximum,
                PendingGradingCount = rows.Count(x => x.Value.State == EducationLearningAssessmentState.PendingGrading), AbsentCount = rows.Count(x => x.Value.State == EducationLearningAssessmentState.Absent),
                NotStartedCount = rows.Count(x => x.Value.State == EducationLearningAssessmentState.NotStarted), InProgressCount = rows.Count(x => x.Value.State == EducationLearningAssessmentState.InProgress),
                AwaitingSealCount = rows.Count(x => x.Value.State == EducationLearningAssessmentState.AwaitingSeal), OutsideWindowCount = rows.Count(x => x.Value.State == EducationLearningAssessmentState.OutsideWindow) };
            var cohort = string.Join('|', rows.OrderBy(x => x.StudentId).Select(x => x.StudentId)) + "/" + string.Join('|', scored.OrderBy(x => x.StudentId).Select(x => x.StudentId));
            entries.Add(new(row, basis, cohort));
        }
        var ordered = entries.OrderBy(x => x.Row.EffectiveAtUtc).ThenBy(x => source.Assessments.Single(a => a.TaskId == x.Row.TaskId).StartsAtUtc).ThenBy(x => x.Row.TaskId).ToList();
        var result = new List<EducationLearningTrendRow>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var current = ordered[i]; var previous = i == 0 ? null : ordered[i - 1]; var limit = previous == null ? EducationLearningTrendLimit.First : EducationLearningTrendLimit.None;
            if (current.Row.ScoredCount == 0 || current.Row.ScoredCount != current.Row.AssignedCount || previous != null && (previous.Row.ScoredCount == 0 || previous.Row.ScoredCount != previous.Row.AssignedCount)) limit |= EducationLearningTrendLimit.MissingScore;
            if (new[] { current.Basis, previous?.Basis }.Where(x => x != null).Any(x => x!.Coverage == null || x.Difficulty == null || x.Scoring == null)) limit |= EducationLearningTrendLimit.MetadataMissing;
            if (previous != null)
            {
                if (current.Basis.Coverage != null && previous.Basis.Coverage != null && current.Basis.Coverage != previous.Basis.Coverage) limit |= EducationLearningTrendLimit.Coverage;
                if (current.Basis.Maximum != previous.Basis.Maximum) limit |= EducationLearningTrendLimit.Maximum;
                if (current.Basis.Difficulty != null && previous.Basis.Difficulty != null && current.Basis.Difficulty != previous.Basis.Difficulty) limit |= EducationLearningTrendLimit.Difficulty;
                if (current.Basis.Scoring != null && previous.Basis.Scoring != null && current.Basis.Scoring != previous.Basis.Scoring) limit |= EducationLearningTrendLimit.Scoring;
                if (current.Cohort != previous.Cohort) limit |= EducationLearningTrendLimit.Cohort;
            }
            result.Add(current.Row with { ComparedWithTaskId = previous?.Row.TaskId, ComparisonLimit = limit });
        }
        return result;
    }
}
