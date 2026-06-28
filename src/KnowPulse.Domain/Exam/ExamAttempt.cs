using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Exam;

public class ExamAttempt : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public const int MaxIdempotencyKeyLength = 128;

    public Guid? TenantId { get; protected set; }

    public Guid PlanId { get; private set; }

    public Guid PaperId { get; private set; }

    public Guid UserId { get; private set; }

    public int AttemptNumber { get; private set; }

    public ExamAttemptStatus Status { get; private set; }

    public DateTime StartedTime { get; private set; }

    public DateTime ExpiresTime { get; private set; }

    public DateTime? LastSavedTime { get; private set; }

    public DateTime? SubmittedTime { get; private set; }

    public DateTime? ScoredTime { get; private set; }

    public string? SubmitIdempotencyKey { get; private set; }

    protected ExamAttempt()
    {
    }

    public ExamAttempt(
        Guid id,
        Guid? tenantId,
        Guid planId,
        Guid paperId,
        Guid userId,
        int attemptNumber,
        DateTime startedTime,
        DateTime expiresTime)
        : base(id)
    {
        if (attemptNumber <= 0)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.ExamAttemptNumberInvalid);
        }

        TenantId = tenantId;
        PlanId = planId;
        PaperId = paperId;
        UserId = userId;
        AttemptNumber = attemptNumber;
        StartedTime = startedTime;
        ExpiresTime = expiresTime;
        Status = ExamAttemptStatus.InProgress;
    }

    public void EnsureCanSave(DateTime now)
    {
        if (Status != ExamAttemptStatus.InProgress)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.ExamAttemptCannotSave);
        }

        if (now > ExpiresTime)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.ExamAttemptExpired);
        }
    }

    public void TouchSaved(DateTime now)
    {
        LastSavedTime = now;
    }

    public void Submit(DateTime now, string? idempotencyKey)
    {
        if (Status is ExamAttemptStatus.Submitted or ExamAttemptStatus.Scored)
        {
            return;
        }

        if (Status != ExamAttemptStatus.InProgress)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.ExamAttemptCannotSubmit);
        }

        if (now > ExpiresTime)
        {
            Status = ExamAttemptStatus.Expired;
            throw new BusinessException(KnowPulseDomainErrorCodes.ExamAttemptExpired);
        }

        Status = ExamAttemptStatus.Submitted;
        SubmittedTime = now;
        SubmitIdempotencyKey = Check.Length(idempotencyKey, nameof(idempotencyKey), MaxIdempotencyKeyLength);
    }

    public void MarkScored(DateTime now)
    {
        if (Status == ExamAttemptStatus.Scored)
        {
            ScoredTime ??= now;
            return;
        }

        if (Status != ExamAttemptStatus.Submitted)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.ExamAttemptCannotScore);
        }

        Status = ExamAttemptStatus.Scored;
        ScoredTime = now;
    }
}
