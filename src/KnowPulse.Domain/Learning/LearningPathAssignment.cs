using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Learning;

/// <summary>
/// 学习路径分配规则，把路径配置到全员、部门、岗位或职级。
/// </summary>
public class LearningPathAssignment : FullAuditedEntity<Guid>, IMultiTenant
{
    /// <summary>
    /// ABP 多租户标识；为空表示宿主侧学习路径分配规则。
    /// </summary>
    public Guid? TenantId { get; protected set; }

    /// <summary>
    /// 所属学习路径 ID。
    /// </summary>
    public Guid LearningPathId { get; private set; }

    /// <summary>
    /// 分配范围类型。支持全员、部门、岗位和职级。
    /// </summary>
    public LearningPathAssignmentScopeType ScopeType { get; private set; }

    /// <summary>
    /// 分配目标 ID。全员范围必须为空，其他范围必须指向对应部门、岗位或职级。
    /// </summary>
    public Guid? TargetId { get; private set; }

    /// <summary>
    /// 分配规则是否启用。停用后该规则不参与个人学习路径匹配。
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// EF Core 使用的受保护构造函数。
    /// </summary>
    protected LearningPathAssignment()
    {
    }

    /// <summary>
    /// 创建学习路径分配规则，并立即校验范围类型和目标 ID 的组合是否合法。
    /// </summary>
    /// <param name="id">分配规则 ID。</param>
    /// <param name="tenantId">租户 ID；宿主侧数据为空。</param>
    /// <param name="learningPathId">所属学习路径 ID。</param>
    /// <param name="scopeType">分配范围类型。</param>
    /// <param name="targetId">分配目标 ID；全员范围传空。</param>
    public LearningPathAssignment(
        Guid id,
        Guid? tenantId,
        Guid learningPathId,
        LearningPathAssignmentScopeType scopeType,
        Guid? targetId)
        : base(id)
    {
        TenantId = tenantId;
        LearningPathId = learningPathId;
        SetScope(scopeType, targetId);
        IsActive = true;
    }

    /// <summary>
    /// 设置分配范围。除全员外，部门、岗位和职级范围都必须提供目标 ID。
    /// </summary>
    /// <param name="scopeType">新的分配范围类型。</param>
    /// <param name="targetId">新的分配目标 ID；全员范围会被归一化为空。</param>
    /// <exception cref="BusinessException">非全员范围未提供目标 ID 时抛出。</exception>
    public void SetScope(LearningPathAssignmentScopeType scopeType, Guid? targetId)
    {
        if (scopeType != LearningPathAssignmentScopeType.All && targetId == null)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.LearningPathAssignmentTargetRequired);
        }

        ScopeType = scopeType;
        TargetId = scopeType == LearningPathAssignmentScopeType.All ? null : targetId;
    }

    /// <summary>
    /// 启用或停用当前分配规则。
    /// </summary>
    /// <param name="isActive">是否启用。</param>
    public void SetActive(bool isActive)
    {
        IsActive = isActive;
    }
}
