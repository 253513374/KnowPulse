using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Learning;

/// <summary>
/// 用户在某条学习路径下的知识阅读记录。
/// </summary>
public class LearningKnowledgeReadRecord : FullAuditedEntity<Guid>, IMultiTenant
{
    /// <summary>
    /// ABP 多租户标识；为空表示宿主侧阅读记录。
    /// </summary>
    public Guid? TenantId { get; protected set; }

    /// <summary>
    /// 所属学习路径 ID。
    /// </summary>
    public Guid LearningPathId { get; private set; }

    /// <summary>
    /// 被阅读的知识条目 ID。
    /// </summary>
    public Guid KnowledgeItemId { get; private set; }

    /// <summary>
    /// 被阅读的知识版本 ID，固定到学习路径配置时的发布版本。
    /// </summary>
    public Guid KnowledgeVersionId { get; private set; }

    /// <summary>
    /// 阅读用户 ID。
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// 最近一次从学习中心打开或标记该知识的时间。
    /// </summary>
    public DateTime LastReadTime { get; private set; }

    /// <summary>
    /// 用户标记完成时间；为空表示只阅读过但未完成。
    /// </summary>
    public DateTime? CompletedTime { get; private set; }

    /// <summary>
    /// 阅读次数。每次打开阅读或标记完成都会递增。
    /// </summary>
    public int ReadCount { get; private set; }

    /// <summary>
    /// EF Core 使用的受保护构造函数。
    /// </summary>
    protected LearningKnowledgeReadRecord()
    {
    }

    /// <summary>
    /// 创建学习知识阅读记录，并记录第一次阅读行为。
    /// </summary>
    /// <param name="id">阅读记录 ID。</param>
    /// <param name="tenantId">租户 ID；宿主侧数据为空。</param>
    /// <param name="learningPathId">所属学习路径 ID。</param>
    /// <param name="knowledgeItemId">知识条目 ID。</param>
    /// <param name="knowledgeVersionId">知识版本 ID。</param>
    /// <param name="userId">阅读用户 ID。</param>
    /// <param name="readTime">阅读发生时间。</param>
    /// <param name="markCompleted">是否同时标记完成。</param>
    public LearningKnowledgeReadRecord(
        Guid id,
        Guid? tenantId,
        Guid learningPathId,
        Guid knowledgeItemId,
        Guid knowledgeVersionId,
        Guid userId,
        DateTime readTime,
        bool markCompleted)
        : base(id)
    {
        TenantId = tenantId;
        LearningPathId = learningPathId;
        KnowledgeItemId = knowledgeItemId;
        KnowledgeVersionId = knowledgeVersionId;
        UserId = userId;
        ReadCount = 0;
        RecordRead(readTime, markCompleted);
    }

    /// <summary>
    /// 记录一次阅读行为，并在需要时更新完成时间。
    /// </summary>
    /// <param name="readTime">阅读发生时间。</param>
    /// <param name="markCompleted">是否将该知识标记为完成。</param>
    public void RecordRead(DateTime readTime, bool markCompleted)
    {
        LastReadTime = readTime;
        ReadCount++;
        if (markCompleted)
        {
            CompletedTime = readTime;
        }
    }
}
