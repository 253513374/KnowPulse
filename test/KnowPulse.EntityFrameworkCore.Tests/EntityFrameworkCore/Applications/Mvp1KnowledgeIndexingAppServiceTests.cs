using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using KnowPulse.KnowledgeBase;
using KnowPulse.KnowledgeIndexing;
using KnowPulse.RagAI;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;
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
    private readonly TestKnowledgeEmbeddingClient _embeddingClient;
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
        _embeddingClient = GetRequiredService<TestKnowledgeEmbeddingClient>();
        _chunkRepository = GetRequiredService<IRepository<KnowledgeChunk, Guid>>();
        _embeddingRecordRepository = GetRequiredService<IRepository<KnowledgeEmbeddingRecord, Guid>>();
        _modelVersionRepository = GetRequiredService<IRepository<EmbeddingModelVersion, Guid>>();
        _embeddingClient.Reset();
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
        var (_, version) = await CreateKnowledgeVersionAsync();
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

        var indexed = await _indexJobAppService.StartAsync(retry.Id);
        indexed = await _indexJobAppService.MarkIndexedAsync(indexed.Id);
        indexed.Status.ShouldBe(KnowledgeIndexJobStatus.Indexed);
        indexed.CompletedTime.ShouldNotBeNull();

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
        var (item, version) = await CreateKnowledgeVersionAsync();
        await _itemAppService.PublishVersionAsync(item.Id, version.Id);
        var publishJob = await GetSingleJobAsync(version.Id, KnowledgeIndexJobTriggerType.Publish);
        var firstChunkId = await InsertChunkAsync(item.Id, version.Id, publishJob.Id, sequence: 0);

        var scope = await _itemAppService.CreatePermissionScopeAsync(item.Id, new CreateKnowledgePermissionScopeDto
        {
            ScopeType = KnowledgePermissionScopeType.All,
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

        var secondChunkId = await InsertChunkAsync(item.Id, version.Id, firstPermissionJob.Id, sequence: 1);
        await _itemAppService.DeletePermissionScopeAsync(item.Id, scope.Id);

        var secondChunk = await GetChunkAsync(secondChunkId);
        secondChunk.IsActive.ShouldBeFalse();
        secondChunk.IsPublishedSnapshot.ShouldBeFalse();

        var jobsAfterDelete = await _indexJobAppService.GetListAsync(new GetKnowledgeIndexJobListInput
        {
            KnowledgeVersionId = version.Id,
            MaxResultCount = 100
        });
        jobsAfterDelete.Items.Count(x => x.TriggerType == KnowledgeIndexJobTriggerType.PermissionChanged).ShouldBe(2);
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
        await UploadAttachmentAsync(item.Id, version.Id, "manual.pdf", "application/pdf", "SECRET-PDF-CONTENT");
        await _itemAppService.PublishVersionAsync(item.Id, version.Id);

        var result = await _ingestionPipeline.IngestAsync(version.Id);

        result.Succeeded.ShouldBeFalse();
        result.FailureStage.ShouldBe("reader");
        result.FailureReason.ShouldNotBeNull();
        result.FailureReason!.ShouldContain("Unsupported attachment type");
        result.FailureReason.ShouldNotContain("SECRET-PDF-CONTENT");

        var job = await GetSingleJobAsync(version.Id, KnowledgeIndexJobTriggerType.Publish);
        job.Status.ShouldBe(KnowledgeIndexJobStatus.Failed);
        job.FailureReason.ShouldNotBeNull();
        job.FailureReason!.ShouldNotContain("SECRET-PDF-CONTENT");
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
        await _ingestionPipeline.IngestAsync(version.Id);
        var chunks = await GetChunksAsync(version.Id);

        var result = await _embeddingPipeline.GenerateForVersionAsync(version.Id);

        result.Succeeded.ShouldBeTrue();
        result.EmbeddingModelVersionId.ShouldNotBeNull();
        result.ProcessedChunkCount.ShouldBe(chunks.Count);
        _embeddingClient.CallCount.ShouldBe(chunks.Count);

        var modelVersion = await GetModelVersionAsync(result.EmbeddingModelVersionId!.Value);
        modelVersion.Provider.ShouldBe("LiteLLM");
        modelVersion.ModelName.ShouldBe("knowpulse-embedding-default");
        modelVersion.ModelVersion.ShouldBe("2026-06-mvp1");
        modelVersion.VectorDimension.ShouldBe(1536);
        modelVersion.CollectionName.ShouldBe("knowledge_chunks_1536_mvp1");
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
    /// 验证需要重建的 chunk 按 embedding 模型版本隔离计算。
    /// </summary>
    [Fact]
    public async Task GetChunksRequiringRebuildAsync_Should_Filter_By_ModelVersion()
    {
        // 重建计划按模型版本计算，避免不同模型版本的记录互相抵消。
        var (item, version) = await CreateKnowledgeVersionAsync("重建查询测试内容。");
        await _itemAppService.PublishVersionAsync(item.Id, version.Id);
        await _ingestionPipeline.IngestAsync(version.Id);
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
        await _ingestionPipeline.IngestAsync(version.Id);
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
    private async Task<(KnowledgeItemDto Item, KnowledgeVersionDto Version)> CreateKnowledgeVersionAsync(string? contentText = "MVP-1 indexable content")
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await _categoryAppService.CreateAsync(new CreateUpdateKnowledgeCategoryDto
        {
            Code = $"KI-{suffix}",
            Name = $"Index Category {suffix}"
        });

        var item = await _itemAppService.CreateAsync(new CreateKnowledgeItemDto
        {
            CategoryId = category.Id,
            Title = $"Index Knowledge {suffix}",
            Summary = "MVP-1 indexing integration test item"
        });

        var version = await _itemAppService.CreateVersionAsync(item.Id, new CreateKnowledgeVersionDto
        {
            Title = $"Index Version {suffix}",
            ContentText = contentText
        });

        return (item, version);
    }

    /// <summary>
    /// 上传测试附件到知识版本。
    /// </summary>
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
    private Task<Guid> InsertEmbeddingModelVersionAsync(string modelName, string modelVersion)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var model = new EmbeddingModelVersion(
                Guid.NewGuid(),
                tenantId: null,
                provider: "LiteLLM",
                modelName,
                modelVersion,
                vectorDimension: 1536,
                collectionName: "knowledge_chunks_1536_mvp1");

            await _modelVersionRepository.InsertAsync(model, autoSave: true);
            return model.Id;
        });
    }
}
