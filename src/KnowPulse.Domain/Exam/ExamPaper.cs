using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Exam;

public class ExamPaper : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public const int MaxTitleLength = 256;

    public Guid? TenantId { get; protected set; }

    public string Title { get; private set; } = string.Empty;

    public ExamPaperBuildMode BuildMode { get; private set; }

    public Guid? ApplicablePositionId { get; private set; }

    public int QuestionCount { get; private set; }

    public decimal TotalScore { get; private set; }

    public int SnapshotVersion { get; private set; }

    protected ExamPaper()
    {
    }

    public ExamPaper(
        Guid id,
        Guid? tenantId,
        string title,
        ExamPaperBuildMode buildMode,
        Guid? applicablePositionId,
        int questionCount,
        decimal totalScore)
        : base(id)
    {
        TenantId = tenantId;
        Title = Check.NotNullOrWhiteSpace(title, nameof(title), MaxTitleLength);
        BuildMode = buildMode;
        ApplicablePositionId = applicablePositionId;
        QuestionCount = questionCount;
        TotalScore = totalScore;
        SnapshotVersion = 1;
    }
}
