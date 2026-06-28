using System;
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
/// LiteLLM embedding client。生产环境通过 OpenAI-compatible `/embeddings` 入口生成向量；
/// 本地没有 key 时返回明确失败，避免阻塞非 RAG 测试和普通启动。
/// </summary>
public class LiteLlmKnowledgeEmbeddingClient : IKnowledgeEmbeddingClient
{
    private readonly RagAiOptions _options;

    /// <summary>
    /// 创建 LiteLLM embedding 客户端。
    /// </summary>
    public LiteLlmKnowledgeEmbeddingClient(IOptions<RagAiOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// 通过 LiteLLM OpenAI-compatible embeddings API 生成向量。
    /// </summary>
    public virtual async Task<KnowledgeEmbeddingClientResult> GenerateAsync(
        KnowledgeEmbeddingClientRequest request,
        CancellationToken cancellationToken = default)
    {
        var apiKey = Environment.GetEnvironmentVariable(_options.LiteLlm.ApiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // 本地开发经常只跑知识库/权限测试而不配置模型 key。
            // 返回失败结果而不是抛异常，可以让上层把失败定位到 embedding 阶段并继续其他非 RAG 测试。
            return KnowledgeEmbeddingClientResult.Failed(
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
                // 这里使用 appsettings 中的 LiteLLM alias，而不是直接供应商模型名。
                // alias 由 deploy/litellm/config.example.yaml 管理，便于 fallback、限流和供应商切换。
                model = request.ModelName,
                input = request.Input
            });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await httpClient.PostAsync("embeddings", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return KnowledgeEmbeddingClientResult.Failed(
                    $"LiteLLM embedding request failed with HTTP {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            // OpenAI-compatible embedding 响应固定从 data[0].embedding 读取。
            // 如果网关返回结构异常，下面的读取会进入捕获分支并返回结构化失败。
            var embedding = document.RootElement
                .GetProperty("data")
                .EnumerateArray()
                .First()
                .GetProperty("embedding")
                .EnumerateArray()
                .Select(x => x.GetSingle())
                .ToArray();

            if (embedding.Length != request.VectorDimension)
            {
                // 向量维度不匹配会污染 collection，必须在写入任何主库或 Qdrant 记录前拦截。
                return KnowledgeEmbeddingClientResult.Failed(
                    $"Embedding dimension mismatch. Expected {request.VectorDimension}, actual {embedding.Length}.");
            }

            return KnowledgeEmbeddingClientResult.Success(embedding);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return KnowledgeEmbeddingClientResult.Failed(
                "LiteLLM embedding request failed: " + ex.GetType().Name);
        }
    }
}
