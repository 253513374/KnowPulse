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
/// MVP-1 RAG 问答集成测试。
/// 覆盖回答生成、citation 返回、无上下文拒答和未授权向量命中被二次校验拦截。
/// </summary>
[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class Mvp1RagAnswerServiceTests : KnowPulseEntityFrameworkCoreTestBase
{
    private readonly IKnowledgeCategoryAppService _categoryAppService;
    private readonly IKnowledgeItemAppService _itemAppService;
    private readonly IKnowledgeIngestionPipeline _ingestionPipeline;
    private readonly IRagAnswerService _answerService;
    private readonly TestKnowledgeEmbeddingClient _embeddingClient;
    private readonly TestKnowledgeVectorStore _vectorStore;
    private readonly TestRagChatClient _chatClient;
    private readonly IRepository<KnowledgeChunk, Guid> _chunkRepository;

    /// <summary>
    /// 初始化 RAG 问答集成测试依赖。
    /// </summary>
    public Mvp1RagAnswerServiceTests()
    {
        _categoryAppService = GetRequiredService<IKnowledgeCategoryAppService>();
        _itemAppService = GetRequiredService<IKnowledgeItemAppService>();
        _ingestionPipeline = GetRequiredService<IKnowledgeIngestionPipeline>();
        _answerService = GetRequiredService<IRagAnswerService>();
        _embeddingClient = GetRequiredService<TestKnowledgeEmbeddingClient>();
        _vectorStore = GetRequiredService<TestKnowledgeVectorStore>();
        _chatClient = GetRequiredService<TestRagChatClient>();
        _chunkRepository = GetRequiredService<IRepository<KnowledgeChunk, Guid>>();
        _embeddingClient.Reset();
        _vectorStore.Reset();
        _chatClient.Reset();
    }

    /// <summary>
    /// 验证问答会返回基于授权知识的回答和 citation。
    /// </summary>
    [Fact]
    public async Task AnswerAsync_Should_Return_Grounded_Answer_With_Citations()
    {
        // 正常问答必须只基于授权 chunk 生成回答，并返回可追溯 citation。
        var chunk = await CreateIndexedChunkAsync(
            "RAG Grounded",
            "员工请假流程需要先在系统提交申请，再由直属主管审批。审批通过后人事归档。");
        _vectorStore.SearchResults.Add(ToHit(chunk, 0.88));

        var answer = await _answerService.AnswerAsync(new RagAnswerRequest
        {
            Question = "员工请假流程是什么？",
            TopK = 3
        });

        answer.Answer.ShouldContain("根据当前可访问知识库");
        answer.Answer.ShouldContain("员工请假流程");
        answer.Citations.Count.ShouldBe(1);
        answer.Citations[0].KnowledgeItemId.ShouldBe(chunk.KnowledgeItemId);
        answer.Citations[0].KnowledgeVersionId.ShouldBe(chunk.KnowledgeVersionId);
        answer.Citations[0].ChunkId.ShouldBe(chunk.Id);
        answer.Citations[0].Quote.ShouldContain("员工请假流程");
        answer.Confidence.ShouldBe(0.88);
        answer.NeedHumanReview.ShouldBeTrue();
        _embeddingClient.CallCount.ShouldBe(1);
        _vectorStore.SearchRequests.Count.ShouldBe(1);
        _chatClient.CallCount.ShouldBe(1);
        _chatClient.Requests[0].ModelName.ShouldBe("knowpulse-chat-default");
        _chatClient.Requests[0].UserPrompt.ShouldContain("员工请假流程");
    }

    /// <summary>
    /// 验证没有检索上下文时返回知识库依据不足。
    /// </summary>
    [Fact]
    public async Task AnswerAsync_Should_Return_Insufficient_When_No_Context()
    {
        // 没有检索上下文时拒答，且不调用 chat client，避免模型靠外部常识编造答案。
        var answer = await _answerService.AnswerAsync(new RagAnswerRequest
        {
            Question = "不存在的制度是什么？"
        });

        answer.Answer.ShouldBe(RagAnswerService.InsufficientKnowledgeAnswer);
        answer.Citations.Count.ShouldBe(0);
        answer.Confidence.ShouldBe(0);
        answer.NeedHumanReview.ShouldBeTrue();
        _embeddingClient.CallCount.ShouldBe(1);
        _vectorStore.SearchRequests.Count.ShouldBe(1);
        _chatClient.CallCount.ShouldBe(0);
    }

    /// <summary>
    /// 验证未授权的向量命中不会进入回答 citation。
    /// </summary>
    [Fact]
    public async Task AnswerAsync_Should_Not_Cite_Unauthorized_Vector_Hits()
    {
        // 即使向量库返回高分命中，只要 PostgreSQL 当前权限不允许，就不能进入回答和 citation。
        var chunk = await CreateIndexedChunkAsync(
            "RAG Hidden",
            "这段内容不应被当前用户引用。",
            new CreateKnowledgePermissionScopeDto
            {
                ScopeType = KnowledgePermissionScopeType.User,
                TargetId = Guid.NewGuid()
            });
        _vectorStore.SearchResults.Add(ToHit(chunk, 0.99));

        var answer = await _answerService.AnswerAsync(new RagAnswerRequest
        {
            Question = "隐藏内容是什么？"
        });

        answer.Answer.ShouldBe(RagAnswerService.InsufficientKnowledgeAnswer);
        answer.Citations.Count.ShouldBe(0);
        answer.Confidence.ShouldBe(0);
        answer.NeedHumanReview.ShouldBeTrue();
        _embeddingClient.CallCount.ShouldBe(1);
        _vectorStore.SearchRequests.Count.ShouldBe(1);
        _chatClient.CallCount.ShouldBe(0);
    }

    /// <summary>
    /// 创建、发布并摄取一个测试知识 chunk。
    /// </summary>
    private async Task<KnowledgeChunk> CreateIndexedChunkAsync(
        string titlePrefix,
        string content,
        CreateKnowledgePermissionScopeDto? scope = null)
    {
        // 构造一条真实发布知识并跑摄取流程，确保测试覆盖的 chunk 来自主库事实而非手写 DTO。
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await _categoryAppService.CreateAsync(new CreateUpdateKnowledgeCategoryDto
        {
            Code = $"RA-{suffix}",
            Name = $"{titlePrefix} Category {suffix}"
        });
        var itemInput = new CreateKnowledgeItemDto
        {
            CategoryId = category.Id,
            Title = $"{titlePrefix} {suffix}",
            Summary = "RAG answer integration test item"
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
        // 模拟 Qdrant 命中。Content 故意写成非最终内容，用来证明答案来源会回主库读取 chunk。
        return new VectorSearchHit
        {
            ChunkId = chunk.Id,
            KnowledgeItemId = chunk.KnowledgeItemId,
            KnowledgeVersionId = chunk.KnowledgeVersionId,
            Content = "Vector payload content is not the final answer source.",
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
