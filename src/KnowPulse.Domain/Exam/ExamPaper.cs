using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Exam;

public class ExamPaper : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public const int MaxTitleLength = 256;

    public Guid? TenantId { get; protected set; }

    /// <summary>
    /// 在授权组卷时固化的个人所有者，不随原题、知识来源删除或修改而改变。
    /// 为空的组织及历史试卷仍须按原题来源复核管理权限。
    /// </summary>
    public Guid? PersonalOwnerUserId { get; private set; }

    public string? CurriculumSnapshotJson { get; private set; }
    public string? SaveOperationRequestHash { get; private set; }

    public void RecordSaveOperation(string requestHash)
    {
        if (SaveOperationRequestHash != null) throw new InvalidOperationException("The paper save operation is immutable.");
        if (requestHash.Length != 64 || !PersonalOwnerUserId.HasValue)
            throw new ArgumentException("A saved paper requires its owner's operation fingerprint.", nameof(requestHash));
        SaveOperationRequestHash = requestHash;
    }
    public void FreezeCurriculum(string snapshotJson)
    {
        if (CurriculumSnapshotJson != null) throw new InvalidOperationException("The paper curriculum is immutable.");
        CurriculumSnapshotJson = Check.NotNullOrWhiteSpace(snapshotJson, nameof(snapshotJson), 200000);
    }

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
        decimal totalScore,
        Guid? personalOwnerUserId = null)
        : base(id)
    {
        if (personalOwnerUserId == Guid.Empty)
        {
            throw new ArgumentException("A personal exam paper requires an identified owner.", nameof(personalOwnerUserId));
        }

        TenantId = tenantId;
        PersonalOwnerUserId = personalOwnerUserId;
        Title = Check.NotNullOrWhiteSpace(title, nameof(title), MaxTitleLength);
        BuildMode = buildMode;
        ApplicablePositionId = applicablePositionId;
        QuestionCount = questionCount;
        TotalScore = totalScore;
        SnapshotVersion = 1;
    }
}
