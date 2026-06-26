using System;
using KnowPulse.KnowledgeBase;
using Shouldly;
using Xunit;

namespace KnowPulse.KnowledgeBase;

public class KnowledgePermissionScopeTests
{
    [Fact]
    public void Should_Clear_Target_For_All_Scope()
    {
        var scope = new KnowledgePermissionScope(
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            KnowledgePermissionScopeType.All,
            Guid.NewGuid());

        scope.ScopeType.ShouldBe(KnowledgePermissionScopeType.All);
        scope.TargetId.ShouldBeNull();
        scope.IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void Should_Require_Target_For_Department_Scope()
    {
        Should.Throw<ArgumentException>(() =>
            new KnowledgePermissionScope(
                Guid.NewGuid(),
                null,
                Guid.NewGuid(),
                KnowledgePermissionScopeType.Department));
    }
}
