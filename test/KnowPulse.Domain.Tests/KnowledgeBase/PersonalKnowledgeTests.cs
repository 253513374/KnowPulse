using System;
using System.Linq;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.KnowledgeBase;

public class PersonalKnowledgeTests
{
    [Fact]
    public void Default_Library_Should_Use_Account_Identity_And_Preserve_Tenant()
    {
        var ownerId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var library = new KnowledgeLibrary(ownerId, tenantId);

        library.Id.ShouldBe(ownerId);
        library.OwnerUserId.ShouldBe(ownerId);
        library.TenantId.ShouldBe(tenantId);
        Should.Throw<ArgumentException>(() => new KnowledgeLibrary(Guid.Empty, tenantId));
    }

    [Fact]
    public void Restoring_A_Library_Should_Keep_Its_Identity_And_Owner()
    {
        var ownerId = Guid.NewGuid();
        var library = new KnowledgeLibrary(ownerId, null)
        {
            IsDeleted = true,
            DeleterId = Guid.NewGuid(),
            DeletionTime = DateTime.UtcNow
        };

        library.Restore();

        library.IsDeleted.ShouldBeFalse();
        library.DeleterId.ShouldBeNull();
        library.DeletionTime.ShouldBeNull();
        library.Id.ShouldBe(ownerId);
        library.OwnerUserId.ShouldBe(ownerId);
    }

    [Fact]
    public void Personal_Item_Should_Require_Both_Nonempty_Ownership_Identifiers()
    {
        var ownerId = Guid.NewGuid();
        Should.Throw<ArgumentException>(() => CreateItem(ownerId, null));
        Should.Throw<ArgumentException>(() => CreateItem(null, ownerId));
        Should.Throw<ArgumentException>(() => CreateItem(Guid.Empty, ownerId));
        Should.Throw<ArgumentException>(() => CreateItem(ownerId, Guid.Empty));
        Should.Throw<ArgumentException>(() => CreateItem(Guid.Empty, Guid.Empty));

        var legacy = CreateItem(null, null);
        legacy.LibraryId.ShouldBeNull();
        legacy.OwnerUserId.ShouldBeNull();
    }

    [Fact]
    public void Personal_Owner_Should_Be_The_Only_Reader_Even_With_Empty_Or_All_Scopes()
    {
        var ownerId = Guid.NewGuid();
        var item = CreateItem(ownerId, ownerId);
        var allScope = new KnowledgePermissionScope(Guid.NewGuid(), null, item.Id, KnowledgePermissionScopeType.All);

        foreach (var scopes in new[] { Array.Empty<KnowledgePermissionScope>(), new[] { allScope } })
        {
            KnowledgeAccessEvaluator.CanAccess(item, scopes, new KnowledgeAccessContext(ownerId)).ShouldBeTrue();
            KnowledgeAccessEvaluator.CanAccess(item, scopes, new KnowledgeAccessContext(Guid.NewGuid(), securityLevel: 10)).ShouldBeFalse();
            KnowledgeAccessEvaluator.CanAccess(item, scopes, new KnowledgeAccessContext(null)).ShouldBeFalse();
        }
    }

    [Fact]
    public void Item_Aware_Access_Should_Preserve_Legacy_Organization_Rules()
    {
        var item = CreateItem(null, null);
        var permittedUser = Guid.NewGuid();
        var scope = new KnowledgePermissionScope(
            Guid.NewGuid(), null, item.Id, KnowledgePermissionScopeType.User, permittedUser);

        KnowledgeAccessEvaluator.CanAccess(item, Array.Empty<KnowledgePermissionScope>(), new KnowledgeAccessContext(Guid.NewGuid())).ShouldBeTrue();
        KnowledgeAccessEvaluator.CanAccess(item, new[] { scope }, new KnowledgeAccessContext(permittedUser)).ShouldBeTrue();
        KnowledgeAccessEvaluator.CanAccess(item, new[] { scope }, new KnowledgeAccessContext(Guid.NewGuid())).ShouldBeFalse();
    }

    [Fact]
    public void Updating_Personal_Metadata_Should_Not_Change_Ownership()
    {
        var ownerId = Guid.NewGuid();
        var item = CreateItem(ownerId, ownerId);

        item.Rename("Revised title");
        item.ChangeSummary("Revised summary");
        item.ChangeCategory(Guid.NewGuid());
        item.SetActive(false);

        item.LibraryId.ShouldBe(ownerId);
        item.OwnerUserId.ShouldBe(ownerId);
    }

    [Fact]
    public void Personal_Preparation_Should_Require_An_Owner_Only_Policy()
    {
        var ownerId = Guid.NewGuid();
        var item = CreateItem(ownerId, ownerId);
        KnowledgeAccessPolicyValidator.EnsureValidForItem(item,
            [new KnowledgeAccessPolicyRule(KnowledgePermissionScopeType.User, ownerId, null)]);

        foreach (var rule in new[]
        {
            new KnowledgeAccessPolicyRule(KnowledgePermissionScopeType.All, null, null),
            new KnowledgeAccessPolicyRule(KnowledgePermissionScopeType.User, Guid.NewGuid(), null),
            new KnowledgeAccessPolicyRule(KnowledgePermissionScopeType.User, ownerId, 1)
        })
        {
            Should.Throw<BusinessException>(() => KnowledgeAccessPolicyValidator.EnsureValidForItem(item, [rule]))
                .Code.ShouldBe(KnowPulseDomainErrorCodes.PersonalKnowledgeRequiresOwnerPolicy);
        }

        KnowledgeAccessPolicyValidator.EnsureValidForItem(CreateItem(null, null),
            [new KnowledgeAccessPolicyRule(KnowledgePermissionScopeType.All, null, null)]);
    }

    [Fact]
    public void Organization_Should_Normalize_Tags_Without_Changing_Ownership_Or_Version()
    {
        var owner = Guid.NewGuid();
        var item = CreateItem(owner, owner);
        var version = Guid.NewGuid();
        item.SetCurrentPublishedVersion(version);
        item.MaterialType.ShouldBe(PersonalMaterialType.Unspecified);
        item.MaterialYear.ShouldBeNull();
        item.GetPersonalTags().ShouldBeEmpty();
        item.OrganizePersonalMaterial(PersonalMaterialType.LessonPlan, 2018, " 数学 ", new[] { " 重点 ", "", "重点", "重点,典型" });
        item.MaterialType.ShouldBe(PersonalMaterialType.LessonPlan);
        item.MaterialYear.ShouldBe(2018);
        item.SubjectName.ShouldBe("数学");
        item.GetPersonalTags().ShouldBe(new[] { "重点", "重点,典型" });
        item.PersonalTags.ShouldBe("\n重点\n重点,典型\n");
        item.OwnerUserId.ShouldBe(owner);
        item.LibraryId.ShouldBe(owner);
        item.CurrentPublishedVersionId.ShouldBe(version);
    }

    [Theory]
    [InlineData(99, 2018, "数学", "重点")]
    [InlineData(1, 0, "数学", "重点")]
    [InlineData(1, 10000, "数学", "重点")]
    [InlineData(1, 2018, "数学\n英语", "重点")]
    [InlineData(1, 2018, "数学", "重点\n扩展")]
    public void Invalid_Organization_Should_Leave_Previous_Fields_Intact(int type, int year, string subject, string tag)
    {
        var owner = Guid.NewGuid();
        var item = CreateItem(owner, owner);
        item.OrganizePersonalMaterial(PersonalMaterialType.Note, 2010, "语文", new[] { "原标签" });
        Should.Throw<BusinessException>(() => item.OrganizePersonalMaterial((PersonalMaterialType)type, year, subject, new[] { tag }))
            .Code.ShouldBe(PersonalKnowledgeOrganizationErrorCodes.InvalidOrganization);
        item.MaterialType.ShouldBe(PersonalMaterialType.Note);
        item.MaterialYear.ShouldBe(2010);
        item.SubjectName.ShouldBe("语文");
        item.GetPersonalTags().ShouldBe(new[] { "原标签" });
    }

    [Fact]
    public void Organization_Should_Reject_Overlong_Or_Too_Many_Tags_And_Organization_Items()
    {
        var owner = Guid.NewGuid();
        var item = CreateItem(owner, owner);
        Should.Throw<BusinessException>(() => item.OrganizePersonalMaterial(PersonalMaterialType.Note, null, new string('a', 129), null));
        Should.Throw<BusinessException>(() => item.OrganizePersonalMaterial(PersonalMaterialType.Note, null, null, new[] { new string('a', 65) }));
        Should.Throw<BusinessException>(() => item.OrganizePersonalMaterial(PersonalMaterialType.Note, null, null,
            Enumerable.Range(0, 21).Select(x => x.ToString())));
        Should.Throw<BusinessException>(() => CreateItem(null, null).OrganizePersonalMaterial(PersonalMaterialType.Note, null, null, null));
        item.OrganizePersonalMaterial(PersonalMaterialType.Unspecified, null, " ", Array.Empty<string>());
        item.SubjectName.ShouldBeNull();
        item.PersonalTags.ShouldBeNull();
    }

    private static KnowledgeItem CreateItem(Guid? libraryId, Guid? ownerUserId)
    {
        return new KnowledgeItem(Guid.NewGuid(), null, "Personal note", libraryId: libraryId, ownerUserId: ownerUserId);
    }
}
