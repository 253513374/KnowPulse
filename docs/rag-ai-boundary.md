# KnowPulse RAG/AI 边界与禁用清单

版本：v0.3
日期：2026-06-27
对应任务：`KP-MVP0-AI-001`、`KP-MVP0-AI-003`、`KP-MVP1-AI-001`、`KP-MVP1-AI-004`、`KP-MVP1-AI-005`

## 1. MVP-0 边界

MVP-0 不实现真实 RAG、文档解析、向量写入、向量检索、AI 问答和 AI 出题。MVP-0 只保留接口边界、环境健康检查和风险清单，防止后续 preview provider 直接渗透业务层。

## 2. 后续接口候选

MVP-1 可以在独立 RAG/AI 模块中定义以下接口：

```csharp
public interface IKnowledgeIngestionPipeline
{
    Task EnqueueAsync(Guid knowledgeVersionId, CancellationToken cancellationToken = default);
}

public interface IKnowledgeVectorStore
{
    Task UpsertChunksAsync(Guid knowledgeVersionId, IReadOnlyCollection<KnowledgeChunkVector> chunks, CancellationToken cancellationToken = default);
}

public interface IKnowledgeRetriever
{
    Task<KnowledgeRetrievalResult> SearchAsync(KnowledgeRetrievalRequest request, CancellationToken cancellationToken = default);
}
```

接口要求：

- 请求必须携带 `TenantId`、`UserId`、部门、岗位、角色和安全等级上下文。
- Qdrant payload filter 只能作为第一层过滤，业务权限必须由 .NET 服务二次校验。
- 返回结果必须包含来源知识 ID、版本 ID、chunk ID 和引用信息。
- 审计必须能串联用户问题、检索结果、引用来源和最终回答。

## 3. MVP-0 禁用清单

- 禁止业务 Application Service 直接引用模型 SDK。
- 禁止业务 Application Service 直接引用 Qdrant SDK。
- 禁止把 DataIngestion preview provider 放入核心业务链路；允许在 Application 实现层通过项目自有接口适配 MarkItDown reader。
- 禁止把 VectorData preview provider 作为唯一抽象。
- 禁止绕过 KnowledgeBase 权限模型直接检索文件或 chunk。
- 禁止在审计日志保存完整文档原文或完整模型 prompt。

## 4. MVP-1 风险闸口

MVP-1 开始前必须评审：

| 风险 | 评审问题 | 降级方案 |
| --- | --- | --- |
| DataIngestion preview | 是否满足文档解析、错误恢复和可观测性要求 | 自研 ingestion worker 和 `IKnowledgeDocumentMarkdownConverter` 隔离层 |
| VectorData/Qdrant provider preview | payload filter、批量写入和删除能力是否稳定 | 直接使用 Qdrant SDK 并包一层自研接口 |
| 权限过滤不足 | 部门、岗位、角色、用户、安全等级是否能完整表达 | Qdrant 粗过滤 + .NET 二次校验 |
| 权限越权测试不足 | 是否覆盖跨部门、跨岗位、跨角色、指定用户和安全等级边界 | 先补权限矩阵测试，未通过不开放问答 |
| Embedding 模型选择不稳定 | 模型维度、语言效果、成本、私有化可用性是否已确认 | 固定模型和向量维度后再建集合 |
| 模型网关漂移 | LiteLLM 的模型名、key 和超时是否可配置 | 统一走内部 `IModelGateway` |
| 引用不可追溯 | 回答是否能追溯到知识版本和 chunk | 强制返回 citation metadata |

MVP-1 不通过上述闸口，不进入真实用户问答链路。

## 5. MVP-1 Embedding 与 Collection 决策

MVP-1 初始 embedding 只通过 LiteLLM 模型 alias 调用，不在业务代码或配置模板中绑定真实供应商密钥。当前固定策略：

| 项 | 值 | 说明 |
| --- | --- | --- |
| Provider | `LiteLLM` | 统一走模型网关，便于审计、限流和 fallback |
| Embedding model alias | `knowpulse-embedding-default` | LiteLLM 内部映射到实际供应商模型 |
| Model version | `2026-06-mvp1` | 写入 `EmbeddingModelVersion.ModelVersion` 和向量 payload |
| Vector dimension | `1536` | 维度固定后才允许创建 Qdrant collection |
| Knowledge collection | `knowledge_chunks_qwen_v4_1536` | 千问 v4 使用独立集合，同维度的不同模型也不能混用旧向量 |
| Fallback alias | `knowpulse-embedding-fallback` | fallback 也必须保持相同向量维度 |
| API key | `KNOWPULSE_LITELLM_API_KEY` 环境变量 | 仓库只保存环境变量名，不保存真实 key |

MVP-1 问答生成使用 LiteLLM chat alias：

| 项 | 值 | 说明 |
| --- | --- | --- |
| Chat model alias | `knowpulse-chat-default` | 应用只依赖稳定 alias |
| 默认供应商映射 | `openai/gpt-6-luna` | 在 `deploy/litellm/config.example.yaml` 中配置；`reasoning_effort: none` 保持现有 temperature 参数兼容 |
| Provider key | `OPENAI_API_KEY` 环境变量 | 真实值只放在本地 `.env` 或安全配置源，`.env.example` 只保留空变量 |

RAG 回答服务只能把 PostgreSQL 二次校验后的 chunk 传给 chat alias。无可访问上下文时必须拒答；模型不可用时降级为保守回答并记录 chat 阶段失败。

配置模板位于：

- `src/KnowPulse.HttpApi.Host/appsettings.json`
- `src/KnowPulse.DbMigrator/appsettings.json`

强制规则：

- `EmbeddingModelVersion` 必须保存 provider、model name、model version、vector dimension、collection name 和 default/active 状态。
- `RagAI:Embedding:VectorDimension` 与 `RagAI:Embedding:CollectionName` 必须和默认 `EmbeddingModelVersion` 一致。
- 维度变化必须创建新 collection，例如 `knowledge_chunks_3072_mvp2`，禁止把不同维度向量写入同一个 collection。
- fallback embedding alias 必须与主 alias 维度一致；维度不一致时只能进入新 collection 或执行显式迁移。
- 本地开发只能提交 endpoint、alias、环境变量名、timeout 和 fallback 配置，不提交真实 LiteLLM key。

## 6. MVP-1 Qdrant 适配边界

`IKnowledgeVectorStore` 是业务层唯一可见的向量库接口。MVP-1 Qdrant 适配使用 REST API 封装 upsert、search 和按知识版本删除，不把 Qdrant SDK、SemanticKernel Qdrant connector、VectorData provider 或 DataIngestion provider 类型暴露给 Application.Contracts、Application 或 Domain。

`IKnowledgeVectorStoreMaintenance` 只负责 collection 和 payload index 的显式初始化/健康检查，不保存业务最终状态。当前应用显式配置 collection 为 `knowledge_chunks_qwen_v4_1536`，向量维度为 `1536`，距离函数为 `Cosine`；旧 MVP-1 集合保留，切换后应重新处理资料，不能将旧向量直接复制到新集合。

MVP-1 必建 payload index：

- `tenantId`
- `knowledgeItemId`
- `knowledgeVersionId`
- `chunkId`
- `isPublished`
- `departmentIds`
- `positionIds`
- `roleIds`
- `userIds`
- `securityLevel`

本地初始化和健康验证脚本：

```powershell
.\ops\verify-qdrant-knowledge-collection.ps1 -Ensure
```

Qdrant 只保存可重建检索索引。重建来源是 PostgreSQL 中的知识版本、分块、embedding 记录和权限范围，以及 MinIO/BlobStoring 中的原始附件。

静态边界检查：

```powershell
.\ops\verify-rag-provider-boundaries.ps1
```

该脚本扫描 `src` 下 C# 和项目文件，发现直接引用上述 provider/SDK 命名空间或包名时失败。

## 7. MVP-1 RAG 审计字段规则

RAG 审计表只保存可追溯、可排障、受控长度的 metadata，不把审计库变成知识正文或 prompt 副本。

允许保存：

- `AppRagCallLogs.QueryHash`：用户问题规范化后的 SHA-256 hash。
- `AppRagCallLogs.QuerySummary`：受控长度问题摘要，最大 256 字符。
- `AppRagCallLogs.ModelProvider`、`ModelName`、`ModelVersion`、`CollectionName`、token、成本、耗时、状态和失败阶段。
- `AppRagCitationRecords.KnowledgeItemId`、`KnowledgeVersionId`、`ChunkId`、rank、score、来源标题和页码。
- `AppRagCitationRecords.QuoteHash`：citation chunk 内容 SHA-256 hash。
- `AppRagCitationRecords.QuotePreview`：受控长度引用摘录，最大 180 字符。

禁止保存：

- 完整 system prompt、完整 user prompt 或完整拼接后的 RAG prompt。
- 完整知识正文、完整 chunk、完整附件内容或文件流。
- 模型供应商真实密钥、LiteLLM master key、上游模型 key。
- 未通过 PostgreSQL 二次校验的 Qdrant payload 正文。

敏感字段扫描命令：

```powershell
.\ops\audit-check.ps1 -FailOnSensitiveHits
```

该命令检查 ABP 审计参数、实体属性变更、RAG 调用日志和 citation 记录；如果发现 `ContentText`、文件流类型名、完整 prompt 标记、附件内容标记，或摘要/preview 超过受控长度，会返回失败。
