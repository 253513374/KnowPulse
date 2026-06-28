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
/// MVP-1 RAG 质量评测应用服务测试。
/// 覆盖命中率、引用准确率、无答案拒答率和越权召回硬失败。
/// </summary>
[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class Mvp1RagEvaluationAppServiceTests : KnowPulseEntityFrameworkCoreTestBase
{
    private readonly IRagEvaluationAppService _evaluationAppService;
    private readonly IKnowledgeCategoryAppService _categoryAppService;
    private readonly IKnowledgeItemAppService _itemAppService;
    private readonly IKnowledgeIngestionPipeline _ingestionPipeline;
    private readonly TestKnowledgeEmbeddingClient _embeddingClient;
    private readonly TestKnowledgeVectorStore _vectorStore;
    private readonly TestRagChatClient _chatClient;
    private readonly IRepository<KnowledgeChunk, Guid> _chunkRepository;

    /// <summary>
    /// 初始化 RAG 评测应用服务测试依赖。
    /// </summary>
    public Mvp1RagEvaluationAppServiceTests()
    {
        _evaluationAppService = GetRequiredService<IRagEvaluationAppService>();
        _categoryAppService = GetRequiredService<IKnowledgeCategoryAppService>();
        _itemAppService = GetRequiredService<IKnowledgeItemAppService>();
        _ingestionPipeline = GetRequiredService<IKnowledgeIngestionPipeline>();
        _embeddingClient = GetRequiredService<TestKnowledgeEmbeddingClient>();
        _vectorStore = GetRequiredService<TestKnowledgeVectorStore>();
        _chatClient = GetRequiredService<TestRagChatClient>();
        _chunkRepository = GetRequiredService<IRepository<KnowledgeChunk, Guid>>();
        _embeddingClient.Reset();
        _vectorStore.Reset();
        _chatClient.Reset();
    }

    /// <summary>
    /// 验证评测运行会记录命中率和引用准确率。
    /// </summary>
    [Fact]
    public async Task RunAsync_Should_Record_Hit_And_Citation_Accuracy()
    {
        var chunk = await CreateIndexedChunkAsync(
            "RAG Evaluation Hit",
            "员工请假流程需要先在系统提交申请，再由直属主管审批。审批通过后人事归档。");
        _vectorStore.SearchResults.Add(ToHit(chunk, 0.92));

        var evaluationCase = await _evaluationAppService.CreateCaseAsync(new CreateRagEvaluationCaseDto
        {
            Name = "请假流程命中用例",
            Question = "员工请假流程是什么？",
            ExpectedCitations =
            {
                new RagEvaluationCitationExpectationDto
                {
                    KnowledgeItemId = chunk.KnowledgeItemId,
                    KnowledgeVersionId = chunk.KnowledgeVersionId,
                    ChunkId = chunk.Id
                }
            }
        });

        var run = await _evaluationAppService.RunAsync(new RunRagEvaluationDto
        {
            Name = "命中率评测",
            CaseIds = { evaluationCase.Id },
            TopK = 3
        });

        run.Status.ShouldBe(RagEvaluationRunStatus.Passed);
        run.TotalCaseCount.ShouldBe(1);
        run.PassedCaseCount.ShouldBe(1);
        run.HitRate.ShouldBe(1);
        run.CitationAccuracyRate.ShouldBe(1);
        run.NoAnswerRejectionRate.ShouldBe(1);
        run.UnauthorizedRecallRate.ShouldBe(0);
        run.HardFailed.ShouldBeFalse();
        run.Results.Count.ShouldBe(1);
        run.Results[0].Hit.ShouldBeTrue();
        run.Results[0].Passed.ShouldBeTrue();
        run.Results[0].ActualCitationCount.ShouldBe(1);
        run.Results[0].MatchedExpectedCitationCount.ShouldBe(1);
        _chatClient.CallCount.ShouldBe(1);
    }

    /// <summary>
    /// 验证期望拒答用例会记录无答案拒答率。
    /// </summary>
    [Fact]
    public async Task RunAsync_Should_Record_Expected_Refusal()
    {
        var evaluationCase = await _evaluationAppService.CreateCaseAsync(new CreateRagEvaluationCaseDto
        {
            Name = "无依据拒答用例",
            Question = "不存在的制度是什么？",
            ExpectedRefusal = true
        });

        var run = await _evaluationAppService.RunAsync(new RunRagEvaluationDto
        {
            Name = "拒答率评测",
            CaseIds = { evaluationCase.Id }
        });

        run.Status.ShouldBe(RagEvaluationRunStatus.Passed);
        run.TotalCaseCount.ShouldBe(1);
        run.NoAnswerRejectionRate.ShouldBe(1);
        run.HitRate.ShouldBe(1);
        run.CitationAccuracyRate.ShouldBe(1);
        run.UnauthorizedRecallRate.ShouldBe(0);
        run.Results.Count.ShouldBe(1);
        run.Results[0].ExpectedRefusal.ShouldBeTrue();
        run.Results[0].ActualRefusal.ShouldBeTrue();
        run.Results[0].Passed.ShouldBeTrue();
        _chatClient.CallCount.ShouldBe(0);
    }

    /// <summary>
    /// 验证命中禁止 citation 时越权召回率会作为硬失败指标。
    /// </summary>
    [Fact]
    public async Task RunAsync_Should_Hard_Fail_When_Unauthorized_Recall_Detected()
    {
        var chunk = await CreateIndexedChunkAsync(
            "RAG Evaluation Forbidden",
            "这段内容用于模拟不应出现在答案里的引用。");
        _vectorStore.SearchResults.Add(ToHit(chunk, 0.95));

        var evaluationCase = await _evaluationAppService.CreateCaseAsync(new CreateRagEvaluationCaseDto
        {
            Name = "越权召回硬失败用例",
            Question = "禁止引用内容是什么？",
            ExpectedCitations =
            {
                new RagEvaluationCitationExpectationDto
                {
                    KnowledgeItemId = chunk.KnowledgeItemId,
                    KnowledgeVersionId = chunk.KnowledgeVersionId,
                    ChunkId = chunk.Id
                }
            },
            ForbiddenCitations =
            {
                new RagEvaluationCitationExpectationDto
                {
                    KnowledgeItemId = chunk.KnowledgeItemId,
                    KnowledgeVersionId = chunk.KnowledgeVersionId,
                    ChunkId = chunk.Id
                }
            }
        });

        var run = await _evaluationAppService.RunAsync(new RunRagEvaluationDto
        {
            Name = "越权召回评测",
            CaseIds = { evaluationCase.Id }
        });

        run.Status.ShouldBe(RagEvaluationRunStatus.Failed);
        run.HardFailed.ShouldBeTrue();
        run.UnauthorizedRecallCount.ShouldBe(1);
        run.UnauthorizedRecallRate.ShouldBe(1);
        run.FailedCaseCount.ShouldBe(1);
        run.Results.Count.ShouldBe(1);
        run.Results[0].UnauthorizedCitationCount.ShouldBe(1);
        run.Results[0].Passed.ShouldBeFalse();
        (run.Results[0].FailureReason ?? string.Empty).ShouldContain("Unauthorized recall detected");
    }

    /// <summary>
    /// 创建、发布并摄取一个测试知识 chunk。
    /// </summary>
    private async Task<KnowledgeChunk> CreateIndexedChunkAsync(string titlePrefix, string content)
    {
        // 走真实知识库应用服务创建数据，保证评测覆盖 RAG 检索的主库二次校验。
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await _categoryAppService.CreateAsync(new CreateUpdateKnowledgeCategoryDto
        {
            Code = $"REV-{suffix}",
            Name = $"{titlePrefix} Category {suffix}"
        });
        var item = await _itemAppService.CreateAsync(new CreateKnowledgeItemDto
        {
            CategoryId = category.Id,
            Title = $"{titlePrefix} {suffix}",
            Summary = "RAG evaluation integration test item"
        });
        var version = await _itemAppService.CreateVersionAsync(item.Id, new CreateKnowledgeVersionDto
        {
            Title = $"{titlePrefix} Version {suffix}",
            ContentText = content
        });
        await _itemAppService.PublishVersionAsync(item.Id, version.Id);
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
        // 模拟向量候选，实际回答 citation 仍必须来自授权后的主库 chunk。
        return new VectorSearchHit
        {
            ChunkId = chunk.Id,
            KnowledgeItemId = chunk.KnowledgeItemId,
            KnowledgeVersionId = chunk.KnowledgeVersionId,
            Content = "Vector payload content is not authoritative.",
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
