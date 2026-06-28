using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Learning;

/// <summary>
/// 学习路径必学知识配置，记录知识条目和被固定下来的发布版本。
/// </summary>
public class LearningPathRequiredKnowledge : FullAuditedEntity<Guid>, IMultiTenant
{
    /// <summary>
    /// 必学知识备注最大长度。
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
    /// 知识条目 ID。
    /// </summary>
    public Guid KnowledgeItemId { get; private set; }

    /// <summary>
    /// 固定的已发布知识版本 ID。路径配置后不随知识条目当前发布版本自动漂移。
    /// </summary>
    public Guid KnowledgeVersionId { get; private set; }

    /// <summary>
    /// 在学习中心中的展示顺序。
    /// </summary>
    public int DisplayOrder { get; private set; }

    /// <summary>
    /// 管理员配置的必学说明或完成要求。
    /// </summary>
    public string? Note { get; private set; }

    /// <summary>
    /// EF Core 使用的受保护构造函数。
    /// </summary>
    protected LearningPathRequiredKnowledge()
    {
    }

    /// <summary>
    /// 创建学习路径必学知识配置。
    /// </summary>
    /// <param name="id">配置记录 ID。</param>
    /// <param name="tenantId">租户 ID；宿主侧数据为空。</param>
    /// <param name="learningPathId">所属学习路径 ID。</param>
    /// <param name="knowledgeItemId">知识条目 ID。</param>
    /// <param name="knowledgeVersionId">已发布知识版本 ID。</param>
    /// <param name="displayOrder">展示顺序。</param>
    /// <param name="note">必学说明或完成要求。</param>
    public LearningPathRequiredKnowledge(
        Guid id,
        Guid? tenantId,
        Guid learningPathId,
        Guid knowledgeItemId,
        Guid knowledgeVersionId,
        int displayOrder,
        string? note = null)
        : base(id)
    {
        TenantId = tenantId;
        LearningPathId = learningPathId;
        KnowledgeItemId = knowledgeItemId;
        KnowledgeVersionId = knowledgeVersionId;
        DisplayOrder = displayOrder;
        Note = Check.Length(note, nameof(note), MaxNoteLength);
    }
}
