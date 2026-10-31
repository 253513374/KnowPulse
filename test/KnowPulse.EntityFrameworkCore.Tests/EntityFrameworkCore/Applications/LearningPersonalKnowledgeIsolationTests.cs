using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.KnowledgeBase;
using KnowPulse.Learning;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Security.Claims;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class LearningPersonalKnowledgeIsolationTests : KnowPulseEntityFrameworkCoreTestBase
{
    private static readonly Guid OwnerUserId = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");
    private static readonly Guid OtherUserId = Guid.Parse("ecf23c6d-4d3d-4fa2-87e3-a910d79927cf");

    private readonly ILearningPathAppService _service;
    private readonly IRepository<KnowledgeLibrary, Guid> _libraries;
    private readonly IRepository<KnowledgeItem, Guid> _items;
    private readonly IRepository<KnowledgeVersion, Guid> _versions;
    private readonly IRepository<LearningPath, Guid> _paths;
    private readonly IRepository<LearningKnowledgeReadRecord, Guid> _reads;
    private readonly ICurrentPrincipalAccessor _principalAccessor;

    public LearningPersonalKnowledgeIsolationTests()
    {
        _service = GetRequiredService<ILearningPathAppService>();
        _libraries = GetRequiredService<IRepository<KnowledgeLibrary, Guid>>();
        _items = GetRequiredService<IRepository<KnowledgeItem, Guid>>();
        _versions = GetRequiredService<IRepository<KnowledgeVersion, Guid>>();
        _paths = GetRequiredService<IRepository<LearningPath, Guid>>();
        _reads = GetRequiredService<IRepository<LearningKnowledgeReadRecord, Guid>>();
        _principalAccessor = GetRequiredService<ICurrentPrincipalAccessor>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Personal_Content_Should_Remain_Owner_Only_In_All_Learning_Views_And_Read_Records(bool allScope)
    {
        var knowledge = await InsertKnowledgeAsync(OwnerUserId, allScope);
        var input = PathInput($"learning-isolation-{Guid.NewGuid():N}", knowledge);
        input.RecommendedKnowledge.Add(new LearningPathKnowledgeInputDto
        {
            KnowledgeItemId = knowledge.Item.Id,
            Reason = "Private recommendation"
        });
        var path = await _service.CreateAsync(input);
        path.RequiredKnowledge.Single().KnowledgeTitle.ShouldBe(knowledge.Item.Title);
        path.RecommendedKnowledge.Single().VersionTitle.ShouldBe(knowledge.Version.Title);

        var completed = await _service.MarkKnowledgeReadAsync(ReadInput(path.Id, knowledge));
        completed.RequiredKnowledge.Single().CompletionStatus.ShouldBe(LearningContentCompletionStatus.Completed);
        completed.RecommendedKnowledge.Single().ReadCount.ShouldBe(1);

        using (ChangeUser(OtherUserId))
        {
            AssertNoPersonalContent(await _service.GetAsync(path.Id), knowledge);
            AssertNoPersonalContent((await _service.GetListAsync(new GetLearningPathListInput { Keyword = path.Name })).Items.Single(), knowledge);
            AssertNoPersonalContent(await _service.GetMyAsync(path.Id), knowledge);
            AssertNoPersonalContent((await _service.GetMyListAsync(new GetLearningPathListInput { Keyword = path.Name })).Items.Single(), knowledge);
            await Should.ThrowAsync<AbpAuthorizationException>(() => _service.MarkKnowledgeReadAsync(ReadInput(path.Id, knowledge)));
        }

        (await WithUnitOfWorkAsync(() =>
            _reads.CountAsync(x => x.LearningPathId == path.Id && x.UserId == OtherUserId))).ShouldBe(0);
        var mine = await _service.GetMyAsync(path.Id);
        mine.RequiredKnowledge.Single().ReadCount.ShouldBe(1);
        mine.RequiredKnowledge.Single().KnowledgeVersionId.ShouldBe(knowledge.Version.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Management_Should_Reject_Foreign_Personal_References_Before_Creating_Or_Replacing_Configuration(bool recommended)
    {
        var privateKnowledge = await InsertKnowledgeAsync(OtherUserId, allScope: true);
        var legacyKnowledge = await InsertKnowledgeAsync(ownerUserId: null);
        var invalid = PathInput($"rejected-learning-{Guid.NewGuid():N}", legacyKnowledge);
        var privateInput = new LearningPathKnowledgeInputDto { KnowledgeItemId = privateKnowledge.Item.Id };
        if (recommended)
        {
            invalid.RecommendedKnowledge.Add(privateInput);
        }
        else
        {
            invalid.RequiredKnowledge.Add(privateInput);
        }

        await Should.ThrowAsync<AbpAuthorizationException>(() => _service.CreateAsync(invalid));
        (await WithUnitOfWorkAsync(() => _paths.CountAsync(x => x.Name == invalid.Name))).ShouldBe(0);

        var existing = await _service.CreateAsync(PathInput($"unchanged-learning-{Guid.NewGuid():N}", legacyKnowledge));
        await Should.ThrowAsync<AbpAuthorizationException>(() => _service.UpdateAsync(existing.Id, invalid));
        var unchanged = await _service.GetAsync(existing.Id);
        unchanged.Name.ShouldBe(existing.Name);
        unchanged.ConfigVersion.ShouldBe(existing.ConfigVersion);
        unchanged.RequiredKnowledge.Single().KnowledgeItemId.ShouldBe(legacyKnowledge.Item.Id);
        unchanged.RecommendedKnowledge.ShouldBeEmpty();
        unchanged.Assignments.Single().ScopeType.ShouldBe(LearningPathAssignmentScopeType.All);
    }

    [Fact]
    public async Task Mismatched_Item_And_Private_Version_Should_Not_Expose_Version_Title_Or_Create_Read_Record()
    {
        var legacyKnowledge = await InsertKnowledgeAsync(ownerUserId: null);
        var privateKnowledge = await InsertKnowledgeAsync(OtherUserId);
        var path = await _service.CreateAsync(PathInput($"mismatched-learning-{Guid.NewGuid():N}", legacyKnowledge));
        await GetRequiredService<IRepository<LearningPathRecommendedKnowledge, Guid>>().InsertAsync(
            new LearningPathRecommendedKnowledge(Guid.NewGuid(), null, path.Id, legacyKnowledge.Item.Id,
                privateKnowledge.Version.Id, 1, "Do not expose this private version"), autoSave: true);

        (await _service.GetAsync(path.Id)).RecommendedKnowledge.ShouldBeEmpty();
        var mine = await _service.GetMyAsync(path.Id);
        mine.RecommendedKnowledge.ShouldBeEmpty();
        mine.RequiredKnowledge.Single().KnowledgeItemId.ShouldBe(legacyKnowledge.Item.Id);
        JsonSerializer.Serialize(mine).ShouldNotContain(privateKnowledge.Version.Title);
        await Should.ThrowAsync<AbpAuthorizationException>(() => _service.MarkKnowledgeReadAsync(new MarkLearningKnowledgeReadDto
        {
            LearningPathId = path.Id,
            KnowledgeItemId = legacyKnowledge.Item.Id,
            KnowledgeVersionId = privateKnowledge.Version.Id,
            MarkCompleted = true
        }));
        (await WithUnitOfWorkAsync(() => _reads.CountAsync(x => x.LearningPathId == path.Id))).ShouldBe(0);
    }

    [Fact]
    public async Task Database_Should_Reject_Personal_Item_With_Conflicting_Library_Owner()
    {
        var library = await WithUnitOfWorkAsync(async () =>
            await _libraries.FindAsync(OtherUserId) ??
            await _libraries.InsertAsync(new KnowledgeLibrary(OtherUserId, null), autoSave: true));
        var itemId = Guid.NewGuid();

        var exception = await Should.ThrowAsync<DbUpdateException>(() => WithUnitOfWorkAsync(() =>
            _items.InsertAsync(new KnowledgeItem(itemId, null, "Invalid library ownership",
                libraryId: library.Id, ownerUserId: OwnerUserId), autoSave: true)));

        exception.InnerException.ShouldBeOfType<SqliteException>().SqliteExtendedErrorCode.ShouldBe(787);
        (await WithUnitOfWorkAsync(() => _items.FindAsync(itemId))).ShouldBeNull();
    }

    [Fact]
    public async Task Legacy_Organizational_Management_Should_Remain_Available_Without_Granting_Personal_Reading_Permission()
    {
        var knowledge = await InsertKnowledgeAsync(ownerUserId: null);
        await GetRequiredService<IRepository<KnowledgePermissionScope, Guid>>().InsertAsync(
            new KnowledgePermissionScope(Guid.NewGuid(), null, knowledge.Item.Id, KnowledgePermissionScopeType.User, OtherUserId),
            autoSave: true);
        var path = await _service.CreateAsync(PathInput($"legacy-learning-{Guid.NewGuid():N}", knowledge));
        path.RequiredKnowledge.Single().KnowledgeTitle.ShouldBe(knowledge.Item.Title);
        (await _service.GetAsync(path.Id)).RequiredKnowledge.Single().KnowledgeItemId.ShouldBe(knowledge.Item.Id);
        (await _service.GetMyAsync(path.Id)).RequiredKnowledge.ShouldBeEmpty();
        await Should.ThrowAsync<AbpAuthorizationException>(() => _service.MarkKnowledgeReadAsync(ReadInput(path.Id, knowledge)));
    }

    private async Task<KnowledgeFixture> InsertKnowledgeAsync(Guid? ownerUserId, bool allScope = false)
    {
        KnowledgeLibrary? library = null;
        if (ownerUserId.HasValue)
        {
            var libraryOwner = ownerUserId.Value;
            library = await _libraries.FindAsync(libraryOwner);
            library ??= await _libraries.InsertAsync(new KnowledgeLibrary(libraryOwner, null), autoSave: true);
        }

        var title = $"knowledge-{Guid.NewGuid():N}";
        var item = await _items.InsertAsync(new KnowledgeItem(Guid.NewGuid(), null, title,
            libraryId: library?.Id, ownerUserId: ownerUserId), autoSave: true);
        var version = new KnowledgeVersion(Guid.NewGuid(), null, item.Id, 1, $"{title} version", "Private study material");
        version.Publish(DateTime.UtcNow);
        await _versions.InsertAsync(version, autoSave: true);
        item.SetCurrentPublishedVersion(version.Id);
        await _items.UpdateAsync(item, autoSave: true);
        if (allScope)
        {
            await GetRequiredService<IRepository<KnowledgePermissionScope, Guid>>().InsertAsync(
                new KnowledgePermissionScope(Guid.NewGuid(), null, item.Id, KnowledgePermissionScopeType.All), autoSave: true);
        }

        return new KnowledgeFixture(item, version);
    }

    private IDisposable ChangeUser(Guid userId)
    {
        return _principalAccessor.Change(new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(AbpClaimTypes.UserId, userId.ToString()),
            new Claim(AbpClaimTypes.UserName, $"test-{userId:N}"),
            new Claim(AbpClaimTypes.Role, "admin")
        ], "Test")));
    }

    private static CreateLearningPathDto PathInput(string name, KnowledgeFixture knowledge)
    {
        return new CreateLearningPathDto
        {
            Name = name,
            Assignments = { new LearningPathAssignmentInputDto { ScopeType = LearningPathAssignmentScopeType.All } },
            RequiredKnowledge = { new LearningPathKnowledgeInputDto { KnowledgeItemId = knowledge.Item.Id, Note = "Private learning instruction" } }
        };
    }

    private static MarkLearningKnowledgeReadDto ReadInput(Guid pathId, KnowledgeFixture knowledge)
    {
        return new MarkLearningKnowledgeReadDto
        {
            LearningPathId = pathId,
            KnowledgeItemId = knowledge.Item.Id,
            KnowledgeVersionId = knowledge.Version.Id,
            MarkCompleted = true
        };
    }

    private static void AssertNoPersonalContent(LearningPathDto path, KnowledgeFixture knowledge)
    {
        path.RequiredKnowledge.ShouldBeEmpty();
        path.RecommendedKnowledge.ShouldBeEmpty();
        var serialized = JsonSerializer.Serialize(path);
        serialized.ShouldNotContain(knowledge.Item.Title);
        serialized.ShouldNotContain(knowledge.Version.Title);
        serialized.ShouldNotContain("Private learning instruction");
        serialized.ShouldNotContain("Private recommendation");
    }

    private sealed record KnowledgeFixture(KnowledgeItem Item, KnowledgeVersion Version);
}
