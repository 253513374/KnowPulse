using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Organization;

/// <summary>
/// 职级聚合根，表示组织中的层级或等级主数据。
/// 职级本身不直接决定知识权限，但会随用户组织关系保存，用于后续学习路径、考试和能力模型。
/// </summary>
public class JobLevel : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    /// <summary>
    /// 职级编码最大长度。
    /// </summary>
    public const int MaxCodeLength = 64;

    /// <summary>
    /// 职级名称最大长度。
    /// </summary>
    public const int MaxNameLength = 128;

    /// <summary>
    /// ABP 多租户标识；为空表示宿主侧数据。
    /// </summary>
    public Guid? TenantId { get; protected set; }

    /// <summary>
    /// 职级编码。
    /// </summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>
    /// 职级名称。
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// 排序号，数值越小越靠前。
    /// </summary>
    public int SortOrder { get; private set; }

    /// <summary>
    /// 职级安全等级。当前主要随用户组织关系进入权限上下文。
    /// </summary>
    public int SecurityLevel { get; private set; }

    /// <summary>
    /// 是否启用该职级。
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// EF Core 反序列化使用的保护构造函数。
    /// </summary>
    protected JobLevel()
    {
    }

    /// <summary>
    /// 创建职级。
    /// </summary>
    public JobLevel(Guid id, Guid? tenantId, string code, string name, int sortOrder = 0, int securityLevel = 0)
        : base(id)
    {
        TenantId = tenantId;
        Code = Check.NotNullOrWhiteSpace(code, nameof(code), MaxCodeLength);
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), MaxNameLength);
        SortOrder = sortOrder;
        SetSecurityLevel(securityLevel);
        IsActive = true;
    }

    /// <summary>
    /// 修改职级名称。
    /// </summary>
    public void Rename(string name)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), MaxNameLength);
    }

    /// <summary>
    /// 修改职级编码。
    /// </summary>
    public void ChangeCode(string code)
    {
        Code = Check.NotNullOrWhiteSpace(code, nameof(code), MaxCodeLength);
    }

    /// <summary>
    /// 设置排序号。
    /// </summary>
    public void SetSortOrder(int sortOrder)
    {
        SortOrder = sortOrder;
    }

    /// <summary>
    /// 设置职级安全等级。等级不能为负数。
    /// </summary>
    public void SetSecurityLevel(int securityLevel)
    {
        if (securityLevel < 0)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.InvalidSecurityLevel);
        }

        SecurityLevel = securityLevel;
    }

    /// <summary>
    /// 启用或停用职级。
    /// </summary>
    public void SetActive(bool isActive)
    {
        IsActive = isActive;
    }
}
