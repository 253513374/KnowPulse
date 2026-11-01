using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.AiModelManagement;
using KnowPulse.KnowledgeBase;
using KnowPulse.KnowledgeIndexing;
using KnowPulse.RagAI;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Users;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

/// <summary>
/// MVP-1 知识索引应用服务集成测试。
/// 覆盖任务创建/重试、版本生命周期触发、摄取分块、附件读取、embedding 记录和失败落库。
/// </summary>
[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class Mvp1KnowledgeIndexingAppServiceTests : KnowPulseEntityFrameworkCoreTestBase
{
    private readonly IKnowledgeCategoryAppService _categoryAppService;
    private readonly IKnowledgeItemAppService _itemAppService;
    private readonly IKnowledgeIndexJobAppService _indexJobAppService;
    private readonly IKnowledgeIngestionPipeline _ingestionPipeline;
    private readonly IKnowledgeEmbeddingPipeline _embeddingPipeline;
    private readonly IKnowledgeIndexJobProcessor _jobProcessor;
    private readonly TestKnowledgeEmbeddingClient _embeddingClient;
    private readonly TestKnowledgeVectorStore _vectorStore;
    private readonly TestKnowledgeDocumentMarkdownConverter _markdownConverter;
    private readonly IRepository<KnowledgeChunk, Guid> _chunkRepository;
    private readonly IRepository<KnowledgeEmbeddingRecord, Guid> _embeddingRecordRepository;
    private readonly IRepository<EmbeddingModelVersion, Guid> _modelVersionRepository;

    /// <summary>
    /// 初始化知识索引集成测试依赖。
    /// </summary>
    public Mvp1KnowledgeIndexingAppServiceTests()
    {
        _categoryAppService = GetRequiredService<IKnowledgeCategoryAppService>();
        _itemAppService = GetRequiredService<IKnowledgeItemAppService>();
        _indexJobAppService = GetRequiredService<IKnowledgeIndexJobAppService>();
        _ingestionPipeline = GetRequiredService<IKnowledgeIngestionPipeline>();
        _embeddingPipeline = GetRequiredService<IKnowledgeEmbeddingPipeline>();
        _jobProcessor = GetRequiredService<IKnowledgeIndexJobProcessor>();
        _embeddingClient = GetRequiredService<TestKnowledgeEmbeddingClient>();
        _vectorStore = GetRequiredService<TestKnowledgeVectorStore>();
        _markdownConverter = GetRequiredService<TestKnowledgeDocumentMarkdownConverter>();
        _chunkRepository = GetRequiredService<IRepository<KnowledgeChunk, Guid>>();
        _embeddingRecordRepository = GetRequiredService<IRepository<KnowledgeEmbeddingRecord, Guid>>();
        _modelVersionRepository = GetRequiredService<IRepository<EmbeddingModelVersion, Guid>>();
        _embeddingClient.Reset();
        _vectorStore.Reset();
        _markdownConverter.Reset();
    }

    /// <summary>
    /// 验证手动重建知识版本会创建待处理索引任务。
    /// </summary>
    [Fact]
    public async Task ReindexVersionAsync_Should_Create_Pending_Manual_Rebuild_Job()
    {
        // 手动重建必须创建新的 Pending 任务，而不是直接修改知识版本或旧任务。
        var (_, version) = await CreateKnowledgeVersionAsync();

        var job = await _indexJobAppService.ReindexVersionAsync(version.Id);

        job.KnowledgeItemId.ShouldBe(version.KnowledgeItemId);
        job.KnowledgeVersionId.ShouldBe(version.Id);
        job.TriggerType.ShouldBe(KnowledgeIndexJobTriggerType.ManualRebuild);
        job.Status.ShouldBe(KnowledgeIndexJobStatus.Pending);
        job.RetryCount.ShouldBe(0);

        var loaded = await _indexJobAppService.GetAsync(job.Id);
        loaded.Id.ShouldBe(job.Id);

        var jobs = await _indexJobAppService.GetListAsync(new GetKnowledgeIndexJobListInput
        {
            KnowledgeVersionId = version.Id,
            MaxResultCount = 100
        });
        jobs.Items.ShouldContain(x => x.Id == job.Id);
    }

    /// <summary>
    /// 验证失败任务重试会创建新任务，并保留旧失败任务审计信息。
    /// </summary>
    [Fact]
    public async Task RetryAsync_Should_Create_New_Job_And_Keep_Failed_Job_Auditable()
    {
        // Retry 不能覆盖失败任务；原任务的失败原因需要继续可查询。
        var (item, version) = await CreateKnowledgeVersionAsync();
        await _itemAppService.PublishVersionAsync(item.Id, version.Id);
        var job = await _indexJobAppService.ReindexVersionAsync(version.Id);

        var processing = await _indexJobAppService.StartAsync(job.Id);
        processing.Status.ShouldBe(KnowledgeIndexJobStatus.Processing);
        processing.StartedTime.ShouldNotBeNull();

        var failed = await _indexJobAppService.MarkFailedAsync(job.Id, new MarkKnowledgeIndexJobFailedDto
        {
            FailureStage = "chunking",
            FailureReason = "Parser returned no chunks.",
            NextRetryTime = DateTime.UtcNow.AddMinutes(5)
        });
        failed.Status.ShouldBe(KnowledgeIndexJobStatus.Failed);
        failed.RetryCount.ShouldBe(1);
        failed.FailureStage.ShouldBe("chunking");

        var retry = await _indexJobAppService.RetryAsync(failed.Id);
        retry.Id.ShouldNotBe(failed.Id);
        retry.KnowledgeItemId.ShouldBe(failed.KnowledgeItemId);
        retry.KnowledgeVersionId.ShouldBe(failed.KnowledgeVersionId);
        retry.TriggerType.ShouldBe(failed.TriggerType);
        retry.Status.ShouldBe(KnowledgeIndexJobStatus.Pending);
        retry.RetryCount.ShouldBe(0);

        (await _indexJobAppService.GetAsync(retry.Id)).Status.ShouldBe(KnowledgeIndexJobStatus.Pending);
        (await GetChunksAsync(version.Id)).ShouldBeEmpty();
        (await GetEmbeddingRecordsAsync(version.Id)).ShouldBeEmpty();
        _vectorStore.UpsertRequests.ShouldBeEmpty();

        var indexed = await _indexJobAppService.MarkIndexedAsync(retry.Id);
        indexed.Status.ShouldBe(KnowledgeIndexJobStatus.Indexed);
        indexed.CompletedTime.ShouldNotBeNull();
        (await GetChunksAsync(version.Id)).ShouldNotBeEmpty();
        (await GetEmbeddingRecordsAsync(version.Id)).ShouldNotBeEmpty();
        _embeddingClient.CallCount.ShouldBeGreaterThan(0);
        _vectorStore.UpsertRequests.Single().ShouldNotBeEmpty();

        var original = await _indexJobAppService.GetAsync(failed.Id);
        original.Status.ShouldBe(KnowledgeIndexJobStatus.Failed);
        original.FailureReason.ShouldBe("Parser returned no chunks.");

        var failedJobs = await _indexJobAppService.GetListAsync(new GetKnowledgeIndexJobListInput
        {
            KnowledgeVersionId = version.Id,
            Status = KnowledgeIndexJobStatus.Failed,
            MaxResultCount = 100
        });
        failedJobs.Items.Select(x => x.Id).ShouldContain(failed.Id);
        failedJobs.Items.Select(x => x.Id).ShouldNotContain(retry.Id);
    }

    /// <summary>
    /// 验证同一版本存在活动任务时重复重建不会制造并行任务。
    /// </summary>
    [Fact]
    public async Task ReindexVersionAsync_Should_Reuse_Active_Job()
    {
        var (_, version) = await CreateKnowledgeVersionAsync();

        var first = await _indexJobAppService.ReindexVersionAsync(version.Id);
        var second = await _indexJobAppService.ReindexVersionAsync(version.Id);

        second.Id.ShouldBe(first.Id);
        var jobs = await _indexJobAppService.GetListAsync(new GetKnowledgeIndexJobListInput
        {
            KnowledgeVersionId = version.Id,
            MaxResultCount = 100
        });
        jobs.Items.Count(x => x.Status is KnowledgeIndexJobStatus.Pending or KnowledgeIndexJobStatus.Processing)
            .ShouldBe(1);
    }

    /// <summary>
    /// 验证共享处理器完成摄取、embedding、向量写入，并跳过重复投递。
    /// </summary>
    [Fact]
    public async Task Processor_Should_Complete_Full_Pipeline_And_Be_Idempotent()
    {
        var (item, version) = await CreateKnowledgeVersionAsync("P3 完整索引执行内容。");
        await _itemAppService.PublishVersionAsync(item.Id, version.Id);
        var job = await GetSingleJobAsync(version.Id, KnowledgeIndexJobTriggerType.Publish);

        await _jobProcessor.ProcessAsync(job.Id);

        var indexed = await _indexJobAppService.GetAsync(job.Id);
        indexed.Status.ShouldBe(KnowledgeIndexJobStatus.Indexed);
        indexed.ProcessingStage.ShouldBe("completed");
        indexed.ProgressPercent.ShouldBe(100);
        indexed.LastHeartbeatTime.ShouldNotBeNull();
        _vectorStore.UpsertRequests.Count.ShouldBe(1);
        _vectorStore.UpsertRequests[0].ShouldNotBeEmpty();
        _vectorStore.UpsertRequests[0].ShouldAllBe(x => x.Embedding.Length == 1536);
        var embeddingCallCount = _embeddingClient.CallCount;

        await _jobProcessor.ProcessAsync(job.Id);

        _embeddingClient.CallCount.ShouldBe(embeddingCallCount);
        _vectorStore.UpsertRequests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Shared_V1_Should_Keep_Its_Index_When_Private_V2_Is_Prepared_And_Rebuilt()
    {
        var (item, version, share) = await CreateSharedPersonalVersionAsync();
        var oldJob = await GetSingleJobAsync(version.Id, KnowledgeIndexJobTriggerType.Publish);
        var originalChunks = await GetChunksAsync(version.Id);
        var originalEmbedding = await GetEmbeddingRecordsAsync(version.Id);
        var next = await _itemAppService.CreateVersionAsync(item.Id, new CreateKnowledgeVersionDto
        {
            Title = "私有 v2", ContentText = "未共享的新教学反思。"
        });
        await _itemAppService.PublishVersionAsync(item.Id, next.Id);

        (await _itemAppService.GetVersionsAsync(item.Id)).Items.Single(x => x.Id == version.Id)
            .IsCurrentPublished.ShouldBeFalse();
        (await _indexJobAppService.GetAsync(oldJob.Id)).Status.ShouldBe(KnowledgeIndexJobStatus.Indexed);
        (await GetChunksAsync(version.Id)).ShouldAllBe(x => x.IsActive && x.IsPublishedSnapshot);
        (await GetEmbeddingRecordsAsync(version.Id)).Select(x => x.Id).ShouldBe(originalEmbedding.Select(x => x.Id));
        originalEmbedding.ShouldAllBe(x => x.IsActive);

        var privateJob = await GetSingleJobAsync(next.Id, KnowledgeIndexJobTriggerType.Publish);
        await _jobProcessor.ProcessAsync(privateJob.Id);
        var privateChunks = await GetChunksAsync(next.Id);
        var sharedRebuild = await _indexJobAppService.ReindexVersionAsync(version.Id);
        await _jobProcessor.ProcessAsync(sharedRebuild.Id);
        (await _indexJobAppService.GetAsync(sharedRebuild.Id)).Status.ShouldBe(KnowledgeIndexJobStatus.Indexed);
        (await GetChunksAsync(version.Id)).Where(x => !originalChunks.Any(old => old.Id == x.Id))
            .ShouldAllBe(x => x.IsActive && x.IsPublishedSnapshot);
        (await GetChunksAsync(next.Id)).Select(x => x.Id).ShouldBe(privateChunks.Select(x => x.Id));
        (await GetChunksAsync(next.Id)).ShouldAllBe(x => x.IsActive);

        var pendingBeforeRevoke = await _indexJobAppService.ReindexVersionAsync(version.Id);
        await RevokeHistoricalSchoolShareAsync(share.Id);
        var embeddingCalls = _embeddingClient.CallCount;
        await _jobProcessor.ProcessAsync(pendingBeforeRevoke.Id);
        (await _indexJobAppService.GetAsync(pendingBeforeRevoke.Id)).Status.ShouldBe(KnowledgeIndexJobStatus.Outdated);
        _embeddingClient.CallCount.ShouldBe(embeddingCalls);
        (await Should.ThrowAsync<BusinessException>(() => _indexJobAppService.ReindexVersionAsync(version.Id)))
            .Code.ShouldBe(KnowPulseDomainErrorCodes.KnowledgeVersionNotPublished);
        (await _indexJobAppService.GetAsync(privateJob.Id)).Status.ShouldBe(KnowledgeIndexJobStatus.Indexed);
        (await GetChunksAsync(next.Id)).ShouldAllBe(x => x.IsActive);
    }

    [Fact]
    public async Task Share_Without_Current_Author_Teacher_Membership_Should_Not_Retain_Old_Index()
    {
        var (item, version, share) = await CreateSharedPersonalVersionAsync();
        await WithUnitOfWorkAsync(async () =>
        {
            var memberships = GetRequiredService<IRepository<SchoolMembership, Guid>>();
            var teacher = (await memberships.GetQueryableAsync()).Single(x =>
                x.SchoolId == share.SchoolId && x.UserId == share.OwnerUserId && x.Role == SchoolMemberRole.Teacher);
            teacher.EndAt(DateTime.UtcNow.AddSeconds(-1));
            await memberships.UpdateAsync(teacher, autoSave: true);
        });
        var next = await _itemAppService.CreateVersionAsync(item.Id, new CreateKnowledgeVersionDto
        {
            Title = "离校后私有版本", ContentText = "新的个人教学资料。"
        });
        await _itemAppService.PublishVersionAsync(item.Id, next.Id);
        (await GetChunksAsync(version.Id)).ShouldAllBe(x => !x.IsActive);
        (await GetSingleJobAsync(version.Id, KnowledgeIndexJobTriggerType.Publish))
            .Status.ShouldBe(KnowledgeIndexJobStatus.Outdated);
    }

    /// <summary>
    /// 验证真实执行失败会记录阶段并安排指数退避重试。
    /// </summary>
    [Fact]
    public async Task Processor_Should_Schedule_Retry_When_Embedding_Fails()
    {
        var (item, version) = await CreateKnowledgeVersionAsync("P3 embedding 失败重试内容。");
        await _itemAppService.PublishVersionAsync(item.Id, version.Id);
        var job = await GetSingleJobAsync(version.Id, KnowledgeIndexJobTriggerType.Publish);
        _embeddingClient.ShouldFail = true;
        _embeddingClient.FailureReason = "P3 test gateway timeout.";

        await _jobProcessor.ProcessAsync(job.Id);

        var failed = await _indexJobAppService.GetAsync(job.Id);
        failed.Status.ShouldBe(KnowledgeIndexJobStatus.Failed);
        failed.FailureStage.ShouldBe("embedding");
        failed.RetryCount.ShouldBe(1);
        failed.CanRetry.ShouldBeTrue();
        failed.NextRetryTime.ShouldNotBeNull();
        failed.NextRetryTime!.Value.ShouldBeGreaterThan(DateTime.UtcNow);
        _vectorStore.UpsertRequests.ShouldBeEmpty();

        var firstChunkIds = (await GetChunksAsync(version.Id)).Select(x => x.Id).ToArray();
        _embeddingClient.ShouldFail = false;
        await _jobProcessor.ProcessAsync(job.Id, ignoreRetrySchedule: true);

        (await _indexJobAppService.GetAsync(job.Id)).Status.ShouldBe(KnowledgeIndexJobStatus.Indexed);
        var resumedChunks = await GetChunksAsync(version.Id);
        resumedChunks.Select(x => x.Id).ShouldBe(firstChunkIds);
        resumedChunks.ShouldAllBe(x => x.IsActive && x.IsPublishedSnapshot);
        _vectorStore.UpsertRequests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task IngestAsync_Should_Keep_Existing_Chunks_When_The_Same_Job_Source_Changes()
    {
        var (item, version) = await CreateKnowledgeVersionAsync("第一次摄取的正文。");
        var job = await _indexJobAppService.ReindexVersionAsync(version.Id);
        (await _ingestionPipeline.IngestAsync(version.Id, job.Id, completeIndexJob: false)).Succeeded.ShouldBeTrue();
        var firstChunks = await GetChunksAsync(version.Id);
        await _itemAppService.UpdateDraftVersionAsync(item.Id, version.Id, new()
        {
            Title = version.Title, ContentText = "已经修改的正文，应使用新的索引任务。"
        });

        var retry = await _ingestionPipeline.IngestAsync(version.Id, job.Id, completeIndexJob: false);

        retry.Succeeded.ShouldBeFalse();
        retry.FailureStage.ShouldBe("persistence");
        var chunks = await GetChunksAsync(version.Id);
        chunks.Select(x => x.Id).ShouldBe(firstChunks.Select(x => x.Id));
        chunks.ShouldAllBe(x => x.Content == "第一次摄取的正文。");
    }

    /// <summary>
    /// 验证人工恢复会终止卡住任务并保留恢复来源。
    /// </summary>
    [Fact]
    public async Task RecoverAsync_Should_Create_Lineage_And_Keep_Stuck_Job_Auditable()
    {
        var (_, version) = await CreateKnowledgeVersionAsync();
        var stuck = await _indexJobAppService.ReindexVersionAsync(version.Id);
        await _indexJobAppService.StartAsync(stuck.Id);

        var recovery = await _indexJobAppService.RecoverAsync(stuck.Id);

        recovery.Status.ShouldBe(KnowledgeIndexJobStatus.Pending);
        recovery.TriggerType.ShouldBe(KnowledgeIndexJobTriggerType.ManualRebuild);
        recovery.RetryOfJobId.ShouldBe(stuck.Id);
        var original = await _indexJobAppService.GetAsync(stuck.Id);
        original.Status.ShouldBe(KnowledgeIndexJobStatus.Failed);
        original.FailureStage.ShouldBe("recovery");
    }

    /// <summary>
    /// 验证知识版本发布、撤回和归档会正确影响索引状态。
    /// </summary>
    [Fact]
    public async Task Version_Lifecycle_Should_Create_Publish_Jobs_And_Outdate_Index_State()
    {
        // 发布会创建索引任务；撤回/归档会让旧 chunk 和任务失效，防止 RAG 返回已下架内容。
        var (item, version) = await CreateKnowledgeVersionAsync();

        var published = await _itemAppService.PublishVersionAsync(item.Id, version.Id);
        published.Status.ShouldBe(KnowledgeVersionStatus.Published);
        published.IsCurrentPublished.ShouldBeTrue();

        var publishJob = await GetSingleJobAsync(version.Id, KnowledgeIndexJobTriggerType.Publish);
        publishJob.Status.ShouldBe(KnowledgeIndexJobStatus.Pending);

        var chunkId = await InsertChunkAsync(item.Id, version.Id, publishJob.Id, sequence: 0);
        var withdrawn = await _itemAppService.WithdrawVersionAsync(item.Id, version.Id);
        withdrawn.Status.ShouldBe(KnowledgeVersionStatus.Withdrawn);
        withdrawn.IsCurrentPublished.ShouldBeFalse();

        var withdrawnChunk = await GetChunkAsync(chunkId);
        withdrawnChunk.IsActive.ShouldBeFalse();
        withdrawnChunk.IsPublishedSnapshot.ShouldBeFalse();
        var outdatedPublishJob = await _indexJobAppService.GetAsync(publishJob.Id);
        outdatedPublishJob.Status.ShouldBe(KnowledgeIndexJobStatus.Outdated);

        var (archiveItem, archiveVersion) = await CreateKnowledgeVersionAsync();
        var archivedPublished = await _itemAppService.PublishVersionAsync(archiveItem.Id, archiveVersion.Id);
        archivedPublished.Status.ShouldBe(KnowledgeVersionStatus.Published);
        var archivePublishJob = await GetSingleJobAsync(archiveVersion.Id, KnowledgeIndexJobTriggerType.Publish);
        var archivedChunkId = await InsertChunkAsync(archiveItem.Id, archiveVersion.Id, archivePublishJob.Id, sequence: 0);

        var archived = await _itemAppService.ArchiveVersionAsync(archiveItem.Id, archiveVersion.Id);
        archived.Status.ShouldBe(KnowledgeVersionStatus.Archived);
        archived.IsCurrentPublished.ShouldBeFalse();

        var archivedChunk = await GetChunkAsync(archivedChunkId);
        archivedChunk.IsActive.ShouldBeFalse();
        archivedChunk.IsPublishedSnapshot.ShouldBeFalse();
        var outdatedArchiveJob = await _indexJobAppService.GetAsync(archivePublishJob.Id);
        outdatedArchiveJob.Status.ShouldBe(KnowledgeIndexJobStatus.Outdated);
    }

    /// <summary>
    /// 验证权限范围变化会创建重建任务并使旧 chunk 过期。
    /// </summary>
    [Fact]
    public async Task Permission_Scope_Changes_Should_Queue_Reindex_And_Outdate_Chunks()
    {
        // 权限范围改变后必须重新索引，因为向量 payload 中保存的是权限快照。
        var (item, version) = await CreateKnowledgeVersionAsync(legacy: true);
        await _itemAppService.PublishVersionAsync(item.Id, version.Id);
        var publishJob = await GetSingleJobAsync(version.Id, KnowledgeIndexJobTriggerType.Publish);
        var firstChunkId = await InsertChunkAsync(item.Id, version.Id, publishJob.Id, sequence: 0);

        var scope = await _itemAppService.CreatePermissionScopeAsync(item.Id, new CreateKnowledgePermissionScopeDto
        {
            ScopeType = KnowledgePermissionScopeType.Department,
            TargetId = Guid.NewGuid(),
            MinimumSecurityLevel = 1
        });

        var firstChunk = await GetChunkAsync(firstChunkId);
        firstChunk.IsActive.ShouldBeFalse();
        firstChunk.IsPublishedSnapshot.ShouldBeFalse();
        var outdatedPublishJob = await _indexJobAppService.GetAsync(publishJob.Id);
        outdatedPublishJob.Status.ShouldBe(KnowledgeIndexJobStatus.Outdated);

        var jobsAfterCreate = await _indexJobAppService.GetListAsync(new GetKnowledgeIndexJobListInput
        {
            KnowledgeVersionId = version.Id,
            MaxResultCount = 100
        });
        jobsAfterCreate.Items.Count(x => x.TriggerType == KnowledgeIndexJobTriggerType.PermissionChanged).ShouldBe(1);
        var firstPermissionJob = jobsAfterCreate.Items.Single(x => x.TriggerType == KnowledgeIndexJobTriggerType.PermissionChanged);
        firstPermissionJob.Status.ShouldBe(KnowledgeIndexJobStatus.Pending);

        // 已发布知识不能删除最后一条显式规则；先增加第二条定向规则，再验证删除仍会触发重建。
        await _itemAppService.CreatePermissionScopeAsync(item.Id, new CreateKnowledgePermissionScopeDto
        {
            ScopeType = KnowledgePermissionScopeType.Department,
            TargetId = Guid.NewGuid()
        });
        var jobsAfterSecondScope = await _indexJobAppService.GetListAsync(new GetKnowledgeIndexJobListInput
        {
            KnowledgeVersionId = version.Id,
            MaxResultCount = 100
        });
        var secondPermissionJob = jobsAfterSecondScope.Items
            .Where(x => x.TriggerType == KnowledgeIndexJobTriggerType.PermissionChanged)
            .OrderByDescending(x => x.CreationTime)
            .First();

        var secondChunkId = await InsertChunkAsync(item.Id, version.Id, secondPermissionJob.Id, sequence: 1);
        await _itemAppService.DeletePermissionScopeAsync(item.Id, scope.Id);

        var secondChunk = await GetChunkAsync(secondChunkId);
        secondChunk.IsActive.ShouldBeFalse();
        secondChunk.IsPublishedSnapshot.ShouldBeFalse();

        var jobsAfterDelete = await _indexJobAppService.GetListAsync(new GetKnowledgeIndexJobListInput
        {
            KnowledgeVersionId = version.Id,
            MaxResultCount = 100
        });
        jobsAfterDelete.Items.Count(x => x.TriggerType == KnowledgeIndexJobTriggerType.PermissionChanged).ShouldBe(3);
    }

    /// <summary>
    /// 验证知识正文可被摄取为 chunk。
    /// </summary>
    [Fact]
    public async Task IngestAsync_Should_Create_Chunks_From_Version_Content()
    {
        // 最小摄取路径：知识版本正文 -> Markdown 文档流 -> chunk -> Indexed 任务状态。
        var (_, version) = await CreateKnowledgeVersionAsync("## 安全制度\n\n第一段内容。\n\n第二段内容。");
        await _itemAppService.PublishVersionAsync(version.KnowledgeItemId, version.Id);

        var result = await _ingestionPipeline.IngestAsync(version.Id);

        result.Succeeded.ShouldBeTrue();
        result.Chunks.Count.ShouldBeGreaterThan(0);
        var chunks = await GetChunksAsync(version.Id);
        chunks.Count.ShouldBe(result.Chunks.Count);
        chunks.ShouldAllBe(x => x.IsActive);
        chunks[0].Sequence.ShouldBe(0);
        chunks[0].SourceTitle.ShouldBe(version.Title);
        chunks[0].ContentHash.Length.ShouldBe(64);
        chunks[0].TokenCount.ShouldBeGreaterThan(0);

        var job = await GetSingleJobAsync(version.Id, KnowledgeIndexJobTriggerType.Publish);
        job.Status.ShouldBe(KnowledgeIndexJobStatus.Indexed);
        (await GetEmbeddingRecordsAsync(version.Id)).Count.ShouldBe(chunks.Count);
        _vectorStore.UpsertRequests.Single().Count.ShouldBe(chunks.Count);
    }

    /// <summary>
    /// 验证文本和 Markdown 附件可参与摄取。
    /// </summary>
    [Fact]
    public async Task IngestAsync_Should_Read_Text_And_Markdown_Attachments()
    {
        // MVP-1 只允许文本和 Markdown 附件，附件来源标题要体现在生成的 chunk 上。
        var (item, version) = await CreateKnowledgeVersionAsync(contentText: null);
        await UploadAttachmentAsync(item.Id, version.Id, "notes.txt", "text/plain", "附件纯文本内容。");
        await UploadAttachmentAsync(item.Id, version.Id, "guide.md", "text/markdown", "# 操作指引\n\nMarkdown 附件内容。");
        await _itemAppService.PublishVersionAsync(item.Id, version.Id);

        var result = await _ingestionPipeline.IngestAsync(version.Id);

        result.Succeeded.ShouldBeTrue();
        var chunks = await GetChunksAsync(version.Id);
        chunks.Select(x => x.SourceTitle).ShouldContain("notes.txt");
        chunks.Select(x => x.SourceTitle).ShouldContain("guide.md");
        chunks.ShouldAllBe(x => x.TokenCount > 0);
    }

    /// <summary>
    /// 验证 MarkItDown 支持的非文本附件可转换为 Markdown 后参与摄取。
    /// </summary>
    [Fact]
    public async Task IngestAsync_Should_Read_MarkItDown_Convertible_Attachments()
    {
        // PDF/Office 等附件不再直接判定为仅附件；它们通过转换器进入 Markdown 文档流。
        var (item, version) = await CreateKnowledgeVersionAsync(contentText: null);
        await UploadAttachmentAsync(item.Id, version.Id, "manual.pdf", "application/pdf", "SECRET-PDF-CONTENT");
        await UploadAttachmentAsync(item.Id, version.Id, "guide.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "SECRET-DOCX-CONTENT");
        await _itemAppService.PublishVersionAsync(item.Id, version.Id);

        var result = await _ingestionPipeline.IngestAsync(version.Id);

        result.Succeeded.ShouldBeTrue();
        _markdownConverter.ConvertedFileNames.ShouldContain("manual.pdf");
        _markdownConverter.ConvertedFileNames.ShouldContain("guide.docx");
        var chunks = await GetChunksAsync(version.Id);
        chunks.Select(x => x.SourceTitle).ShouldContain("manual.pdf");
        chunks.Select(x => x.SourceTitle).ShouldContain("guide.docx");
        chunks.Select(x => x.SourcePage).ShouldContain(1);
        chunks.ShouldContain(x => x.Content.Contains("转换后的 .pdf 内容。"));
        chunks.ShouldContain(x => x.Content.Contains("转换后的 .docx 内容。"));
        chunks.ShouldAllBe(x => !x.Content.Contains("SECRET-"));
    }

    /// <summary>
    /// 验证空知识内容会让摄取任务在 reader 阶段失败。
    /// </summary>
    [Fact]
    public async Task IngestAsync_Should_Mark_Job_Failed_For_Empty_Content()
    {
        // 没有正文或可读附件时，失败必须落到 reader 阶段，方便运维页定位。
        var (item, version) = await CreateKnowledgeVersionAsync(contentText: null);
        await _itemAppService.PublishVersionAsync(item.Id, version.Id);

        var result = await _ingestionPipeline.IngestAsync(version.Id);

        result.Succeeded.ShouldBeFalse();
        result.FailureStage.ShouldBe("reader");
        result.FailureReason.ShouldBe("No readable knowledge content was found.");

        var job = await GetSingleJobAsync(version.Id, KnowledgeIndexJobTriggerType.Publish);
        job.Status.ShouldBe(KnowledgeIndexJobStatus.Failed);
        job.RetryCount.ShouldBe(1);
        job.FailureReason.ShouldBe(result.FailureReason);
    }

    /// <summary>
    /// 验证长内容会被切成多个 chunk，重复索引会使旧 chunk 过期。
    /// </summary>
    [Fact]
    public async Task IngestAsync_Should_Split_Long_Content_And_Outdate_Old_Chunks_On_Reindex()
    {
        // 重复摄取同一版本会保留旧 chunk 审计链路，但只让新 chunk 保持 active。
        var longContent = string.Join(
            "\n\n",
            Enumerable.Range(1, 80).Select(i => $"第 {i} 段：这是用于验证超长内容分块的知识正文，包含足够多的文字以产生多个 chunk。"));
        var (item, version) = await CreateKnowledgeVersionAsync(longContent);
        await _itemAppService.PublishVersionAsync(item.Id, version.Id);

        var firstResult = await _ingestionPipeline.IngestAsync(version.Id);
        firstResult.Succeeded.ShouldBeTrue();
        var firstChunks = await GetChunksAsync(version.Id);
        firstChunks.Count(x => x.IsActive).ShouldBeGreaterThan(1);
        var firstChunkIds = firstChunks.Select(x => x.Id).ToHashSet();

        await _indexJobAppService.ReindexVersionAsync(version.Id);
        var secondResult = await _ingestionPipeline.IngestAsync(version.Id);
        secondResult.Succeeded.ShouldBeTrue();

        var allChunks = await GetChunksAsync(version.Id);
        allChunks.Where(x => firstChunkIds.Contains(x.Id)).ShouldAllBe(x => !x.IsActive && !x.IsPublishedSnapshot);
        allChunks.Count(x => x.IsActive).ShouldBeGreaterThan(1);
    }

    /// <summary>
    /// 验证不支持的附件类型不会把文件内容写入失败原因。
    /// </summary>
    [Fact]
    public async Task IngestAsync_Should_Fail_Unsupported_Attachment_Without_Storing_File_Content_In_Failure()
    {
        // 不支持的附件只能写入类型错误，不能把文件内容泄漏到 FailureReason。
        var (item, version) = await CreateKnowledgeVersionAsync(contentText: null);
        await UploadAttachmentAsync(item.Id, version.Id, "manual.exe", "application/octet-stream", "SECRET-BINARY-CONTENT");
        await _itemAppService.PublishVersionAsync(item.Id, version.Id);

        var result = await _ingestionPipeline.IngestAsync(version.Id);

        result.Succeeded.ShouldBeFalse();
        result.FailureStage.ShouldBe("reader");
        result.FailureReason.ShouldNotBeNull();
        result.FailureReason!.ShouldContain("Unsupported attachment type");
        result.FailureReason.ShouldNotContain("SECRET-BINARY-CONTENT");

        var job = await GetSingleJobAsync(version.Id, KnowledgeIndexJobTriggerType.Publish);
        job.Status.ShouldBe(KnowledgeIndexJobStatus.Failed);
        job.FailureReason.ShouldNotBeNull();
        job.FailureReason!.ShouldNotContain("SECRET-BINARY-CONTENT");
    }

    /// <summary>
    /// 验证 embedding pipeline 会创建模型版本和 embedding 追踪记录。
    /// </summary>
    [Fact]
    public async Task GenerateForVersionAsync_Should_Create_ModelVersion_And_EmbeddingRecords()
    {
        // embedding 记录必须绑定模型版本、维度和 content hash，支持后续模型升级和重建判断。
        var (item, version) = await CreateKnowledgeVersionAsync("## Embedding\n\n需要生成向量的知识内容。");
        await _itemAppService.PublishVersionAsync(item.Id, version.Id);
        await _ingestionPipeline.IngestAsync(version.Id, completeIndexJob: false);
        var chunks = await GetChunksAsync(version.Id);

        var result = await _embeddingPipeline.GenerateForVersionAsync(version.Id);

        result.Succeeded.ShouldBeTrue();
        result.EmbeddingModelVersionId.ShouldNotBeNull();
        result.ProcessedChunkCount.ShouldBe(chunks.Count);
        _embeddingClient.CallCount.ShouldBe(chunks.Count);

        var modelVersion = await GetModelVersionAsync(result.EmbeddingModelVersionId!.Value);
        modelVersion.Provider.ShouldBe("LiteLLM");
        modelVersion.ModelName.ShouldBe("knowpulse-embedding-default");
        modelVersion.ModelVersion.ShouldBe((await WithUnitOfWorkAsync(() => GetRequiredService<IAiModelResolver>().ResolveEmbeddingAsync())).BusinessModelVersion);
        modelVersion.VectorDimension.ShouldBe(1536);
        modelVersion.CollectionName.ShouldBe(GetRequiredService<IOptions<RagAiOptions>>().Value.VectorStore.KnowledgeCollectionName);
        modelVersion.IsDefault.ShouldBeTrue();

        var records = await GetEmbeddingRecordsAsync(version.Id);
        records.Count.ShouldBe(chunks.Count);
        records.ShouldAllBe(x => x.EmbeddingModelVersionId == modelVersion.Id);
        records.ShouldAllBe(x => x.VectorDimension == 1536);

        foreach (var record in records)
        {
            Guid.TryParse(record.VectorStoreRecordId, out _).ShouldBeTrue();
            var chunk = chunks.Single(x => x.Id == record.KnowledgeChunkId);
            record.ContentHash.ShouldBe(chunk.ContentHash);
            record.VectorStoreRecordId.ShouldBe(chunk.Id.ToString("D"));
        }

        var job = await GetSingleJobAsync(version.Id, KnowledgeIndexJobTriggerType.Publish);
        job.EmbeddingModelVersionId.ShouldBe(modelVersion.Id);
        job.Status.ShouldBe(KnowledgeIndexJobStatus.Indexed);
    }

    /// <summary>
    /// 切换模型后，旧 collection 或维度的任务必须在调用模型和写入向量前失败，并保留已有记录。
    /// </summary>
    [Theory]
    [InlineData("knowledge_chunks_previous_provider", 1536)]
    [InlineData("knowledge_chunks_1536_mvp1", 1024)]
    public async Task GenerateForVersionAsync_Should_Reject_ModelVersion_Outside_Current_VectorStore(
        string collectionName,
        int vectorDimension)
    {
        var (item, version) = await CreateKnowledgeVersionAsync("切换模型前已有的资料分块。");
        await _itemAppService.PublishVersionAsync(item.Id, version.Id);
        await _ingestionPipeline.IngestAsync(version.Id, completeIndexJob: false);
        var chunk = (await GetChunksAsync(version.Id)).Single();
        var modelVersionId = await InsertEmbeddingModelVersionAsync(
            "previous-embedding-model", "previous-provider-version", collectionName, vectorDimension);
        var recordId = Guid.NewGuid();
        await WithUnitOfWorkAsync(() => _embeddingRecordRepository.InsertAsync(new KnowledgeEmbeddingRecord(
            recordId, null, chunk.Id, version.Id, modelVersionId, chunk.Id.ToString("D"),
            chunk.ContentHash, vectorDimension, DateTime.UtcNow), autoSave: true));

        var result = await _embeddingPipeline.GenerateForVersionAsync(version.Id, modelVersionId);

        _embeddingClient.CallCount.ShouldBe(0);
        _vectorStore.UpsertRequests.ShouldBeEmpty();
        result.Succeeded.ShouldBeFalse();
        result.FailureStage.ShouldBe("model-switch");
        result.FailureReason.ShouldNotBeNull();
        result.FailureReason!.ShouldContain("模型已切换");
        result.FailureReason.ShouldContain("新建");
        var record = (await GetEmbeddingRecordsAsync(version.Id)).ShouldHaveSingleItem();
        record.Id.ShouldBe(recordId);
        record.IsActive.ShouldBeTrue();
        record.DeletedTime.ShouldBeNull();
        record.EmbeddingModelVersionId.ShouldBe(modelVersionId);
        var job = await GetSingleJobAsync(version.Id, KnowledgeIndexJobTriggerType.Publish);
        job.Status.ShouldBe(KnowledgeIndexJobStatus.Failed);
        job.FailureStage.ShouldBe("model-switch");
    }

    /// <summary>
    /// 验证需要重建的 chunk 按 embedding 模型版本隔离计算。
    /// </summary>
    [Fact]
    public async Task GetChunksRequiringRebuildAsync_Should_Filter_By_ModelVersion()
    {
        // 重建计划按模型版本计算，避免不同模型版本的记录互相抵消。
        var (item, version) = await CreateKnowledgeVersionAsync("重建查询测试内容。");
        await _itemAppService.PublishVersionAsync(item.Id, version.Id);
        await _ingestionPipeline.IngestAsync(version.Id, completeIndexJob: false);
        var chunks = await GetChunksAsync(version.Id);
        var modelVersionId = await InsertEmbeddingModelVersionAsync("knowpulse-embedding-shadow", "2026-06-shadow");

        var before = await _embeddingPipeline.GetChunksRequiringRebuildAsync(modelVersionId);

        before.Count.ShouldBe(chunks.Count);
        before.ShouldAllBe(x => x.EmbeddingModelVersionId == modelVersionId);
        before.ShouldAllBe(x => x.Reason == "MissingEmbeddingRecord");

        var result = await _embeddingPipeline.GenerateForVersionAsync(version.Id, modelVersionId);
        result.Succeeded.ShouldBeTrue();

        var after = await _embeddingPipeline.GetChunksRequiringRebuildAsync(modelVersionId);
        after.Count.ShouldBe(0);
    }

    /// <summary>
    /// 验证 embedding client 失败时任务失败且不写入半成品记录。
    /// </summary>
    [Fact]
    public async Task GenerateForVersionAsync_Should_Record_Failure_When_Embedding_Client_Fails()
    {
        // embedding client 失败时不应写入半成品 embedding 记录，任务状态应落为 Failed。
        var (item, version) = await CreateKnowledgeVersionAsync("会触发 embedding 失败的测试内容。");
        await _itemAppService.PublishVersionAsync(item.Id, version.Id);
        await _ingestionPipeline.IngestAsync(version.Id, completeIndexJob: false);
        _embeddingClient.ShouldFail = true;
        _embeddingClient.FailureReason = "Test gateway timeout.";

        var result = await _embeddingPipeline.GenerateForVersionAsync(version.Id);

        result.Succeeded.ShouldBeFalse();
        result.FailureStage.ShouldBe("embedding");
        result.FailureReason.ShouldBe("Test gateway timeout.");

        var records = await GetEmbeddingRecordsAsync(version.Id);
        records.Count.ShouldBe(0);
        var job = await GetSingleJobAsync(version.Id, KnowledgeIndexJobTriggerType.Publish);
        job.Status.ShouldBe(KnowledgeIndexJobStatus.Failed);
        job.RetryCount.ShouldBe(1);
        job.FailureStage.ShouldBe("embedding");
        job.FailureReason.ShouldBe("Test gateway timeout.");
    }

    /// <summary>
    /// 验证 LiteLLM embedding client 在缺少 API key 时返回清晰失败信息。
    /// </summary>
    [Fact]
    public async Task LiteLlmEmbeddingClient_Should_Return_Clear_Error_When_Key_Is_Missing()
    {
        // 缺少 LiteLLM key 是常见本地配置问题，需要返回明确失败结果而不是抛未处理异常。
        const string envName = "__KNOWPULSE_TEST_MISSING_LITELLM_KEY__";
        Environment.SetEnvironmentVariable(envName, null);
        var client = new LiteLlmKnowledgeEmbeddingClient(Options.Create(new RagAiOptions
        {
            LiteLlm =
            {
                ApiKeyEnvironmentVariable = envName
            }
        }));

        var result = await client.GenerateAsync(new KnowledgeEmbeddingClientRequest
        {
            Input = "test",
            Provider = "LiteLLM",
            ModelName = "knowpulse-embedding-default",
            ModelVersion = "2026-06-mvp1",
            VectorDimension = 1536,
            CollectionName = "knowledge_chunks_1536_mvp1"
        });

        result.Succeeded.ShouldBeFalse();
        result.FailureStage.ShouldBe("embedding");
        result.FailureReason.ShouldBe($"LiteLLM API key environment variable '{envName}' is not configured.");
    }

    /// <summary>
    /// 创建测试用知识条目和知识版本。
    /// </summary>
    private async Task<(KnowledgeItemDto Item, KnowledgeVersionDto Version)> CreateKnowledgeVersionAsync(string? contentText = "MVP-1 indexable content", bool legacy = false)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await _categoryAppService.CreateAsync(new CreateUpdateKnowledgeCategoryDto
        {
            Code = $"KI-{suffix}",
            Name = $"Index Category {suffix}"
        });

        KnowledgeItemDto item;
        if (legacy)
        {
            // 企业组织权限测试保留无教师归属的历史资料语义。
            var entity = await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<KnowledgeItem, Guid>>().InsertAsync(
                new KnowledgeItem(Guid.NewGuid(), null, $"Index Knowledge {suffix}", category.Id), autoSave: true));
            item = await _itemAppService.GetAsync(entity.Id);
        }
        else
        {
            item = await _itemAppService.CreateAsync(new CreateKnowledgeItemDto
            {
                CategoryId = category.Id, Title = $"Index Knowledge {suffix}", Summary = "MVP-1 indexing integration test item"
            });
        }

        var version = await _itemAppService.CreateVersionAsync(item.Id, new CreateKnowledgeVersionDto
        {
            Title = $"Index Version {suffix}",
            ContentText = contentText
        });

        return (item, version);
    }

    /// <summary>
    /// 用真实发布与索引流程准备版本，并播种历史学校共享记录。
    /// </summary>
    private async Task<(KnowledgeItemDto Item, KnowledgeVersionDto Version, SchoolKnowledgeShare Share)>
        CreateSharedPersonalVersionAsync()
    {
        var ownerId = GetRequiredService<ICurrentUser>().Id!.Value;
        await WithUnitOfWorkAsync(async () =>
        {
            var users = GetRequiredService<IRepository<IdentityUser, Guid>>();
            if (await users.FindAsync(ownerId) == null)
            {
                await users.InsertAsync(new IdentityUser(ownerId, $"index-owner-{ownerId:N}",
                    $"index-owner-{ownerId:N}@example.invalid"), autoSave: true);
            }
        });
        var school = new School(Guid.NewGuid(), null, $"IDX-{Guid.NewGuid():N}", "历史索引测试学校");
        await WithUnitOfWorkAsync(async () =>
        {
            await GetRequiredService<IRepository<School, Guid>>().InsertAsync(school, autoSave: true);
            await GetRequiredService<IRepository<SchoolMembership, Guid>>().InsertAsync(new SchoolMembership(
                Guid.NewGuid(), null, school.Id, ownerId, SchoolMemberRole.Teacher,
                DateTime.UtcNow.AddMinutes(-1)), autoSave: true);
        });
        var item = await GetRequiredService<IKnowledgeLibraryAppService>().CreateItemAsync(new CreateKnowledgeItemDto
        {
            Title = "教师个人材料"
        });
        var version = await _itemAppService.CreateVersionAsync(item.Id, new CreateKnowledgeVersionDto
        {
            Title = "共享 v1", ContentText = "第一版可共享的教学方法。"
        });
        await _itemAppService.PublishVersionAsync(item.Id, version.Id);
        var job = await GetSingleJobAsync(version.Id, KnowledgeIndexJobTriggerType.Publish);
        await _jobProcessor.ProcessAsync(job.Id);
        (await _indexJobAppService.GetAsync(job.Id)).Status.ShouldBe(KnowledgeIndexJobStatus.Indexed);
        var share = await SeedHistoricalSchoolShareAsync(item.Id, version.Id, school.Id,
            SchoolKnowledgeShareUses.Read | SchoolKnowledgeShareUses.Retrieve);
        return (item, version, share);
    }

    private Task UploadAttachmentAsync(Guid itemId, Guid versionId, string fileName, string contentType, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return _itemAppService.UploadAttachmentAsync(
            itemId,
            versionId,
            new RemoteStreamContent(new MemoryStream(bytes), fileName, contentType, bytes.Length));
    }

    /// <summary>
    /// 查询指定知识版本和触发类型对应的唯一索引任务。
    /// </summary>
    private async Task<KnowledgeIndexJobDto> GetSingleJobAsync(Guid knowledgeVersionId, KnowledgeIndexJobTriggerType triggerType)
    {
        var jobs = await _indexJobAppService.GetListAsync(new GetKnowledgeIndexJobListInput
        {
            KnowledgeVersionId = knowledgeVersionId,
            MaxResultCount = 100
        });

        return jobs.Items.Single(x => x.TriggerType == triggerType);
    }

    /// <summary>
    /// 直接插入测试 chunk。
    /// </summary>
    private Task<Guid> InsertChunkAsync(Guid knowledgeItemId, Guid knowledgeVersionId, Guid knowledgeIndexJobId, int sequence)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var chunk = new KnowledgeChunk(
                Guid.NewGuid(),
                tenantId: null,
                knowledgeItemId,
                knowledgeVersionId,
                knowledgeIndexJobId,
                sequence,
                $"Indexed chunk {sequence}",
                Guid.NewGuid().ToString("N"),
                $"Source {sequence}");

            await _chunkRepository.InsertAsync(chunk, autoSave: true);
            return chunk.Id;
        });
    }

    /// <summary>
    /// 按编号读取测试 chunk。
    /// </summary>
    private Task<KnowledgeChunk> GetChunkAsync(Guid id)
    {
        return WithUnitOfWorkAsync(() => _chunkRepository.GetAsync(id));
    }

    /// <summary>
    /// 读取某个知识版本的全部测试 chunk。
    /// </summary>
    private Task<List<KnowledgeChunk>> GetChunksAsync(Guid knowledgeVersionId)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var queryable = await _chunkRepository.GetQueryableAsync();
            return queryable
                .Where(x => x.KnowledgeVersionId == knowledgeVersionId)
                .OrderBy(x => x.Sequence)
                .ToList();
        });
    }

    /// <summary>
    /// 读取某个知识版本的 embedding 追踪记录。
    /// </summary>
    private Task<List<KnowledgeEmbeddingRecord>> GetEmbeddingRecordsAsync(Guid knowledgeVersionId)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var queryable = await _embeddingRecordRepository.GetQueryableAsync();
            return queryable
                .Where(x => x.KnowledgeVersionId == knowledgeVersionId)
                .OrderBy(x => x.VectorStoreRecordId)
                .ToList();
        });
    }

    /// <summary>
    /// 按编号读取 embedding 模型版本。
    /// </summary>
    private Task<EmbeddingModelVersion> GetModelVersionAsync(Guid id)
    {
        return WithUnitOfWorkAsync(() => _modelVersionRepository.GetAsync(id));
    }

    /// <summary>
    /// 直接插入测试 embedding 模型版本。
    /// </summary>
    private Task<Guid> InsertEmbeddingModelVersionAsync(
        string modelName,
        string modelVersion,
        string? collectionName = null,
        int vectorDimension = 1536)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var model = new EmbeddingModelVersion(
                Guid.NewGuid(),
                tenantId: null,
                provider: "LiteLLM",
                modelName,
                modelVersion,
                vectorDimension,
                collectionName ?? GetRequiredService<IOptions<RagAiOptions>>().Value.VectorStore.KnowledgeCollectionName);

            await _modelVersionRepository.InsertAsync(model, autoSave: true);
            return model.Id;
        });
    }
}
