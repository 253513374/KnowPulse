using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Organization;

/// <summary>
/// 部门聚合根，表示企业组织结构中的部门节点。
/// 部门的安全等级会参与知识权限范围判断，是知识可见性的重要组织维度。
/// </summary>
public class Department : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    /// <summary>
    /// 部门编码最大长度。
    /// </summary>
    public const int MaxCodeLength = 64;

    /// <summary>
    /// 部门名称最大长度。
    /// </summary>
    public const int MaxNameLength = 128;

    /// <summary>
    /// ABP 多租户标识；为空表示宿主侧数据。
    /// </summary>
    public Guid? TenantId { get; protected set; }

    /// <summary>
    /// 父级部门编号。为空表示一级部门。
    /// </summary>
    public Guid? ParentId { get; private set; }

    /// <summary>
    /// 部门编码，用于稳定排序、导入和外部系统对齐。
    /// </summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>
    /// 部门名称。
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// 部门安全等级。用户组织关系会保存用户在部门内的有效安全等级。
    /// </summary>
    public int SecurityLevel { get; private set; }

    /// <summary>
    /// 是否启用该部门。
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// EF Core 反序列化使用的保护构造函数。
    /// </summary>
    protected Department()
    {
    }

    /// <summary>
    /// 创建部门。
    /// </summary>
    public Department(Guid id, Guid? tenantId, string code, string name, Guid? parentId = null, int securityLevel = 0)
        : base(id)
    {
        TenantId = tenantId;
        ParentId = parentId;
        Code = Check.NotNullOrWhiteSpace(code, nameof(code), MaxCodeLength);
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), MaxNameLength);
        SetSecurityLevel(securityLevel);
        IsActive = true;
    }

    /// <summary>
    /// 修改部门名称。
    /// </summary>
    public void Rename(string name)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), MaxNameLength);
    }

    /// <summary>
    /// 修改部门编码。
    /// </summary>
    public void ChangeCode(string code)
    {
        Code = Check.NotNullOrWhiteSpace(code, nameof(code), MaxCodeLength);
    }

    /// <summary>
    /// 移动部门到新的父级；传入空值表示移动到根级。
    /// </summary>
    public void MoveTo(Guid? parentId)
    {
        ParentId = parentId;
    }

    /// <summary>
    /// 设置安全等级。等级不能为负数，避免权限判断出现反向含义。
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
    /// 启用或停用部门。
    /// </summary>
    public void SetActive(bool isActive)
    {
        IsActive = isActive;
    }
}
