using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.AiQuestioning;
using KnowPulse.KnowledgeBase;
using KnowPulse.KnowledgeIndexing;
using KnowPulse.QuestionBank;
using KnowPulse.RagAI;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

/// <summary>
/// MVP-2 AI 候选题生成应用服务集成测试。
/// 测试覆盖生成链路的主要安全边界：必须有授权 RAG 上下文、必须有 citation、模型坏格式或失败要安全降级，
/// 候选题只能先采纳为可编辑草稿，并且相似题提示需要可落库追溯。
/// </summary>
[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class Mvp2AiQuestionGenerationAppServiceTests : KnowPulseEntityFrameworkCoreTestBase
{
    // 被测应用服务，负责创建 AI 出题任务、生成候选题和提交审核。
    private readonly IAiQuestionGenerationAppService _generationAppService;

    // 题库应用服务，用于准备相似题基线并验证候选题提交后的审核状态。
    private readonly IQuestionAppService _questionAppService;
    private readonly IQuestionWorkflowAppService _questionWorkflowAppService;

    // 知识库分类和条目服务，用于构造可检索的 RAG 测试数据。
    private readonly IKnowledgeCategoryAppService _categoryAppService;
    private readonly IKnowledgeItemAppService _itemAppService;

    // MVP-1 知识入库管道，用于生成 chunk、embedding 和向量 payload。
    private readonly IKnowledgeIngestionPipeline _ingestionPipeline;

    // 测试替身：embedding、向量库和模型客户端均可控，避免测试依赖真实外部服务。
    private readonly TestKnowledgeEmbeddingClient _embeddingClient;
    private readonly TestKnowledgeVectorStore _vectorStore;
    private readonly TestRagChatClient _chatClient;

    // 仓储用于读取生成链路中的 chunk、模型调用日志和相似题记录。
    private readonly IRepository<KnowledgeChunk, Guid> _chunkRepository;
    private readonly IRepository<AiQuestionGenerationTask, Guid> _taskRepository;
    private readonly IRepository<AiQuestionModelCallLog, Guid> _modelCallLogRepository;
    private readonly IRepository<QuestionSimilarityCheck, Guid> _similarityRepository;

    /// <summary>
    /// 初始化测试依赖，并重置可控替身状态。
    /// </summary>
    public Mvp2AiQuestionGenerationAppServiceTests()
    {
        _generationAppService = GetRequiredService<IAiQuestionGenerationAppService>();
        _questionAppService = GetRequiredService<IQuestionAppService>();
        _questionWorkflowAppService = GetRequiredService<IQuestionWorkflowAppService>();
        _categoryAppService = GetRequiredService<IKnowledgeCategoryAppService>();
        _itemAppService = GetRequiredService<IKnowledgeItemAppService>();
        _ingestionPipeline = GetRequiredService<IKnowledgeIngestionPipeline>();
        _embeddingClient = GetRequiredService<TestKnowledgeEmbeddingClient>();
        _vectorStore = GetRequiredService<TestKnowledgeVectorStore>();
        _chatClient = GetRequiredService<TestRagChatClient>();
        _chunkRepository = GetRequiredService<IRepository<KnowledgeChunk, Guid>>();
        _taskRepository = GetRequiredService<IRepository<AiQuestionGenerationTask, Guid>>();
        _modelCallLogRepository = GetRequiredService<IRepository<AiQuestionModelCallLog, Guid>>();
        _similarityRepository = GetRequiredService<IRepository<QuestionSimilarityCheck, Guid>>();
        _embeddingClient.Reset();
        _vectorStore.Reset();
        _chatClient.Reset();
    }

    /// <summary>
    /// 验证 KP-MVP2-AI-001 的基础结构：
    /// prompt 模板/版本、模型配置和调用日志可以查询，且该验证不触发模型调用或候选题生成。
    /// </summary>
    [Fact]
    public async Task Ai001_Foundation_Should_Expose_Prompt_Model_Config_And_Call_Logs_Without_Generating_Candidates()
    {
        var config = await _generationAppService.GetModelConfigurationAsync();

        config.PromptTemplateKey.ShouldBe("mvp2-ai-question-candidate");
        config.PromptVersion.ShouldBe("mvp2-ai-question-v1");
        config.ModelProvider.ShouldBe("LiteLLM");
        config.ModelName.ShouldBe("knowpulse-chat-default");
        config.ModelVersion.ShouldBe("2026-06-mvp2-questioning");
        config.ApiKeyEnvironmentVariable.ShouldBe("KNOWPULSE_LITELLM_API_KEY");

        var templates = await _generationAppService.GetPromptTemplatesAsync(new GetAiQuestionPromptTemplateListInput
        {
            TemplateKey = config.PromptTemplateKey,
            Version = config.PromptVersion
        });
        templates.TotalCount.ShouldBeGreaterThanOrEqualTo(1);

        var template = templates.Items.First();
        template.IsActive.ShouldBeTrue();
        template.SystemPrompt.ShouldContain("知识片段");
        template.UserPromptTemplate.ShouldContain("{contexts}");

        var task = new AiQuestionGenerationTask(
            Guid.NewGuid(),
            tenantId: null,
            template.Id,
            config.PromptVersion,
            config.ModelProvider,
            config.ModelName,
            config.ModelVersion,
            QuestionType.SingleChoice,
            QuestionDifficulty.Normal,
            requestedCount: 1,
            topK: 3,
            knowledgeScope: "AI-001 可追溯结构验证",
            retrievalQuery: "AI-001 可追溯结构验证 SingleChoice Normal",
            applicablePositionId: null,
            securityLevel: 1,
            tags: "ai-001");
        await _taskRepository.InsertAsync(task, autoSave: true);

        await _modelCallLogRepository.InsertAsync(
            new AiQuestionModelCallLog(
                Guid.NewGuid(),
                tenantId: null,
                task.Id,
                template.Id,
                config.PromptVersion,
                RagCallStatus.Failed,
                new string('a', AiQuestionModelCallLog.MaxQueryHashLength),
                "AI-001 可追溯结构验证",
                config.ModelProvider,
                config.ModelName,
                config.ModelVersion,
                topK: 3,
                retrievedChunkCount: 0,
                candidateCount: 0,
                promptTokenCount: 0,
                completionTokenCount: 0,
                totalTokenCount: 0,
                executionDurationMs: 0,
                failureStage: "configuration",
                failureReason: "Foundation-only traceability check."),
            autoSave: true);

        var logs = await _generationAppService.GetCallLogsAsync(new GetAiQuestionModelCallLogListInput
        {
            TaskId = task.Id,
            PromptVersion = config.PromptVersion,
            ModelVersion = config.ModelVersion
        });

        logs.TotalCount.ShouldBe(1);
        var log = logs.Items.Single();
        log.PromptTemplateId.ShouldBe(template.Id);
        log.CandidateCount.ShouldBe(0);
        log.FailureStage.ShouldBe("configuration");
        log.QuerySummary.ShouldContain("AI-001");

        var taskDetail = await _generationAppService.GetAsync(task.Id);
        taskDetail.Candidates.Count.ShouldBe(0);
        taskDetail.CallLogs.Count.ShouldBe(1);
        _chatClient.CallCount.ShouldBe(0);
    }

    /// <summary>
    /// 验证正常生成链路：
    /// 授权 chunk 会进入 prompt，候选题保存 citation，采纳后只生成可编辑草稿且不可直接用于考试。
    /// </summary>
    [Fact]
    public async Task CreateAsync_Should_Generate_Candidate_With_Traceable_Citation_And_Adopt_As_Draft()
    {
        var chunk = await CreateIndexedChunkAsync(
            "AI Question Grounded",
            "员工请假流程需要先在系统提交申请，再由直属主管审批。审批通过后人事归档。");
        _vectorStore.SearchResults.Add(ToHit(chunk, 0.91));
        _chatClient.ResponseContent = BuildQuestionResponse(
            chunk.Id,
            "员工请假流程考点",
            "员工请假流程第一步是什么？");

        var input = CreateInput("员工请假流程");
        input.KnowledgePointText = "只考概念，不出计算题";
        input.KnowledgeItemIds = [chunk.KnowledgeItemId];

        var task = await _generationAppService.CreateAsync(input);

        task.Status.ShouldBe(AiQuestionGenerationTaskStatus.Generated);
        task.GeneratedCount.ShouldBe(1);
        task.PromptVersion.ShouldBe("mvp2-ai-question-v1");
        task.Candidates.Count.ShouldBe(1);

        var candidate = task.Candidates.Single();
        candidate.Status.ShouldBe(AiQuestionCandidateStatus.Generated);
        candidate.LinkedQuestionId.ShouldBeNull();
        candidate.Citations.Count.ShouldBe(1);
        candidate.Citations[0].KnowledgeItemId.ShouldBe(chunk.KnowledgeItemId);
        candidate.Citations[0].KnowledgeVersionId.ShouldBe(chunk.KnowledgeVersionId);
        candidate.Citations[0].ChunkId.ShouldBe(chunk.Id);
        candidate.Citations[0].SourceExcerpt.ShouldContain("员工请假流程");
        candidate.Citations[0].SourceExcerpt.Length.ShouldBeLessThanOrEqualTo(512);
        candidate.Citations[0].SourceHash.ShouldNotBeNullOrWhiteSpace();
        _chatClient.CallCount.ShouldBe(1);
        var prompt = _chatClient.Requests.Single().UserPrompt;
        prompt.ShouldContain("题型=SingleChoice");
        prompt.ShouldContain("难度=Normal");
        prompt.ShouldContain("数量=1");
        prompt.ShouldContain("知识范围=员工请假流程");
        prompt.ShouldContain("每道题必须至少引用一个 citations.chunkId");
        prompt.ShouldContain("chunkId 必须来自下方知识片段");
        prompt.ShouldContain(chunk.Id.ToString());
        task.RetrievalQuery.ShouldContain("只考概念，不出计算题");
        prompt.ShouldContain("只考概念，不出计算题");

        var adopted = await _questionWorkflowAppService.AdoptAiCandidateAsync(candidate.Id);

        adopted.CreationSource.ShouldBe(QuestionCreationSource.AiGenerated);
        adopted.Status.ShouldBe(QuestionStatus.Draft);
        adopted.IsUsableForExam.ShouldBeFalse();
        adopted.Sources.Count.ShouldBe(1);
        adopted.Sources[0].KnowledgeVersionId.ShouldBe(chunk.KnowledgeVersionId);
        adopted.Sources[0].ChunkId.ShouldBe(chunk.Id);
        adopted.Sources[0].SourceHash.ShouldBe(candidate.Citations[0].SourceHash);

        var refreshedCandidate = await _generationAppService.GetCandidateAsync(candidate.Id);
        refreshedCandidate.LinkedQuestionId.ShouldBe(adopted.Id);
        refreshedCandidate.Status.ShouldBe(AiQuestionCandidateStatus.AdoptedAsDraft);

        var logs = await GetCallLogsAsync(task.Id);
        logs.Count.ShouldBe(1);
        logs[0].Status.ShouldBe(RagCallStatus.Succeeded);
        logs[0].PromptVersion.ShouldBe("mvp2-ai-question-v1");
        logs[0].QueryHash.ShouldNotBeNullOrWhiteSpace();
        logs[0].QuerySummary.ShouldContain("员工请假流程");
        logs[0].CandidateCount.ShouldBe(1);

        var retried = await _generationAppService.RetryAsync(task.Id);

        retried.Status.ShouldBe(AiQuestionGenerationTaskStatus.Generated);
        _chatClient.CallCount.ShouldBe(2);
        _chatClient.Requests[1].UserPrompt.ShouldContain("只考概念，不出计算题");
    }

    /// <summary>
    /// 验证 QA-002 对 citation 的追溯和安全保存要求：
    /// 候选题 citation 与提交后的题库来源都能追溯到知识、版本和 chunk，但只保存短摘录与 hash，不复制完整知识正文。
    /// </summary>
    [Fact]
    public async Task Qa002_Citation_Should_Be_Traceable_And_Not_Copy_Full_Knowledge_Content()
    {
        var tailMarker = "QA002完整正文尾部不得落入citation";
        var longContent =
            "员工请假流程需要先在系统提交申请，再由直属主管审批。" +
            string.Concat(Enumerable.Repeat("审批记录需要保留申请人、直属主管、人事归档时间。", 24)) +
            tailMarker;
        longContent.Length.ShouldBeGreaterThan(512);

        var chunk = await CreateIndexedChunkAsync("AI Question Citation Safety", longContent);
        chunk.Content.ShouldContain(tailMarker);
        _vectorStore.SearchResults.Add(ToHit(chunk, 0.93));
        _chatClient.ResponseContent = BuildQuestionResponse(
            chunk.Id,
            "员工请假 citation 追溯",
            "员工请假流程第一步是什么？");

        var task = await _generationAppService.CreateAsync(CreateInput("员工请假 citation 追溯"));
        var candidate = task.Candidates.Single();
        var citation = candidate.Citations.Single();

        citation.KnowledgeItemId.ShouldBe(chunk.KnowledgeItemId);
        citation.KnowledgeVersionId.ShouldBe(chunk.KnowledgeVersionId);
        citation.ChunkId.ShouldBe(chunk.Id);
        citation.SourceHash.ShouldBe(ComputeHash(chunk.Content));
        citation.SourceExcerpt.Length.ShouldBeLessThanOrEqualTo(512);
        citation.SourceExcerpt.ShouldNotBe(chunk.Content);
        citation.SourceExcerpt.ShouldNotContain(tailMarker);
        citation.SourceExcerpt.ShouldNotContain("Vector payload content should not be authoritative.");

        var adopted = await _generationAppService.AdoptCandidateAsDraftAsync(candidate.Id);
        var source = adopted.Sources.Single();

        source.KnowledgeItemId.ShouldBe(chunk.KnowledgeItemId);
        source.KnowledgeVersionId.ShouldBe(chunk.KnowledgeVersionId);
        source.ChunkId.ShouldBe(chunk.Id);
        source.SourceHash.ShouldBe(citation.SourceHash);
        var sourceExcerpt = source.SourceExcerpt.ShouldNotBeNull();
        sourceExcerpt.ShouldBe(citation.SourceExcerpt);
        sourceExcerpt.ShouldNotContain(tailMarker);

        var logs = await GetCallLogsAsync(task.Id);
        logs.Single().QuerySummary.ShouldNotContain(tailMarker);
    }

    /// <summary>
    /// 验证无授权上下文时的安全降级。
    /// 没有 RAG context 时禁止调用模型，任务失败并写入 NoContext 调用日志。
    /// </summary>
    [Fact]
    public async Task CreateAsync_Should_Fail_Safely_When_No_Context()
    {
        var task = await _generationAppService.CreateAsync(CreateInput("不存在的制度"));

        task.Status.ShouldBe(AiQuestionGenerationTaskStatus.Failed);
        task.FailureStage.ShouldBe("retrieval");
        task.Candidates.Count.ShouldBe(0);
        _chatClient.CallCount.ShouldBe(0);

        var logs = await GetCallLogsAsync(task.Id);
        logs.Single().Status.ShouldBe(RagCallStatus.NoContext);
    }

    /// <summary>
    /// 验证越权向量命中不会被用于 AI 出题。
    /// 即使向量库返回高分命中，MVP-1 权限过滤后无可用 chunk，也必须失败且不调用模型。
    /// </summary>
    [Fact]
    public async Task CreateAsync_Should_Not_Use_Unauthorized_Vector_Hits()
    {
        var chunk = await CreateIndexedChunkAsync(
            "AI Question Hidden",
            "这段内容不允许当前用户用于 AI 出题。",
            new CreateKnowledgePermissionScopeDto
            {
                ScopeType = KnowledgePermissionScopeType.User,
                TargetId = Guid.NewGuid()
            });
        _vectorStore.SearchResults.Add(ToHit(chunk, 0.99));

        var task = await _generationAppService.CreateAsync(CreateInput("隐藏制度"));

        task.Status.ShouldBe(AiQuestionGenerationTaskStatus.Failed);
        task.FailureStage.ShouldBe("retrieval");
        task.Candidates.Count.ShouldBe(0);
        _chatClient.CallCount.ShouldBe(0);
        _vectorStore.SearchRequests.Count.ShouldBe(1);
    }

    /// <summary>
    /// 验证模型返回坏格式时安全失败。
    /// 无法解析为候选题 JSON 的响应不会创建候选题，失败阶段记录为 parse。
    /// </summary>
    [Fact]
    public async Task CreateAsync_Should_Fail_Safely_When_Model_Output_Is_Bad_Format()
    {
        var chunk = await CreateIndexedChunkAsync(
            "AI Question Bad Format",
            "员工请假流程需要先在系统提交申请，再由直属主管审批。");
        _vectorStore.SearchResults.Add(ToHit(chunk, 0.88));
        _chatClient.ResponseContent = "not-json";

        var task = await _generationAppService.CreateAsync(CreateInput("员工请假流程坏格式"));

        task.Status.ShouldBe(AiQuestionGenerationTaskStatus.Failed);
        task.FailureStage.ShouldBe("parse");
        task.Candidates.Count.ShouldBe(0);

        var logs = await GetCallLogsAsync(task.Id);
        logs.Single().FailureStage.ShouldBe("parse");
    }

    /// <summary>
    /// 验证模型网关失败时安全失败。
    /// chat 阶段失败会写入任务失败原因和模型调用日志，不产生候选题。
    /// </summary>
    [Fact]
    public async Task CreateAsync_Should_Fail_Safely_When_Model_Fails()
    {
        var chunk = await CreateIndexedChunkAsync(
            "AI Question Model Failure",
            "员工请假流程需要先在系统提交申请，再由直属主管审批。");
        _vectorStore.SearchResults.Add(ToHit(chunk, 0.88));
        _chatClient.ShouldFail = true;
        _chatClient.FailureReason = "Model gateway unavailable.";

        var task = await _generationAppService.CreateAsync(CreateInput("员工请假流程模型失败"));

        task.Status.ShouldBe(AiQuestionGenerationTaskStatus.Failed);
        task.FailureStage.ShouldBe("chat");
        task.FailureReason.ShouldNotBeNull();
        task.FailureReason!.ShouldContain("Model gateway unavailable");
        task.Candidates.Count.ShouldBe(0);

        var logs = await GetCallLogsAsync(task.Id);
        logs.Single().Status.ShouldBe(RagCallStatus.Failed);
    }

    /// <summary>
    /// 验证结构化坏输出只能作为失败候选题落库，不能提交到题库审核流。
    /// 覆盖解析、来源、题型、难度和答案结构等 AI-003 硬规则。
    /// </summary>
    [Theory]
    [InlineData("缺少解析", "SingleChoice", "Normal", null, true, "A", "缺少解析")]
    [InlineData("缺少来源", "SingleChoice", "Normal", "依据知识片段。", false, "A", "缺少可追溯 citation")]
    [InlineData("缺少题型", null, "Normal", "依据知识片段。", true, "A", "缺少题型")]
    [InlineData("非法题型", "Essay", "Normal", "依据知识片段。", true, "A", "模型输出题型无效")]
    [InlineData("题型不一致", "MultipleChoice", "Normal", "依据知识片段。", true, "A", "模型输出题型与任务题型不一致")]
    [InlineData("缺少难度", "SingleChoice", null, "依据知识片段。", true, "A", "缺少难度")]
    [InlineData("非法难度", "SingleChoice", "Impossible", "依据知识片段。", true, "A", "模型输出难度无效")]
    [InlineData("难度不一致", "SingleChoice", "Hard", "依据知识片段。", true, "A", "模型输出难度与任务难度不一致")]
    [InlineData("答案不匹配选项", "SingleChoice", "Normal", "依据知识片段。", true, "C", "题目结构校验失败")]
    public async Task CreateAsync_Should_Keep_Invalid_Model_Output_As_Unsubmittable_Candidate(
        string scenario,
        string? outputType,
        string? outputDifficulty,
        string? analysis,
        bool includeCitation,
        string answer,
        string expectedValidationError)
    {
        var chunk = await CreateIndexedChunkAsync(
            $"AI Question Invalid {scenario}",
            "员工请假流程需要先在系统提交申请，再由直属主管审批。");
        _vectorStore.SearchResults.Add(ToHit(chunk, 0.89));
        _chatClient.ResponseContent = BuildQuestionResponse(
            chunk.Id,
            $"员工请假流程坏输出 {scenario}",
            "员工请假流程第一步是什么？",
            outputType,
            outputDifficulty,
            analysis,
            includeCitation,
            answer);

        var task = await _generationAppService.CreateAsync(CreateInput($"员工请假流程坏输出 {scenario}"));

        task.Status.ShouldBe(AiQuestionGenerationTaskStatus.Generated);
        task.GeneratedCount.ShouldBe(0);
        task.Candidates.Count.ShouldBe(1);

        var candidate = task.Candidates.Single();
        candidate.Status.ShouldBe(AiQuestionCandidateStatus.ValidationFailed);
        candidate.LinkedQuestionId.ShouldBeNull();
        candidate.ValidationErrors.ShouldNotBeNullOrWhiteSpace();
        candidate.ValidationErrors!.ShouldContain(expectedValidationError);

        await Should.ThrowAsync<BusinessException>(
            () => _generationAppService.AdoptCandidateAsDraftAsync(candidate.Id));
    }

    /// <summary>
    /// 验证候选题相似题提示。
    /// 与已有已审核题高度相似时，候选题保存最高相似题，采纳为草稿后记录 QuestionSimilarityCheck。
    /// </summary>
    [Fact]
    public async Task CreateAsync_Should_Record_Similar_Question_Warnings()
    {
        var existing = await _questionAppService.CreateAsync(CreateManualQuestionInput(
            "员工请假流程重复题",
            "员工请假流程第一步是什么？"));
        await _questionAppService.SubmitReviewAsync(existing.Id);
        await _questionAppService.ApproveAsync(existing.Id, new ReviewQuestionDto { Comment = "Approved baseline" });

        var chunk = await CreateIndexedChunkAsync(
            "AI Question Similar",
            "员工请假流程需要先在系统提交申请，再由直属主管审批。");
        _vectorStore.SearchResults.Add(ToHit(chunk, 0.9));
        _chatClient.ResponseContent = BuildQuestionResponse(
            chunk.Id,
            "员工请假流程重复题",
            "员工请假流程第一步是什么？");

        var task = await _generationAppService.CreateAsync(CreateInput("员工请假流程相似题"));
        var candidate = task.Candidates.Single();

        candidate.HighestSimilarQuestionId.ShouldBe(existing.Id);
        candidate.HighestSimilarityScore.ShouldNotBeNull();
        candidate.HighestSimilarityScore!.Value.ShouldBeGreaterThanOrEqualTo(0.7);

        var adopted = await _generationAppService.AdoptCandidateAsDraftAsync(candidate.Id);
        var similarityCount = await WithUnitOfWorkAsync(async () =>
        {
            var queryable = await _similarityRepository.GetQueryableAsync();
            return queryable.Count(x => x.QuestionId == adopted.Id && x.SimilarQuestionId == existing.Id);
        });
        similarityCount.ShouldBe(1);
    }

    /// <summary>
    /// 构造 AI 出题任务输入。
    /// 统一测试题型、难度和安全等级，便于各测试只关注不同失败/成功条件。
    /// </summary>
    private static CreateAiQuestionGenerationTaskDto CreateInput(string scope)
    {
        return new CreateAiQuestionGenerationTaskDto
        {
            Type = QuestionType.SingleChoice,
            Difficulty = QuestionDifficulty.Normal,
            RequestedCount = 1,
            TopK = 3,
            KnowledgeScope = scope,
            KnowledgePointText = "请假流程",
            SecurityLevel = 1,
            Tags = "mvp2, ai"
        };
    }

    /// <summary>
    /// 构造人工题目输入。
    /// 用于相似题检测基线，先进入正式题库审核流再作为候选题相似对象。
    /// </summary>
    private static CreateQuestionDto CreateManualQuestionInput(string title, string stem)
    {
        return new CreateQuestionDto
        {
            Type = QuestionType.SingleChoice,
            Difficulty = QuestionDifficulty.Normal,
            CreationSource = QuestionCreationSource.Manual,
            Title = title,
            Stem = stem,
            SecurityLevel = 1,
            Options =
            {
                new CreateUpdateQuestionOptionDto { OptionKey = "A", Content = "先在系统提交申请", DisplayOrder = 1 },
                new CreateUpdateQuestionOptionDto { OptionKey = "B", Content = "直接口头请假", DisplayOrder = 2 }
            },
            Answers =
            {
                new CreateUpdateQuestionAnswerDto { Value = "A", DisplayOrder = 1 }
            },
            Sources =
            {
                new CreateUpdateQuestionSourceDto { ManualSourceDescription = "Manual baseline" }
            }
        };
    }

    /// <summary>
    /// 构造符合 AI 出题 prompt schema 的模型响应。
    /// 响应中的 citation chunkId 来自本次授权检索结果，用于验证 citation 可追溯。
    /// </summary>
    private static string BuildQuestionResponse(
        Guid chunkId,
        string title,
        string stem,
        string? type = "SingleChoice",
        string? difficulty = "Normal",
        string? analysis = "依据知识片段，员工请假流程需要先在系统提交申请。",
        bool includeCitation = true,
        string answer = "A")
    {
        object citations = includeCitation
            ? new[] { new { chunkId } }
            : Array.Empty<object>();

        return JsonSerializer.Serialize(new
        {
            questions = new[]
            {
                new
                {
                    type,
                    difficulty,
                    title,
                    stem,
                    options = new[]
                    {
                        new { optionKey = "A", content = "先在系统提交申请", displayOrder = 1 },
                        new { optionKey = "B", content = "直接口头请假", displayOrder = 2 }
                    },
                    answers = new[] { answer },
                    analysis,
                    knowledgePoints = new[] { "请假流程" },
                    citations
                }
            }
        });
    }

    /// <summary>
    /// 创建、发布并入库一条知识版本，然后返回激活的知识 chunk。
    /// 可选权限范围用于模拟当前用户无权访问的知识命中。
    /// </summary>
    private async Task<KnowledgeChunk> CreateIndexedChunkAsync(
        string titlePrefix,
        string content,
        CreateKnowledgePermissionScopeDto? scope = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = await _categoryAppService.CreateAsync(new CreateUpdateKnowledgeCategoryDto
        {
            Code = $"AI-{suffix}",
            Name = $"{titlePrefix} Category {suffix}"
        });
        var itemInput = new CreateKnowledgeItemDto
        {
            CategoryId = category.Id,
            Title = $"{titlePrefix} {suffix}",
            Summary = "AI question generation test item"
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
    /// 将数据库中的知识 chunk 包装为测试向量命中。
    /// payload 内容故意不作为权威正文，验证服务端最终 citation 摘录来自数据库 chunk。
    /// </summary>
    private static VectorSearchHit ToHit(KnowledgeChunk chunk, double score)
    {
        return new VectorSearchHit
        {
            ChunkId = chunk.Id,
            KnowledgeItemId = chunk.KnowledgeItemId,
            KnowledgeVersionId = chunk.KnowledgeVersionId,
            Content = "Vector payload content should not be authoritative.",
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

    /// <summary>
    /// 读取指定 AI 出题任务的模型调用日志。
    /// 测试通过日志验证成功、无上下文、解析失败和模型失败等状态是否可追溯。
    /// </summary>
    private Task<List<AiQuestionModelCallLog>> GetCallLogsAsync(Guid taskId)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var queryable = await _modelCallLogRepository.GetQueryableAsync();
            return queryable.Where(x => x.TaskId == taskId).OrderBy(x => x.CreationTime).ToList();
        });
    }

    /// <summary>
    /// 计算测试期望的 SHA-256 hash，和应用服务 citation hash 规则保持一致。
    /// </summary>
    private static string ComputeHash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
