using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Organization;

/// <summary>
/// 用户组织关系聚合根，记录用户与部门、岗位、职级之间的关联。
/// 知识权限判断会从该实体提取用户的部门、岗位和最高安全等级。
/// </summary>
public class UserOrganizationAssignment : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    /// <summary>
    /// ABP 多租户标识；为空表示宿主侧数据。
    /// </summary>
    public Guid? TenantId { get; protected set; }

    /// <summary>
    /// 用户编号，对应 ABP Identity 用户。
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// 用户所属部门编号。
    /// </summary>
    public Guid DepartmentId { get; private set; }

    /// <summary>
    /// 用户岗位编号。允许为空，表示只绑定部门。
    /// </summary>
    public Guid? PositionId { get; private set; }

    /// <summary>
    /// 用户职级编号。允许为空，表示暂未维护职级。
    /// </summary>
    public Guid? JobLevelId { get; private set; }

    /// <summary>
    /// 用户在该组织关系下的安全等级。权限上下文会取所有有效组织关系中的最高值。
    /// </summary>
    public int SecurityLevel { get; private set; }

    /// <summary>
    /// 是否为用户的主组织关系。应用服务会保证同一用户最多一个有效主关系。
    /// </summary>
    public bool IsPrimary { get; private set; }

    /// <summary>
    /// 该组织关系是否启用。停用后不参与知识权限上下文。
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// EF Core 反序列化使用的保护构造函数。
    /// </summary>
    protected UserOrganizationAssignment()
    {
    }

    /// <summary>
    /// 创建用户组织关系。
    /// </summary>
    /// <param name="id">关系编号。</param>
    /// <param name="tenantId">所属租户编号。</param>
    /// <param name="userId">用户编号。</param>
    /// <param name="departmentId">部门编号。</param>
    /// <param name="positionId">可选岗位编号。</param>
    /// <param name="jobLevelId">可选职级编号。</param>
    /// <param name="securityLevel">安全等级，不能为负数。</param>
    /// <param name="isPrimary">是否主组织关系。</param>
    public UserOrganizationAssignment(
        Guid id,
        Guid? tenantId,
        Guid userId,
        Guid departmentId,
        Guid? positionId = null,
        Guid? jobLevelId = null,
        int securityLevel = 0,
        bool isPrimary = false)
        : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        DepartmentId = departmentId;
        PositionId = positionId;
        JobLevelId = jobLevelId;
        SetSecurityLevel(securityLevel);
        IsPrimary = isPrimary;
        IsActive = true;
    }

    /// <summary>
    /// 修改部门、岗位和职级关联。
    /// </summary>
    public void ChangeOrganization(Guid departmentId, Guid? positionId, Guid? jobLevelId)
    {
        DepartmentId = departmentId;
        PositionId = positionId;
        JobLevelId = jobLevelId;
    }

    /// <summary>
    /// 修改关联用户。应用服务会先校验目标用户存在。
    /// </summary>
    public void ChangeUser(Guid userId)
    {
        UserId = userId;
    }

    /// <summary>
    /// 设置安全等级。等级不能为负数。
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
    /// 设置是否主组织关系。唯一性由应用服务在保存前清理其他主关系。
    /// </summary>
    public void SetPrimary(bool isPrimary)
    {
        IsPrimary = isPrimary;
    }

    /// <summary>
    /// 启用或停用该组织关系。
    /// </summary>
    public void SetActive(bool isActive)
    {
        IsActive = isActive;
    }
}
