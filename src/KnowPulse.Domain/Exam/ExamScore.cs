using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Exam;

public class ExamScore : FullAuditedEntity<Guid>, IMultiTenant
{
    public const int MaxScoringVersionLength = 32;

    public Guid? TenantId { get; protected set; }

    public Guid PlanId { get; private set; }

    public Guid PaperId { get; private set; }

    public Guid AttemptId { get; private set; }

    public Guid UserId { get; private set; }

    public decimal TotalScore { get; private set; }

    public decimal ObtainedScore { get; private set; }

    public decimal PassingScore { get; private set; }

    public bool IsPassed { get; private set; }

    public DateTime ScoredTime { get; private set; }

    public string ScoringVersion { get; private set; } = string.Empty;

    protected ExamScore()
    {
    }

    public ExamScore(
        Guid id,
        Guid? tenantId,
        Guid planId,
        Guid paperId,
        Guid attemptId,
        Guid userId,
        decimal totalScore,
        decimal obtainedScore,
        decimal passingScore,
        DateTime scoredTime,
        string scoringVersion)
        : base(id)
    {
        TenantId = tenantId;
        PlanId = planId;
        PaperId = paperId;
        AttemptId = attemptId;
        UserId = userId;
        Refresh(totalScore, obtainedScore, passingScore, scoredTime, scoringVersion);
    }

    public void Refresh(
        decimal totalScore,
        decimal obtainedScore,
        decimal passingScore,
        DateTime scoredTime,
        string scoringVersion)
    {
        TotalScore = totalScore;
        ObtainedScore = obtainedScore;
        PassingScore = passingScore;
        IsPassed = obtainedScore >= passingScore;
        ScoredTime = scoredTime;
        ScoringVersion = scoringVersion.Length <= MaxScoringVersionLength
            ? scoringVersion
            : scoringVersion[..MaxScoringVersionLength];
    }
}
