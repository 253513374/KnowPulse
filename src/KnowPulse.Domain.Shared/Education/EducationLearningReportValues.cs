using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Volo.Abp;

namespace KnowPulse.Education;

public enum EducationLearningReportJobStatus { Queued = 0, Running = 1, Completed = 2, Failed = 3, Superseded = 4, SourceChanged = 5 }
public enum EducationLearningAssessmentState { NotStarted = 0, InProgress = 1, AwaitingSeal = 2, PendingGrading = 3, Scored = 4, Absent = 5, OutsideWindow = 6 }
public enum EducationLearningReportFailureStage { None = 0, Calculate = 1, Source = 2, Persistence = 3 }

public static class EducationLearningReportErrors
{
    public const string Invalid = "KnowPulse:LearningReport:Invalid";
    public const string Capacity = "KnowPulse:LearningReport:Capacity";
    public const string SourceChanged = "KnowPulse:LearningReport:SourceChanged";
    public const string OperationConflict = "KnowPulse:LearningReport:OperationConflict";
}

public static class EducationLearningReportJson
{
    public const int MaxSourceBytes = 32 * 1024 * 1024;
    public const int MaxStudentBytes = 8 * 1024 * 1024;
    public const int MaxSnapshotBytes = 128 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { MaxDepth = 32, AllowDuplicateProperties = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { EducationTeachingPlanJson.PreserveEmptyMaterialCompatibility } } };
    public static string Write<T>(T value, int limit)
    {
        var json = JsonSerializer.Serialize(value, Json);
        if (Encoding.UTF8.GetByteCount(json) > limit) throw new BusinessException(EducationLearningReportErrors.Capacity);
        return json;
    }
    public static T Read<T>(string json, int limit)
    {
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > limit) throw new BusinessException(EducationLearningReportErrors.Invalid);
        try { return JsonSerializer.Deserialize<T>(json, Json) ?? throw new BusinessException(EducationLearningReportErrors.Invalid); }
        catch (JsonException) { throw new BusinessException(EducationLearningReportErrors.Invalid); }
    }
}

public sealed record EducationLearningReportStudent
{
    public Guid StudentUserId { get; init; }
    public Guid StudentEnrollmentId { get; init; }
    public Guid SchoolMembershipId { get; init; }
    public string StudentName { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
}

public sealed record EducationLearningReportAssessment
{
    public Guid TaskId { get; init; }
    public Guid TaskRevisionId { get; init; }
    public Guid ExamPaperId { get; init; }
    public int PaperSnapshotVersion { get; init; }
    public string Title { get; init; } = string.Empty;
    public DateTime StartsAtUtc { get; init; }
    public DateTime EndsAtUtc { get; init; }
    public int AssignedCount { get; init; }
    public int VisibleAssignedCount { get; init; }
}

public sealed record EducationLearningReportAssessmentResult
{
    public Guid TaskId { get; init; }
    public Guid RecipientId { get; init; }
    public Guid? AttemptId { get; init; }
    public Guid? SubmissionId { get; init; }
    public EducationLearningAssessmentState State { get; init; }
    public int QuestionCount { get; init; }
    public int ConfirmedQuestionCount { get; init; }
    public decimal? TotalScore { get; init; }
    public decimal MaxScore { get; init; }
    public DateTime? EffectiveAtUtc { get; init; }
    public Guid? ResultReleaseId { get; init; }
}

/// <summary>Only authorized scoring facts and minimal identity/timing; no raw answers, rubrics or private feedback.</summary>
public sealed record EducationLearningReportStudentSource
{
    public EducationLearningReportStudent Student { get; init; } = new();
    public List<EducationLearningQuestionEvidence> Evidence { get; init; } = [];
    public List<EducationLearningQuestionAttempt> Attempts { get; init; } = [];
    public List<EducationLearningQuestionExposure> Exposures { get; init; } = [];
    public List<EducationLearningReportAssessmentResult> Assessments { get; init; } = [];
}

public sealed record EducationLearningReportSource
{
    public int SchemaVersion { get; init; } = 1;
    public Guid? TenantId { get; init; }
    public Guid OwnerUserId { get; init; }
    public Guid AcademicTermId { get; init; }
    public Guid SchoolClassId { get; init; }
    public string SchoolClassName { get; init; } = string.Empty;
    public EducationCurriculumSnapshot Curriculum { get; init; } = new();
    public DateTime WindowStartsAtUtc { get; init; }
    public DateTime WindowEndsAtUtc { get; init; }
    public DateTime CapturedAtUtc { get; init; }
    public string Fingerprint { get; init; } = string.Empty;
    public bool IncludesScoringData { get; init; } = true;
    public EducationMasteryCalculationRule? Rule { get; init; }
    public Guid? RuleDecisionRevisionId { get; init; }
    public int? RuleDecisionRevisionNumber { get; init; }
    public List<EducationLearningReportAssessment> Assessments { get; init; } = [];
    public List<EducationLearningReportStudentSource> Students { get; init; } = [];
    public List<EducationLearningQuestionRelationship> Relationships { get; init; } = [];

    public EducationLearningAnalysisScope StudentScope(Guid studentId) => new()
    {
        TenantId = TenantId, SchoolId = Curriculum.SchoolId, TeachingAssignmentId = Curriculum.TeachingAssignmentId,
        StudentUserId = studentId, EducationSubjectId = Curriculum.EducationSubjectId, TextbookVersionId = Curriculum.TextbookVersionId,
        WindowStartsAtUtc = WindowStartsAtUtc, WindowEndsAtUtc = WindowEndsAtUtc,
        Points = Curriculum.Mappings.DistinctBy(x => (x.KnowledgePointId, x.KnowledgePointRevisionId)).OrderBy(x => x.KnowledgePointId).ThenBy(x => x.KnowledgePointRevisionId)
            .Select(x => new EducationLearningPoint(x.KnowledgePointId, x.KnowledgePointRevisionId, x.KnowledgePointCode, x.KnowledgePointName)).ToList()
    };
}

public sealed record EducationLearningReportStudentResult
{
    public EducationLearningReportStudent Student { get; init; } = new();
    public EducationLearningAnalysisResult Analysis { get; init; } = new();
    public List<EducationLearningReportAssessmentResult> Assessments { get; init; } = [];
}

public sealed record EducationLearningReportStudentSummary
{
    public EducationLearningReportStudent Student { get; init; } = new();
    public int AssignedCount { get; init; }
    public int ScoredCount { get; init; }
    public int PendingGradingCount { get; init; }
    public int AbsentCount { get; init; }
    public int NotStartedCount { get; init; }
    public int InProgressCount { get; init; }
    public int AwaitingSealCount { get; init; }
    public int OutsideWindowCount { get; init; }
    public int AssessedPointCount { get; init; }
    public int TargetPointCount { get; init; }
    public int NeedsConsolidationCount { get; init; }
    public int InsufficientEvidenceCount { get; init; }
}

public sealed record EducationLearningReportPointSummary
{
    public EducationLearningPoint Point { get; init; } = new(Guid.Empty, Guid.Empty, "", "");
    public decimal ObtainedScore { get; init; }
    public decimal MaxScore { get; init; }
    public decimal? Ratio { get; init; }
    public int UnassessedCount { get; init; }
    public int InsufficientEvidenceCount { get; init; }
    public int NeedsConsolidationCount { get; init; }
    public int CurrentTargetMetCount { get; init; }
}

public sealed record EducationLearningReportSummary
{
    public string CalculationVersion { get; init; } = string.Empty;
    public string SourceFingerprint { get; init; } = string.Empty;
    public DateTime CalculatedAtUtc { get; init; }
    public DateTime WindowStartsAtUtc { get; init; }
    public DateTime WindowEndsAtUtc { get; init; }
    public string SchoolClassName { get; init; } = string.Empty;
    public string SubjectName { get; init; } = string.Empty;
    public string TextbookTitle { get; init; } = string.Empty;
    public bool RuleMissing { get; init; }
    public Guid? RuleRevisionId { get; init; }
    public int? RuleRevisionNumber { get; init; }
    public Guid? RuleDecisionRevisionId { get; init; }
    public int? RuleDecisionRevisionNumber { get; init; }
    public List<EducationLearningReportAssessment> Assessments { get; init; } = [];
    public List<EducationLearningReportStudentSummary> Students { get; init; } = [];
    public List<EducationLearningReportPointSummary> Points { get; init; } = [];
}
