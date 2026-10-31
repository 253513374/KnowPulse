using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KnowPulse.Exam;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

public class EducationAssessmentQuestionGrade : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid TaskRevisionId { get; private set; }
    public Guid AttemptId { get; private set; }
    public Guid SubmissionId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid ExamPaperId { get; private set; }
    public Guid PaperQuestionId { get; private set; }
    public int RevisionNumber { get; private set; }
    public Guid? CurrentRevisionId { get; private set; }
    public Guid? CurrentConfirmedRevisionId { get; private set; }
    protected EducationAssessmentQuestionGrade() { }
    public EducationAssessmentQuestionGrade(Guid id, EducationAssessmentTaskRevision task, EducationAssessmentAttempt attempt,
        EducationAssessmentSubmission submission, ExamPaperQuestion question) : base(EducationRules.RequireId(id))
    {
        if (task.TenantId != attempt.TenantId || submission.TenantId != attempt.TenantId || question.TenantId != attempt.TenantId ||
            task.Id != attempt.TaskRevisionId || task.TaskId != attempt.TaskId || submission.AttemptId != attempt.Id ||
            attempt.SubmissionId != submission.Id || submission.StudentUserId != attempt.StudentUserId ||
            question.PaperId != attempt.ExamPaperId || submission.ExamPaperId != question.PaperId) throw Invalid();
        TenantId = attempt.TenantId; OwnerUserId = task.OwnerUserId; TaskId = task.TaskId; TaskRevisionId = task.Id;
        AttemptId = attempt.Id; SubmissionId = submission.Id; StudentUserId = attempt.StudentUserId;
        ExamPaperId = question.PaperId; PaperQuestionId = question.Id;
    }
    public void Advance(EducationAssessmentGradeRevision revision)
    {
        if (revision.QuestionGradeId != Id || revision.TenantId != TenantId || revision.SubmissionId != SubmissionId ||
            revision.PaperQuestionId != PaperQuestionId || revision.RevisionNumber != RevisionNumber + 1 ||
            revision.BasedOnRevisionId != CurrentRevisionId || revision.ExpectedConcurrencyStamp != ConcurrencyStamp) throw Invalid();
        CurrentRevisionId = revision.Id; RevisionNumber = revision.RevisionNumber;
        if (revision.Kind == EducationAssessmentGradeRevisionKind.Confirmed) CurrentConfirmedRevisionId = revision.Id;
    }
    private static BusinessException Invalid() => new(EducationAssessmentErrorCodes.InvalidGrade);
}

public class EducationAssessmentGradeRevision : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid AttemptId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid ExamPaperId { get; private set; }
    public int PaperSnapshotVersion { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid QuestionGradeId { get; private set; }
    public Guid SubmissionId { get; private set; }
    public Guid PaperQuestionId { get; private set; }
    public int RevisionNumber { get; private set; }
    public Guid? BasedOnRevisionId { get; private set; }
    public Guid? SupersedesRevisionId { get; private set; }
    public Guid? ResponseRevisionId { get; private set; }
    public string? ResponseHash { get; private set; }
    public Guid QuestionDefinitionRevisionId { get; private set; }
    public string ScoringDefinitionHash { get; private set; } = string.Empty;
    public string CalculationVersion { get; private set; } = EducationAssessmentGradeEvidence.AlgorithmVersion;
    public EducationAssessmentGradeRevisionKind Kind { get; private set; }
    public EducationAssessmentGradingMethod Method { get; private set; }
    public string ContentJson { get; private set; } = string.Empty;
    public EducationAssessmentGradeContent Content => EducationAssessmentGradeContent.Parse(ContentJson, Kind == EducationAssessmentGradeRevisionKind.Confirmed);
    public decimal MaxScore { get; private set; }
    public decimal? ObtainedScore { get; private set; }
    public string? Feedback { get; private set; }
    public string? Reason { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string ExpectedConcurrencyStamp { get; private set; } = string.Empty;
    public string RequestFingerprint { get; private set; } = string.Empty;
    public int? ExpectedGradeSetVersion { get; private set; }
    public int? ResultGradeSetVersion { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public DateTime? ConfirmedAtUtc { get; private set; }
    protected EducationAssessmentGradeRevision() { }
    public EducationAssessmentGradeRevision(Guid id, EducationAssessmentQuestionGrade grade, EducationAssessmentSubmission submission,
        EducationAssessmentResponseRevision? response, ExamPaperQuestion question, EducationAssessmentGradeRevisionKind kind,
        EducationAssessmentGradingMethod method, IReadOnlyCollection<EducationAssessmentPartMark> marks, string? feedback,
        string? reason, Guid? actor, string expectedStamp, string fingerprint, DateTime now, int? expectedGradeSetVersion)
        : base(EducationRules.RequireId(id))
    {
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(method) || grade.SubmissionId != submission.Id ||
            grade.TenantId != submission.TenantId || grade.PaperQuestionId != question.Id || grade.ExamPaperId != question.PaperId ||
            grade.TenantId != question.TenantId || expectedStamp != grade.ConcurrencyStamp || marks == null ||
            marks.Any(x => x == null) || marks.Select(x => x.PartId).Distinct().Count() != marks.Count ||
            !question.QuestionDefinitionRevisionId.HasValue || actor == Guid.Empty ||
            (method == EducationAssessmentGradingMethod.Teacher && actor != grade.OwnerUserId) ||
            (method == EducationAssessmentGradingMethod.Automatic && (actor.HasValue || grade.CurrentRevisionId.HasValue)) ||
            (kind == EducationAssessmentGradeRevisionKind.Confirmed && (expectedGradeSetVersion is null or < 0 or int.MaxValue)) ||
            (kind == EducationAssessmentGradeRevisionKind.Draft && expectedGradeSetVersion.HasValue)) throw Invalid();
        var entry = JsonSerializer.Deserialize<List<EducationAssessmentSubmissionManifestEntry>>(submission.ManifestJson)!
            .SingleOrDefault(x => x.PaperQuestionId == question.Id) ?? throw Invalid();
        if (entry.ResponseRevisionId != response?.Id || entry.ResponseHash != response?.ResponseHash || response != null &&
            (response.AttemptId != grade.AttemptId || response.StudentUserId != grade.StudentUserId || response.TenantId != grade.TenantId ||
             response.ExamPaperId != grade.ExamPaperId || response.PaperQuestionId != question.Id ||
             response.PaperSnapshotVersion != submission.PaperSnapshotVersion || response.ResultResponseSetVersion > submission.ResponseSetVersion ||
             Hash(response.ResponseJson) != response.ResponseHash)) throw Invalid();
        var calculated = EducationAssessmentScoring.Calculate(question, response == null ? null : EducationAssessmentResponseValue.Parse(response.ResponseJson));
        if (marks.Count != calculated.Parts.Count || marks.Any(x => !calculated.Parts.Any(p => p.PartId == x.PartId))) throw Invalid();
        var parts = calculated.Parts.Select(part => part with { ObtainedScore = marks.Single(x => x.PartId == part.PartId).ObtainedScore }).ToList();
        if (method == EducationAssessmentGradingMethod.Automatic && !parts.SequenceEqual(calculated.Parts)) throw Invalid();
        var score = new EducationAssessmentQuestionScore(question.Score, parts.Any(x => !x.ObtainedScore.HasValue) ? null : parts.Sum(x => x.ObtainedScore!.Value), parts);
        var content = new EducationAssessmentGradeContent { Parts = parts.Select(x => new EducationAssessmentGradePart
        { PartId = x.PartId, MaxScore = x.MaxScore, ObtainedScore = x.ObtainedScore, TextbookKnowledgePointMappingId = x.TextbookKnowledgePointMappingId }).ToList() };
        if (kind == EducationAssessmentGradeRevisionKind.Confirmed)
        {
            content.MappingEvidence = EducationAssessmentGradeEvidence.Build(question, score).Select(x => new EducationAssessmentMappingGradeFact
            { TextbookKnowledgePointMappingId = x.TextbookKnowledgePointMappingId, KnowledgePointId = x.KnowledgePointId,
              KnowledgePointRevisionId = x.KnowledgePointRevisionId, MaxScore = x.MaxScore, ObtainedScore = x.ObtainedScore, AlgorithmVersion = x.AlgorithmVersion }).ToList();
            if (grade.CurrentConfirmedRevisionId.HasValue && string.IsNullOrWhiteSpace(reason)) throw Invalid();
        }
        ContentJson = content.ToCanonicalJson(kind == EducationAssessmentGradeRevisionKind.Confirmed);
        TenantId = grade.TenantId; TaskId = grade.TaskId; QuestionGradeId = grade.Id; SubmissionId = submission.Id; PaperQuestionId = question.Id;
        AttemptId = grade.AttemptId; StudentUserId = grade.StudentUserId; ExamPaperId = grade.ExamPaperId; PaperSnapshotVersion = submission.PaperSnapshotVersion;
        RevisionNumber = checked(grade.RevisionNumber + 1); BasedOnRevisionId = grade.CurrentRevisionId;
        SupersedesRevisionId = kind == EducationAssessmentGradeRevisionKind.Confirmed ? grade.CurrentConfirmedRevisionId : null;
        ResponseRevisionId = response?.Id; ResponseHash = response?.ResponseHash; QuestionDefinitionRevisionId = question.QuestionDefinitionRevisionId.Value;
        ScoringDefinitionHash = Hash(question.ScoringDefinitionJson ?? "legacy-objective"); Kind = kind; Method = method;
        MaxScore = content.MaxScore; ObtainedScore = content.ObtainedScore; Feedback = Check.Length(feedback, nameof(feedback), 4000);
        Reason = Check.Length(reason, nameof(reason), 2048); ActorUserId = actor;
        ExpectedConcurrencyStamp = Check.NotNullOrWhiteSpace(expectedStamp, nameof(expectedStamp), 40);
        RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint); RecordedAtUtc = EducationRules.RequireUtc(now);
        if (kind == EducationAssessmentGradeRevisionKind.Confirmed)
        { ExpectedGradeSetVersion = expectedGradeSetVersion; ResultGradeSetVersion = expectedGradeSetVersion + 1; ConfirmedAtUtc = now; }
    }
    public static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static BusinessException Invalid() => new(EducationAssessmentErrorCodes.InvalidGrade);
}

public class EducationAssessmentClosure : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid TaskRevisionId { get; private set; }
    public string ExpectedConcurrencyStamp { get; private set; } = string.Empty;
    public string RequestFingerprint { get; private set; } = string.Empty;
    public string ManifestJson { get; private set; } = string.Empty;
    public EducationAssessmentClosureValue Manifest => EducationAssessmentClosureValue.Parse(ManifestJson);
    public string SubmissionSetFingerprint { get; private set; } = string.Empty;
    public int ValidSubmissionCount { get; private set; }
    public DateTime EffectiveAtUtc { get; private set; }
    public DateTime ClosedAtUtc { get; private set; }
    protected EducationAssessmentClosure() { }
    public EducationAssessmentClosure(Guid id, EducationAssessmentTask task, EducationAssessmentTaskRevision revision,
        EducationAssessmentClosureValue manifest, string expectedStamp, string fingerprint, DateTime now) : base(EducationRules.RequireId(id))
    {
        if (task.TenantId != revision.TenantId || task.PublishedRevisionId != revision.Id || revision.TaskId != task.Id ||
            task.ClosureId.HasValue || now < revision.EndsAtUtc || string.IsNullOrWhiteSpace(expectedStamp)) throw Invalid();
        TenantId = task.TenantId; TaskId = task.Id; TaskRevisionId = revision.Id;
        ManifestJson = manifest.ToCanonicalJson(); SubmissionSetFingerprint = manifest.SubmissionSetFingerprint;
        ValidSubmissionCount = manifest.ValidSubmissionCount; ExpectedConcurrencyStamp = expectedStamp;
        RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint);
        EffectiveAtUtc = revision.EndsAtUtc; ClosedAtUtc = EducationRules.RequireUtc(now);
    }
    private static BusinessException Invalid() => new(EducationAssessmentErrorCodes.InvalidResultRelease);
}

public class EducationAssessmentResultRelease : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid TaskRevisionId { get; private set; }
    public Guid ClosureId { get; private set; }
    public Guid? PreviousReleaseId { get; private set; }
    public int ConfirmedGradeSetVersion { get; private set; }
    public string ExpectedConcurrencyStamp { get; private set; } = string.Empty;
    public string RequestFingerprint { get; private set; } = string.Empty;
    public string SubmissionSetFingerprint { get; private set; } = string.Empty;
    public int ValidSubmissionCount { get; private set; }
    public ExamResultVisibility ConfiguredVisibility { get; private set; }
    public DateTime? ScoreOpensAtUtc { get; private set; }
    public DateTime? ReviewOpensAtUtc { get; private set; }
    public Guid ConfirmedByUserId { get; private set; }
    public DateTime ReleasedAtUtc { get; private set; }
    public string? Reason { get; private set; }
    protected EducationAssessmentResultRelease() { }
    public EducationAssessmentResultRelease(Guid id, EducationAssessmentTask task, EducationAssessmentClosure closure,
        ExamResultVisibility visibility, DateTime? scoreAt, DateTime? reviewAt, string fingerprint, string? reason, DateTime now, string? expectedStamp = null)
        : base(EducationRules.RequireId(id))
    {
        if (task.ClosureId != closure.Id || task.Id != closure.TaskId || task.TenantId != closure.TenantId || !Enum.IsDefined(visibility) ||
            now < closure.ClosedAtUtc || visibility != ExamResultVisibility.Hidden && !scoreAt.HasValue ||
            visibility == ExamResultVisibility.FullReview && (!reviewAt.HasValue || reviewAt < scoreAt) ||
            scoreAt.HasValue && scoreAt < closure.EffectiveAtUtc || reviewAt.HasValue && reviewAt < closure.EffectiveAtUtc ||
            task.CurrentResultReleaseId.HasValue && string.IsNullOrWhiteSpace(reason)) throw Invalid();
        if (scoreAt.HasValue) EducationRules.RequireUtc(scoreAt.Value);
        if (reviewAt.HasValue) EducationRules.RequireUtc(reviewAt.Value);
        TenantId = task.TenantId; TaskId = task.Id; TaskRevisionId = closure.TaskRevisionId; ClosureId = closure.Id;
        PreviousReleaseId = task.CurrentResultReleaseId; ConfirmedGradeSetVersion = task.ConfirmedGradeSetVersion;
        ExpectedConcurrencyStamp = expectedStamp ?? task.ConcurrencyStamp; RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint);
        SubmissionSetFingerprint = closure.SubmissionSetFingerprint; ValidSubmissionCount = closure.ValidSubmissionCount;
        ConfiguredVisibility = visibility; ScoreOpensAtUtc = scoreAt; ReviewOpensAtUtc = reviewAt; ConfirmedByUserId = task.OwnerUserId;
        ReleasedAtUtc = EducationRules.RequireUtc(now); Reason = Check.Length(reason, nameof(reason), 2048);
    }
    public ExamResultVisibility VisibilityAt(DateTime now) => ConfiguredVisibility == ExamResultVisibility.Hidden || !ScoreOpensAtUtc.HasValue ||
        now < ScoreOpensAtUtc.Value || now < ReleasedAtUtc ? ExamResultVisibility.Hidden :
        ConfiguredVisibility == ExamResultVisibility.FullReview && ReviewOpensAtUtc.HasValue && now >= ReviewOpensAtUtc.Value ? ExamResultVisibility.FullReview : ExamResultVisibility.ScoreOnly;
    private static BusinessException Invalid() => new(EducationAssessmentErrorCodes.InvalidResultRelease);
}

public class EducationAssessmentResultReleaseItem : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid ResultReleaseId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid RecipientId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid AttemptId { get; private set; }
    public Guid SubmissionId { get; private set; }
    public decimal TotalScore { get; private set; }
    public decimal MaxScore { get; private set; }
    public string GradesJson { get; private set; } = string.Empty;
    public EducationAssessmentReleasedGradesValue Grades => EducationAssessmentReleasedGradesValue.Parse(GradesJson);
    protected EducationAssessmentResultReleaseItem() { }
    public EducationAssessmentResultReleaseItem(Guid id, EducationAssessmentResultRelease release, EducationAssessmentClosureEntry entry,
        IReadOnlyCollection<EducationAssessmentGradeRevision> grades) : base(EducationRules.RequireId(id))
    {
        if (entry.Outcome != EducationAssessmentRecipientOutcome.Submitted || !entry.SubmissionId.HasValue || !entry.AttemptId.HasValue ||
            grades.Count == 0 || grades.Any(x => x.TenantId != release.TenantId || x.TaskId != release.TaskId || x.SubmissionId != entry.SubmissionId ||
            x.Kind != EducationAssessmentGradeRevisionKind.Confirmed || !x.ObtainedScore.HasValue) ||
            grades.Select(x => x.PaperQuestionId).Distinct().Count() != grades.Count) throw new BusinessException(EducationAssessmentErrorCodes.GradingIncomplete);
        TenantId = release.TenantId; ResultReleaseId = release.Id; TaskId = release.TaskId; RecipientId = entry.RecipientId;
        StudentUserId = entry.StudentUserId; AttemptId = entry.AttemptId.Value; SubmissionId = entry.SubmissionId.Value;
        TotalScore = grades.Sum(x => x.ObtainedScore!.Value); MaxScore = grades.Sum(x => x.MaxScore);
        GradesJson = new EducationAssessmentReleasedGradesValue { Grades = grades.Select(x => new EducationAssessmentReleasedGradeReference
        { PaperQuestionId = x.PaperQuestionId, QuestionGradeId = x.QuestionGradeId, GradeRevisionId = x.Id }).ToList() }.ToCanonicalJson();
    }
}

public class EducationAssessmentScoreReviewRequest : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid SubmissionId { get; private set; }
    public Guid ResultReleaseId { get; private set; }
    public Guid? PaperQuestionId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string RequestFingerprint { get; private set; } = string.Empty;
    public DateTime RequestedAtUtc { get; private set; }
    public Guid? ResolutionOperationId { get; private set; }
    public string? ResolutionRequestFingerprint { get; private set; }
    public string? ExpectedResolutionConcurrencyStamp { get; private set; }
    public EducationAssessmentScoreReviewDecision? Decision { get; private set; }
    public string? ResolutionReason { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public Guid? CorrectedGradeRevisionId { get; private set; }
    public Guid? ResultingReleaseId { get; private set; }
    protected EducationAssessmentScoreReviewRequest() { }
    public EducationAssessmentScoreReviewRequest(Guid id, EducationAssessmentResultReleaseItem item, Guid? questionId,
        string reason, string fingerprint, DateTime now) : base(EducationRules.RequireId(id))
    {
        if (questionId == Guid.Empty || questionId.HasValue && !item.Grades.Grades.Any(x => x.PaperQuestionId == questionId)) throw Invalid();
        TenantId = item.TenantId; TaskId = item.TaskId; StudentUserId = item.StudentUserId; SubmissionId = item.SubmissionId;
        ResultReleaseId = item.ResultReleaseId; PaperQuestionId = questionId;
        Reason = Check.NotNullOrWhiteSpace(reason, nameof(reason), 2048); RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint);
        RequestedAtUtc = EducationRules.RequireUtc(now);
    }
    public void Resolve(Guid operationId, string fingerprint, string stamp, EducationAssessmentScoreReviewDecision decision,
        string reason, Guid? correctedGrade, Guid? release, DateTime now)
    {
        if (ResolutionOperationId.HasValue || stamp != ConcurrencyStamp || !Enum.IsDefined(decision) ||
            decision == EducationAssessmentScoreReviewDecision.Corrected && (!correctedGrade.HasValue || !release.HasValue) ||
            correctedGrade == Guid.Empty || release == Guid.Empty || now < RequestedAtUtc) throw Invalid();
        ResolutionOperationId = EducationRules.RequireId(operationId); ResolutionRequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint);
        ExpectedResolutionConcurrencyStamp = stamp; Decision = decision; ResolutionReason = Check.NotNullOrWhiteSpace(reason, nameof(reason), 2048);
        CorrectedGradeRevisionId = correctedGrade; ResultingReleaseId = release; ResolvedAtUtc = EducationRules.RequireUtc(now);
    }
    private static BusinessException Invalid() => new(EducationAssessmentErrorCodes.InvalidGrade);
}
