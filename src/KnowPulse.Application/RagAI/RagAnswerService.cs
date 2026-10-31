using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.AiModelManagement;
using KnowPulse.Permissions;
using Microsoft.Extensions.Options;

namespace KnowPulse.RagAI;

/// <summary>
/// MVP-1 RAG 问答服务。当前不接 Agent workflow，
/// 只把已经通过 PostgreSQL 二次校验的检索结果交给 LiteLLM chat alias 生成回答。
/// </summary>
[Volo.Abp.RemoteService(false)]
public class RagAnswerService : KnowPulseAppService, IRagAnswerService
{
    /// <summary>
    /// 统一拒答文案，既服务前端，也服务测试和审计查询；避免各处拼不同版本的“无依据”回答。
    /// </summary>
    public const string InsufficientKnowledgeAnswer = "知识库依据不足，无法根据当前可访问知识回答该问题。";

    // MVP-1 阶段限制引用摘录长度和上下文数量，避免把大段原文回传给前端或写入审计链路。
    private const int MaxQuoteLength = 180;
    private const int MaxContextChunks = 5;
    private const int MaxPromptChunkLength = 1200;

    private const string SystemPrompt =
        "你是 KnowPulse 企业知识库问答助手。只能依据用户可访问的知识片段回答。" +
        "不得使用外部常识补充制度、流程、数字或日期。" +
        "如果知识片段不足以回答，必须回答“知识库依据不足”。" +
        "回答要简洁、中文表达，并在相关句子后引用 [1]、[2] 这类来源编号。";

    private readonly IKnowledgeRetriever _knowledgeRetriever;
    private readonly IRagChatClient _chatClient;
    private readonly IRagCallLogWriter _callLogWriter;
    private readonly IAiModelResolver _modelResolver;
    private readonly RagAiOptions _options;

    /// <summary>
    /// 创建 RAG 问答服务。
    /// </summary>
    public RagAnswerService(
        IKnowledgeRetriever knowledgeRetriever,
        IRagChatClient chatClient,
        IRagCallLogWriter callLogWriter,
        IAiModelResolver modelResolver,
        IOptions<RagAiOptions> options)
    {
        _knowledgeRetriever = knowledgeRetriever;
        _chatClient = chatClient;
        _callLogWriter = callLogWriter;
        _modelResolver = modelResolver;
        _options = options.Value;
    }

    /// <summary>
    /// 基于授权检索结果生成 RAG 回答并写入问答审计日志。
    /// </summary>
    public virtual async Task<RagAnswerResult> AnswerAsync(
        RagAnswerRequest request,
        CancellationToken cancellationToken = default)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Rag.Answer);
        var stopwatch = Stopwatch.StartNew();
        var topK = request.TopK <= 0 ? MaxContextChunks : Math.Min(request.TopK, MaxContextChunks);
        var modelSelection = await _modelResolver.ResolveAsync(AiModelUsage.RagAnswerChat, cancellationToken);

        // 空问题直接拒答，但仍写入 Answer 日志，方便运营侧区分“无输入”和“检索无结果”。
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            var insufficient = CreateInsufficientResult();
            await WriteAnswerLogAsync(
                request,
                topK,
                RagCallStatus.NoContext,
                Array.Empty<RetrievedChunk>(),
                insufficient,
                modelSelection,
                chatResult: null,
                stopwatch,
                failureStage: "input",
                failureReason: "Empty question.",
                cancellationToken);

            return insufficient;
        }

        IReadOnlyList<RetrievedChunk> chunks;
        try
        {
            // 问答服务不直接访问向量库，只消费 KnowledgeRetriever 已经二次校验过的结果。
            // 这个边界保证真实 chat client 接入后也只能看到授权后的上下文。
            chunks = await _knowledgeRetriever.SearchAsync(new RagSearchRequest
            {
                Query = request.Question,
                TenantId = request.TenantId,
                UserId = request.UserId,
                Scope = request.Scope,
                SchoolId = request.SchoolId,
                TopK = topK
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            var failed = CreateInsufficientResult();
            await WriteAnswerLogAsync(
                request,
                topK,
                RagCallStatus.Failed,
                Array.Empty<RetrievedChunk>(),
                failed,
                modelSelection,
                chatResult: null,
                stopwatch,
                failureStage: "search",
                failureReason: ex.Message,
                cancellationToken);

            return failed;
        }

        // 无授权上下文时必须拒答，不能让模型根据常识、训练数据或提示词补答案。
        if (chunks.Count == 0)
        {
            var insufficient = CreateInsufficientResult();
            await WriteAnswerLogAsync(
                request,
                topK,
                RagCallStatus.NoContext,
                chunks,
                insufficient,
                modelSelection,
                chatResult: null,
                stopwatch,
                failureStage: "retrieval",
                failureReason: "No authorized retrieval context was available.",
                cancellationToken);

            return insufficient;
        }

        var chatResult = await _chatClient.GenerateAsync(new RagChatClientRequest
        {
            ModelName = modelSelection.Alias,
            SystemPrompt = SystemPrompt,
            UserPrompt = BuildUserPrompt(request.Question, chunks),
            MaxTokens = Math.Max(1, modelSelection.MaxOutputTokens),
            Temperature = modelSelection.Temperature
        }, cancellationToken);

        var answer = new RagAnswerResult
        {
            // 优先使用 LiteLLM/DeepSeek 的回答；若模型网关不可用，返回确定性降级回答，
            // 但 citation 仍来自已授权 chunks，不会暴露未授权候选内容。
            Answer = chatResult.Succeeded ? chatResult.Content : BuildGroundedAnswer(request.Question, chunks),
            Citations = chunks.Select(CreateCitation).ToList(),
            Confidence = CalculateConfidence(chunks),
            NeedHumanReview = !chatResult.Succeeded || chunks.Count < 2 || chunks.Max(x => x.Score) < 0.65
        };

        await WriteAnswerLogAsync(
            request,
            topK,
            chatResult.Succeeded ? RagCallStatus.Succeeded : RagCallStatus.Failed,
            chunks,
            answer,
            modelSelection,
            chatResult,
            stopwatch,
            failureStage: chatResult.Succeeded ? null : chatResult.FailureStage,
            failureReason: chatResult.Succeeded ? null : chatResult.FailureReason,
            cancellationToken);

        return answer;
    }

    /// <summary>
    /// 写入 RAG Answer 调用日志。
    /// </summary>
    private Task WriteAnswerLogAsync(
        RagAnswerRequest request,
        int topK,
        RagCallStatus status,
        IReadOnlyList<RetrievedChunk> citations,
        RagAnswerResult result,
        AiModelSelection modelSelection,
        RagChatClientResult? chatResult,
        Stopwatch stopwatch,
        string? failureStage,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        var estimatedPromptTokens = RagCallLogWriter.EstimateTokenCount(request.Question) +
                                    citations.Sum(x => RagCallLogWriter.EstimateTokenCount(x.Content));
        var promptTokens = chatResult?.PromptTokenCount > 0
            ? chatResult.PromptTokenCount
            : estimatedPromptTokens;
        var completionTokens = chatResult?.CompletionTokenCount > 0
            ? chatResult.CompletionTokenCount
            : RagCallLogWriter.EstimateTokenCount(result.Answer);

        // token/cost 优先使用 LiteLLM 返回的 usage；模型不可用并降级时再使用估算值。
        // 完整 prompt 不入库，只记录问题摘要、引用链路和模型 metadata。
        return _callLogWriter.WriteAsync(
            new RagCallLogWriteRequest
            {
                CallType = RagCallType.Answer,
                Status = status,
                Query = request.Question,
                ModelProvider = modelSelection.Provider,
                ModelName = modelSelection.Alias,
                ModelVersion = modelSelection.BusinessModelVersion,
                CollectionName = _options.Embedding.CollectionName,
                TopK = topK,
                RetrievedChunkCount = citations.Count,
                PromptTokenCount = promptTokens,
                CompletionTokenCount = completionTokens,
                EstimatedCostUsd = 0,
                Confidence = result.Confidence,
                NeedHumanReview = result.NeedHumanReview,
                ExecutionDurationMs = ToElapsedMilliseconds(stopwatch),
                FailureStage = failureStage,
                FailureReason = failureReason
            },
            citations,
            cancellationToken);
    }

    /// <summary>
    /// 创建知识库依据不足的标准返回结果。
    /// </summary>
    private static RagAnswerResult CreateInsufficientResult()
    {
        return new RagAnswerResult
        {
            Answer = InsufficientKnowledgeAnswer,
            Citations = Array.Empty<RagCitation>(),
            Confidence = 0,
            NeedHumanReview = true
        };
    }

    /// <summary>
    /// 在 chat client 不可用时构造确定性、带来源的降级回答。
    /// </summary>
    private static string BuildGroundedAnswer(string question, IReadOnlyList<RetrievedChunk> chunks)
    {
        // MVP-1 不追求自然语言润色，优先用稳定格式把答案和来源绑在一起，
        // 便于人工验收 citation 是否能回到知识版本和 chunk。
        var builder = new StringBuilder();
        builder.Append("根据当前可访问知识库，针对“");
        builder.Append(TrimInline(question, 80));
        builder.AppendLine("”，可以参考以下依据：");

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            builder.Append(i + 1);
            builder.Append(". ");
            builder.Append(TrimInline(chunk.Content, MaxQuoteLength));
            builder.Append("（来源：");
            builder.Append(string.IsNullOrWhiteSpace(chunk.SourceTitle) ? "未命名知识" : chunk.SourceTitle);
            if (chunk.SourcePage.HasValue)
            {
                builder.Append("，第 ");
                builder.Append(chunk.SourcePage.Value);
                builder.Append(" 页");
            }

            builder.AppendLine("）");
        }

        builder.Append("上述回答仅基于已授权知识片段；未覆盖的信息需要人工确认或补充知识库。");
        return builder.ToString();
    }

    /// <summary>
    /// 构造传给 chat client 的用户提示词。
    /// </summary>
    private static string BuildUserPrompt(string question, IReadOnlyList<RetrievedChunk> chunks)
    {
        var builder = new StringBuilder();
        builder.AppendLine("用户问题：");
        builder.AppendLine(question.Trim());
        builder.AppendLine();
        builder.AppendLine("已通过权限校验的知识片段：");

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            builder.Append('[');
            builder.Append(i + 1);
            builder.Append("] 来源：");
            builder.Append(string.IsNullOrWhiteSpace(chunk.SourceTitle) ? "未命名知识" : chunk.SourceTitle);
            if (chunk.SourcePage.HasValue)
            {
                builder.Append("，第 ");
                builder.Append(chunk.SourcePage.Value);
                builder.Append(" 页");
            }

            builder.AppendLine();
            builder.AppendLine(TrimInline(chunk.Content, MaxPromptChunkLength));
            builder.AppendLine();
        }

        builder.AppendLine("回答要求：");
        builder.AppendLine("- 只能依据上述片段回答。");
        builder.AppendLine("- 需要在答案中使用 [1]、[2] 形式标注引用。");
        builder.AppendLine("- 如果上述片段不足以回答，回答“知识库依据不足”。");

        return builder.ToString();
    }

    /// <summary>
    /// 将检索 chunk 映射为问答结果 citation。
    /// </summary>
    private static RagCitation CreateCitation(RetrievedChunk chunk)
    {
        return new RagCitation
        {
            SchoolKnowledgeShareId = chunk.SchoolKnowledgeShareId,
            KnowledgeItemId = chunk.KnowledgeItemId,
            KnowledgeVersionId = chunk.KnowledgeVersionId,
            ChunkId = chunk.ChunkId,
            Title = chunk.SourceTitle,
            Page = chunk.SourcePage,
            Quote = TrimInline(chunk.Content, MaxQuoteLength)
        };
    }

    /// <summary>
    /// 根据检索分数计算回答置信度。
    /// </summary>
    private static double CalculateConfidence(IReadOnlyList<RetrievedChunk> chunks)
    {
        var score = chunks.Count == 0 ? 0 : chunks.Take(MaxContextChunks).Average(x => Math.Clamp(x.Score, 0, 1));
        return Math.Round(score, 2);
    }

    /// <summary>
    /// 将多行文本压缩成单行并限制最大长度。
    /// </summary>
    private static string TrimInline(string value, int maxLength)
    {
        var normalized = value
            .Replace("\r\n", " ")
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength].TrimEnd() + "...";
    }

    /// <summary>
    /// 将 Stopwatch 耗时转换为 int 毫秒值。
    /// </summary>
    private static int ToElapsedMilliseconds(Stopwatch stopwatch)
    {
        return stopwatch.ElapsedMilliseconds > int.MaxValue
            ? int.MaxValue
            : (int)stopwatch.ElapsedMilliseconds;
    }
}
