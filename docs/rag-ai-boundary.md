# KnowPulse RAG/AI 边界与禁用清单

版本：v0.1  
日期：2026-06-24  
对应任务：`KP-MVP0-AI-001`、`KP-MVP0-AI-003`

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
- 禁止把 DataIngestion preview provider 放入核心业务链路。
- 禁止把 VectorData preview provider 作为唯一抽象。
- 禁止绕过 KnowledgeBase 权限模型直接检索文件或 chunk。
- 禁止在审计日志保存完整文档原文或完整模型 prompt。

## 4. MVP-1 风险闸口

MVP-1 开始前必须评审：

| 风险 | 评审问题 | 降级方案 |
| --- | --- | --- |
| DataIngestion preview | 是否满足文档解析、错误恢复和可观测性要求 | 自研 ingestion worker |
| VectorData/Qdrant provider preview | payload filter、批量写入和删除能力是否稳定 | 直接使用 Qdrant SDK 并包一层自研接口 |
| 权限过滤不足 | 部门、岗位、角色、用户、安全等级是否能完整表达 | Qdrant 粗过滤 + .NET 二次校验 |
| 权限越权测试不足 | 是否覆盖跨部门、跨岗位、跨角色、指定用户和安全等级边界 | 先补权限矩阵测试，未通过不开放问答 |
| Embedding 模型选择不稳定 | 模型维度、语言效果、成本、私有化可用性是否已确认 | 固定模型和向量维度后再建集合 |
| 模型网关漂移 | LiteLLM 的模型名、key 和超时是否可配置 | 统一走内部 `IModelGateway` |
| 引用不可追溯 | 回答是否能追溯到知识版本和 chunk | 强制返回 citation metadata |

MVP-1 不通过上述闸口，不进入真实用户问答链路。
