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
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Security.Claims;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class SchoolKnowledgeRetrievalTests : KnowPulseEntityFrameworkCoreTestBase
{
    private readonly IKnowledgeSchoolSharingAppService _sharing;
    private readonly IKnowledgeRetriever _retriever;
    private readonly TestKnowledgeVectorStore _vectors;
    private readonly TestRagChatClient _chat;

    public SchoolKnowledgeRetrievalTests()
    {
        _sharing = GetRequiredService<IKnowledgeSchoolSharingAppService>();
        _retriever = GetRequiredService<IKnowledgeRetriever>();
        _vectors = GetRequiredService<TestKnowledgeVectorStore>();
        _chat = GetRequiredService<TestRagChatClient>();
        _vectors.Reset();
        _chat.Reset();
        GetRequiredService<IOptions<AiQuestionGenerationWorkerOptions>>().Value.MaxAttempts = 1;
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

        var answer = await GetRequiredService<IRagAnswerService>().AnswerAsync(new RagAnswerRequest
        {
            Question = "教材教学经验", Scope = KnowledgeSearchScope.SchoolShared, SchoolId = fixture.SchoolId
        });
        answer.Citations.Single().Title.ShouldBe("共享V1标题");
        answer.Citations.Single().SchoolKnowledgeShareId.ShouldBe(fixture.ShareId);
        _chat.Requests.Single().UserPrompt.ShouldContain("共享V1正文");
        _chat.Requests.Single().UserPrompt.ShouldNotContain("PRIVATE-V2");
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
        all.Select(x => x.ChunkId).ShouldBe(new[] { organization.Id, own.Id, fixture.Chunk.Id });
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
        using (ChangeUser(fixture.Owner)) await _sharing.RevokeAsync(fixture.ShareId);
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
    public async Task Retrieve_And_Generate_Uses_Should_Be_Independent_And_AI_Must_Enforce_Generation(SchoolKnowledgeShareUses uses, bool canRetrieve)
    {
        var fixture = await CreateFixtureAsync(uses);
        _vectors.SearchResults.Add(Hit(fixture.Chunk));
        using var reader = ChangeUser(fixture.Reader);
        (await _retriever.SearchAsync(Search(fixture))).Count.ShouldBe(canRetrieve ? 1 : 0);
        var generation = await GetRequiredService<IAiQuestionGenerationAppService>().CreateAsync(new CreateAiQuestionGenerationTaskDto
        {
            KnowledgeScope = "共享教材经验", Scope = KnowledgeSearchScope.SchoolShared, SchoolId = fixture.SchoolId
        });
        generation = await ProcessAsync(generation.Id);
        generation.Status.ShouldBe(AiQuestionGenerationTaskStatus.Failed);
        generation.CallLogs.Last().Status.ShouldBe(RagCallStatus.NoContext);
        _chat.CallCount.ShouldBe(0);
        _vectors.SearchRequests.Last().PermissionFilter.AllowedSharedVersionIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task Allowed_Generation_And_Retry_Should_Keep_School_Scope_And_Respect_Revocation()
    {
        var fixture = await CreateFixtureAsync(SchoolKnowledgeShareUses.Read | SchoolKnowledgeShareUses.Retrieve | SchoolKnowledgeShareUses.GenerateQuestions);
        _vectors.SearchResults.Add(Hit(fixture.Chunk));
        _chat.ResponseContent = QuestionResponse(fixture.Chunk.Id);
        AiQuestionGenerationTaskDto generation;
        using (ChangeUser(fixture.Reader))
        {
            generation = await GetRequiredService<IAiQuestionGenerationAppService>().CreateAsync(new CreateAiQuestionGenerationTaskDto
            {
                KnowledgeScope = "教学方法", Scope = KnowledgeSearchScope.SchoolShared, SchoolId = fixture.SchoolId
            });
            generation = await ProcessAsync(generation.Id);
            generation.Scope.ShouldBe(KnowledgeSearchScope.SchoolShared);
            generation.SchoolId.ShouldBe(fixture.SchoolId);
            generation.GeneratedCount.ShouldBe(1);
            _chat.CallCount.ShouldBe(1);
            _chat.Requests.Single().UserPrompt.ShouldContain("共享V1标题");
            generation.Candidates.Single().Citations.Single().SourceTitle.ShouldBe("共享V1标题");
            (await GetRequiredService<IAiQuestionGenerationAppService>().RetryAsync(generation.Id)).GeneratedCount.ShouldBe(1);
            _chat.CallCount.ShouldBe(1);
            _chat.Reset();
            _chat.ShouldFail = true;
            var queued = await GetRequiredService<IAiQuestionGenerationAppService>().RegenerateAsync(generation.Id,
                new RegenerateAiQuestionGenerationTaskDto { IdempotencyKey = Guid.NewGuid() });
            queued.Status.ShouldBe(AiQuestionGenerationTaskStatus.Pending);
            var failed = await ProcessAsync(generation.Id);
            failed.Status.ShouldBe(AiQuestionGenerationTaskStatus.Failed);
            failed.Scope.ShouldBe(KnowledgeSearchScope.SchoolShared);
            failed.SchoolId.ShouldBe(fixture.SchoolId);
            _chat.CallCount.ShouldBe(1);
        }
        using (ChangeUser(fixture.Owner)) await _sharing.RevokeAsync(fixture.ShareId);
        _chat.Reset();
        using (ChangeUser(fixture.Reader))
        {
            // Older successful candidates remain in history. Withdrawing their source prevents
            // reopening that task and exposing its citations; a new task still checks the chosen scope.
            await Should.ThrowAsync<AbpAuthorizationException>(() => GetRequiredService<IAiQuestionGenerationAppService>().RetryAsync(generation.Id));
            await Should.ThrowAsync<AbpAuthorizationException>(() => GetRequiredService<IAiQuestionGenerationAppService>().RegenerateAsync(generation.Id,
                new RegenerateAiQuestionGenerationTaskDto { IdempotencyKey = Guid.NewGuid() }));
            var retried = await GetRequiredService<IAiQuestionGenerationAppService>().CreateAsync(new CreateAiQuestionGenerationTaskDto
                { KnowledgeScope = "教学方法", Scope = KnowledgeSearchScope.SchoolShared, SchoolId = fixture.SchoolId });
            retried.Status.ShouldBe(AiQuestionGenerationTaskStatus.Pending);
            retried = await ProcessAsync(retried.Id);
            retried.Status.ShouldBe(AiQuestionGenerationTaskStatus.Failed);
            retried.Scope.ShouldBe(KnowledgeSearchScope.SchoolShared);
            retried.SchoolId.ShouldBe(fixture.SchoolId);
            _chat.CallCount.ShouldBe(0);
            _vectors.SearchRequests.Last().PermissionFilter.Scope.ShouldBe(KnowledgeSearchScope.SchoolShared);
        }
    }

    [Fact]
    public async Task New_Generation_Should_Default_To_Personal_Sources_And_Require_Explicit_Selection_For_School_Sources()
    {
        var fixture = await CreateFixtureAsync(SchoolKnowledgeShareUses.Read | SchoolKnowledgeShareUses.Retrieve | SchoolKnowledgeShareUses.GenerateQuestions);
        _vectors.SearchResults.Add(Hit(fixture.Chunk));
        _chat.ResponseContent = QuestionResponse(fixture.Chunk.Id);
        using var reader = ChangeUser(fixture.Reader);
        var generation = GetRequiredService<IAiQuestionGenerationAppService>();
        var personalOnly = await generation.CreateAsync(new CreateAiQuestionGenerationTaskDto { KnowledgeScope = "教学方法" });
        personalOnly = await ProcessAsync(personalOnly.Id);
        personalOnly.Scope.ShouldBe(KnowledgeSearchScope.PersonalLibrary);
        personalOnly.Status.ShouldBe(AiQuestionGenerationTaskStatus.Failed);
        personalOnly.CallLogs.Single().Status.ShouldBe(RagCallStatus.NoContext);
        _chat.CallCount.ShouldBe(0);
        _vectors.SearchRequests.Single().PermissionFilter.Scope.ShouldBe(KnowledgeSearchScope.PersonalLibrary);
        _vectors.SearchRequests.Single().PermissionFilter.AllowedSharedVersionIds.ShouldBeEmpty();

        var selected = await generation.CreateAsync(new CreateAiQuestionGenerationTaskDto
        {
            KnowledgeScope = "教学方法", Scope = KnowledgeSearchScope.AllAccessible
        });
        selected = await ProcessAsync(selected.Id);
        selected.GeneratedCount.ShouldBe(1);
        selected.Scope.ShouldBe(KnowledgeSearchScope.AllAccessible);
        _chat.CallCount.ShouldBe(1);
        _chat.Requests.Single().UserPrompt.ShouldContain("共享V1正文");
        selected.Candidates.Single().Citations.Single().KnowledgeVersionId.ShouldBe(fixture.Chunk.KnowledgeVersionId);

        // 旧任务没有显式范围字段，再次生成仍沿用原有全部获准知识语义并保留已有候选。
        await WithUnitOfWorkAsync(async () =>
        {
            var oldTask = await Repo<AiQuestionGenerationTask>().GetAsync(selected.Id);
            oldTask.ExtraProperties.Remove("KnowledgeSearchScope");
            await Repo<AiQuestionGenerationTask>().UpdateManyAsync(new[] { oldTask }, autoSave: true);
        });
        await generation.RegenerateAsync(selected.Id, new RegenerateAiQuestionGenerationTaskDto { IdempotencyKey = Guid.NewGuid() });
        var legacyRetry = await ProcessAsync(selected.Id);
        legacyRetry.Scope.ShouldBe(KnowledgeSearchScope.AllAccessible);
        legacyRetry.GeneratedCount.ShouldBe(1);
        legacyRetry.Candidates.Count.ShouldBe(2);
        _chat.CallCount.ShouldBe(2);
        _vectors.SearchRequests.Last().PermissionFilter.Scope.ShouldBe(KnowledgeSearchScope.AllAccessible);
    }

    [Fact]
    public async Task Share_Revoked_During_Vector_Call_Should_Be_Rejected_By_Postgres_Secondary_Check()
    {
        var fixture = await CreateFixtureAsync();
        var vectors = Substitute.For<IKnowledgeVectorStore>();
        vectors.SearchAsync(Arg.Any<KnowledgeVectorSearchRequest>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            call.Arg<KnowledgeVectorSearchRequest>().PermissionFilter.AllowedSharedVersionIds.ShouldContain(fixture.Chunk.KnowledgeVersionId);
            using (ChangeUser(fixture.Owner)) await _sharing.RevokeAsync(fixture.ShareId);
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
        SchoolKnowledgeShareDto share;
        using (ChangeUser(owner)) share = await _sharing.ShareVersionAsync(new CreateSchoolKnowledgeShareDto
        {
            IdempotencyKey = Guid.NewGuid(), KnowledgeItemId = chunk.KnowledgeItemId, KnowledgeVersionId = chunk.KnowledgeVersionId,
            SchoolId = school, Uses = uses, SharedSummary = "共享摘要"
        });
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

    private async Task<AiQuestionGenerationTaskDto> ProcessAsync(Guid id)
    {
        await GetRequiredService<AiQuestionGenerationProcessor>().ProcessAsync(id);
        return await GetRequiredService<IAiQuestionGenerationAppService>().GetAsync(id);
    }

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

    private static string QuestionResponse(Guid chunkId) => $$"""
        {"questions":[{"type":"SingleChoice","difficulty":"Normal","title":"教学题","stem":"请选择正确方法","options":[{"optionKey":"A","content":"方法一"},{"optionKey":"B","content":"方法二"}],"answers":["A"],"analysis":"依据共享教材","knowledgePoints":["教学方法"],"citations":[{"chunkId":"{{chunkId:D}}"}]}]}
        """;

    private sealed record Fixture(Guid Owner, Guid Reader, Guid SchoolId, Guid ItemId, Guid ShareId, KnowledgeChunk Chunk);
}
