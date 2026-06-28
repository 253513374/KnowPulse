using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Exam;

public class WrongQuestionRecord : FullAuditedEntity<Guid>, IMultiTenant
{
    public const int MaxSnapshotLength = 4000;
    public const int MaxJsonLength = 4000;
    public const int MaxAnalysisLength = 2048;

    public Guid? TenantId { get; protected set; }

    public Guid ScoreId { get; private set; }

    public Guid PlanId { get; private set; }

    public Guid PaperId { get; private set; }

    public Guid AttemptId { get; private set; }

    public Guid PaperQuestionId { get; private set; }

    public Guid OriginalQuestionId { get; private set; }

    public Guid UserId { get; private set; }

    public string QuestionSnapshot { get; private set; } = string.Empty;

    public string UserAnswerJson { get; private set; } = "[]";

    public string CorrectAnswerJson { get; private set; } = "[]";

    public string? AnalysisSnapshot { get; private set; }

    public decimal MaxScore { get; private set; }

    public decimal ObtainedScore { get; private set; }

    protected WrongQuestionRecord()
    {
    }

    public WrongQuestionRecord(
        Guid id,
        Guid? tenantId,
        Guid scoreId,
        Guid planId,
        Guid paperId,
        Guid attemptId,
        Guid paperQuestionId,
        Guid originalQuestionId,
        Guid userId,
        string questionSnapshot,
        string userAnswerJson,
        string correctAnswerJson,
        string? analysisSnapshot,
        decimal maxScore,
        decimal obtainedScore)
        : base(id)
    {
        TenantId = tenantId;
        ScoreId = scoreId;
        PlanId = planId;
        PaperId = paperId;
        AttemptId = attemptId;
        PaperQuestionId = paperQuestionId;
        OriginalQuestionId = originalQuestionId;
        UserId = userId;
        QuestionSnapshot = Check.NotNullOrWhiteSpace(questionSnapshot, nameof(questionSnapshot), MaxSnapshotLength);
        UserAnswerJson = Check.NotNull(userAnswerJson, nameof(userAnswerJson)).Length <= MaxJsonLength
            ? userAnswerJson
            : throw new BusinessException(KnowPulseDomainErrorCodes.ExamAnswerTooLarge);
        CorrectAnswerJson = Check.NotNull(correctAnswerJson, nameof(correctAnswerJson)).Length <= MaxJsonLength
            ? correctAnswerJson
            : throw new BusinessException(KnowPulseDomainErrorCodes.ExamAnswerTooLarge);
        AnalysisSnapshot = Check.Length(analysisSnapshot, nameof(analysisSnapshot), MaxAnalysisLength);
        MaxScore = maxScore;
        ObtainedScore = obtainedScore;
    }
}
