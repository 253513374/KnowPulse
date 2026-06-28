using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.RagAI;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace KnowPulse.RagAI;

/// <summary>
/// Qdrant REST adapter 的单元测试。
/// 通过录制 HttpClient 请求体验证 URL、payload、filter 和 collection 维护逻辑，不依赖真实 Qdrant 服务。
/// </summary>
public class QdrantKnowledgeVectorStoreTests
{
    /// <summary>
    /// 验证 Qdrant upsert、search 和 delete REST 请求结构及响应映射。
    /// </summary>
    [Fact]
    public async Task Should_Upsert_Search_And_Delete_Through_Qdrant_Rest_Api()
    {
        // 该场景覆盖三类外部请求：upsert point、search with payload filter、按知识版本删除 point。
        var tenantId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJsonResponse("""{"result":{"operation_id":1},"status":"ok"}""");
        handler.EnqueueJsonResponse($$"""
        {
          "result": [
            {
              "id": "{{chunkId:D}}",
              "score": 0.91,
              "payload": {
                "tenantId": "{{tenantId:D}}",
                "knowledgeItemId": "{{itemId:D}}",
                "knowledgeVersionId": "{{versionId:D}}",
                "chunkId": "{{chunkId:D}}",
                "content": "命中的 chunk 内容",
                "visibility": "Department",
                "departmentIds": ["{{departmentId:D}}"],
                "roleIds": ["{{roleId:D}}"],
                "userIds": ["{{userId:D}}"],
                "securityLevel": 3,
                "isPublished": true,
                "sourceTitle": "员工手册",
                "sourcePage": 12,
                "embeddingModel": "knowpulse-embedding-default",
                "embeddingModelVersion": "2026-06-mvp1"
              }
            }
          ],
          "status": "ok"
        }
        """);
        handler.EnqueueJsonResponse("""{"result":{"operation_id":2},"status":"ok"}""");
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://qdrant.test/")
        };
        var store = new QdrantKnowledgeVectorStore(CreateOptions(), httpClient);

        await store.UpsertChunksAsync(new[]
        {
            new KnowledgeChunkVectorRecord
            {
                ChunkId = chunkId,
                KnowledgeItemId = itemId,
                KnowledgeVersionId = versionId,
                TenantId = tenantId,
                Content = "命中的 chunk 内容",
                Embedding = new[] { 0.1f, 0.2f, 0.3f },
                Payload = new KnowledgeVectorPayload
                {
                    TenantId = tenantId,
                    KnowledgeItemId = itemId,
                    KnowledgeVersionId = versionId,
                    ChunkId = chunkId,
                    Visibility = "Department",
                    DepartmentIds = new[] { departmentId },
                    RoleIds = new[] { roleId },
                    UserIds = new[] { userId },
                    SecurityLevel = 3,
                    IsPublished = true,
                    SourceTitle = "员工手册",
                    SourcePage = 12,
                    EmbeddingModel = "knowpulse-embedding-default",
                    EmbeddingModelVersion = "2026-06-mvp1"
                }
            }
        });

        var hits = await store.SearchAsync(new KnowledgeVectorSearchRequest
        {
            TenantId = tenantId,
            QueryEmbedding = new[] { 0.1f, 0.2f, 0.3f },
            TopK = 5,
            PermissionFilter = new KnowledgeVectorPermissionFilter
            {
                TenantId = tenantId,
                UserId = userId,
                DepartmentIds = new[] { departmentId },
                RoleIds = new[] { roleId },
                SecurityLevel = 4,
                PublishedOnly = true
            }
        });
        await store.DeleteByKnowledgeVersionAsync(versionId);

        // Upsert 必须以 chunk id 作为 point id，并把权限相关 payload 转成 Qdrant keyword/bool/integer 字段。
        handler.Requests.Count.ShouldBe(3);
        handler.Requests[0].Method.ShouldBe(HttpMethod.Put);
        handler.Requests[0].PathAndQuery.ShouldBe("/collections/knowledge_chunks_1536_mvp1/points?wait=true");
        var upsertJson = JsonDocument.Parse(handler.Requests[0].Body).RootElement;
        upsertJson.GetProperty("points")[0].GetProperty("id").GetString().ShouldBe(chunkId.ToString("D"));
        upsertJson.GetProperty("points")[0].GetProperty("vector").GetArrayLength().ShouldBe(3);
        upsertJson.GetProperty("points")[0].GetProperty("payload").GetProperty("departmentIds")[0].GetString().ShouldBe(departmentId.ToString("D"));

        handler.Requests[1].Method.ShouldBe(HttpMethod.Post);
        handler.Requests[1].PathAndQuery.ShouldBe("/collections/knowledge_chunks_1536_mvp1/points/search");
        var searchJson = JsonDocument.Parse(handler.Requests[1].Body).RootElement;
        searchJson.GetProperty("limit").GetInt32().ShouldBe(5);
        var filter = searchJson.GetProperty("filter");
        // 这里验证 Qdrant 只做前置粗过滤：must 是硬条件，should 是任一可见范围。
        filter.GetProperty("must").EnumerateArray().ShouldContain(x => ContainsMatch(x, "tenantId", tenantId.ToString("D")));
        filter.GetProperty("must").EnumerateArray().ShouldContain(x => ContainsMatch(x, "isPublished", true));
        filter.GetProperty("must").EnumerateArray().ShouldContain(x => ContainsRangeLte(x, "securityLevel", 4));
        filter.GetProperty("should").EnumerateArray().ShouldContain(x => ContainsMatch(x, "visibility", "All"));
        filter.GetProperty("should").EnumerateArray().ShouldContain(x => ContainsMatch(x, "departmentIds", departmentId.ToString("D")));
        filter.GetProperty("should").EnumerateArray().ShouldContain(x => ContainsMatch(x, "roleIds", roleId.ToString("D")));
        filter.GetProperty("should").EnumerateArray().ShouldContain(x => ContainsMatch(x, "userIds", userId.ToString("D")));

        hits.Count.ShouldBe(1);
        hits[0].ChunkId.ShouldBe(chunkId);
        hits[0].KnowledgeItemId.ShouldBe(itemId);
        hits[0].KnowledgeVersionId.ShouldBe(versionId);
        hits[0].Score.ShouldBe(0.91);
        hits[0].Content.ShouldBe("命中的 chunk 内容");
        hits[0].Payload.Visibility.ShouldBe("Department");
        hits[0].Payload.DepartmentIds.ShouldContain(departmentId);
        hits[0].Payload.SourcePage.ShouldBe(12);

        handler.Requests[2].Method.ShouldBe(HttpMethod.Post);
        handler.Requests[2].PathAndQuery.ShouldBe("/collections/knowledge_chunks_1536_mvp1/points/delete?wait=true");
        var deleteJson = JsonDocument.Parse(handler.Requests[2].Body).RootElement;
        deleteJson.GetProperty("filter").GetProperty("must").EnumerateArray()
            .ShouldContain(x => ContainsMatch(x, "knowledgeVersionId", versionId.ToString("D")));
    }

    /// <summary>
    /// 验证 collection 缺失时会创建 collection，并补齐必需 payload index。
    /// </summary>
    [Fact]
    public async Task Should_Ensure_Collection_And_Required_Payload_Indexes()
    {
        // collection 不存在时先创建，再补齐缺失 payload index，最后返回健康结果。
        var handler = new RecordingHttpMessageHandler();
        handler.EnqueueJsonResponse("{}", HttpStatusCode.NotFound);
        handler.EnqueueJsonResponse("""{"result":{"operation_id":1},"status":"ok"}""");
        handler.EnqueueJsonResponse($$"""
        {
          "result": {
            "config": {
              "params": {
                "vectors": {
                  "size": 1536,
                  "distance": "Cosine"
                }
              }
            },
            "payload_schema": {
              "tenantId": {},
              "knowledgeItemId": {},
              "knowledgeVersionId": {}
            }
          },
          "status": "ok"
        }
        """);
        foreach (var _ in Enumerable.Range(0, 7))
        {
            handler.EnqueueJsonResponse("""{"result":{"operation_id":2},"status":"ok"}""");
        }

        handler.EnqueueJsonResponse($$"""
        {
          "result": {
            "config": {
              "params": {
                "vectors": {
                  "size": 1536,
                  "distance": "Cosine"
                }
              }
            },
            "payload_schema": {
              "tenantId": {},
              "knowledgeItemId": {},
              "knowledgeVersionId": {},
              "chunkId": {},
              "isPublished": {},
              "departmentIds": {},
              "positionIds": {},
              "roleIds": {},
              "userIds": {},
              "securityLevel": {}
            }
          },
          "status": "ok"
        }
        """);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://qdrant.test/")
        };
        var store = new QdrantKnowledgeVectorStore(CreateOptions(), httpClient);

        var health = await store.EnsureKnowledgeCollectionAsync();

        health.IsHealthy.ShouldBeTrue();
        health.CollectionName.ShouldBe("knowledge_chunks_1536_mvp1");
        health.ExpectedVectorDimension.ShouldBe(1536);
        health.ActualVectorDimension.ShouldBe(1536);
        health.MissingPayloadIndexes.Count.ShouldBe(0);

        handler.Requests[0].Method.ShouldBe(HttpMethod.Get);
        handler.Requests[0].PathAndQuery.ShouldBe("/collections/knowledge_chunks_1536_mvp1");
        handler.Requests[1].Method.ShouldBe(HttpMethod.Put);
        handler.Requests[1].PathAndQuery.ShouldBe("/collections/knowledge_chunks_1536_mvp1");
        var createJson = JsonDocument.Parse(handler.Requests[1].Body).RootElement;
        createJson.GetProperty("vectors").GetProperty("size").GetInt32().ShouldBe(1536);
        createJson.GetProperty("vectors").GetProperty("distance").GetString().ShouldBe("Cosine");

        var indexRequests = handler.Requests
            .Where(x => x.PathAndQuery == "/collections/knowledge_chunks_1536_mvp1/index?wait=true")
            .ToList();
        indexRequests.Count.ShouldBe(7);
        var indexedFields = indexRequests
            .Select(x => JsonDocument.Parse(x.Body).RootElement.GetProperty("field_name").GetString())
            .ToHashSet();
        indexedFields.ShouldContain("chunkId");
        indexedFields.ShouldContain("isPublished");
        indexedFields.ShouldContain("departmentIds");
        indexedFields.ShouldContain("positionIds");
        indexedFields.ShouldContain("roleIds");
        indexedFields.ShouldContain("userIds");
        indexedFields.ShouldContain("securityLevel");
    }

    /// <summary>
    /// 创建测试用 RagAI options。
    /// </summary>
    private static IOptions<RagAiOptions> CreateOptions()
    {
        return Options.Create(new RagAiOptions
        {
            VectorStore =
            {
                Endpoint = "http://qdrant.test",
                KnowledgeCollectionName = "knowledge_chunks_1536_mvp1",
                VectorDimension = 1536,
                Distance = "Cosine"
            }
        });
    }

    /// <summary>
    /// 判断 Qdrant filter 条件是否包含指定 match.value。
    /// </summary>
    private static bool ContainsMatch(JsonElement element, string key, object value)
    {
        var matchValue = element.GetProperty("match").GetProperty("value");
        return element.GetProperty("key").GetString() == key &&
               (value switch
               {
                   bool boolValue => matchValue.GetBoolean() == boolValue,
                   string stringValue => matchValue.GetString() == stringValue,
                   _ => false
               });
    }

    /// <summary>
    /// 判断 Qdrant filter 条件是否包含指定 range.lte。
    /// </summary>
    private static bool ContainsRangeLte(JsonElement element, string key, int value)
    {
        return element.GetProperty("key").GetString() == key &&
               element.GetProperty("range").GetProperty("lte").GetInt32() == value;
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        // 轻量 fake handler：按队列返回预设响应，并保存每个请求的 method/path/body 供断言。
        private readonly Queue<HttpResponseMessage> _responses = new();

        public List<RecordedRequest> Requests { get; } = [];

        /// <summary>
        /// 入队一条 fake JSON 响应。
        /// </summary>
        public void EnqueueJsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responses.Enqueue(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }

        /// <summary>
        /// 记录 HTTP 请求并返回队列中的下一条 fake 响应。
        /// </summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri?.PathAndQuery ?? string.Empty,
                request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));

            return _responses.Dequeue();
        }
    }

    /// <summary>
    /// 测试中记录的 HTTP 请求摘要。
    /// </summary>
    private sealed record RecordedRequest(HttpMethod Method, string PathAndQuery, string Body);
}
