using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Organization;

/// <summary>
/// 岗位聚合根，表示部门或组织中的岗位主数据。
/// 岗位编号会进入知识权限范围，用于让岗位维度的知识只对匹配岗位可见。
/// </summary>
public class Position : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    /// <summary>
    /// 岗位编码最大长度。
    /// </summary>
    public const int MaxCodeLength = 64;

    /// <summary>
    /// 岗位名称最大长度。
    /// </summary>
    public const int MaxNameLength = 128;

    /// <summary>
    /// ABP 多租户标识；为空表示宿主侧数据。
    /// </summary>
    public Guid? TenantId { get; protected set; }

    /// <summary>
    /// 岗位所属部门编号。允许为空，表示暂未绑定部门。
    /// </summary>
    public Guid? DepartmentId { get; private set; }

    /// <summary>
    /// 岗位编码。
    /// </summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>
    /// 岗位名称。
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// 岗位安全等级，供知识权限范围和后续岗位能力模型使用。
    /// </summary>
    public int SecurityLevel { get; private set; }

    /// <summary>
    /// 是否启用该岗位。
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// EF Core 反序列化使用的保护构造函数。
    /// </summary>
    protected Position()
    {
    }

    /// <summary>
    /// 创建岗位。
    /// </summary>
    public Position(Guid id, Guid? tenantId, string code, string name, Guid? departmentId = null, int securityLevel = 0)
        : base(id)
    {
        TenantId = tenantId;
        DepartmentId = departmentId;
        Code = Check.NotNullOrWhiteSpace(code, nameof(code), MaxCodeLength);
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), MaxNameLength);
        SetSecurityLevel(securityLevel);
        IsActive = true;
    }

    /// <summary>
    /// 修改岗位名称。
    /// </summary>
    public void Rename(string name)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), MaxNameLength);
    }

    /// <summary>
    /// 修改岗位编码。
    /// </summary>
    public void ChangeCode(string code)
    {
        Code = Check.NotNullOrWhiteSpace(code, nameof(code), MaxCodeLength);
    }

    /// <summary>
    /// 绑定岗位所属部门；传入空值表示暂不归属具体部门。
    /// </summary>
    public void AssignDepartment(Guid? departmentId)
    {
        DepartmentId = departmentId;
    }

    /// <summary>
    /// 设置岗位安全等级。等级不能为负数。
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
    /// 启用或停用岗位。
    /// </summary>
    public void SetActive(bool isActive)
    {
        IsActive = isActive;
    }
}
