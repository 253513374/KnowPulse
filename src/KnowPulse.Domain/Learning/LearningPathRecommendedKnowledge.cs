using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Learning;

/// <summary>
/// 学习路径推荐知识配置，用于补充非强制但建议阅读的知识内容。
/// </summary>
public class LearningPathRecommendedKnowledge : FullAuditedEntity<Guid>, IMultiTenant
{
    /// <summary>
    /// 推荐理由最大长度。
    /// </summary>
    public const int MaxReasonLength = 512;

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
    /// 固定的已发布知识版本 ID。
    /// </summary>
    public Guid KnowledgeVersionId { get; private set; }

    /// <summary>
    /// 在学习中心中的展示顺序。
    /// </summary>
    public int DisplayOrder { get; private set; }

    /// <summary>
    /// 推荐理由，说明该知识为什么适合当前路径学习者阅读。
    /// </summary>
    public string? Reason { get; private set; }

    /// <summary>
    /// EF Core 使用的受保护构造函数。
    /// </summary>
    protected LearningPathRecommendedKnowledge()
    {
    }

    /// <summary>
    /// 创建学习路径推荐知识配置。
    /// </summary>
    /// <param name="id">配置记录 ID。</param>
    /// <param name="tenantId">租户 ID；宿主侧数据为空。</param>
    /// <param name="learningPathId">所属学习路径 ID。</param>
    /// <param name="knowledgeItemId">知识条目 ID。</param>
    /// <param name="knowledgeVersionId">已发布知识版本 ID。</param>
    /// <param name="displayOrder">展示顺序。</param>
    /// <param name="reason">推荐理由。</param>
    public LearningPathRecommendedKnowledge(
        Guid id,
        Guid? tenantId,
        Guid learningPathId,
        Guid knowledgeItemId,
        Guid knowledgeVersionId,
        int displayOrder,
        string? reason = null)
        : base(id)
    {
        TenantId = tenantId;
        LearningPathId = learningPathId;
        KnowledgeItemId = knowledgeItemId;
        KnowledgeVersionId = knowledgeVersionId;
        DisplayOrder = displayOrder;
        Reason = Check.Length(reason, nameof(reason), MaxReasonLength);
    }
}
