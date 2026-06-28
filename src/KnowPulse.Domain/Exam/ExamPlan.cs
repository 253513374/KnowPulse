using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Exam;

public class ExamPlan : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public const int MaxNameLength = 256;
    public const int MaxDescriptionLength = 1024;

    public Guid? TenantId { get; protected set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public Guid PaperId { get; private set; }

    public DateTime StartTime { get; private set; }

    public DateTime EndTime { get; private set; }

    public int DurationMinutes { get; private set; }

    public decimal PassingScore { get; private set; }

    public int MaxAttempts { get; private set; }

    public ExamResultVisibility ResultVisibility { get; private set; }

    public ExamPlanStatus Status { get; private set; }

    protected ExamPlan()
    {
    }

    public ExamPlan(
        Guid id,
        Guid? tenantId,
        string name,
        Guid paperId,
        DateTime startTime,
        DateTime endTime,
        int durationMinutes,
        decimal passingScore,
        int maxAttempts,
        ExamResultVisibility resultVisibility,
        string? description = null)
        : base(id)
    {
        TenantId = tenantId;
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), MaxNameLength);
        Description = Check.Length(description, nameof(description), MaxDescriptionLength);
        PaperId = paperId;
        StartTime = startTime;
        EndTime = endTime;
        SetDuration(durationMinutes);
        SetAttempts(maxAttempts);
        PassingScore = passingScore;
        ResultVisibility = resultVisibility;
        Status = ExamPlanStatus.Published;
        EnsureWindowIsValid();
    }

    public void Cancel()
    {
        Status = ExamPlanStatus.Cancelled;
    }

    public void EnsureCanStart(DateTime now)
    {
        if (Status != ExamPlanStatus.Published)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.ExamPlanNotPublished);
        }

        if (now < StartTime)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.ExamPlanNotStarted);
        }

        if (now > EndTime)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.ExamPlanEnded);
        }
    }

    private void SetDuration(int durationMinutes)
    {
        if (durationMinutes <= 0)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.ExamPlanDurationInvalid);
        }

        DurationMinutes = durationMinutes;
    }

    private void SetAttempts(int maxAttempts)
    {
        if (maxAttempts <= 0)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.ExamPlanAttemptLimitInvalid);
        }

        MaxAttempts = maxAttempts;
    }

    private void EnsureWindowIsValid()
    {
        if (EndTime <= StartTime)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.ExamPlanWindowInvalid);
        }
    }
}
