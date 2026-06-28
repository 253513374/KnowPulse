using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.KnowledgeBase;
using KnowPulse.KnowledgeIndexing;
using KnowPulse.Organization;
using KnowPulse.RagAI;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

/// <summary>
/// MVP-1 权限化检索集成测试。
/// 覆盖 Qdrant 前置过滤参数和 PostgreSQL 二次授权，确保旧 payload、权限变化和租户不匹配不会泄漏内容。
/// </summary>
[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class Mvp1KnowledgeRetrieverTests : KnowPulseEntityFrameworkCoreTestBase
{
    private static readonly Guid CurrentUserId = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");

    private readonly IDepartmentAppService _departmentAppService;
    private readonly IPositionAppService _positionAppService;
    private readonly IKnowledgeCategoryAppService _categoryAppService;
    private readonly IKnowledgeItemAppService _itemAppService;
    private readonly IKnowledgeIngestionPipeline _ingestionPipeline;
    private readonly IKnowledgeRetriever _retriever;
    private readonly TestKnowledgeEmbeddingClient _embeddingClient;
    private readonly TestKnowledgeVectorStore _vectorStore;
    private readonly IIdentityRoleRepository _roleRepository;
    private readonly IRepository<UserOrganizationAssignment, Guid> _assignmentRepository;
    private readonly IRepository<KnowledgeItem, Guid> _itemRepository;
    private readonly IRepository<KnowledgeVersion, Guid> _versionRepository;
    private readonly IRepository<KnowledgeIndexJob, Guid> _jobRepository;
    private readonly IRepository<KnowledgeChunk, Guid> _chunkRepository;

    /// <summary>
    /// 初始化知识检索集成测试依赖。
    /// </summary>
    public Mvp1KnowledgeRetrieverTests()
    {
        _departmentAppService = GetRequiredService<IDepartmentAppService>();
        _positionAppService = GetRequiredService<IPositionAppService>();
        _categoryAppService = GetRequiredService<IKnowledgeCategoryAppService>();
        _itemAppService = GetRequiredService<IKnowledgeItemAppService>();
        _ingestionPipeline = GetRequiredService<IKnowledgeIngestionPipeline>();
        _retriever = GetRequiredService<IKnowledgeRetriever>();
        _embeddingClient = GetRequiredService<TestKnowledgeEmbeddingClient>();
        _vectorStore = GetRequiredService<TestKnowledgeVectorStore>();
        _roleRepository = GetRequiredService<IIdentityRoleRepository>();
        _assignmentRepository = GetRequiredService<IRepository<UserOrganizationAssignment, Guid>>();
        _itemRepository = GetRequiredService<IRepository<KnowledgeItem, Guid>>();
        _versionRepository = GetRequiredService<IRepository<KnowledgeVersion, Guid>>();
        _jobRepository = GetRequiredService<IRepository<KnowledgeIndexJob, Guid>>();
        _chunkRepository = GetRequiredService<IRepository<KnowledgeChunk, Guid>>();
        _embeddingClient.Reset();
        _vectorStore.Reset();
    }

    /// <summary>
    /// 验证检索会同时应用 Qdrant 前置过滤和 PostgreSQL 二次授权。
    /// </summary>
    [Fact]
    public async Task SearchAsync_Should_Apply_Qdrant_Filter_And_Postgres_Authorization()
    {
        // 先构造当前用户的组织上下文，再混入允许和拒绝的多种权限命中，验证最终只返回授权 chunk。
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var allowedDepartment = await _departmentAppService.CreateAsync(new CreateUpdateDepartmentDto
        {
            Code = $"RD-{suffix}",
            Name = $"Retriever Department {suffix}",
            SecurityLevel = 3
        });
        var blockedDepartment = await _departmentAppService.CreateAsync(new CreateUpdateDepartmentDto
        {
            Code = $"RB-{suffix}",
            Name = $"Blocked Retriever Department {suffix}",
            SecurityLevel = 3
        });
        var allowedPosition = await _positionAppService.CreateAsync(new CreateUpdatePositionDto
        {
            DepartmentId = allowedDepartment.Id,
            Code = $"RP-{suffix}",
            Name = $"Retriever Position {suffix}",
            SecurityLevel = 3
        });

        await WithUnitOfWorkAsync(async () =>
        {
            await _assignmentRepository.InsertAsync(
                new UserOrganizationAssignment(
                    Guid.NewGuid(),
                    tenantId: null,
                    userId: CurrentUserId,
                    departmentId: allowedDepartment.Id,
                    positionId: allowedPosition.Id,
                    securityLevel: 3,
                    isPrimary: true),
                autoSave: true);
        });

        var category = await _categoryAppService.CreateAsync(new CreateUpdateKnowledgeCategoryDto
        {
            Code = $"RC-{suffix}",
            Name = $"Retriever Category {suffix}"
        });
        var adminRole = (await _roleRepository.GetListAsync(new[] { "admin" })).Single(x => x.Name == "admin");

        var allowedDepartmentChunk = await CreateIndexedItemAsync(
            category.Id,
            $"Allowed Department {suffix}",
            new CreateKnowledgePermissionScopeDto
            {
                ScopeType = KnowledgePermissionScopeType.Department,
                TargetId = allowedDepartment.Id,
                MinimumSecurityLevel = 1
            });
        var allowedPositionChunk = await CreateIndexedItemAsync(
            category.Id,
            $"Allowed Position {suffix}",
            new CreateKnowledgePermissionScopeDto
            {
                ScopeType = KnowledgePermissionScopeType.Position,
                TargetId = allowedPosition.Id
            });
        var allowedRoleChunk = await CreateIndexedItemAsync(
            category.Id,
            $"Allowed Role {suffix}",
            new CreateKnowledgePermissionScopeDto
            {
                ScopeType = KnowledgePermissionScopeType.Role,
                TargetId = adminRole.Id
            });
        var allowedUserChunk = await CreateIndexedItemAsync(
            category.Id,
            $"Allowed User {suffix}",
            new CreateKnowledgePermissionScopeDto
            {
                ScopeType = KnowledgePermissionScopeType.User,
                TargetId = CurrentUserId
            });
        var blockedDepartmentChunk = await CreateIndexedItemAsync(
            category.Id,
            $"Blocked Department {suffix}",
            new CreateKnowledgePermissionScopeDto
            {
                ScopeType = KnowledgePermissionScopeType.Department,
                TargetId = blockedDepartment.Id
            });
        var blockedUserChunk = await CreateIndexedItemAsync(
            category.Id,
            $"Blocked User {suffix}",
            new CreateKnowledgePermissionScopeDto
            {
                ScopeType = KnowledgePermissionScopeType.User,
                TargetId = Guid.NewGuid()
            });
        var blockedSecurityChunk = await CreateIndexedItemAsync(
            category.Id,
            $"Blocked Security {suffix}",
            new CreateKnowledgePermissionScopeDto
            {
                ScopeType = KnowledgePermissionScopeType.All,
                MinimumSecurityLevel = 999
            });
        var withdrawnChunk = await CreateWithdrawnIndexedItemAsync(category.Id, $"Withdrawn {suffix}");
        var permissionChangedChunk = await CreatePermissionChangedIndexedItemAsync(
            category.Id,
            $"Permission Changed {suffix}",
            blockedDepartment.Id);
        var tenantMismatchChunk = await InsertTenantMismatchChunkAsync($"Tenant Mismatch {suffix}");

        _vectorStore.SearchResults.AddRange(new[]
        {
            ToHit(blockedDepartmentChunk, 0.99),
            ToHit(blockedUserChunk, 0.98),
            ToHit(blockedSecurityChunk, 0.97),
            ToHit(withdrawnChunk, 0.96),
            ToHit(permissionChangedChunk, 0.95),
            ToHit(tenantMismatchChunk, 0.94),
            ToHit(allowedDepartmentChunk, 0.93),
            ToHit(allowedPositionChunk, 0.92),
            ToHit(allowedRoleChunk, 0.91),
            ToHit(allowedUserChunk, 0.90)
        });

        var results = await _retriever.SearchAsync(new RagSearchRequest
        {
            Query = "检索员工制度",
            TopK = 10
        });

        results.Select(x => x.ChunkId).ShouldBe(new[]
        {
            allowedDepartmentChunk.Id,
            allowedPositionChunk.Id,
            allowedRoleChunk.Id,
            allowedUserChunk.Id
        });
        results.ShouldAllBe(x => !string.IsNullOrWhiteSpace(x.SourceTitle));
        results.ShouldAllBe(x => x.KnowledgeItemId != Guid.Empty);
        results.ShouldAllBe(x => x.KnowledgeVersionId != Guid.Empty);

        _embeddingClient.CallCount.ShouldBe(1);
        _vectorStore.SearchRequests.Count.ShouldBe(1);
        var vectorRequest = _vectorStore.SearchRequests.Single();
        vectorRequest.TopK.ShouldBe(30);
        vectorRequest.PermissionFilter.TenantId.ShouldBeNull();
        vectorRequest.PermissionFilter.PublishedOnly.ShouldBeTrue();
        vectorRequest.PermissionFilter.DepartmentIds.ShouldContain(allowedDepartment.Id);
        vectorRequest.PermissionFilter.PositionIds.ShouldContain(allowedPosition.Id);
        vectorRequest.PermissionFilter.RoleIds.ShouldContain(adminRole.Id);
        vectorRequest.PermissionFilter.UserId.ShouldBe(CurrentUserId);
        vectorRequest.PermissionFilter.UserIds.ShouldContain(CurrentUserId);
        vectorRequest.PermissionFilter.SecurityLevel.ShouldBeGreaterThanOrEqualTo(3);

        var rejectedSpoofedUserRequest = await _retriever.SearchAsync(new RagSearchRequest
        {
            Query = "伪造用户检索",
            UserId = Guid.NewGuid()
        });
        // 请求体里的 UserId 不能代表真实身份；伪造用户请求会在生成 embedding 前被拒绝。
        rejectedSpoofedUserRequest.Count.ShouldBe(0);
        _embeddingClient.CallCount.ShouldBe(1);
        _vectorStore.SearchRequests.Count.ShouldBe(1);
    }

    /// <summary>
    /// 创建并索引一个测试知识条目。
    /// </summary>
    private async Task<KnowledgeChunk> CreateIndexedItemAsync(
        Guid categoryId,
        string title,
        CreateKnowledgePermissionScopeDto? scope = null)
    {
        // 走真实应用服务创建、发布和摄取，避免测试绕过版本状态和权限范围副作用。
        var item = await _itemAppService.CreateAsync(new CreateKnowledgeItemDto
        {
            CategoryId = categoryId,
            Title = title,
            Summary = "MVP-1 retriever test item"
        });

        if (scope != null)
        {
            await _itemAppService.CreatePermissionScopeAsync(item.Id, scope);
        }

        var version = await _itemAppService.CreateVersionAsync(item.Id, new CreateKnowledgeVersionDto
        {
            Title = title,
            ContentText = $"{title} 正文内容。"
        });
        await _itemAppService.PublishVersionAsync(item.Id, version.Id);
        var ingestion = await _ingestionPipeline.IngestAsync(version.Id);
        ingestion.Succeeded.ShouldBeTrue();

        return await GetSingleActiveChunkAsync(version.Id);
    }

    /// <summary>
    /// 创建一个已撤回版本对应的测试 chunk。
    /// </summary>
    private async Task<KnowledgeChunk> CreateWithdrawnIndexedItemAsync(Guid categoryId, string title)
    {
        // 返回已被撤回的 chunk，用来证明 Qdrant 中的旧命中会被主库版本状态挡掉。
        var chunk = await CreateIndexedItemAsync(categoryId, title);
        await _itemAppService.WithdrawVersionAsync(chunk.KnowledgeItemId, chunk.KnowledgeVersionId);
        return chunk;
    }

    /// <summary>
    /// 创建一个索引后权限被收窄的测试 chunk。
    /// </summary>
    private async Task<KnowledgeChunk> CreatePermissionChangedIndexedItemAsync(
        Guid categoryId,
        string title,
        Guid blockedDepartmentId)
    {
        // 先索引，再改变权限范围，用来模拟 Qdrant payload 仍是旧权限快照的情况。
        var chunk = await CreateIndexedItemAsync(categoryId, title);
        await _itemAppService.CreatePermissionScopeAsync(chunk.KnowledgeItemId, new CreateKnowledgePermissionScopeDto
        {
            ScopeType = KnowledgePermissionScopeType.Department,
            TargetId = blockedDepartmentId
        });

        return chunk;
    }

    /// <summary>
    /// 直接插入不同租户下的知识链路，用于验证租户边界。
    /// </summary>
    private Task<KnowledgeChunk> InsertTenantMismatchChunkAsync(string title)
    {
        // 手工插入不同 tenant 的完整知识链路，验证租户边界不会只依赖 Qdrant payload。
        return WithUnitOfWorkAsync(async () =>
        {
            var tenantId = Guid.NewGuid();
            var item = new KnowledgeItem(Guid.NewGuid(), tenantId, title);
            var version = new KnowledgeVersion(Guid.NewGuid(), tenantId, item.Id, 1, title, $"{title} tenant content.");
            version.Publish(DateTime.UtcNow);
            item.SetCurrentPublishedVersion(version.Id);
            var job = new KnowledgeIndexJob(
                Guid.NewGuid(),
                tenantId,
                item.Id,
                version.Id,
                KnowledgeIndexJobTriggerType.Publish);
            job.MarkIndexed(DateTime.UtcNow);
            var chunk = new KnowledgeChunk(
                Guid.NewGuid(),
                tenantId,
                item.Id,
                version.Id,
                job.Id,
                sequence: 0,
                content: $"{title} chunk.",
                contentHash: Guid.NewGuid().ToString("N"),
                sourceTitle: title,
                tokenCount: 8,
                isPublishedSnapshot: true,
                securityLevelSnapshot: 0);

            await _itemRepository.InsertAsync(item, autoSave: true);
            await _versionRepository.InsertAsync(version, autoSave: true);
            await _jobRepository.InsertAsync(job, autoSave: true);
            await _chunkRepository.InsertAsync(chunk, autoSave: true);
            return chunk;
        });
    }

    /// <summary>
    /// 读取某个知识版本的唯一 active chunk。
    /// </summary>
    private Task<KnowledgeChunk> GetSingleActiveChunkAsync(Guid knowledgeVersionId)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var queryable = await _chunkRepository.GetQueryableAsync();
            return queryable.Single(x => x.KnowledgeVersionId == knowledgeVersionId && x.IsActive);
        });
    }

    /// <summary>
    /// 将主库 chunk 包装成模拟 Qdrant 命中。
    /// </summary>
    private static VectorSearchHit ToHit(KnowledgeChunk chunk, double score)
    {
        // Qdrant 命中只作为候选输入；最终 SourceTitle/Content 必须由主库 chunk 决定。
        return new VectorSearchHit
        {
            ChunkId = chunk.Id,
            KnowledgeItemId = chunk.KnowledgeItemId,
            KnowledgeVersionId = chunk.KnowledgeVersionId,
            Content = "Qdrant payload content should not be authoritative.",
            Score = score,
            Payload = new KnowledgeVectorPayload
            {
                TenantId = chunk.TenantId,
                KnowledgeItemId = chunk.KnowledgeItemId,
                KnowledgeVersionId = chunk.KnowledgeVersionId,
                ChunkId = chunk.Id,
                IsPublished = true,
                SecurityLevel = chunk.SecurityLevelSnapshot,
                SourceTitle = chunk.SourceTitle,
                SourcePage = chunk.SourcePage
            }
        };
    }
}
