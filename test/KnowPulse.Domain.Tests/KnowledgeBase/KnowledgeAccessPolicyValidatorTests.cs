using System;
using System.Collections.Generic;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.KnowledgeBase;

public class KnowledgeAccessPolicyValidatorTests
{
    [Fact]
    public void Should_Accept_Explicit_All_Policy()
    {
        Should.NotThrow(() => KnowledgeAccessPolicyValidator.EnsureValid(
        [
            new KnowledgeAccessPolicyRule(KnowledgePermissionScopeType.All, null, null)
        ]));
    }

    [Fact]
    public void Should_Accept_Multiple_Targeted_Rules()
    {
        Should.NotThrow(() => KnowledgeAccessPolicyValidator.EnsureValid(
        [
            new KnowledgeAccessPolicyRule(KnowledgePermissionScopeType.Department, Guid.NewGuid(), null),
            new KnowledgeAccessPolicyRule(KnowledgePermissionScopeType.Role, Guid.NewGuid(), 2)
        ]));
    }

    [Fact]
    public void Should_Require_At_Least_One_Rule()
    {
        var exception = Should.Throw<BusinessException>(() =>
            KnowledgeAccessPolicyValidator.EnsureValid(Array.Empty<KnowledgeAccessPolicyRule>()));

        exception.Code.ShouldBe(KnowPulseDomainErrorCodes.KnowledgeAccessPolicyRequired);
    }

    [Fact]
    public void Should_Not_Combine_All_With_Targeted_Rules()
    {
        var exception = Should.Throw<BusinessException>(() => KnowledgeAccessPolicyValidator.EnsureValid(
        [
            new KnowledgeAccessPolicyRule(KnowledgePermissionScopeType.All, null, null),
            new KnowledgeAccessPolicyRule(KnowledgePermissionScopeType.Department, Guid.NewGuid(), null)
        ]));

        exception.Code.ShouldBe(KnowPulseDomainErrorCodes.KnowledgeAccessPolicyAllCannotBeCombined);
    }

    [Fact]
    public void Should_Reject_Duplicate_Targets()
    {
        var targetId = Guid.NewGuid();
        var exception = Should.Throw<BusinessException>(() => KnowledgeAccessPolicyValidator.EnsureValid(
        [
            new KnowledgeAccessPolicyRule(KnowledgePermissionScopeType.User, targetId, null),
            new KnowledgeAccessPolicyRule(KnowledgePermissionScopeType.User, targetId, 1)
        ]));

        exception.Code.ShouldBe(KnowPulseDomainErrorCodes.KnowledgeAccessPolicyDuplicateScope);
    }

    [Fact]
    public void Should_Require_Target_For_Restricted_Rule()
    {
        var exception = Should.Throw<BusinessException>(() => KnowledgeAccessPolicyValidator.EnsureValid(
        [
            new KnowledgeAccessPolicyRule(KnowledgePermissionScopeType.Position, null, null)
        ]));

        exception.Code.ShouldBe(KnowPulseDomainErrorCodes.KnowledgeAccessPolicyTargetRequired);
    }

    [Fact]
    public void Should_Reject_Negative_Minimum_Security_Level()
    {
        var exception = Should.Throw<BusinessException>(() => KnowledgeAccessPolicyValidator.EnsureValid(
        [
            new KnowledgeAccessPolicyRule(KnowledgePermissionScopeType.All, null, -1)
        ]));

        exception.Code.ShouldBe(KnowPulseDomainErrorCodes.InvalidSecurityLevel);
    }
}
