using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Volo.Abp;

namespace KnowPulse.Education;

/// <summary>Explains one student's evidence in one teaching, subject, textbook and UTC window.</summary>
public static class EducationLearningAnalysis
{
    public const string AlgorithmVersion = "education-learning-evidence-v1";
    public const string ExactContextAlgorithmVersion = "education-learning-evidence-v2-exact-context";

    public static EducationLearningAnalysisResult Calculate(EducationLearningAnalysisScope scope,
        IReadOnlyCollection<EducationLearningQuestionEvidence> evidence,
        IReadOnlyCollection<EducationLearningQuestionAttempt> attempts,
        IReadOnlyCollection<EducationLearningQuestionRelationship> relationships,
        IReadOnlyCollection<EducationLearningQuestionExposure> exposures, DateTime calculatedAtUtc,
        EducationMasteryCalculationRule? rule = null)
    {
        ValidateScope(scope, calculatedAtUtc); ValidateRule(scope, rule);
        if (rule != null) rule = rule with { TargetRatio = Number(rule.TargetRatio) };
        if (evidence == null || attempts == null || relationships == null || exposures == null || evidence.Any(x => x == null || x.Points == null) || attempts.Any(x => x == null)) throw Invalid();
        var rows = Unique(evidence.Select(x => x with { MaxScore = Number(x.MaxScore), ObtainedScore = Number(x.ObtainedScore),
            Points = x.Points.Select(p => p == null ? null! : p with { MaxScore = Number(p.MaxScore), ObtainedScore = Number(p.ObtainedScore) })
                .OrderBy(p => p?.MappingId).ToArray() }), x => x.GradeRevisionId);
        var links = Unique(relationships, x => x.Id); var seen = Unique(exposures, x => x.Id);
        var history = attempts.GroupBy(x => (x.AttemptId, x.PaperQuestionId)).Select(g =>
        { if (g.Select(x => JsonSerializer.Serialize(x)).Distinct(StringComparer.Ordinal).Count() != 1) throw Invalid(); return g.First(); }).ToList();
        var historyByQuestion = history.ToDictionary(x => (x.AttemptId, x.PaperQuestionId));
        foreach (var row in rows) ValidateEvidence(scope, row, calculatedAtUtc);
        if (rows.GroupBy(x => (x.SubmissionId, x.PaperQuestionId)).Any(g => g.Count() != 1) ||
            history.Any(x => x.TenantId != scope.TenantId || x.SchoolId != scope.SchoolId || x.EducationSubjectId != scope.EducationSubjectId || x.StudentUserId != scope.StudentUserId ||
                x.TaskId == Guid.Empty || x.AttemptId == Guid.Empty || x.PaperQuestionId == Guid.Empty || x.OriginalQuestionId == Guid.Empty || x.DisplayOrder < 1 ||
                x.StartedAtUtc.Kind != DateTimeKind.Utc || x.StartedAtUtc > calculatedAtUtc) ||
            rows.Any(row => !historyByQuestion.TryGetValue((row.AttemptId, row.PaperQuestionId), out var prior) || prior.TaskId != row.TaskId ||
                prior.OriginalQuestionId != row.OriginalQuestionId || prior.DisplayOrder != row.DisplayOrder || prior.StartedAtUtc != row.AttemptStartedAtUtc) ||
            links.Any(x => x.FirstQuestionId == Guid.Empty || x.SecondQuestionId == Guid.Empty || x.FirstQuestionId == x.SecondQuestionId) ||
            seen.Any(x => x.StudentUserId != scope.StudentUserId || x.OriginalQuestionId == Guid.Empty || x.ExposedAtUtc.Kind != DateTimeKind.Utc || x.ExposedAtUtc > calculatedAtUtc ||
                !Enum.IsDefined(x.Use) || x.PaperQuestionId == Guid.Empty || x.HintReviewRevisionId == Guid.Empty || x.Use == EducationLearningExposureUse.ConfirmedNoHints && !x.HintReviewRevisionId.HasValue)) throw Invalid();

        var parents = new Dictionary<Guid, Guid>();
        Guid Group(Guid question)
        {
            if (!parents.ContainsKey(question)) parents.Add(question, question);
            var root = question;
            while (parents[root] != root) root = parents[root];
            while (parents[question] != question) { var next = parents[question]; parents[question] = root; question = next; }
            return root;
        }
        foreach (var link in links.OrderBy(x => x.Id))
        {
            var first = Group(link.FirstQuestionId); var second = Group(link.SecondQuestionId);
            if (first == second) continue;
            if (first.CompareTo(second) < 0) parents[second] = first; else parents[first] = second;
        }
        var ordered = rows.OrderBy(x => x.AttemptStartedAtUtc).ThenBy(x => x.EffectiveAtUtc).ThenBy(x => x.TaskId)
            .ThenBy(x => x.DisplayOrder).ThenBy(x => x.PaperQuestionId).ToList();
        // Ungraded and abandoned attempts still establish a known previous encounter with the same question.
        var firstByGroup = history.OrderBy(x => x.StartedAtUtc).ThenBy(x => x.TaskId).ThenBy(x => x.DisplayOrder).ThenBy(x => x.PaperQuestionId)
            .GroupBy(x => Group(x.OriginalQuestionId)).ToDictionary(g => g.Key, g => (g.First().AttemptId, g.First().PaperQuestionId));
        var exposureByGroup = seen.Where(x => x.Use == EducationLearningExposureUse.Confirmed).GroupBy(x => Group(x.OriginalQuestionId))
            .ToDictionary(g => g.Key, g => g.Min(x => x.ExposedAtUtc));
        var unverifiedExposureByGroup = seen.Where(x => x.PaperQuestionId == null && x.Use == EducationLearningExposureUse.Unverified).GroupBy(x => Group(x.OriginalQuestionId))
            .ToDictionary(g => g.Key, g => g.Min(x => x.ExposedAtUtc));
        var unverifiedByPaperQuestion = seen.Where(x => x.PaperQuestionId.HasValue && x.Use == EducationLearningExposureUse.Unverified).GroupBy(x => x.PaperQuestionId!.Value)
            .ToDictionary(g => g.Key, g => g.Min(x => x.ExposedAtUtc));
        var decisions = ordered.Select(row =>
        {
            var group = Group(row.OriginalQuestionId);
            var exposed = exposureByGroup.TryGetValue(group, out var exposedAt) && exposedAt <= row.EffectiveAtUtc;
            var unverified = unverifiedExposureByGroup.TryGetValue(group, out var unverifiedAt) && unverifiedAt <= row.EffectiveAtUtc ||
                unverifiedByPaperQuestion.TryGetValue(row.PaperQuestionId, out var exactUnverifiedAt) && exactUnverifiedAt <= row.EffectiveAtUtc;
            var exclusion = row.Use == EducationLearningEvidenceUse.Invalid || row.InvalidationRevisionId.HasValue ? EducationLearningExclusionReason.Invalid :
                !InWindow(scope, row) ? EducationLearningExclusionReason.OutsideWindow :
                firstByGroup[group] != (row.AttemptId, row.PaperQuestionId) ? EducationLearningExclusionReason.EarlierAttempt :
                exposed ? EducationLearningExclusionReason.EarlierExposure :
                unverified ? EducationLearningExclusionReason.UnverifiedExposure :
                row.Use == EducationLearningEvidenceUse.Unverified ? EducationLearningExclusionReason.Unverified :
                row.Use == EducationLearningEvidenceUse.Practice ? EducationLearningExclusionReason.TeacherPractice : EducationLearningExclusionReason.None;
            return new EducationLearningEvidenceDecision(row.GradeRevisionId, row.TaskId, row.SubmissionId, row.PaperQuestionId, group, exclusion);
        }).ToList();
        var decisionsById = decisions.ToDictionary(x => x.GradeRevisionId);
        var points = new List<EducationLearningPointEstimate>();
        foreach (var point in scope.Points.OrderBy(x => x.KnowledgePointId).ThenBy(x => x.KnowledgePointRevisionId))
        {
            var related = rows.Where(row => InWindow(scope, row) && row.Points.Any(p => Matches(p, point))).ToList();
            var valid = related.Where(row => decisionsById[row.GradeRevisionId].ExclusionReason == EducationLearningExclusionReason.None).ToList();
            var marks = valid.SelectMany(row => row.Points.Where(p => Matches(p, point))).ToList();
            var maximum = marks.Sum(x => x.MaxScore); var obtained = marks.Sum(x => x.ObtainedScore);
            var count = valid.Select(x => Group(x.OriginalQuestionId)).Distinct().Count(); var assessments = valid.Select(x => x.TaskId).Distinct().Count();
            var latest = valid.Count == 0 ? (DateTime?)null : valid.Max(x => x.EffectiveAtUtc);
            var missingQuestions = rule == null ? 0 : Math.Max(0, rule.MinimumIndependentQuestionCount - count);
            var missingAssessments = rule == null ? 0 : Math.Max(0, rule.MinimumAssessmentCount - assessments);
            var missingRecent = rule != null && (!latest.HasValue || scope.WindowEndsAtUtc - latest.Value > TimeSpan.FromDays(rule.MaximumLatestEvidenceAgeDays));
            var state = maximum == 0 ? EducationMasteryState.Unassessed : rule == null || missingQuestions > 0 || missingAssessments > 0 || missingRecent ?
                EducationMasteryState.InsufficientEvidence : obtained < rule.TargetRatio * maximum ? EducationMasteryState.NeedsConsolidation : EducationMasteryState.CurrentTargetMet;
            points.Add(new() { Point = point, State = state, ObtainedScore = obtained, MaxScore = maximum, Ratio = maximum == 0 ? null : obtained / maximum,
                IndependentQuestionCount = count, AssessmentCount = assessments, LatestEvidenceAtUtc = latest, RuleMissing = rule == null,
                MissingIndependentQuestionCount = missingQuestions, MissingAssessmentCount = missingAssessments, RecentEvidenceMissing = missingRecent,
                UnverifiedQuestionCount = related.Count(row => decisionsById[row.GradeRevisionId].ExclusionReason is EducationLearningExclusionReason.Unverified or EducationLearningExclusionReason.UnverifiedExposure),
                InvalidQuestionCount = related.Count(row => decisionsById[row.GradeRevisionId].ExclusionReason == EducationLearningExclusionReason.Invalid),
                PracticeQuestionCount = related.Count(row => decisionsById[row.GradeRevisionId].ExclusionReason is EducationLearningExclusionReason.TeacherPractice or
                    EducationLearningExclusionReason.EarlierAttempt or EducationLearningExclusionReason.EarlierExposure) });
        }
        var algorithm = seen.Any(x => x.PaperQuestionId.HasValue) ? ExactContextAlgorithmVersion : AlgorithmVersion;
        var canonical = JsonSerializer.Serialize(new { AlgorithmVersion = algorithm, Scope = scope with { Points = scope.Points.OrderBy(x => x.KnowledgePointId).ThenBy(x => x.KnowledgePointRevisionId).ToArray() },
            Rule = rule, Evidence = rows.OrderBy(x => x.GradeRevisionId), History = history.OrderBy(x => x.AttemptId).ThenBy(x => x.PaperQuestionId),
            Relationships = links.OrderBy(x => x.Id), Exposures = seen.OrderBy(x => x.Id) });
        return new() { CalculationVersion = algorithm, SourceFingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))),
            CalculatedAtUtc = calculatedAtUtc, WindowStartsAtUtc = scope.WindowStartsAtUtc, WindowEndsAtUtc = scope.WindowEndsAtUtc,
            RuleRevisionId = rule?.Id, RuleRevisionNumber = rule?.RevisionNumber,
            TargetPointCount = points.Count, AssessedPointCount = points.Count(x => x.MaxScore > 0), Points = points.AsReadOnly(), Evidence = decisions.AsReadOnly() };
    }

    private static bool Matches(EducationLearningPointScore score, EducationLearningPoint point) => score.KnowledgePointId == point.KnowledgePointId && score.KnowledgePointRevisionId == point.KnowledgePointRevisionId;
    private static bool InWindow(EducationLearningAnalysisScope scope, EducationLearningQuestionEvidence row) => scope.WindowStartsAtUtc <= row.EffectiveAtUtc && row.EffectiveAtUtc < scope.WindowEndsAtUtc;
    private static bool Score(decimal value) => value >= 0 && value <= 1000 && decimal.Round(value, 2) == value;
    private static decimal Number(decimal value) => decimal.Parse(value.ToString("G29", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
    private static BusinessException Invalid() => new(EducationLearningErrorCodes.InvalidEvidence);
    private static List<T> Unique<T>(IEnumerable<T> values, Func<T, Guid> id) where T : class
    {
        var result = new List<T>();
        if (values.Any(x => x == null)) throw Invalid();
        foreach (var group in values.GroupBy(id))
        {
            if (group.Key == Guid.Empty || group.Select(x => JsonSerializer.Serialize(x)).Distinct(StringComparer.Ordinal).Count() != 1) throw Invalid();
            result.Add(group.First());
        }
        return result;
    }
    private static void ValidateScope(EducationLearningAnalysisScope scope, DateTime now)
    {
        if (scope == null || scope.TenantId == Guid.Empty || scope.SchoolId == Guid.Empty || scope.TeachingAssignmentId == Guid.Empty || scope.StudentUserId == Guid.Empty ||
            scope.EducationSubjectId == Guid.Empty || scope.TextbookVersionId == Guid.Empty || now.Kind != DateTimeKind.Utc ||
            scope.WindowStartsAtUtc.Kind != DateTimeKind.Utc || scope.WindowEndsAtUtc.Kind != DateTimeKind.Utc ||
            scope.WindowStartsAtUtc >= scope.WindowEndsAtUtc || scope.WindowEndsAtUtc > now ||
            scope.Points == null || scope.Points.Count is < 1 or > 3000 || scope.Points.Any(x => x == null || x.KnowledgePointId == Guid.Empty ||
                x.KnowledgePointRevisionId == Guid.Empty || string.IsNullOrWhiteSpace(x.Code) || string.IsNullOrWhiteSpace(x.Name)) ||
            scope.Points.Select(x => (x.KnowledgePointId, x.KnowledgePointRevisionId)).Distinct().Count() != scope.Points.Count)
            throw new BusinessException(EducationLearningErrorCodes.InvalidScope);
    }
    private static void ValidateRule(EducationLearningAnalysisScope scope, EducationMasteryCalculationRule? rule)
    {
        if (rule == null) return;
        if (rule.Id == Guid.Empty || rule.TenantId != scope.TenantId || rule.SchoolId != scope.SchoolId || rule.EducationSubjectId != scope.EducationSubjectId ||
            rule.RevisionNumber < 1 || rule.WindowDays is < 1 or > 730 || rule.MinimumIndependentQuestionCount is < 1 or > 1000 ||
            rule.MinimumAssessmentCount is < 1 or > 1000 || rule.MaximumLatestEvidenceAgeDays < 1 || rule.MaximumLatestEvidenceAgeDays > rule.WindowDays ||
            rule.TargetRatio is <= 0 or > 1 || decimal.Round(rule.TargetRatio, 4) != rule.TargetRatio ||
            scope.WindowEndsAtUtc - scope.WindowStartsAtUtc > TimeSpan.FromDays(rule.WindowDays)) throw new BusinessException(EducationLearningErrorCodes.InvalidRule);
    }
    private static void ValidateEvidence(EducationLearningAnalysisScope scope, EducationLearningQuestionEvidence row, DateTime now)
    {
        if (row.TenantId != scope.TenantId || row.SchoolId != scope.SchoolId || row.TeachingAssignmentId != scope.TeachingAssignmentId || row.StudentUserId != scope.StudentUserId ||
            row.EducationSubjectId != scope.EducationSubjectId || row.TextbookVersionId != scope.TextbookVersionId || row.TaskId == Guid.Empty || row.SubmissionId == Guid.Empty ||
            row.QuestionGradeId == Guid.Empty || row.AttemptId == Guid.Empty || row.PaperQuestionId == Guid.Empty || row.OriginalQuestionId == Guid.Empty || row.QuestionDefinitionRevisionId == Guid.Empty || row.DisplayOrder < 1 ||
            !Enum.IsDefined(row.Use) || row.QualificationRevisionId == Guid.Empty || row.InvalidationRevisionId == Guid.Empty ||
            row.Use is EducationLearningEvidenceUse.Independent or EducationLearningEvidenceUse.Practice && !row.QualificationRevisionId.HasValue ||
            row.Use == EducationLearningEvidenceUse.Invalid && !row.QualificationRevisionId.HasValue && !row.InvalidationRevisionId.HasValue ||
            row.Use == EducationLearningEvidenceUse.Independent && (!row.NoHintsOrAnswersConfirmed || !row.NewRequirementsConfirmed || !row.OriginalRelationshipsChecked) ||
            row.AttemptStartedAtUtc.Kind != DateTimeKind.Utc || row.EffectiveAtUtc.Kind != DateTimeKind.Utc || row.GradeConfirmedAtUtc.Kind != DateTimeKind.Utc ||
            row.AttemptStartedAtUtc > row.EffectiveAtUtc || row.GradeConfirmedAtUtc < row.EffectiveAtUtc || row.GradeConfirmedAtUtc > now ||
            !Score(row.MaxScore) || row.MaxScore == 0 || !Score(row.ObtainedScore) || row.ObtainedScore > row.MaxScore || row.Points.Count is < 1 or > 3000 ||
            row.Points.Any(x => x == null || x.MappingId == Guid.Empty || x.KnowledgePointId == Guid.Empty || x.KnowledgePointRevisionId == Guid.Empty ||
                !Score(x.MaxScore) || x.MaxScore == 0 || !Score(x.ObtainedScore) || x.ObtainedScore > x.MaxScore) || row.Points.Select(x => x.MappingId).Distinct().Count() != row.Points.Count ||
            row.Points.Sum(x => x.MaxScore) != row.MaxScore || row.Points.Sum(x => x.ObtainedScore) != row.ObtainedScore) throw Invalid();
    }
}
