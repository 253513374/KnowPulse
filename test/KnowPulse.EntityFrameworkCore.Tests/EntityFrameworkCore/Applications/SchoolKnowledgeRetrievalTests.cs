using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.AiQuestioning;
using KnowPulse.AiModelManagement;
using KnowPulse.Education;
using KnowPulse.KnowledgeBase;
using KnowPulse.KnowledgeIndexing;
using KnowPulse.RagAI;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Security.Claims;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class SchoolKnowledgeRetrievalTests : KnowPulseEntityFrameworkCoreTestBase
{
    private readonly IKnowledgeRetriever _retriever;
    private readonly TestKnowledgeVectorStore _vectors;
    private readonly TestRagChatClient _chat;

    public SchoolKnowledgeRetrievalTests()
    {
        _retriever = GetRequiredService<IKnowledgeRetriever>();
        _vectors = GetRequiredService<TestKnowledgeVectorStore>();
        _chat = GetRequiredService<TestRagChatClient>();
        _vectors.Reset();
        _chat.Reset();
    }

    [Fact]
    public async Task Shared_V1_Should_Be_Usable_After_Private_V2_Without_Exposing_Private_Metadata()
    {
        var fixture = await CreateFixtureAsync();
        using (ChangeUser(fixture.Owner))
        {
            var items = GetRequiredService<IKnowledgeItemAppService>();
            await items.UpdateAsync(fixture.ItemId, new UpdateKnowledgeItemDto { Title = "PRIVATE-V2-TITLE", Summary = "PRIVATE-V2-SUMMARY" });
            var version = await items.CreateVersionAsync(fixture.ItemId, new CreateKnowledgeVersionDto
            {
                Title = "PRIVATE-V2-TITLE", ContentText = "PRIVATE-V2-CONTENT"
            });
            await items.PublishVersionAsync(fixture.ItemId, version.Id);
            _vectors.SearchResults.Add(Hit(await AddIndexedChunkAsync(fixture.ItemId, version.Id, "PRIVATE-V2-CONTENT")));
        }
        _vectors.SearchResults.Add(Hit(fixture.Chunk));
        using var reader = ChangeUser(fixture.Reader);
        var result = await _retriever.SearchAsync(Search(fixture));
        result.Single().KnowledgeVersionId.ShouldBe(fixture.Chunk.KnowledgeVersionId);
        result.Single().SourceTitle.ShouldBe("共享V1标题");
        result.Single().Content.ShouldBe("共享V1正文");
        result.Single().SchoolKnowledgeShareId.ShouldBe(fixture.ShareId);
        _vectors.SearchRequests.Single().PermissionFilter.AllowedSharedVersionIds.ShouldBe(new[] { fixture.Chunk.KnowledgeVersionId });

    }

    [Fact]
    public async Task Requested_Scope_Should_Be_Rechecked_Against_Database_Even_When_Vector_Store_Ignores_Filter()
    {
        var fixture = await CreateFixtureAsync();
        var own = await CreateIndexedSourceAsync(fixture.Reader, "个人资料");
        var organization = await CreateIndexedSourceAsync(null, "组织资料");
        _vectors.SearchResults.AddRange(new[] { Hit(organization), Hit(own), Hit(fixture.Chunk) });
        using var reader = ChangeUser(fixture.Reader);
        var personal = await _retriever.SearchAsync(new RagSearchRequest { Query = "资料", Scope = KnowledgeSearchScope.PersonalLibrary });
        personal.Select(x => x.ChunkId).ShouldBe(new[] { own.Id });
        _vectors.SearchRequests.Last().PermissionFilter.AllowedSharedVersionIds.ShouldBeEmpty();
        var school = await _retriever.SearchAsync(Search(fixture));
        school.Select(x => x.ChunkId).ShouldBe(new[] { fixture.Chunk.Id });
        var all = await _retriever.SearchAsync(new RagSearchRequest { Query = "资料" });
        all.Select(x => x.ChunkId).ShouldBe(new[] { organization.Id, own.Id });
        _vectors.SearchRequests.Last().PermissionFilter.AllowedSharedVersionIds.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(SchoolMemberRole.Student, 0)]
    [InlineData(SchoolMemberRole.Manager, 0)]
    [InlineData(SchoolMemberRole.Teacher, 1)]
    [InlineData(SchoolMemberRole.Teacher, -1)]
    public async Task Claimed_Admin_Should_Not_Replace_A_Current_Teacher_Membership(SchoolMemberRole role, int period)
    {
        var fixture = await CreateFixtureAsync(readerRole: role, readerPeriod: period);
        _vectors.SearchResults.Add(Hit(fixture.Chunk));
        using var reader = ChangeUser(fixture.Reader); // Includes admin claim deliberately.
        var result = await _retriever.SearchAsync(new RagSearchRequest { Query = "共享经验" });
        result.ShouldBeEmpty();
        _vectors.SearchRequests.Last().PermissionFilter.AllowedSharedVersionIds.ShouldBeEmpty();
        await Should.ThrowAsync<AbpAuthorizationException>(() => _retriever.SearchAsync(Search(fixture)));
    }

    [Fact]
    public async Task Other_School_And_Revoked_Sharing_Should_Not_Grant_New_Retrieval()
    {
        var fixture = await CreateFixtureAsync();
        var outsider = Guid.NewGuid();
        await WithUnitOfWorkAsync(async () =>
        {
            await Repo<IdentityUser>().InsertAsync(new IdentityUser(outsider, $"out-{outsider:N}", $"{outsider:N}@example.invalid"), autoSave: true);
            var otherSchool = new School(Guid.NewGuid(), null, $"S{Guid.NewGuid():N}", "其他学校");
            await Repo<School>().InsertAsync(otherSchool, autoSave: true);
            await Repo<SchoolMembership>().InsertAsync(new SchoolMembership(Guid.NewGuid(), null, otherSchool.Id,
                outsider, SchoolMemberRole.Teacher, DateTime.UtcNow.AddDays(-1)), autoSave: true);
        });
        _vectors.SearchResults.Add(Hit(fixture.Chunk));
        using (ChangeUser(outsider))
        {
            (await _retriever.SearchAsync(new RagSearchRequest { Query = "共享经验" })).ShouldBeEmpty();
            await Should.ThrowAsync<AbpAuthorizationException>(() => _retriever.SearchAsync(Search(fixture)));
        }
        using (ChangeUser(fixture.Owner)) await RevokeHistoricalSchoolShareAsync(fixture.ShareId);
        using (ChangeUser(fixture.Reader))
        {
            (await _retriever.SearchAsync(Search(fixture))).ShouldBeEmpty();
            _vectors.SearchRequests.Last().PermissionFilter.AllowedSharedVersionIds.ShouldBeEmpty();
            var answer = await GetRequiredService<IRagAnswerService>().AnswerAsync(new RagAnswerRequest { Question = "共享经验" });
            answer.Citations.ShouldBeEmpty();
            _chat.CallCount.ShouldBe(0);
        }
    }

    [Theory]
    [InlineData(SchoolKnowledgeShareUses.Read, false)]
    [InlineData(SchoolKnowledgeShareUses.Read | SchoolKnowledgeShareUses.Retrieve, true)]
    public async Task Internal_Retrieval_Should_Respect_Uses_While_Public_Generation_Rejects_School_Scope(SchoolKnowledgeShareUses uses, bool canRetrieve)
    {
        var fixture = await CreateFixtureAsync(uses);
        _vectors.SearchResults.Add(Hit(fixture.Chunk));
        using var reader = ChangeUser(fixture.Reader);
        (await _retriever.SearchAsync(Search(fixture))).Count.ShouldBe(canRetrieve ? 1 : 0);
        (await Should.ThrowAsync<BusinessException>(() => GetRequiredService<IAiQuestionGenerationAppService>().CreateAsync(
            new CreateAiQuestionGenerationTaskDto
            {
                KnowledgeScope = "共享教材经验", Scope = KnowledgeSearchScope.SchoolShared, SchoolId = fixture.SchoolId
            }))).Code.ShouldBe("KnowPulse:Education:SchoolWorkflowRetired");
        _chat.CallCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("school")]
    [InlineData("curriculum")]
    public async Task New_Generation_Should_Reject_School_Or_Curriculum_Input(string scope)
    {
        var fixture = await CreateFixtureAsync();
        using var reader = ChangeUser(fixture.Reader);
        var input = new CreateAiQuestionGenerationTaskDto { KnowledgeScope = "教学方法", IdempotencyKey = Guid.NewGuid() };
        if (scope == "school") input.SchoolId = fixture.SchoolId;
        else input.CurriculumScope = new EducationCurriculumScopeDto
        {
            TeacherTextbookSelectionId = Guid.NewGuid(), TextbookKnowledgePointMappingIds = [Guid.NewGuid()]
        };
        (await Should.ThrowAsync<BusinessException>(() => GetRequiredService<IAiQuestionGenerationAppService>().CreateAsync(input)))
            .Code.ShouldBe("KnowPulse:Education:SchoolWorkflowRetired");
        _chat.CallCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Public_Question_Answering_Should_Reject_Explicit_School_Scopes(bool explicitSchool)
    {
        var request = new RagAnswerRequest
        {
            Question = "教材教学经验", Scope = explicitSchool ? KnowledgeSearchScope.AllAccessible : KnowledgeSearchScope.SchoolShared,
            SchoolId = explicitSchool ? Guid.NewGuid() : null
        };
        (await Should.ThrowAsync<BusinessException>(() => GetRequiredService<IRagAnswerService>().AnswerAsync(request)))
            .Code.ShouldBe("KnowPulse:Education:SchoolWorkflowRetired");
        _chat.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Historical_School_Task_Should_Reject_Retry_And_Regeneration_Without_Calling_Model()
    {
        var fixture = await CreateFixtureAsync();
        using var reader = ChangeUser(fixture.Reader);
        var generation = GetRequiredService<IAiQuestionGenerationAppService>();
        var pending = await generation.CreateAsync(new CreateAiQuestionGenerationTaskDto { KnowledgeScope = "旧任务" });
        await WithUnitOfWorkAsync(async () =>
        {
            var task = await Repo<AiQuestionGenerationTask>().GetAsync(pending.Id);
            task.ExtraProperties["KnowledgeSearchScope"] = KnowledgeSearchScope.SchoolShared.ToString();
            await Repo<AiQuestionGenerationTask>().UpdateAsync(task, autoSave: true);
        });
        (await Should.ThrowAsync<BusinessException>(() => generation.RetryAsync(pending.Id)))
            .Code.ShouldBe("KnowPulse:Education:SchoolWorkflowRetired");
        (await Should.ThrowAsync<BusinessException>(() => generation.RegenerateAsync(pending.Id,
            new RegenerateAiQuestionGenerationTaskDto { IdempotencyKey = Guid.NewGuid() })))
            .Code.ShouldBe("KnowPulse:Education:SchoolWorkflowRetired");
        await GetRequiredService<AiQuestionGenerationProcessor>().ProcessAsync(pending.Id);
        (await generation.GetAsync(pending.Id)).Status.ShouldBe(AiQuestionGenerationTaskStatus.Failed);
        _chat.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Share_Revoked_During_Vector_Call_Should_Be_Rejected_By_Postgres_Secondary_Check()
    {
        var fixture = await CreateFixtureAsync();
        var vectors = Substitute.For<IKnowledgeVectorStore>();
        vectors.SearchAsync(Arg.Any<KnowledgeVectorSearchRequest>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            call.Arg<KnowledgeVectorSearchRequest>().PermissionFilter.AllowedSharedVersionIds.ShouldContain(fixture.Chunk.KnowledgeVersionId);
            using (ChangeUser(fixture.Owner)) await RevokeHistoricalSchoolShareAsync(fixture.ShareId);
            return (IReadOnlyList<VectorSearchHit>)new[] { Hit(fixture.Chunk) };
        });
        var retriever = new KnowledgeRetriever(GetRequiredService<IKnowledgeEmbeddingClient>(), vectors,
            GetRequiredService<IKnowledgeAccessContextProvider>(), Repo<KnowledgeItem>(), Repo<KnowledgeVersion>(),
            Repo<KnowledgePermissionScope>(), Repo<KnowledgeChunk>(), GetRequiredService<IRagCallLogWriter>(),
            GetRequiredService<IAiModelResolver>(), GetRequiredService<IOptions<RagAiOptions>>())
        {
            LazyServiceProvider = GetRequiredService<KnowledgeRetriever>().LazyServiceProvider
        };
        using var reader = ChangeUser(fixture.Reader);
        var result = await WithUnitOfWorkAsync(() => retriever.SearchAsync(Search(fixture)));
        result.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Shared_Retrieval_Should_Require_Active_Chunks_And_Their_Completed_Index_Job(bool invalidateJob)
    {
        var fixture = await CreateFixtureAsync();
        await WithUnitOfWorkAsync(async () =>
        {
            if (invalidateJob)
            {
                var job = await Repo<KnowledgeIndexJob>().GetAsync(fixture.Chunk.KnowledgeIndexJobId);
                job.MarkOutdated();
                await Repo<KnowledgeIndexJob>().UpdateAsync(job, autoSave: true);
            }
            else
            {
                var chunk = await Repo<KnowledgeChunk>().GetAsync(fixture.Chunk.Id);
                chunk.Deactivate();
                await Repo<KnowledgeChunk>().UpdateAsync(chunk, autoSave: true);
            }
        });
        _vectors.SearchResults.Add(Hit(fixture.Chunk));
        using var reader = ChangeUser(fixture.Reader);
        (await _retriever.SearchAsync(Search(fixture))).ShouldBeEmpty();
    }

    private async Task<Fixture> CreateFixtureAsync(SchoolKnowledgeShareUses uses = SchoolKnowledgeShareUses.Read | SchoolKnowledgeShareUses.Retrieve,
        SchoolMemberRole readerRole = SchoolMemberRole.Teacher, int readerPeriod = 0)
    {
        var owner = Guid.NewGuid();
        var reader = Guid.NewGuid();
        var school = Guid.NewGuid();
        await WithUnitOfWorkAsync(async () =>
        {
            foreach (var userId in new[] { owner, reader })
            {
                await Repo<IdentityUser>().InsertAsync(new IdentityUser(userId, $"retrieval-{userId:N}", $"{userId:N}@example.invalid"), autoSave: true);
                await Repo<KnowledgeLibrary>().InsertAsync(new KnowledgeLibrary(userId, null), autoSave: true);
            }
            await Repo<School>().InsertAsync(new School(school, null, $"S{Guid.NewGuid():N}", "检索测试初中"), autoSave: true);
            await Repo<SchoolMembership>().InsertAsync(new SchoolMembership(Guid.NewGuid(), null, school, owner,
                SchoolMemberRole.Teacher, DateTime.UtcNow.AddDays(-2)), autoSave: true);
            await Repo<SchoolMembership>().InsertAsync(new SchoolMembership(Guid.NewGuid(), null, school, reader,
                readerRole, DateTime.UtcNow.AddDays(readerPeriod > 0 ? 1 : -2),
                readerPeriod < 0 ? DateTime.UtcNow.AddDays(-1) : null), autoSave: true);
        });
        var chunk = await CreateIndexedSourceAsync(owner, "共享V1标题", "共享V1正文");
        var share = await SeedHistoricalSchoolShareAsync(chunk.KnowledgeItemId, chunk.KnowledgeVersionId,
            school, uses, "共享摘要");
        return new Fixture(owner, reader, school, chunk.KnowledgeItemId, share.Id, chunk);
    }

    private Task<KnowledgeChunk> CreateIndexedSourceAsync(Guid? owner, string title, string? content = null) => WithUnitOfWorkAsync(async () =>
    {
        var item = new KnowledgeItem(Guid.NewGuid(), null, title, summary: title, libraryId: owner, ownerUserId: owner);
        var version = new KnowledgeVersion(Guid.NewGuid(), null, item.Id, 1, title, content ?? title);
        version.Publish(DateTime.UtcNow);
        item.SetCurrentPublishedVersion(version.Id);
        await Repo<KnowledgeItem>().InsertAsync(item, autoSave: true);
        await Repo<KnowledgeVersion>().InsertAsync(version, autoSave: true);
        return await AddIndexedChunkAsync(item.Id, version.Id, content ?? title);
    });

    private Task<KnowledgeChunk> AddIndexedChunkAsync(Guid itemId, Guid versionId, string content) => WithUnitOfWorkAsync(async () =>
    {
        // 构造主库授权事实；完整 embedding/vector-store 生命周期另由管道集成测试覆盖。
        var job = new KnowledgeIndexJob(Guid.NewGuid(), null, itemId, versionId, KnowledgeIndexJobTriggerType.Publish);
        job.MarkIndexed(DateTime.UtcNow);
        await Repo<KnowledgeIndexJob>().InsertAsync(job, autoSave: true);
        var chunk = new KnowledgeChunk(Guid.NewGuid(), null, itemId, versionId, job.Id, 0, content, Guid.NewGuid().ToString("N"),
            "UNTRUSTED-CHUNK-TITLE", isPublishedSnapshot: true);
        await Repo<KnowledgeChunk>().InsertAsync(chunk, autoSave: true);
        return chunk;
    });

    private IRepository<T, Guid> Repo<T>() where T : class, Volo.Abp.Domain.Entities.IEntity<Guid> => GetRequiredService<IRepository<T, Guid>>();

    private IDisposable ChangeUser(Guid userId) => GetRequiredService<ICurrentPrincipalAccessor>().Change(new ClaimsPrincipal(new ClaimsIdentity(new[]
    {
        new Claim(AbpClaimTypes.UserId, userId.ToString()), new Claim(AbpClaimTypes.UserName, $"retrieval-{userId:N}"),
        new Claim(AbpClaimTypes.Role, "admin")
    }, "Test")));

    private static RagSearchRequest Search(Fixture fixture) => new()
    {
        Query = "教学经验", Scope = KnowledgeSearchScope.SchoolShared, SchoolId = fixture.SchoolId
    };

    private static VectorSearchHit Hit(KnowledgeChunk chunk) => new()
    {
        ChunkId = chunk.Id, KnowledgeItemId = chunk.KnowledgeItemId, KnowledgeVersionId = chunk.KnowledgeVersionId,
        Content = "Untrusted vector content", Score = 0.9
    };

    private sealed record Fixture(Guid Owner, Guid Reader, Guid SchoolId, Guid ItemId, Guid ShareId, KnowledgeChunk Chunk);
}
