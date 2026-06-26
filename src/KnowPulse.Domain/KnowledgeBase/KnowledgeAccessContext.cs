using System;
using System.Collections.Generic;

namespace KnowPulse.KnowledgeBase;

/// <summary>
/// 知识访问上下文，聚合当前访问人的用户编号、组织关系、角色和安全等级。
/// 应用服务负责从身份系统与组织关系表组装该对象，领域层只依赖这个轻量模型做权限判断。
/// </summary>
public class KnowledgeAccessContext
{
    /// <summary>
    /// 当前用户编号。匿名或无法识别用户时为空。
    /// </summary>
    public Guid? UserId { get; }

    /// <summary>
    /// 当前用户所属部门集合。一个用户可以拥有多个组织关系。
    /// </summary>
    public IReadOnlySet<Guid> DepartmentIds { get; }

    /// <summary>
    /// 当前用户关联岗位集合。
    /// </summary>
    public IReadOnlySet<Guid> PositionIds { get; }

    /// <summary>
    /// 当前用户拥有的角色编号集合。
    /// </summary>
    public IReadOnlySet<Guid> RoleIds { get; }

    /// <summary>
    /// 当前用户最高安全等级。没有组织关系时默认为 0。
    /// </summary>
    public int SecurityLevel { get; }

    /// <summary>
    /// 创建知识访问上下文。集合参数为空时自动转为空集合，避免权限判断时出现空引用。
    /// </summary>
    public KnowledgeAccessContext(
        Guid? userId,
        IReadOnlySet<Guid>? departmentIds = null,
        IReadOnlySet<Guid>? positionIds = null,
        IReadOnlySet<Guid>? roleIds = null,
        int securityLevel = 0)
    {
        UserId = userId;
        DepartmentIds = departmentIds ?? new HashSet<Guid>();
        PositionIds = positionIds ?? new HashSet<Guid>();
        RoleIds = roleIds ?? new HashSet<Guid>();
        SecurityLevel = securityLevel;
    }
}
