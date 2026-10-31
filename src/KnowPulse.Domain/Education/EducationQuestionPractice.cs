using System;
using System.Linq;
using System.Text.Json;
using KnowPulse.Exam;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

/// <summary>One repeat of a frozen question. Its explicit teaching copy seals once and never becomes a formal attempt/grade.</summary>
public class EducationQuestionPractice : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid TeacherUserId { get; private set; }
    public Guid RecipientId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid TaskRevisionId { get; private set; }
    public Guid SourceSubmissionId { get; private set; }
    public Guid ResultReleaseId { get; private set; }
    public Guid QuestionGradeId { get; private set; }
    public Guid GradeRevisionId { get; private set; }
    public Guid ExamPaperId { get; private set; }
    public Guid PaperQuestionId { get; private set; }
    public Guid OriginalQuestionId { get; private set; }
    public Guid QuestionDefinitionRevisionId { get; private set; }
    public string StartFingerprint { get; private set; } = "";
    public DateTime StartedAtUtc { get; private set; }
    public Guid? SubmissionOperationId { get; private set; }
    public string? SubmissionFingerprint { get; private set; }
    public string? PreparationFingerprint { get; private set; }
    public DateTime? SubmittedAtUtc { get; private set; }
    public string? ResponseJson { get; private set; }
    public string? EvaluationJson { get; private set; }
    protected EducationQuestionPractice() { }

    public EducationQuestionPractice(Guid id, EducationAssessmentRecipient recipient, EducationAssessmentTaskRevision revision,
        EducationAssessmentResultRelease release, EducationAssessmentResultReleaseItem item, ExamPaperQuestion question, string fingerprint, DateTime now)
        : base(EducationRules.RequireId(id))
    {
        var grade = item.Grades.Grades.SingleOrDefault(x => x.PaperQuestionId == question.Id);
        if (recipient.TenantId != revision.TenantId || release.TenantId != revision.TenantId || item.TenantId != revision.TenantId || question.TenantId != revision.TenantId ||
            recipient.TaskId != revision.TaskId || recipient.TaskRevisionId != revision.Id || release.TaskId != revision.TaskId || release.TaskRevisionId != revision.Id ||
            item.ResultReleaseId != release.Id || item.RecipientId != recipient.Id || item.StudentUserId != recipient.StudentUserId || item.TaskId != revision.TaskId ||
            question.PaperId != revision.ExamPaperId || question.QuestionDefinitionRevisionId is not Guid definition || definition == Guid.Empty || grade == null ||
            release.VisibilityAt(now) != ExamResultVisibility.FullReview) throw Invalid();
        TenantId = recipient.TenantId; StudentUserId = recipient.StudentUserId; TeacherUserId = revision.OwnerUserId;
        RecipientId = recipient.Id; TaskId = revision.TaskId; TaskRevisionId = revision.Id; SourceSubmissionId = item.SubmissionId;
        ResultReleaseId = release.Id; QuestionGradeId = grade.QuestionGradeId; GradeRevisionId = grade.GradeRevisionId;
        ExamPaperId = revision.ExamPaperId; PaperQuestionId = question.Id; OriginalQuestionId = question.OriginalQuestionId; QuestionDefinitionRevisionId = definition;
        StartFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint); StartedAtUtc = EducationRules.RequireUtc(now);
    }
    public void Submit(Guid operationId, EducationAssessmentResponseValue response, ExamPaperQuestion question,
        string preparationFingerprint, string fingerprint, bool confirmSharing, bool confirmPracticeOnly, DateTime now)
    {
        ValidateResponse(response);
        if (operationId == Guid.Empty || operationId == Id || !confirmSharing || !confirmPracticeOnly || question.TenantId != TenantId || question.Id != PaperQuestionId || question.PaperId != ExamPaperId ||
            question.OriginalQuestionId != OriginalQuestionId || question.QuestionDefinitionRevisionId != QuestionDefinitionRevisionId || EducationRules.RequireUtc(now) < StartedAtUtc) throw Invalid();
        if (SubmissionOperationId.HasValue) throw new BusinessException(EducationPracticeErrors.Conflict);
        var score = EducationAssessmentScoring.Calculate(question, response);
        PreparationFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(preparationFingerprint);
        SubmissionFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint); ResponseJson = response.ToCanonicalJson();
        EvaluationJson = JsonSerializer.Serialize(new EducationPracticeEvaluation(score.MaxScore, score.ObtainedScore,
            score.Parts.Select(x => new EducationPracticePartResult(x.PartId, x.MaxScore, x.ObtainedScore)).ToList()));
        SubmissionOperationId = operationId; SubmittedAtUtc = now;
    }
    public static void ValidateResponse(EducationAssessmentResponseValue response)
    {
        if (response == null || response.IsUnanswered() || response.Images.Count > 5) throw Invalid();
    }
    private static BusinessException Invalid() => new(EducationPracticeErrors.Invalid);
}

public class EducationPracticeImage : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid PracticeId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public string FileName { get; private set; } = "";
    public string ContentType { get; private set; } = "";
    public long Size { get; private set; }
    public string ContentHash { get; private set; } = "";
    public string BlobName { get; private set; } = "";
    public string RequestFingerprint { get; private set; } = "";
    public DateTime ReservedAtUtc { get; private set; }
    public DateTime? UploadedAtUtc { get; private set; }
    protected EducationPracticeImage() { }
    public EducationPracticeImage(Guid id, EducationQuestionPractice practice, string name, string type, long size, string hash, string fingerprint, DateTime now)
        : base(EducationRules.RequireId(id))
    {
        if (practice.SubmittedAtUtc.HasValue || size is < 1 or > 5242880 || type is not ("image/png" or "image/jpeg" or "image/webp") ||
            name == null || name.IndexOfAny(['/', '\\', '\r', '\n', '\0']) >= 0 || EducationRules.RequireUtc(now) < practice.StartedAtUtc) throw new BusinessException(EducationPracticeErrors.Invalid);
        TenantId = practice.TenantId; PracticeId = practice.Id; StudentUserId = practice.StudentUserId;
        FileName = Check.NotNullOrWhiteSpace(name, nameof(name), 256); ContentType = type; Size = size;
        ContentHash = EducationAssessmentTaskRevision.RequireFingerprint(hash); RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint);
        BlobName = $"practice/{TenantId?.ToString("N") ?? "host"}/{StudentUserId:N}/{PracticeId:N}/{id:N}"; ReservedAtUtc = now;
    }
    public void Confirm(DateTime now) { if (EducationRules.RequireUtc(now) < ReservedAtUtc) throw new BusinessException(EducationPracticeErrors.Invalid); UploadedAtUtc ??= now; }
}
