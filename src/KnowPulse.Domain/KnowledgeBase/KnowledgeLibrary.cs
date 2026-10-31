using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.KnowledgeBase;

/// <summary>
/// 账号的默认个人知识库。身份用户编号同时作为库编号，归属不随资料创建者或学校身份变化。
/// </summary>
public class KnowledgeLibrary : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public const int MaxNameLength = 128;
    public const string DefaultName = "个人知识库";

    public Guid? TenantId { get; protected set; }

    public Guid OwnerUserId { get; private set; }

    public string Name { get; private set; } = DefaultName;

    protected KnowledgeLibrary()
    {
    }

    public KnowledgeLibrary(Guid ownerUserId, Guid? tenantId)
        : base(ownerUserId)
    {
        if (ownerUserId == Guid.Empty)
        {
            throw new ArgumentException("A personal library requires an identified owner.", nameof(ownerUserId));
        }

        TenantId = tenantId;
        OwnerUserId = ownerUserId;
    }

    /// <summary>
    /// 恢复同一默认库，不另建编号，也不改变资料归属。
    /// </summary>
    public void Restore()
    {
        IsDeleted = false;
        DeleterId = null;
        DeletionTime = null;
    }
}
