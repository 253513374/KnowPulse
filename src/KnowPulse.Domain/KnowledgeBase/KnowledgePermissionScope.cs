using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.KnowledgeBase;

/// <summary>
/// 知识权限范围聚合根，描述某个知识文档允许哪些组织、岗位、角色、用户或全员访问。
/// 该实体只表达授权规则本身，真正的访问判断由 <see cref="KnowledgeAccessEvaluator"/> 基于当前用户上下文完成。
/// </summary>
public class KnowledgePermissionScope : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    /// <summary>
    /// ABP 多租户标识；为空表示宿主侧数据。
    /// </summary>
    public Guid? TenantId { get; protected set; }

    /// <summary>
    /// 授权规则所属知识文档编号。
    /// </summary>
    public Guid KnowledgeItemId { get; private set; }

    /// <summary>
    /// 授权范围类型，例如全员、部门、岗位、角色或指定用户。
    /// </summary>
    public KnowledgePermissionScopeType ScopeType { get; private set; }

    /// <summary>
    /// 授权目标编号。全员范围不需要目标编号，其他范围必须指定目标。
    /// </summary>
    public Guid? TargetId { get; private set; }

    /// <summary>
    /// 最低安全等级要求。为空表示只匹配范围，不额外限制安全等级。
    /// </summary>
    public int? MinimumSecurityLevel { get; private set; }

    /// <summary>
    /// 规则是否启用。停用规则会被访问判定忽略，但仍保留审计记录。
    /// </summary>
    public bool IsEnabled { get; private set; }

    /// <summary>
    /// EF Core 反序列化使用的保护构造函数。
    /// </summary>
    protected KnowledgePermissionScope()
    {
    }

    /// <summary>
    /// 创建知识权限范围，并立即校验范围类型、目标编号和安全等级。
    /// </summary>
    /// <param name="id">权限范围编号。</param>
    /// <param name="tenantId">所属租户编号。</param>
    /// <param name="knowledgeItemId">所属知识文档编号。</param>
    /// <param name="scopeType">授权范围类型。</param>
    /// <param name="targetId">授权目标编号；非全员范围必填。</param>
    /// <param name="minimumSecurityLevel">最低安全等级。</param>
    public KnowledgePermissionScope(
        Guid id,
        Guid? tenantId,
        Guid knowledgeItemId,
        KnowledgePermissionScopeType scopeType,
        Guid? targetId = null,
        int? minimumSecurityLevel = null)
        : base(id)
    {
        TenantId = tenantId;
        KnowledgeItemId = knowledgeItemId;
        SetScope(scopeType, targetId);
        SetMinimumSecurityLevel(minimumSecurityLevel);
        IsEnabled = true;
    }

    /// <summary>
    /// 设置授权范围。非全员范围必须带目标编号，全员范围会强制清空目标编号。
    /// </summary>
    public void SetScope(KnowledgePermissionScopeType scopeType, Guid? targetId)
    {
        if (scopeType != KnowledgePermissionScopeType.All && targetId == null)
        {
            throw new ArgumentException("TargetId is required for non-All permission scopes.", nameof(targetId));
        }

        ScopeType = scopeType;
        TargetId = scopeType == KnowledgePermissionScopeType.All ? null : targetId;
    }

    /// <summary>
    /// 设置最低安全等级。安全等级不允许为负数，避免错误配置导致越权或不可达。
    /// </summary>
    public void SetMinimumSecurityLevel(int? minimumSecurityLevel)
    {
        if (minimumSecurityLevel < 0)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.InvalidSecurityLevel);
        }

        MinimumSecurityLevel = minimumSecurityLevel;
    }

    /// <summary>
    /// 启用或停用该授权规则。
    /// </summary>
    public void SetEnabled(bool isEnabled)
    {
        IsEnabled = isEnabled;
    }
}
