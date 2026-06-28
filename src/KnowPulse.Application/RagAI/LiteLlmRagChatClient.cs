using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace KnowPulse.RagAI;

/// <summary>
/// RAG 问答模型客户端抽象。
/// 应用服务只依赖该接口，具体供应商、网关认证和响应解析由实现类负责。
/// </summary>
public interface IRagChatClient
{
    /// <summary>
    /// 根据系统提示词、用户提示词和模型参数生成回答。
    /// </summary>
    Task<RagChatClientResult> GenerateAsync(RagChatClientRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Chat completion 请求。
/// SystemPrompt 表达安全边界，UserPrompt 表达已经授权的知识上下文和用户问题。
/// </summary>
public class RagChatClientRequest
{
    /// <summary>
    /// LiteLLM 中配置的 chat alias。
    /// </summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>
    /// 系统提示词，用于约束模型只能依据知识片段回答。
    /// </summary>
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>
    /// 用户提示词，包含问题和已通过权限校验的知识片段。
    /// </summary>
    public string UserPrompt { get; set; } = string.Empty;

    /// <summary>
    /// 最大输出 token 数。
    /// </summary>
    public int MaxTokens { get; set; }

    /// <summary>
    /// 模型 temperature。
    /// </summary>
    public double Temperature { get; set; }
}

/// <summary>
/// Chat completion 结果。
/// 成功时包含回答和 token usage，失败时包含阶段和原因，供 RAG 审计日志记录。
/// </summary>
public class RagChatClientResult
{
    /// <summary>
    /// 模型调用是否成功。
    /// </summary>
    public bool Succeeded { get; set; }

    /// <summary>
    /// 模型生成的回答内容。
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// prompt token 数。网关未返回 usage 时为 0。
    /// </summary>
    public int PromptTokenCount { get; set; }

    /// <summary>
    /// completion token 数。网关未返回 usage 时为 0。
    /// </summary>
    public int CompletionTokenCount { get; set; }

    /// <summary>
    /// 总 token 数。网关未返回 usage 时为 0。
    /// </summary>
    public int TotalTokenCount { get; set; }

    /// <summary>
    /// 失败阶段，默认 chat。
    /// </summary>
    public string? FailureStage { get; set; }

    /// <summary>
    /// 失败原因摘要，不应包含完整 prompt 或知识正文。
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// 构造成功结果。
    /// </summary>
    public static RagChatClientResult Success(
        string content,
        int promptTokenCount,
        int completionTokenCount,
        int totalTokenCount)
    {
        return new RagChatClientResult
        {
            Succeeded = true,
            Content = content,
            PromptTokenCount = promptTokenCount,
            CompletionTokenCount = completionTokenCount,
            TotalTokenCount = totalTokenCount
        };
    }

    /// <summary>
    /// 构造失败结果。
    /// </summary>
    public static RagChatClientResult Failed(string failureReason, string failureStage = "chat")
    {
        return new RagChatClientResult
        {
            Succeeded = false,
            FailureStage = failureStage,
            FailureReason = failureReason
        };
    }
}

/// <summary>
/// LiteLLM chat client。DeepSeek/OpenAI 等具体供应商只在 LiteLLM 配置里表达，
/// 应用层只调用 knowpulse-chat-default 这类稳定 alias。
/// </summary>
public class LiteLlmRagChatClient : IRagChatClient
{
    private readonly RagAiOptions _options;

    /// <summary>
    /// 创建 LiteLLM RAG chat 客户端。
    /// </summary>
    public LiteLlmRagChatClient(IOptions<RagAiOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// 调用 LiteLLM chat completion API 生成回答。
    /// </summary>
    public virtual async Task<RagChatClientResult> GenerateAsync(
        RagChatClientRequest request,
        CancellationToken cancellationToken = default)
    {
        var apiKey = Environment.GetEnvironmentVariable(_options.LiteLlm.ApiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // 缺少 key 时返回结构化失败而不是抛异常，让 RAG Answer 可以降级为确定性引用回答并写审计日志。
            return RagChatClientResult.Failed(
                $"LiteLLM API key environment variable '{_options.LiteLlm.ApiKeyEnvironmentVariable}' is not configured.");
        }

        try
        {
            using var httpClient = new HttpClient
            {
                BaseAddress = new Uri(_options.LiteLlm.BaseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(_options.LiteLlm.TimeoutSeconds)
            };
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var payload = JsonSerializer.Serialize(new
            {
                // 只传 appsettings 中的 LiteLLM alias，不在应用层泄漏具体供应商模型名。
                model = request.ModelName,
                messages = new object[]
                {
                    new { role = "system", content = request.SystemPrompt },
                    new { role = "user", content = request.UserPrompt }
                },
                temperature = request.Temperature,
                max_tokens = request.MaxTokens
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await httpClient.PostAsync("v1/chat/completions", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return RagChatClientResult.Failed(
                    $"LiteLLM chat request failed with HTTP {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var answer = ReadAnswerContent(document.RootElement);
            if (string.IsNullOrWhiteSpace(answer))
            {
                return RagChatClientResult.Failed("LiteLLM chat response did not include message content.");
            }

            var usage = ReadUsage(document.RootElement);
            return RagChatClientResult.Success(
                answer.Trim(),
                usage.PromptTokens,
                usage.CompletionTokens,
                usage.TotalTokens);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return RagChatClientResult.Failed("LiteLLM chat request failed: " + ex.GetType().Name);
        }
    }

    /// <summary>
    /// 从 OpenAI-compatible 响应中读取第一条回答文本。
    /// </summary>
    private static string? ReadAnswerContent(JsonElement root)
    {
        // OpenAI-compatible 响应的 choices[0].message.content 是稳定字段。
        // 解析失败时返回 null，由调用方记录结构化失败。
        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var firstChoice = choices.EnumerateArray().FirstOrDefault();
        if (firstChoice.ValueKind != JsonValueKind.Object ||
            !firstChoice.TryGetProperty("message", out var message) ||
            !message.TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return content.GetString();
    }

    /// <summary>
    /// 从响应中读取 token usage，缺失时返回 0。
    /// </summary>
    private static ChatUsage ReadUsage(JsonElement root)
    {
        // usage 在部分网关或降级响应中可能缺失；缺失时返回 0，由上层使用估算 token 数。
        if (!root.TryGetProperty("usage", out var usage) ||
            usage.ValueKind != JsonValueKind.Object)
        {
            return new ChatUsage(0, 0, 0);
        }

        var promptTokens = ReadInt(usage, "prompt_tokens");
        var completionTokens = ReadInt(usage, "completion_tokens");
        var totalTokens = ReadInt(usage, "total_tokens");

        return new ChatUsage(promptTokens, completionTokens, totalTokens);
    }

    /// <summary>
    /// 从 JSON 对象中安全读取整数属性。
    /// </summary>
    private static int ReadInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) &&
               value.TryGetInt32(out var result)
            ? result
            : 0;
    }

    /// <summary>
    /// LiteLLM 返回的 token usage 摘要。
    /// </summary>
    private sealed record ChatUsage(int PromptTokens, int CompletionTokens, int TotalTokens);
}
