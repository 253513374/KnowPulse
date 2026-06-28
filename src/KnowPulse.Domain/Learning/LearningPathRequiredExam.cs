using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Learning;

/// <summary>
/// 学习路径必考考试配置，引用已发布的考试计划。
/// </summary>
public class LearningPathRequiredExam : FullAuditedEntity<Guid>, IMultiTenant
{
    /// <summary>
    /// 必考考试备注最大长度。
    /// </summary>
    public const int MaxNoteLength = 512;

    /// <summary>
    /// ABP 多租户标识；为空表示宿主侧配置。
    /// </summary>
    public Guid? TenantId { get; protected set; }

    /// <summary>
    /// 所属学习路径 ID。
    /// </summary>
    public Guid LearningPathId { get; private set; }

    /// <summary>
    /// 必考考试计划 ID。创建配置时应用服务保证考试计划已发布。
    /// </summary>
    public Guid ExamPlanId { get; private set; }

    /// <summary>
    /// 在学习中心中的展示顺序。
    /// </summary>
    public int DisplayOrder { get; private set; }

    /// <summary>
    /// 是否要求达到及格线才算完成。为 false 时有成绩即可视为完成。
    /// </summary>
    public bool PassingRequired { get; private set; }

    /// <summary>
    /// 管理员配置的考试说明或完成要求。
    /// </summary>
    public string? Note { get; private set; }

    /// <summary>
    /// EF Core 使用的受保护构造函数。
    /// </summary>
    protected LearningPathRequiredExam()
    {
    }

    /// <summary>
    /// 创建学习路径必考考试配置。
    /// </summary>
    /// <param name="id">配置记录 ID。</param>
    /// <param name="tenantId">租户 ID；宿主侧数据为空。</param>
    /// <param name="learningPathId">所属学习路径 ID。</param>
    /// <param name="examPlanId">已发布考试计划 ID。</param>
    /// <param name="displayOrder">展示顺序。</param>
    /// <param name="passingRequired">是否必须及格。</param>
    /// <param name="note">考试说明或完成要求。</param>
    public LearningPathRequiredExam(
        Guid id,
        Guid? tenantId,
        Guid learningPathId,
        Guid examPlanId,
        int displayOrder,
        bool passingRequired = true,
        string? note = null)
        : base(id)
    {
        TenantId = tenantId;
        LearningPathId = learningPathId;
        ExamPlanId = examPlanId;
        DisplayOrder = displayOrder;
        PassingRequired = passingRequired;
        Note = Check.Length(note, nameof(note), MaxNoteLength);
    }
}
