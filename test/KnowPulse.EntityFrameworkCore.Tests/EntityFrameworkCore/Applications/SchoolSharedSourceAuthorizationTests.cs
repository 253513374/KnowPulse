using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.KnowledgeBase;
using KnowPulse.KnowledgeIndexing;
using KnowPulse.QuestionBank;
using KnowPulse.RagAI;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Security.Claims;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class SchoolSharedSourceAuthorizationTests : KnowPulseEntityFrameworkCoreTestBase
{
    private const SchoolKnowledgeShareUses GenerationUses = SchoolKnowledgeShareUses.Read |
        SchoolKnowledgeShareUses.Retrieve | SchoolKnowledgeShareUses.GenerateQuestions;
    private readonly KnowledgeSourceAccessGuard _guard;

    public SchoolSharedSourceAuthorizationTests()
    {
        _guard = GetRequiredService<KnowledgeSourceAccessGuard>();
        GetRequiredService<TestKnowledgeEmbeddingClient>().Reset();
        GetRequiredService<TestKnowledgeVectorStore>().Reset();
    }

    [Theory]
    [InlineData(SchoolKnowledgeShareUses.Read)]
    [InlineData(SchoolKnowledgeShareUses.Read | SchoolKnowledgeShareUses.Retrieve)]
    public async Task Reading_Does_Not_Grant_New_Question_Use(SchoolKnowledgeShareUses uses)
    {
        var fixture = await CreateFixtureAsync(uses);
        using var user = ChangeUser(fixture.Reader);
        (await ReadSourceAsync(fixture)).Id.ShouldBe(fixture.ItemId);
        await Should.ThrowAsync<AbpAuthorizationException>(() => WithUnitOfWorkAsync(() =>
            _guard.EnsureSourceAsync(fixture.ItemId, fixture.VersionId, fixture.Chunk.Id, requireUsable: true)));
        (await WithUnitOfWorkAsync(() => Repository<Question>().CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task Item_Only_Private_Version_Mismatched_References_And_Outsider_Should_Not_Use_Shared_Authorization()
    {
        var fixture = await CreateFixtureAsync();
        var privateVersion = await PublishPrivateRevisionAsync(fixture);
        using (ChangeUser(fixture.Reader))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => WithUnitOfWorkAsync(() =>
                _guard.EnsureSourceAsync(fixture.ItemId, null, null)));
            await Should.ThrowAsync<AbpAuthorizationException>(() => WithUnitOfWorkAsync(() =>
                _guard.EnsureSourceAsync(fixture.ItemId, privateVersion, null)));
            await Should.ThrowAsync<AbpAuthorizationException>(() => WithUnitOfWorkAsync(() =>
                _guard.EnsureSourceAsync(fixture.ItemId, privateVersion, fixture.Chunk.Id)));
            await Should.ThrowAsync<AbpAuthorizationException>(() => WithUnitOfWorkAsync(() =>
                _guard.EnsureSourceAsync(Guid.NewGuid(), fixture.VersionId, fixture.Chunk.Id)));
            (await WithUnitOfWorkAsync(() => _guard.EnsureSourceAsync(null, null, fixture.Chunk.Id)))!
                .Id.ShouldBe(fixture.ItemId);
        }
        using (ChangeUser(fixture.Outsider))
            await Should.ThrowAsync<AbpAuthorizationException>(() => ReadSourceAsync(fixture));
    }

    [Fact]
    public async Task New_Use_Should_Require_Indexed_Job_For_The_Exact_Active_Chunk()
    {
        var fixture = await CreateFixtureAsync();
        await WithUnitOfWorkAsync(async () =>
        {
            var job = await Repository<KnowledgeIndexJob>().GetAsync(fixture.Chunk.KnowledgeIndexJobId);
            job.MarkOutdated();
            await Repository<KnowledgeIndexJob>().UpdateAsync(job, autoSave: true);
        });
        using var user = ChangeUser(fixture.Reader);
        (await ReadSourceAsync(fixture)).Id.ShouldBe(fixture.ItemId);
        await Should.ThrowAsync<AbpAuthorizationException>(() => WithUnitOfWorkAsync(() =>
            _guard.EnsureSourceAsync(fixture.ItemId, fixture.VersionId, fixture.Chunk.Id, requireUsable: true)));
    }

    [Fact]
    public async Task Revocation_Should_Deny_Historical_Source_While_Preserving_The_Authors_Private_History()
    {
        var fixture = await CreateFixtureAsync();
        await PublishPrivateRevisionAsync(fixture);
        using (ChangeUser(fixture.Reader))
            (await ReadSourceAsync(fixture)).Id.ShouldBe(fixture.ItemId);
        await RevokeHistoricalSchoolShareAsync(fixture.ShareId);
        using (ChangeUser(fixture.Reader))
            await Should.ThrowAsync<AbpAuthorizationException>(() => ReadSourceAsync(fixture));
        using (ChangeUser(fixture.Owner))
            (await ReadSourceAsync(fixture)).Id.ShouldBe(fixture.ItemId);
    }

    [Fact]
    public async Task Missing_Original_Source_Should_Deny_Historical_Reference()
    {
        var fixture = await CreateFixtureAsync();
        await WithUnitOfWorkAsync(() => Repository<KnowledgeItem>().DeleteAsync(fixture.ItemId, autoSave: true));
        using var user = ChangeUser(fixture.Reader);
        await Should.ThrowAsync<AbpAuthorizationException>(() => ReadSourceAsync(fixture));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task New_Question_Should_Reject_Historical_School_Source(bool explicitShareId)
    {
        var fixture = await CreateFixtureAsync();
        using var user = ChangeUser(fixture.Reader);
        var error = await Should.ThrowAsync<BusinessException>(() =>
            GetRequiredService<IQuestionWorkflowAppService>().CreateManualDraftAsync(new SaveQuestionDraftDto
            {
                Type = QuestionType.TrueFalse, Difficulty = QuestionDifficulty.Normal, Title = "旧共享题", Stem = "旧共享练习",
                Answers = { new CreateUpdateQuestionAnswerDto { Value = "true", DisplayOrder = 1 } },
                Sources = { new CreateUpdateQuestionSourceDto
                {
                    KnowledgeItemId = fixture.ItemId, KnowledgeVersionId = fixture.VersionId,
                    ChunkId = fixture.Chunk.Id, SchoolKnowledgeShareId = explicitShareId ? fixture.ShareId : null
                } }
            }));
        error.Code.ShouldBe("KnowPulse:Education:SchoolWorkflowRetired");
        (await WithUnitOfWorkAsync(() => Repository<Question>().CountAsync())).ShouldBe(0);
    }

    private async Task<Fixture> CreateFixtureAsync(SchoolKnowledgeShareUses uses = GenerationUses)
    {
        var seed = await WithUnitOfWorkAsync(async () =>
        {
            var owner = Guid.NewGuid();
            var reader = Guid.NewGuid();
            var outsider = Guid.NewGuid();
            foreach (var id in new[] { owner, reader, outsider })
                await Repository<IdentityUser>().InsertAsync(new IdentityUser(id, $"shared-source-{id:N}", $"{id:N}@example.invalid"), autoSave: true);
            var school = new School(Guid.NewGuid(), null, $"S{Guid.NewGuid():N}", "共享来源测试初中");
            await Repository<School>().InsertAsync(school, autoSave: true);
            foreach (var id in new[] { owner, reader })
                await Repository<SchoolMembership>().InsertAsync(new SchoolMembership(Guid.NewGuid(), null,
                    school.Id, id, SchoolMemberRole.Teacher, DateTime.UtcNow.AddDays(-1)), autoSave: true);
            await Repository<KnowledgeLibrary>().InsertAsync(new KnowledgeLibrary(owner, null), autoSave: true);
            var item = new KnowledgeItem(Guid.NewGuid(), null, "共享V1标题", summary: "旧条目摘要", libraryId: owner, ownerUserId: owner);
            var version = new KnowledgeVersion(Guid.NewGuid(), null, item.Id, 1, "共享V1标题", "先明确教材知识点，再设计练习。共享V1正文提供教学依据。");
            version.Publish(DateTime.UtcNow);
            item.SetCurrentPublishedVersion(version.Id);
            await Repository<KnowledgeItem>().InsertAsync(item, autoSave: true);
            await Repository<KnowledgeVersion>().InsertAsync(version, autoSave: true);
            return (owner, reader, outsider, itemId: item.Id, versionId: version.Id, schoolId: school.Id);
        });
        SchoolKnowledgeShare share;
        using (ChangeUser(seed.owner))
        {
            (await GetRequiredService<IKnowledgeIngestionPipeline>().IngestAsync(seed.versionId)).Succeeded.ShouldBeTrue();
            share = await SeedHistoricalSchoolShareAsync(seed.itemId, seed.versionId, seed.schoolId,
                uses, "作者确认的共享摘要");
        }
        var chunk = await WithUnitOfWorkAsync(async () => await Repository<KnowledgeChunk>().GetAsync(x =>
            x.KnowledgeVersionId == seed.versionId && x.IsActive));
        return new Fixture(seed.owner, seed.reader, seed.outsider, seed.itemId, seed.versionId, share.Id, chunk);
    }

    private async Task<Guid> PublishPrivateRevisionAsync(Fixture fixture)
    {
        using var user = ChangeUser(fixture.Owner);
        var service = GetRequiredService<IKnowledgeItemAppService>();
        await service.UpdateAsync(fixture.ItemId, new UpdateKnowledgeItemDto { Title = "PRIVATE-V2-TITLE", Summary = "PRIVATE-V2-SUMMARY" });
        var version = await service.CreateVersionAsync(fixture.ItemId, new CreateKnowledgeVersionDto
        {
            Title = "PRIVATE-V2-TITLE", ContentText = "PRIVATE-V2-CONTENT"
        });
        await service.PublishVersionAsync(fixture.ItemId, version.Id);
        return version.Id;
    }

    private async Task<KnowledgeItem> ReadSourceAsync(Fixture fixture)
        => (await WithUnitOfWorkAsync(() => _guard.EnsureSourceAsync(fixture.ItemId, fixture.VersionId, fixture.Chunk.Id)))!;

    private IRepository<T, Guid> Repository<T>() where T : class, Volo.Abp.Domain.Entities.IEntity<Guid>
        => GetRequiredService<IRepository<T, Guid>>();

    private IDisposable ChangeUser(Guid userId) => GetRequiredService<ICurrentPrincipalAccessor>().Change(
        new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(AbpClaimTypes.UserId, userId.ToString()), new Claim(AbpClaimTypes.UserName, $"shared-source-{userId:N}"),
            // 管理角色声明也不替代真实学校身份或个人衍生内容所有者。
            new Claim(AbpClaimTypes.Role, "admin")
        }, "Test")));

    private sealed record Fixture(Guid Owner, Guid Reader, Guid Outsider, Guid ItemId, Guid VersionId,
        Guid ShareId, KnowledgeChunk Chunk);
}
