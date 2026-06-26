using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.KnowledgeBase;

/// <summary>
/// 知识标签聚合根，用于给知识文档提供横向检索和后续 AI 推荐的轻量标记。
/// MVP-0 只维护标签主数据，知识与标签的多对多关系可在后续版本扩展。
/// </summary>
public class KnowledgeTag : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    /// <summary>
    /// 标签名称最大长度。
    /// </summary>
    public const int MaxNameLength = 64;

    /// <summary>
    /// ABP 多租户标识；为空表示宿主侧数据。
    /// </summary>
    public Guid? TenantId { get; protected set; }

    /// <summary>
    /// 标签名称。
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// 标签是否启用。停用标签不会删除历史引用。
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// EF Core 反序列化使用的保护构造函数。
    /// </summary>
    protected KnowledgeTag()
    {
    }

    /// <summary>
    /// 创建知识标签。
    /// </summary>
    public KnowledgeTag(Guid id, Guid? tenantId, string name)
        : base(id)
    {
        TenantId = tenantId;
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), MaxNameLength);
        IsActive = true;
    }

    /// <summary>
    /// 修改标签名称。
    /// </summary>
    public void Rename(string name)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), MaxNameLength);
    }

    /// <summary>
    /// 启用或停用标签。
    /// </summary>
    public void SetActive(bool isActive)
    {
        IsActive = isActive;
    }
}
