using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace KnowPulse.RagAI;

/// <summary>
/// Qdrant REST 适配层。这里不引入 Qdrant SDK 或 VectorData preview provider，
/// 只把项目内 IKnowledgeVectorStore 契约转换为 Qdrant HTTP 请求。
/// 该类只负责“向量库投影”的读写和维护，不负责最终业务授权；
/// 最终能否返回某个 chunk，必须由 KnowledgeRetriever 回 PostgreSQL 再次校验。
/// </summary>
public class QdrantKnowledgeVectorStore : IKnowledgeVectorStore, IKnowledgeVectorStoreMaintenance, IDisposable
{
    // 这些字段是权限前置过滤和引用追踪的最低索引集。
    // Qdrant 只保存可重建 payload，真正的业务事实仍以 PostgreSQL 为准。
    private static readonly IReadOnlyDictionary<string, string> RequiredPayloadIndexes = new Dictionary<string, string>
    {
        // 租户隔离字段；host 侧数据会以字符串 "host" 写入 payload。
        ["tenantId"] = "keyword",

        // citation 和二次校验必须能从向量命中回到知识条目、版本和 chunk。
        ["knowledgeItemId"] = "keyword",
        ["knowledgeVersionId"] = "keyword",
        ["chunkId"] = "keyword",

        // RAG 默认只检索已发布知识，Qdrant 先做粗过滤，主库再做最终判断。
        ["isPublished"] = "bool",

        // 组织、角色和用户范围只用于缩小候选集，不能替代业务权限校验。
        ["departmentIds"] = "keyword",
        ["positionIds"] = "keyword",
        ["roleIds"] = "keyword",
        ["userIds"] = "keyword",

        // 安全等级使用 range filter，低权限用户不应召回高密级候选。
        ["securityLevel"] = "integer"
    };

    /// <summary>
    /// JSON 序列化时忽略 null，避免把未设置的可选 payload 字段写成显式 null。
    /// Qdrant payload filter 更适合处理“字段不存在”和具体值，而不是混入大量 null。
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly RagVectorStoreOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _disposeHttpClient;

    /// <summary>
    /// 生产默认构造函数。
    /// 根据 RagAI:VectorStore 配置创建 HttpClient，并由当前适配器负责释放。
    /// </summary>
    /// <param name="options">RAG/AI 配置，主要使用其中的 VectorStore 配置。</param>
    public QdrantKnowledgeVectorStore(IOptions<RagAiOptions> options)
        : this(options, CreateDefaultHttpClient(options.Value.VectorStore), disposeHttpClient: true)
    {
    }

    /// <summary>
    /// 测试或外部托管 HttpClient 的构造函数。
    /// 传入的 HttpClient 生命周期由调用方管理，本类不会 Dispose 它。
    /// </summary>
    /// <param name="options">RAG/AI 配置，主要使用其中的 VectorStore 配置。</param>
    /// <param name="httpClient">外部创建并配置好的 HttpClient。</param>
    public QdrantKnowledgeVectorStore(IOptions<RagAiOptions> options, HttpClient httpClient)
        : this(options, httpClient, disposeHttpClient: false)
    {
    }

    /// <summary>
    /// 统一初始化入口，记录是否由本类释放 HttpClient。
    /// 这样既能支持生产默认 HttpClient，也能支持测试注入 fake handler。
    /// </summary>
    /// <param name="options">RAG/AI 配置，主要使用其中的 VectorStore 配置。</param>
    /// <param name="httpClient">用于访问 Qdrant REST API 的 HttpClient。</param>
    /// <param name="disposeHttpClient">当前适配器 Dispose 时是否同时释放 HttpClient。</param>
    private QdrantKnowledgeVectorStore(IOptions<RagAiOptions> options, HttpClient httpClient, bool disposeHttpClient)
    {
        _options = options.Value.VectorStore;
        _httpClient = httpClient;
        _disposeHttpClient = disposeHttpClient;
    }

    /// <summary>
    /// 将知识分块向量写入 Qdrant collection。
    /// 使用 chunk id 作为 point id，因此重复写入同一 chunk 会覆盖旧投影，保持向量库幂等。
    /// </summary>
    /// <param name="chunks">待写入的知识分块向量记录，包含 embedding 和可重建业务 payload。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步写入任务。</returns>
    public virtual async Task UpsertChunksAsync(
        IReadOnlyList<KnowledgeChunkVectorRecord> chunks,
        CancellationToken cancellationToken = default)
    {
        // 空集合是合法输入，直接返回可让调用方在无新增 chunk 时保持幂等。
        if (chunks.Count == 0)
        {
            return;
        }

        // point id 固定使用 chunk id，重复写入同一 chunk 时表现为 upsert。
        // 如果内容或模型版本变化，主库会先产生新的 active embedding 记录，再由这里覆盖向量投影。
        var body = new
        {
            points = chunks.Select(x => new
            {
                id = BuildPointId(x),
                vector = x.Embedding.ToArray(),
                payload = BuildPayload(x)
            }).ToArray()
        };

        using var response = await SendJsonAsync(
            HttpMethod.Put,
            $"collections/{EscapeCollectionName()}/points?wait=true",
            body,
            cancellationToken);
        await EnsureSuccessAsync(response, "Qdrant upsert failed.", cancellationToken);
    }

    /// <summary>
    /// 按查询向量和权限粗过滤条件从 Qdrant 检索候选分块。
    /// 返回值只是候选结果，调用方必须继续回 PostgreSQL 做知识状态和权限二次校验。
    /// </summary>
    /// <param name="request">向量检索请求，包含查询向量、TopK 和当前用户权限过滤条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>Qdrant 返回并映射后的候选命中列表。</returns>
    public virtual async Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
        KnowledgeVectorSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        // 检索时要求 Qdrant 返回 payload，因为后续 KnowledgeRetriever 需要拿 chunk/version/id
        // 回 PostgreSQL 做二次校验和 citation 组装。
        var body = new
        {
            vector = request.QueryEmbedding.ToArray(),
            limit = request.TopK,
            filter = BuildSearchFilter(request.PermissionFilter),
            with_payload = true
        };

        using var response = await SendJsonAsync(
            HttpMethod.Post,
            $"collections/{EscapeCollectionName()}/points/search",
            body,
            cancellationToken);
        await EnsureSuccessAsync(response, "Qdrant search failed.", cancellationToken);

        // Qdrant REST 标准响应形如 { result: [...] }。
        // 若不同版本/错误代理返回了非数组 result，按无命中处理，避免解析异常泄漏到上层检索流程。
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("result", out var result) ||
            result.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<VectorSearchHit>();
        }

        return result.EnumerateArray().Select(MapSearchHit).ToList();
    }

    /// <summary>
    /// 删除指定知识版本在 Qdrant 中的所有 point。
    /// 该操作只清理向量库投影，不删除 PostgreSQL 中的 chunk、embedding 记录或审计数据。
    /// </summary>
    /// <param name="knowledgeVersionId">需要删除向量投影的知识版本编号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步删除任务。</returns>
    public virtual async Task DeleteByKnowledgeVersionAsync(Guid knowledgeVersionId, CancellationToken cancellationToken = default)
    {
        // 删除按 knowledgeVersionId 过滤，覆盖某个版本的所有 chunk point。
        // 主库 embedding/chunk 记录仍会保留逻辑状态，Qdrant 只作为可重建投影被清理。
        var body = new
        {
            filter = new
            {
                must = new object[]
                {
                    MatchValue("knowledgeVersionId", knowledgeVersionId.ToString("D"))
                }
            }
        };

        using var response = await SendJsonAsync(
            HttpMethod.Post,
            $"collections/{EscapeCollectionName()}/points/delete?wait=true",
            body,
            cancellationToken);
        await EnsureSuccessAsync(response, "Qdrant delete failed.", cancellationToken);
    }

    /// <summary>
    /// 确保知识向量 collection 和必需 payload index 已存在。
    /// 方法可重复执行：缺失 collection 时创建，缺失 index 时补齐，不会删除已有 point。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>补齐后的 collection 健康检查结果。</returns>
    public virtual async Task<KnowledgeVectorCollectionHealthResult> EnsureKnowledgeCollectionAsync(
        CancellationToken cancellationToken = default)
    {
        // 先读健康状态，缺 collection 时创建 collection，再根据缺失 index 逐个补齐。
        // 该方法不删除已有 point，也不重建 collection，适合本地和部署时重复执行。
        var health = await GetKnowledgeCollectionHealthAsync(cancellationToken);
        if (!health.CollectionExists)
        {
            await CreateCollectionAsync(cancellationToken);
            health = await GetKnowledgeCollectionHealthAsync(cancellationToken);
        }

        foreach (var missingIndex in health.MissingPayloadIndexes)
        {
            // payload index 创建是幂等维护动作。脚本和应用维护接口使用同一组字段，避免环境漂移。
            await CreatePayloadIndexAsync(missingIndex, cancellationToken);
        }

        return await GetKnowledgeCollectionHealthAsync(cancellationToken);
    }

    /// <summary>
    /// 读取知识向量 collection 的只读健康状态。
    /// 用于判断 collection 是否存在、维度是否匹配以及 payload index 是否齐全。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>collection 健康检查结果；collection 不存在时返回结构化失败结果。</returns>
    public virtual async Task<KnowledgeVectorCollectionHealthResult> GetKnowledgeCollectionHealthAsync(
        CancellationToken cancellationToken = default)
    {
        // 健康检查必须是只读操作：collection 不存在时返回结构化结果，不抛异常。
        using var response = await _httpClient.GetAsync($"collections/{EscapeCollectionName()}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return CreateHealthResult(collectionExists: false, actualVectorDimension: null, existingPayloadIndexes: Array.Empty<string>());
        }

        await EnsureSuccessAsync(response, "Qdrant collection health check failed.", cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        // Qdrant 正常响应包在 result 下；测试 fake 或某些代理可能直接返回 collection 对象。
        var result = document.RootElement.TryGetProperty("result", out var resultElement)
            ? resultElement
            : document.RootElement;

        var actualDimension = ReadVectorDimension(result);
        var existingPayloadIndexes = ReadPayloadSchemaFields(result);

        return CreateHealthResult(
            collectionExists: true,
            actualVectorDimension: actualDimension,
            existingPayloadIndexes: existingPayloadIndexes);
    }

    /// <summary>
    /// 释放由本适配器创建的 HttpClient。
    /// 外部注入的 HttpClient 不在这里释放，以免破坏测试或 DI 托管生命周期。
    /// </summary>
    public void Dispose()
    {
        // 只有默认构造函数创建的 HttpClient 才由本类释放；
        // 注入的 HttpClient 通常由 DI、测试或外部生命周期管理。
        if (_disposeHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    /// <summary>
    /// 发送 JSON HTTP 请求到 Qdrant。
    /// 该方法只负责序列化请求体和发送请求，不负责判断响应是否成功。
    /// </summary>
    /// <param name="method">HTTP 方法。</param>
    /// <param name="requestUri">相对 Qdrant BaseAddress 的请求路径。</param>
    /// <param name="body">需要序列化为 JSON 的请求体对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>Qdrant 返回的 HTTP 响应，调用方负责释放。</returns>
    private async Task<HttpResponseMessage> SendJsonAsync(
        HttpMethod method,
        string requestUri,
        object body,
        CancellationToken cancellationToken)
    {
        // 每次请求都创建独立 HttpRequestMessage，避免 HttpContent 被复用导致 ObjectDisposedException。
        // 调用方负责使用 using 释放返回的 HttpResponseMessage。
        using var content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(method, requestUri)
        {
            Content = content
        };

        return await _httpClient.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// 创建知识向量 collection。
    /// 当前使用单 unnamed vector，维度和距离算法来自 RagAI:VectorStore 配置。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步创建任务。</returns>
    private async Task CreateCollectionAsync(CancellationToken cancellationToken)
    {
        // MVP-1 使用单 unnamed vector collection；维度和距离算法来自 appsettings。
        // 如果未来切换 named vector，应在这里和 ReadVectorDimension 中同时扩展。
        var body = new
        {
            vectors = new
            {
                size = _options.VectorDimension,
                distance = _options.Distance
            }
        };

        using var response = await SendJsonAsync(
            HttpMethod.Put,
            $"collections/{EscapeCollectionName()}",
            body,
            cancellationToken);
        await EnsureSuccessAsync(response, "Qdrant collection creation failed.", cancellationToken);
    }

    /// <summary>
    /// 为指定 payload 字段创建 Qdrant payload index。
    /// 字段必须来自 RequiredPayloadIndexes，确保应用和运维脚本使用同一套索引策略。
    /// </summary>
    /// <param name="fieldName">需要创建索引的 payload 字段名。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步创建任务。</returns>
    private async Task CreatePayloadIndexAsync(string fieldName, CancellationToken cancellationToken)
    {
        // 只允许创建 RequiredPayloadIndexes 中声明的字段，避免调用方传入任意 payload 字段污染索引策略。
        var body = new
        {
            field_name = fieldName,
            field_schema = RequiredPayloadIndexes[fieldName]
        };

        using var response = await SendJsonAsync(
            HttpMethod.Put,
            $"collections/{EscapeCollectionName()}/index?wait=true",
            body,
            cancellationToken);
        await EnsureSuccessAsync(response, $"Qdrant payload index creation failed for '{fieldName}'.", cancellationToken);
    }

    /// <summary>
    /// 校验 Qdrant HTTP 响应是否成功。
    /// 失败时读取响应体并拼入异常，帮助定位 collection、filter 或 payload 问题。
    /// </summary>
    /// <param name="response">Qdrant HTTP 响应。</param>
    /// <param name="message">失败时使用的业务错误前缀。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>响应成功时完成的任务。</returns>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string message, CancellationToken cancellationToken)
    {
        // Qdrant 错误响应通常带有可读 body。把 body 拼进异常，便于运维定位 collection、维度或 filter 问题。
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"{message} HTTP {(int)response.StatusCode}. {body}");
    }

    /// <summary>
    /// 构造 Qdrant 搜索过滤条件。
    /// must 表达租户、发布态、安全等级等硬条件，should 表达任一可见权限范围。
    /// </summary>
    /// <param name="filter">当前用户的权限粗过滤条件。</param>
    /// <returns>可直接序列化给 Qdrant search API 的 filter 对象。</returns>
    private object BuildSearchFilter(KnowledgeVectorPermissionFilter filter)
    {
        // must / should 结构与 Qdrant filter 契约保持一致：
        // - must 中每项都必须满足；
        // - should 中至少满足一个范围即可成为候选。
        var must = new List<object>();
        if (filter.KnowledgeItemIds.Count > 0)
        {
            must.Add(new { key = "knowledgeItemId", match = new { any = filter.KnowledgeItemIds.Select(x => x.ToString("D")).ToArray() } });
        }
        var should = new List<object>();

        // must 表达硬条件：租户、发布态、安全等级。should 表达“任一组织/角色/用户范围可见”。
        // 这些条件只减少候选，不替代 KnowledgeRetriever 中的 PostgreSQL 二次校验。
        if (filter.TenantId.HasValue)
        {
            must.Add(MatchValue("tenantId", filter.TenantId.Value.ToString("D")));
        }

        if (filter.PublishedOnly)
        {
            must.Add(MatchValue("isPublished", true));
        }

        must.Add(Range("securityLevel", lessThanOrEqual: filter.SecurityLevel));

        if (filter.AllowedCurriculumChunkIds != null)
        {
            var chunkIds = filter.AllowedCurriculumChunkIds.Distinct().ToArray();
            if (chunkIds.Length == 0) must.Add(MatchValue("chunkId", Guid.Empty.ToString("D")));
            else must.Add(new
            {
                key = "chunkId",
                match = new { any = chunkIds.Select(x => x.ToString("D")).ToArray() }
            });
        }

        if (filter.Scope == KnowledgeSearchScope.AllAccessible)
        {
            should.Add(MatchValue("visibility", "All"));
            AddAnyMatch(should, "departmentIds", filter.DepartmentIds);
            AddAnyMatch(should, "positionIds", filter.PositionIds);
            AddAnyMatch(should, "roleIds", filter.RoleIds);
            AddAnyMatch(should, "userIds", filter.UserIds);
        }
        if (filter.Scope != KnowledgeSearchScope.SchoolShared && filter.UserId.HasValue)
        {
            should.Add(MatchValue("userIds", filter.UserId.Value.ToString("D")));
        }
        if (filter.Scope != KnowledgeSearchScope.PersonalLibrary && filter.AllowedSharedVersionIds.Count > 0)
            should.Add(new
            {
                key = "knowledgeVersionId",
                match = new { any = filter.AllowedSharedVersionIds.Distinct().Select(x => x.ToString("D")).ToArray() }
            });
        // 空学校授权不能退化成只有 tenant/published 的宽范围搜索。
        if (should.Count == 0) must.Add(MatchValue("knowledgeVersionId", Guid.Empty.ToString("D")));

        return new
        {
            must,
            should
        };
    }

    /// <summary>
    /// 为某个 payload 数组字段追加多个 match 条件。
    /// 用于部门、岗位、角色和用户范围的 should 过滤。
    /// </summary>
    /// <param name="conditions">正在构造的 Qdrant should 条件集合。</param>
    /// <param name="key">payload 字段名。</param>
    /// <param name="values">需要匹配的 Guid 值集合。</param>
    private static void AddAnyMatch(List<object> conditions, string key, IEnumerable<Guid> values)
    {
        // 同一字段可能来自多个组织/角色/用户范围，Distinct 可减少重复 should 条件。
        foreach (var value in values.Distinct())
        {
            conditions.Add(MatchValue(key, value.ToString("D")));
        }
    }

    /// <summary>
    /// 将业务侧 chunk 向量记录转换为 Qdrant payload。
    /// payload 只保存可重建投影和粗过滤字段，最终返回前仍以 PostgreSQL 当前状态为准。
    /// </summary>
    /// <param name="record">业务侧知识分块向量记录。</param>
    /// <returns>可序列化到 Qdrant point 的 payload 字典。</returns>
    private static Dictionary<string, object> BuildPayload(KnowledgeChunkVectorRecord record)
    {
        // payload 使用字符串形式保存 Guid，便于 Qdrant keyword index 精确匹配。
        // content 存在这里只用于向量召回后的候选文本，最终返回前仍会回主库读取 active chunk。
        var payload = new Dictionary<string, object>
        {
            // Qdrant keyword 不支持 Guid 类型，统一写入 D 格式字符串，host 数据用固定字符串区分。
            ["tenantId"] = record.TenantId?.ToString("D") ?? "host",
            ["knowledgeItemId"] = record.KnowledgeItemId.ToString("D"),
            ["knowledgeVersionId"] = record.KnowledgeVersionId.ToString("D"),
            ["chunkId"] = record.ChunkId.ToString("D"),

            // content 是候选召回文本副本，不是最终回答/审计的可信来源。
            ["content"] = record.Content,

            // visibility 为空时按公开处理，避免旧数据或最小测试数据因为缺值完全无法召回。
            ["visibility"] = string.IsNullOrWhiteSpace(record.Payload.Visibility) ? "All" : record.Payload.Visibility,
            ["tagIds"] = record.Payload.TagIds.Select(x => x.ToString("D")).ToArray(),
            ["departmentIds"] = record.Payload.DepartmentIds.Select(x => x.ToString("D")).ToArray(),
            ["positionIds"] = record.Payload.PositionIds.Select(x => x.ToString("D")).ToArray(),
            ["roleIds"] = record.Payload.RoleIds.Select(x => x.ToString("D")).ToArray(),
            ["userIds"] = record.Payload.UserIds.Select(x => x.ToString("D")).ToArray(),
            ["securityLevel"] = record.Payload.SecurityLevel,
            ["isPublished"] = record.Payload.IsPublished,
            ["sourceTitle"] = record.Payload.SourceTitle,
            ["embeddingModel"] = record.Payload.EmbeddingModel,
            ["embeddingModelVersion"] = record.Payload.EmbeddingModelVersion
        };

        if (record.Payload.CategoryId.HasValue)
        {
            // 可选字段只在有值时写入，保持 payload 紧凑，也避免 Qdrant 中出现 categoryId=null。
            payload["categoryId"] = record.Payload.CategoryId.Value.ToString("D");
        }

        if (record.Payload.SourcePage.HasValue)
        {
            // 页码用于 citation 展示，不参与权限过滤。
            payload["sourcePage"] = record.Payload.SourcePage.Value;
        }

        if (record.Payload.EffectiveFrom.HasValue)
        {
            // 生效时间使用 round-trip UTC 字符串，后续若引入 Qdrant 时间过滤可以保持解析稳定。
            payload["effectiveFrom"] = record.Payload.EffectiveFrom.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        }

        if (record.Payload.EffectiveTo.HasValue)
        {
            payload["effectiveTo"] = record.Payload.EffectiveTo.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        }

        return payload;
    }

    /// <summary>
    /// 将 Qdrant point search 结果映射为项目内候选命中 DTO。
    /// 只读取业务需要的稳定字段，未知字段忽略，缺失字段使用保守默认值。
    /// </summary>
    /// <param name="point">Qdrant search result 中的单个 point JSON。</param>
    /// <returns>项目内向量检索候选命中。</returns>
    private static VectorSearchHit MapSearchHit(JsonElement point)
    {
        // Qdrant REST 返回结构在不同版本/配置下可能包含 result 包装或 named vector 信息；
        // 这里只解析业务需要的稳定 payload 字段，未知字段全部忽略。
        var payload = point.TryGetProperty("payload", out var payloadElement)
            ? payloadElement
            : default;
        // 如果 payload 中没有 chunkId，退回使用 Qdrant point id。
        // 这让旧数据或最小化测试响应仍能被映射成候选，再由上层二次校验决定是否可用。
        var pointId = point.TryGetProperty("id", out var idElement) ? ReadGuid(idElement) : null;

        return new VectorSearchHit
        {
            ChunkId = ReadGuid(payload, "chunkId") ?? pointId ?? Guid.Empty,
            KnowledgeItemId = ReadGuid(payload, "knowledgeItemId") ?? Guid.Empty,
            KnowledgeVersionId = ReadGuid(payload, "knowledgeVersionId") ?? Guid.Empty,
            Score = point.TryGetProperty("score", out var score) && score.TryGetDouble(out var scoreValue) ? scoreValue : 0,
            Content = ReadString(payload, "content") ?? string.Empty,
            Payload = new KnowledgeVectorPayload
            {
                TenantId = ReadGuid(payload, "tenantId"),
                KnowledgeItemId = ReadGuid(payload, "knowledgeItemId") ?? Guid.Empty,
                KnowledgeVersionId = ReadGuid(payload, "knowledgeVersionId") ?? Guid.Empty,
                ChunkId = ReadGuid(payload, "chunkId") ?? pointId ?? Guid.Empty,
                CategoryId = ReadGuid(payload, "categoryId"),
                Visibility = ReadString(payload, "visibility") ?? "All",
                TagIds = ReadGuidArray(payload, "tagIds"),
                DepartmentIds = ReadGuidArray(payload, "departmentIds"),
                PositionIds = ReadGuidArray(payload, "positionIds"),
                RoleIds = ReadGuidArray(payload, "roleIds"),
                UserIds = ReadGuidArray(payload, "userIds"),
                SecurityLevel = ReadInt(payload, "securityLevel"),
                IsPublished = ReadBool(payload, "isPublished"),
                EffectiveFrom = ReadDateTime(payload, "effectiveFrom"),
                EffectiveTo = ReadDateTime(payload, "effectiveTo"),
                SourceTitle = ReadString(payload, "sourceTitle") ?? string.Empty,
                SourcePage = ReadNullableInt(payload, "sourcePage"),
                EmbeddingModel = ReadString(payload, "embeddingModel") ?? string.Empty,
                EmbeddingModelVersion = ReadString(payload, "embeddingModelVersion") ?? string.Empty
            }
        };
    }

    /// <summary>
    /// 构造 Qdrant 精确匹配条件。
    /// 适用于 keyword、bool 等需要 match.value 的 payload 字段。
    /// </summary>
    /// <param name="key">payload 字段名。</param>
    /// <param name="value">需要精确匹配的值。</param>
    /// <returns>Qdrant match filter 条件对象。</returns>
    private static object MatchValue(string key, object value)
    {
        // Qdrant match value 条件，用于 keyword / bool 精确匹配。
        return new
        {
            key,
            match = new
            {
                value
            }
        };
    }

    /// <summary>
    /// 构造 Qdrant 数值范围条件。
    /// 当前用于 securityLevel 小于等于当前用户安全等级的过滤。
    /// </summary>
    /// <param name="key">payload 字段名。</param>
    /// <param name="lessThanOrEqual">lte 上界。</param>
    /// <returns>Qdrant range filter 条件对象。</returns>
    private static object Range(string key, int lessThanOrEqual)
    {
        // Qdrant range 条件。当前只需要 securityLevel <= 当前用户等级。
        return new
        {
            key,
            range = new
            {
                lte = lessThanOrEqual
            }
        };
    }

    /// <summary>
    /// 构造 Qdrant point id。
    /// 使用 chunk id 的 D 格式字符串，确保同一 chunk 的向量投影可以幂等覆盖。
    /// </summary>
    /// <param name="record">知识分块向量记录。</param>
    /// <returns>Qdrant point id。</returns>
    private static string BuildPointId(KnowledgeChunkVectorRecord record)
    {
        // 使用 chunk id 作为 point id，可保证同一 chunk 重复写入时覆盖旧投影而不是生成重复 point。
        return record.ChunkId.ToString("D");
    }

    /// <summary>
    /// 转义 collection 名称以便放入 URL path。
    /// </summary>
    /// <returns>URL 安全的 collection 名称。</returns>
    private string EscapeCollectionName()
    {
        // collection name 出现在 URL path 中，必须转义，避免特殊字符破坏请求路径。
        return Uri.EscapeDataString(_options.KnowledgeCollectionName);
    }

    /// <summary>
    /// 根据向量库配置创建默认 HttpClient。
    /// BaseAddress 来自 Qdrant endpoint，API key 只从配置指定的环境变量读取。
    /// </summary>
    /// <param name="options">向量库配置。</param>
    /// <returns>配置好 BaseAddress 和可选 api-key header 的 HttpClient。</returns>
    private static HttpClient CreateDefaultHttpClient(RagVectorStoreOptions options)
    {
        // BaseAddress 统一补尾部斜杠，使后续相对路径拼接保持稳定。
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(options.Endpoint.TrimEnd('/') + "/")
        };

        if (!string.IsNullOrWhiteSpace(options.ApiKeyEnvironmentVariable))
        {
            // API key 只从环境变量读取，不进入 appsettings 或仓库。
            var apiKey = Environment.GetEnvironmentVariable(options.ApiKeyEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                httpClient.DefaultRequestHeaders.ApiKey(apiKey);
            }
        }

        return httpClient;
    }

    /// <summary>
    /// 根据 collection 检查结果组装健康 DTO。
    /// 同时计算缺失 payload index，供 EnsureKnowledgeCollectionAsync 做幂等补齐。
    /// </summary>
    /// <param name="collectionExists">collection 是否存在。</param>
    /// <param name="actualVectorDimension">实际向量维度，无法读取时为空。</param>
    /// <param name="existingPayloadIndexes">Qdrant 当前已有 payload index 字段。</param>
    /// <returns>项目内 collection 健康检查结果。</returns>
    private KnowledgeVectorCollectionHealthResult CreateHealthResult(
        bool collectionExists,
        int? actualVectorDimension,
        IReadOnlyList<string> existingPayloadIndexes)
    {
        // 使用 Ordinal 比较确保字段名严格匹配，避免大小写漂移导致 filter 在运行时失效。
        var missingPayloadIndexes = RequiredPayloadIndexes.Keys
            .Except(existingPayloadIndexes, StringComparer.Ordinal)
            .ToArray();

        return new KnowledgeVectorCollectionHealthResult
        {
            CollectionName = _options.KnowledgeCollectionName,
            ExpectedVectorDimension = _options.VectorDimension,
            ActualVectorDimension = actualVectorDimension,
            CollectionExists = collectionExists,
            VectorDimensionMatches = actualVectorDimension == _options.VectorDimension,
            RequiredPayloadIndexes = RequiredPayloadIndexes.Keys.ToArray(),
            ExistingPayloadIndexes = existingPayloadIndexes,
            MissingPayloadIndexes = missingPayloadIndexes
        };
    }

    /// <summary>
    /// 从 Qdrant collection JSON 中读取向量维度。
    /// 同时兼容 unnamed vector 和 named vector 的响应结构。
    /// </summary>
    /// <param name="collection">Qdrant collection JSON。</param>
    /// <returns>读取到的向量维度；无法识别时返回 null。</returns>
    private static int? ReadVectorDimension(JsonElement collection)
    {
        // Qdrant unnamed vector: config.params.vectors.size。
        // 早期或 named vector 配置可能是 config.params.vectors.{name}.size，下面两种都兼容。
        if (!collection.TryGetProperty("config", out var config) ||
            !config.TryGetProperty("params", out var parameters) ||
            !parameters.TryGetProperty("vectors", out var vectors))
        {
            return null;
        }

        if (vectors.ValueKind == JsonValueKind.Object &&
            vectors.TryGetProperty("size", out var unnamedSize) &&
            unnamedSize.TryGetInt32(out var unnamedDimension))
        {
            return unnamedDimension;
        }

        if (vectors.ValueKind == JsonValueKind.Object)
        {
            foreach (var namedVector in vectors.EnumerateObject())
            {
                if (namedVector.Value.ValueKind == JsonValueKind.Object &&
                    namedVector.Value.TryGetProperty("size", out var namedSize) &&
                    namedSize.TryGetInt32(out var namedDimension))
                {
                    return namedDimension;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 从 Qdrant collection JSON 中读取已创建的 payload index 字段名。
    /// </summary>
    /// <param name="collection">Qdrant collection JSON。</param>
    /// <returns>payload_schema 中的字段名列表。</returns>
    private static IReadOnlyList<string> ReadPayloadSchemaFields(JsonElement collection)
    {
        // payload_schema 的属性名就是 Qdrant 已创建 payload index 的字段名。
        if (!collection.TryGetProperty("payload_schema", out var payloadSchema) ||
            payloadSchema.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<string>();
        }

        return payloadSchema.EnumerateObject().Select(x => x.Name).ToArray();
    }

    /// <summary>
    /// 从 payload 对象的指定属性读取 Guid。
    /// 字段不存在、payload 非对象或值不是有效 Guid 时返回 null。
    /// </summary>
    /// <param name="payload">Qdrant payload JSON。</param>
    /// <param name="property">需要读取的属性名。</param>
    /// <returns>解析出的 Guid，或 null。</returns>
    private static Guid? ReadGuid(JsonElement payload, string property)
    {
        // payload 缺字段或不是对象时返回 null，让调用方使用默认值或 point id fallback。
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(property, out var value))
        {
            return null;
        }

        return ReadGuid(value);
    }

    /// <summary>
    /// 从单个 JSON 值读取 Guid。
    /// Qdrant payload 中 Guid 按字符串保存，因此只接受 JSON string。
    /// </summary>
    /// <param name="value">JSON 值。</param>
    /// <returns>解析出的 Guid，或 null。</returns>
    private static Guid? ReadGuid(JsonElement value)
    {
        // Qdrant payload 中的 Guid 统一按字符串保存，解析失败视为缺失。
        return value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var guid)
            ? guid
            : null;
    }

    /// <summary>
    /// 从 payload 对象的指定属性读取字符串。
    /// 只接受 JSON string，避免异常类型被隐式转换成误导性文本。
    /// </summary>
    /// <param name="payload">Qdrant payload JSON。</param>
    /// <param name="property">需要读取的属性名。</param>
    /// <returns>读取到的字符串，或 null。</returns>
    private static string? ReadString(JsonElement payload, string property)
    {
        // 只接受 JSON string，避免数字/对象等异常 payload 被隐式转换成误导性的文本。
        return payload.ValueKind == JsonValueKind.Object &&
               payload.TryGetProperty(property, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    /// <summary>
    /// 从 payload 对象的指定属性读取 Guid 数组。
    /// 非数组或非法 Guid 元素会被安全忽略，避免坏 payload 阻断整次检索。
    /// </summary>
    /// <param name="payload">Qdrant payload JSON。</param>
    /// <param name="property">需要读取的数组属性名。</param>
    /// <returns>解析成功的 Guid 列表。</returns>
    private static IReadOnlyList<Guid> ReadGuidArray(JsonElement payload, string property)
    {
        // 权限范围字段都是 Guid 字符串数组。非法元素会被跳过，避免一条坏数据导致整次检索失败。
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<Guid>();
        }

        return value.EnumerateArray()
            .Select(ReadGuid)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToList();
    }

    /// <summary>
    /// 从 payload 对象的指定属性读取整数。
    /// 读取失败时返回 0，最终权限判断仍会回主库读取真实业务值。
    /// </summary>
    /// <param name="payload">Qdrant payload JSON。</param>
    /// <param name="property">需要读取的属性名。</param>
    /// <returns>读取到的整数，或默认值 0。</returns>
    private static int ReadInt(JsonElement payload, string property)
    {
        // 安全等级缺失时按 0 处理；最终权限仍会回主库读取当前安全等级。
        return payload.ValueKind == JsonValueKind.Object &&
               payload.TryGetProperty(property, out var value) &&
               value.TryGetInt32(out var result)
            ? result
            : 0;
    }

    /// <summary>
    /// 从 payload 对象的指定属性读取可空整数。
    /// 用于页码等展示字段，读取失败不应影响检索流程。
    /// </summary>
    /// <param name="payload">Qdrant payload JSON。</param>
    /// <param name="property">需要读取的属性名。</param>
    /// <returns>读取到的整数，或 null。</returns>
    private static int? ReadNullableInt(JsonElement payload, string property)
    {
        // 页码是展示字段，缺失或格式不对时不应阻断检索。
        return payload.ValueKind == JsonValueKind.Object &&
               payload.TryGetProperty(property, out var value) &&
               value.TryGetInt32(out var result)
            ? result
            : null;
    }

    /// <summary>
    /// 从 payload 对象的指定属性读取布尔值。
    /// 只有 JSON true 被视为 true，缺失、false 或类型异常都按 false 处理。
    /// </summary>
    /// <param name="payload">Qdrant payload JSON。</param>
    /// <param name="property">需要读取的属性名。</param>
    /// <returns>布尔读取结果。</returns>
    private static bool ReadBool(JsonElement payload, string property)
    {
        // 只有 JSON true 才视为 true，缺失、false 或非 bool 都按 false 处理。
        return payload.ValueKind == JsonValueKind.Object &&
               payload.TryGetProperty(property, out var value) &&
               value.ValueKind == JsonValueKind.True;
    }

    /// <summary>
    /// 从 payload 对象的指定属性读取时间。
    /// 解析 BuildPayload 写入的 round-trip 字符串格式，保留 UTC/local 信息。
    /// </summary>
    /// <param name="payload">Qdrant payload JSON。</param>
    /// <param name="property">需要读取的属性名。</param>
    /// <returns>解析出的时间，或 null。</returns>
    private static DateTime? ReadDateTime(JsonElement payload, string property)
    {
        // 使用 RoundtripKind 解析 BuildPayload 写入的 "O" 格式时间，保证 UTC/local 信息不丢失。
        return payload.ValueKind == JsonValueKind.Object &&
               payload.TryGetProperty(property, out var value) &&
               value.ValueKind == JsonValueKind.String &&
               DateTime.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result)
            ? result
            : null;
    }
}

/// <summary>
/// Qdrant HTTP 请求头扩展，集中处理 api-key 认证头写入。
/// </summary>
internal static class HttpRequestHeadersExtensions
{
    /// <summary>
    /// Qdrant 使用 api-key 头进行认证。TryAddWithoutValidation 避免本地或云端 key 格式差异导致 header 校验失败。
    /// </summary>
    public static void ApiKey(this HttpRequestHeaders headers, string apiKey)
    {
        headers.TryAddWithoutValidation("api-key", apiKey);
    }
}
