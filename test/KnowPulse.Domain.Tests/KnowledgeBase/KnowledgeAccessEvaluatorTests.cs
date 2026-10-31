using System;
using System.Collections.Generic;
using Shouldly;
using Xunit;

namespace KnowPulse.KnowledgeBase;

public class KnowledgeAccessEvaluatorTests
{
    [Fact]
    public void Should_Keep_Teacher_Ownership_With_Item_Aware_Three_Argument_Calls()
    {
        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var item = new KnowledgeItem(Guid.NewGuid(), null, "教师资料");
        item.AssignTeacher(ownerId);
        var allScope = new KnowledgePermissionScope(Guid.NewGuid(), null, item.Id, KnowledgePermissionScopeType.All);

        KnowledgeAccessEvaluator.CanAccess(item, new[] { allScope }, new KnowledgeAccessContext(ownerId)).ShouldBeTrue();
        KnowledgeAccessEvaluator.CanAccess(item, new[] { allScope }, new KnowledgeAccessContext(otherUserId)).ShouldBeFalse();
        KnowledgeAccessEvaluator.CanAccess(item, new[] { allScope }, new KnowledgeAccessContext(otherUserId), allowLegacy: true).ShouldBeFalse();

        var version = new KnowledgeVersion(Guid.NewGuid(), null, item.Id, 1, "公开内容");
        item.SetCurrentPublishedVersion(version.Id);
        var userScope = new KnowledgePermissionScope(Guid.NewGuid(), null, item.Id, KnowledgePermissionScopeType.User, otherUserId);

        KnowledgeAccessEvaluator.CanAccess(item, new[] { userScope }, new KnowledgeAccessContext(otherUserId)).ShouldBeTrue();
    }

    [Fact]
    public void Should_Allow_When_No_Enabled_Scopes()
    {
        var context = new KnowledgeAccessContext(Guid.NewGuid());

        KnowledgeAccessEvaluator.CanAccess(Array.Empty<KnowledgePermissionScope>(), context).ShouldBeTrue();
    }

    [Fact]
    public void Should_Match_Department_And_Security_Level()
    {
        var itemId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var context = new KnowledgeAccessContext(
            Guid.NewGuid(),
            new HashSet<Guid> { departmentId },
            securityLevel: 3);

        var scopes = new[]
        {
            new KnowledgePermissionScope(
                Guid.NewGuid(),
                null,
                itemId,
                KnowledgePermissionScopeType.Department,
                departmentId,
                minimumSecurityLevel: 2)
        };

        KnowledgeAccessEvaluator.CanAccess(scopes, context).ShouldBeTrue();
    }

    [Fact]
    public void Should_Deny_When_Security_Level_Is_Too_Low()
    {
        var itemId = Guid.NewGuid();
        var context = new KnowledgeAccessContext(Guid.NewGuid(), securityLevel: 1);

        var scopes = new[]
        {
            new KnowledgePermissionScope(
                Guid.NewGuid(),
                null,
                itemId,
                KnowledgePermissionScopeType.All,
                minimumSecurityLevel: 2)
        };

        KnowledgeAccessEvaluator.CanAccess(scopes, context).ShouldBeFalse();
    }

    [Fact]
    public void Should_Match_User_Scope()
    {
        var userId = Guid.NewGuid();
        var context = new KnowledgeAccessContext(userId);

        var scopes = new[]
        {
            new KnowledgePermissionScope(
                Guid.NewGuid(),
                null,
                Guid.NewGuid(),
                KnowledgePermissionScopeType.User,
                userId)
        };

        KnowledgeAccessEvaluator.CanAccess(scopes, context).ShouldBeTrue();
    }

    [Fact]
    public void Should_Ignore_Disabled_Scopes()
    {
        var scope = new KnowledgePermissionScope(
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            KnowledgePermissionScopeType.User,
            Guid.NewGuid());
        scope.SetEnabled(false);

        KnowledgeAccessEvaluator.CanAccess(new[] { scope }, new KnowledgeAccessContext(Guid.NewGuid())).ShouldBeTrue();
    }
}
