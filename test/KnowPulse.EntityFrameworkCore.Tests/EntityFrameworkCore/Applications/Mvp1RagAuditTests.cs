using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.KnowledgeBase;
using KnowPulse.KnowledgeIndexing;
using KnowPulse.RagAI;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

/// <summary>
/// MVP-1 RAG 审计集成测试。
/// 验证 Search/Answer 日志可查询、citation 链路完整、失败原因可定位且不会保存完整敏感正文。
/// </summary>
[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class Mvp1RagAuditTests : KnowPulseEntityFrameworkCoreTestBase
{
    private static readonly Guid CurrentUserId = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");

    private readonly IKnowledgeCategoryAppService _categoryAppService;
    private readonly IKnowledgeItemAppService _itemAppService;
    private readonly IKnowledgeIngestionPipeline _ingestionPipeline;
    private readonly IKnowledgeRetriever _retriever;
    private readonly IRagAnswerService _answerService;
    private readonly IRagCallLogAppService _callLogAppService;
    private readonly TestKnowledgeEmbeddingClient _embeddingClient;
    private readonly TestKnowledgeVectorStore _vectorStore;
    private readonly TestRagChatClient _chatClient;
    private readonly IRepository<KnowledgeChunk, Guid> _chunkRepository;

    /// <summary>
    /// 初始化 RAG 审计集成测试依赖。
    /// </summary>
    public Mvp1RagAuditTests()
    {
        _categoryAppService = GetRequiredService<IKnowledgeCategoryAppService>();
        _itemAppService = GetRequiredService<IKnowledgeItemAppService>();
        _ingestionPipeline = GetRequiredService<IKnowledgeIngestionPipeline>();
        _retriever = GetRequiredService<IKnowledgeRetriever>();
        _answerService = GetRequiredService<IRagAnswerService>();
        _callLogAppService = GetRequiredService<IRagCallLogAppService>();
        _embeddingClient = GetRequiredService<TestKnowledgeEmbeddingClient>();
        _vectorStore = GetRequiredService<TestKnowledgeVectorStore>();
        _chatClient = GetRequiredService<TestRagChatClient>();
        _chunkRepository = GetRequiredService<IRepository<KnowledgeChunk, Guid>>();
        _embeddingClient.Reset();
        _vectorStore.Reset();
        _chatClient.Reset();
    }

    /// <summary>
    /// 验证问答会写入可查询的脱敏日志和 citation 链路。
    /// </summary>
    [Fact]
    public async Task AnswerAsync_Should_Write_Queryable_Log_With_Citation_Chain_And_Redacted_Content()
    {
        // 构造带敏感尾部的长内容，确保日志只保存短摘录和 hash，不保存完整 chunk。
        var sensitiveTail = "FULL_DOCUMENT_SECRET_SHOULD_NOT_APPEAR_IN_RAG_LOG";
        var chunk = await CreateIndexedChunkAsync(
            "RAG Audit",
            "报销制度要求发票、审批单、付款记录三项材料齐全后再提交财务复核。"
            + new string('甲', 220)
            + sensitiveTail);
        _vectorStore.SearchResults.Add(ToHit(chunk, 0.91));

        var question = $"报销材料要求是什么？{Guid.NewGuid():N}";
        var windowStart = DateTime.UtcNow.AddDays(-1);
        var windowEnd = DateTime.UtcNow.AddDays(1);

        var answer = await _answerService.AnswerAsync(new RagAnswerRequest
        {
            Question = question,
            TopK = 3
        });

        answer.Citations.Count.ShouldBe(1);

        var logs = await _callLogAppService.GetListAsync(new GetRagCallLogListInput
        {
            UserId = CurrentUserId,
            KnowledgeVersionId = chunk.KnowledgeVersionId,
            ModelVersion = "2026-06-mvp1",
            CallType = RagCallType.Answer,
            StartTime = windowStart,
            EndTime = windowEnd,
            MaxResultCount = 50
        });

        logs.TotalCount.ShouldBe(1L);
        var log = logs.Items.Single();
        log.Status.ShouldBe(RagCallStatus.Succeeded);
        log.UserId.ShouldBe(CurrentUserId);
        log.QueryHash.Length.ShouldBe(64);
        log.QuerySummary.ShouldContain("报销材料要求");
        log.QuerySummary.Length.ShouldBeLessThanOrEqualTo(RagCallLog.MaxQuerySummaryLength);
        log.ModelName.ShouldBe("knowpulse-chat-default");
        log.CitationCount.ShouldBe(1);
        log.Citations.Count.ShouldBe(1);
        log.Citations[0].KnowledgeItemId.ShouldBe(chunk.KnowledgeItemId);
        log.Citations[0].KnowledgeVersionId.ShouldBe(chunk.KnowledgeVersionId);
        log.Citations[0].ChunkId.ShouldBe(chunk.Id);
        log.Citations[0].QuoteHash.Length.ShouldBe(64);
        log.Citations[0].QuotePreview.Length.ShouldBeLessThanOrEqualTo(RagCitationRecord.MaxQuotePreviewLength);
        log.Citations[0].QuotePreview.ShouldNotContain(sensitiveTail);
        log.QuerySummary.ShouldNotContain(sensitiveTail);
        (log.FailureReason ?? string.Empty).ShouldNotContain(sensitiveTail);
    }

    /// <summary>
    /// 验证无结果和二次授权拒绝都会被记录为可区分的 NoContext 日志。
    /// </summary>
    [Fact]
    public async Task SearchAsync_Should_Record_No_Result_And_Secondary_Check_Rejection()
    {
        // 区分两类无上下文：向量库无候选，以及候选被 PostgreSQL 二次权限校验拒绝。
        var noResultQuestion = $"没有召回的制度是什么？{Guid.NewGuid():N}";
        var deniedQuestion = $"被权限拒绝的制度是什么？{Guid.NewGuid():N}";

        await _retriever.SearchAsync(new RagSearchRequest
        {
            Query = noResultQuestion
        });

        var hiddenChunk = await CreateIndexedChunkAsync(
            "RAG Hidden Audit",
            "这段隐藏内容不应被当前用户引用。",
            new CreateKnowledgePermissionScopeDto
            {
                ScopeType = KnowledgePermissionScopeType.User,
                TargetId = Guid.NewGuid()
            });
        _vectorStore.SearchResults.Add(ToHit(hiddenChunk, 0.98));

        await _retriever.SearchAsync(new RagSearchRequest
        {
            Query = deniedQuestion
        });

        var logs = await _callLogAppService.GetListAsync(new GetRagCallLogListInput
        {
            UserId = CurrentUserId,
            ModelVersion = "2026-06-mvp1",
            CallType = RagCallType.Search,
            Status = RagCallStatus.NoContext,
            StartTime = DateTime.UtcNow.AddDays(-1),
            EndTime = DateTime.UtcNow.AddDays(1),
            MaxResultCount = 100
        });

        var noResultLog = logs.Items.Single(x => x.QuerySummary == noResultQuestion);
        noResultLog.FailureStage.ShouldBe("retrieval");
        (noResultLog.FailureReason ?? string.Empty).ShouldContain("No vector candidates");
        noResultLog.RetrievedChunkCount.ShouldBe(0);
        noResultLog.CitationCount.ShouldBe(0);

        var deniedLog = logs.Items.Single(x => x.QuerySummary == deniedQuestion);
        deniedLog.FailureStage.ShouldBe("secondary-check");
        (deniedLog.FailureReason ?? string.Empty).ShouldContain("PostgreSQL authorization check");
        deniedLog.RetrievedChunkCount.ShouldBe(0);
        deniedLog.CitationCount.ShouldBe(0);
    }

    /// <summary>
    /// 验证模型失败会同时记录 Search 和 Answer 失败日志。
    /// </summary>
    [Fact]
    public async Task AnswerAsync_Should_Record_Model_Failure_On_Search_And_Answer_Logs()
    {
        // embedding 失败会同时留下 Search 失败日志和 Answer 失败日志，便于从任一入口排查。
        var question = $"模型失败时怎么记录？{Guid.NewGuid():N}";
        _embeddingClient.ShouldFail = true;
        _embeddingClient.FailureReason = "Synthetic embedding provider failure.";

        var answer = await _answerService.AnswerAsync(new RagAnswerRequest
        {
            Question = question
        });

        answer.Answer.ShouldBe(RagAnswerService.InsufficientKnowledgeAnswer);

        var logs = await _callLogAppService.GetListAsync(new GetRagCallLogListInput
        {
            UserId = CurrentUserId,
            ModelVersion = "2026-06-mvp1",
            Status = RagCallStatus.Failed,
            StartTime = DateTime.UtcNow.AddDays(-1),
            EndTime = DateTime.UtcNow.AddDays(1),
            MaxResultCount = 100
        });

        var searchFailure = logs.Items.Single(x => x.CallType == RagCallType.Search && x.QuerySummary == question);
        searchFailure.FailureStage.ShouldBe("embedding");
        (searchFailure.FailureReason ?? string.Empty).ShouldContain("Synthetic embedding provider failure");

        var answerFailure = logs.Items.Single(x => x.CallType == RagCallType.Answer && x.QuerySummary == question);
        answerFailure.FailureStage.ShouldBe("search");
        (answerFailure.FailureReason ?? string.Empty).ShouldContain("Synthetic embedding provider failure");
        answerFailure.CitationCount.ShouldBe(0);
    }

    /// <summary>
    /// 创建、发布并摄取一个测试知识 chunk。
    /// </summary>
    private async Task<KnowledgeChunk> CreateIndexedChunkAsync(
        string titlePrefix,
        string content,
        CreateKnowledgePermissionScopeDto? scope = null)
    {
        // 复用真实知识发布和摄取流程，保证审计日志引用的是实际持久化 chunk。
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await _categoryAppService.CreateAsync(new CreateUpdateKnowledgeCategoryDto
        {
            Code = $"RAU-{suffix}",
            Name = $"{titlePrefix} Category {suffix}"
        });
        var itemInput = new CreateKnowledgeItemDto
        {
            CategoryId = category.Id,
            Title = $"{titlePrefix} {suffix}",
            Summary = "RAG audit integration test item"
        };

        var item = scope == null
            ? await _itemAppService.CreateAsync(itemInput)
            : await CreateLegacyKnowledgeAsync(itemInput);

        var version = await _itemAppService.CreateVersionAsync(item.Id, new CreateKnowledgeVersionDto
        {
            Title = $"{titlePrefix} Version {suffix}",
            ContentText = content
        });
        await _itemAppService.PublishVersionAsync(item.Id, version.Id);

        if (scope != null)
        {
            await _itemAppService.CreatePermissionScopeAsync(item.Id, scope);
        }

        var ingestion = await _ingestionPipeline.IngestAsync(version.Id);
        ingestion.Succeeded.ShouldBeTrue();

        return await WithUnitOfWorkAsync(async () =>
        {
            var queryable = await _chunkRepository.GetQueryableAsync();
            return queryable.Single(x => x.KnowledgeVersionId == version.Id && x.IsActive);
        });
    }

    /// <summary>
    /// 将主库 chunk 包装成模拟 Qdrant 命中。
    /// </summary>
    private static VectorSearchHit ToHit(KnowledgeChunk chunk, double score)
    {
        // 模拟向量命中时不信任 payload 中的正文，审计 preview 应来自授权后的主库 chunk。
        return new VectorSearchHit
        {
            ChunkId = chunk.Id,
            KnowledgeItemId = chunk.KnowledgeItemId,
            KnowledgeVersionId = chunk.KnowledgeVersionId,
            Content = "Vector payload content must not be used as persisted citation text.",
            Score = score,
            Payload = new KnowledgeVectorPayload
            {
                TenantId = chunk.TenantId,
                KnowledgeItemId = chunk.KnowledgeItemId,
                KnowledgeVersionId = chunk.KnowledgeVersionId,
                ChunkId = chunk.Id,
                IsPublished = true,
                SourceTitle = chunk.SourceTitle,
                SourcePage = chunk.SourcePage
            }
        };
    }
}
