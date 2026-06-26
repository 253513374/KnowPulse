using System.Collections.Generic;
using System.Linq;

namespace KnowPulse.KnowledgeBase;

/// <summary>
/// 知识访问判定器，集中实现权限范围和当前用户上下文之间的匹配规则。
/// 该类不访问数据库，只对已经加载出来的授权范围进行纯内存判断，便于在领域测试中覆盖边界条件。
/// </summary>
public static class KnowledgeAccessEvaluator
{
    /// <summary>
    /// 判断当前上下文是否可以访问某个知识文档。
    /// 没有任何启用范围时按 MVP-0 规则视为默认可见；一旦配置了启用范围，则至少命中一条规则才可见。
    /// </summary>
    /// <param name="scopes">知识文档的授权范围集合。</param>
    /// <param name="context">当前访问人的组织、岗位、角色、用户和安全等级上下文。</param>
    /// <returns>可以访问返回 true，否则返回 false。</returns>
    public static bool CanAccess(IEnumerable<KnowledgePermissionScope> scopes, KnowledgeAccessContext context)
    {
        var enabledScopes = scopes.Where(x => x.IsEnabled).ToList();
        if (enabledScopes.Count == 0)
        {
            return true;
        }

        return enabledScopes.Any(scope => CanAccess(scope, context));
    }

    /// <summary>
    /// 判断单条授权范围是否命中当前访问上下文。
    /// 安全等级是所有范围类型的前置门槛，只有达到最低安全等级后才继续匹配范围目标。
    /// </summary>
    private static bool CanAccess(KnowledgePermissionScope scope, KnowledgeAccessContext context)
    {
        if (scope.MinimumSecurityLevel.HasValue && context.SecurityLevel < scope.MinimumSecurityLevel.Value)
        {
            return false;
        }

        return scope.ScopeType switch
        {
            KnowledgePermissionScopeType.All => true,
            KnowledgePermissionScopeType.Department => scope.TargetId.HasValue && context.DepartmentIds.Contains(scope.TargetId.Value),
            KnowledgePermissionScopeType.Position => scope.TargetId.HasValue && context.PositionIds.Contains(scope.TargetId.Value),
            KnowledgePermissionScopeType.Role => scope.TargetId.HasValue && context.RoleIds.Contains(scope.TargetId.Value),
            KnowledgePermissionScopeType.User => scope.TargetId.HasValue && context.UserId == scope.TargetId.Value,
            _ => false
        };
    }
}
