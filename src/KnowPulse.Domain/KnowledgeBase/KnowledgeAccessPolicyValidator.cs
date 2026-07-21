using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;

namespace KnowPulse.KnowledgeBase;

/// <summary>
/// 发布时访问策略的领域校验器。
/// 策略必须显式存在，并且只能选择“全员”或一个以上定向范围中的一种模式。
/// </summary>
public static class KnowledgeAccessPolicyValidator
{
    public static void EnsureValid(IEnumerable<KnowledgeAccessPolicyRule> rules)
    {
        var policy = rules.ToList();
        if (policy.Count == 0)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.KnowledgeAccessPolicyRequired);
        }

        if (policy.Any(x => x.ScopeType == KnowledgePermissionScopeType.All) && policy.Count > 1)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.KnowledgeAccessPolicyAllCannotBeCombined);
        }

        if (policy.Any(x => x.ScopeType != KnowledgePermissionScopeType.All && !x.TargetId.HasValue))
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.KnowledgeAccessPolicyTargetRequired);
        }

        if (policy.Any(x => x.MinimumSecurityLevel < 0))
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.InvalidSecurityLevel);
        }

        var hasDuplicate = policy
            .GroupBy(x => new { x.ScopeType, x.TargetId })
            .Any(x => x.Count() > 1);
        if (hasDuplicate)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.KnowledgeAccessPolicyDuplicateScope);
        }
    }
}

/// <summary>
/// 与持久化实体无关的访问策略规则值，用于在写入前完成整组校验。
/// </summary>
public sealed record KnowledgeAccessPolicyRule(
    KnowledgePermissionScopeType ScopeType,
    Guid? TargetId,
    int? MinimumSecurityLevel);
