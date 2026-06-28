# KnowPulse MVP-1 开发任务拆分与分发

版本：v0.1
日期：2026-06-26
角色：架构任务拆分与开发分发官
输入文档：

- `docs/system-architecture.md`
- `docs/technical-architecture.md`
- `docs/rag-ai-boundary.md`
- `docs/mvp-0-acceptance-matrix.md`

## 1. 拆分范围和假设

### 1.1 拆分范围

本次只拆分 MVP-1：权限化 RAG 问答闭环。

MVP-1 目标：

- 建立 KnowledgeIndexing 数据模型、迁移、索引任务和状态跟踪。
- 基于已发布知识版本完成文档读取、Markdown 化、chunk、embedding 和向量写入。
- 通过项目内 `IKnowledgeVectorStore` 适配 Qdrant，不让业务代码直接依赖 provider 或 SDK。
- 完成 Qdrant payload filter 前置过滤和 PostgreSQL 业务权限二次校验。
- 提供 RAG 检索和问答 API，回答必须包含引用来源。
- 建立 RAG 调用日志、基础评测集和权限越权测试。

### 1.2 本次不包含

- 不实现 AI 自动出题。
- 不实现题库、考试、评分和错题记录。
- 不引入 RabbitMQ。
- 不拆分独立微服务。
- 不让 Agent workflow 成为 MVP-1 问答的必需链路。
- 不允许 AI 生成内容直接修改知识、权限、题目或考试业务状态。

### 1.3 执行假设

- MVP-0 的 Organization、KnowledgeBase、权限范围、附件、审计和本地依赖环境已经可用。
- 默认主前端继续使用 Blazor Web App。
- PostgreSQL 是业务事实库，Qdrant 只是可重建的检索索引库。
- LiteLLM 是模型网关，应用内模型调用通过项目内接口或 `Microsoft.Extensions.AI` 封装。
- `Microsoft.Extensions.DataIngestion`、VectorData/Qdrant provider 仍按 preview 风险管理，必须被本项目接口隔离。
- 单个任务控制在 0.5 到 3 人日。

## 2. 任务总览

| ID | 标题 | 模块 | 负责人角色 | 优先级 | 预估 | 依赖 |
| --- | --- | --- | --- | --- | --- | --- |
| KP-MVP1-BE-001 | 实现 KnowledgeIndexing 数据模型与迁移 | KnowledgeIndexing | Backend | P0 | 2d | MVP-0 完成 |
| KP-MVP1-BE-002 | 实现索引任务应用服务与手动重建入口 | KnowledgeIndexing | Backend | P0 | 2d | KP-MVP1-BE-001 |
| KP-MVP1-BE-003 | 发布、下架、归档和权限变更触发索引状态更新 | KnowledgeBase | Backend | P0 | 1.5d | KP-MVP1-BE-001, KP-MVP0-BE-010 |
| KP-MVP1-AI-001 | 固定 embedding 模型、维度和 collection 策略 | RAG/AI | RAG/AI | P0 | 1d | MVP-0 RAG 风险闸口 |
| KP-MVP1-AI-002 | 实现文档读取、Markdown 化和 chunk pipeline | KnowledgeIndexing | RAG/AI | P0 | 2.5d | KP-MVP1-BE-001, KP-MVP1-AI-001 |
| KP-MVP1-AI-003 | 实现 embedding 生成和模型版本记录 | KnowledgeIndexing | RAG/AI | P0 | 2d | KP-MVP1-AI-001, KP-MVP1-AI-002 |
| KP-MVP1-AI-004 | 实现 `IKnowledgeVectorStore` Qdrant 适配层 | RAG Service | RAG/AI | P0 | 2.5d | KP-MVP1-AI-001 |
| KP-MVP1-AI-005 | 创建 Qdrant `knowledge_chunks` collection 和 payload index | RAG Service | RAG/AI | P0 | 1.5d | KP-MVP1-AI-004 |
| KP-MVP1-BE-004 | 实现权限化 `IKnowledgeRetriever` | RAG Service | Backend | P0 | 2.5d | KP-MVP1-AI-004, KP-MVP1-BE-001 |
| KP-MVP1-BE-005 | 实现 RAG 搜索和问答应用服务/API | RAG Service | Backend | P0 | 2.5d | KP-MVP1-BE-004, KP-MVP1-AI-003 |
| KP-MVP1-BE-006 | 实现 RAG 调用日志、引用记录和敏感字段保护 | Audit | Backend | P0 | 2d | KP-MVP1-BE-005 |
| KP-MVP1-FE-001 | 在知识版本页面展示索引状态和重建操作 | Frontend | Frontend | P1 | 1.5d | KP-MVP1-BE-002 |
| KP-MVP1-FE-002 | 实现基础 RAG 问答页面 | Frontend | Frontend | P1 | 2d | KP-MVP1-BE-005 |
| KP-MVP1-QA-001 | 编写索引任务和失败定位测试 | QA | QA | P0 | 1.5d | KP-MVP1-BE-002, KP-MVP1-AI-002 |
| KP-MVP1-QA-002 | 编写权限化检索越权测试矩阵 | QA | QA | P0 | 2d | KP-MVP1-BE-004 |
| KP-MVP1-QA-003 | 编写 RAG 问答引用和无依据拒答测试 | QA | QA | P0 | 1.5d | KP-MVP1-BE-005 |
| KP-MVP1-DEVOPS-001 | 补充 Qdrant collection 初始化和健康验证脚本 | DevOps | DevOps | P1 | 1d | KP-MVP1-AI-005 |
| KP-MVP1-DEVOPS-002 | 补充 LiteLLM 模型配置模板和本地校验 | DevOps | DevOps | P1 | 1d | KP-MVP1-AI-001 |

## 3. 按角色分发

### 3.1 Backend

| 任务 ID | 任务标题 | 优先级 | 预估 | 依赖 |
| --- | --- | --- | --- | --- |
| KP-MVP1-BE-001 | 实现 KnowledgeIndexing 数据模型与迁移 | P0 | 2d | MVP-0 完成 |
| KP-MVP1-BE-002 | 实现索引任务应用服务与手动重建入口 | P0 | 2d | KP-MVP1-BE-001 |
| KP-MVP1-BE-003 | 发布、下架、归档和权限变更触发索引状态更新 | P0 | 1.5d | KP-MVP1-BE-001, KP-MVP0-BE-010 |
| KP-MVP1-BE-004 | 实现权限化 `IKnowledgeRetriever` | P0 | 2.5d | KP-MVP1-AI-004, KP-MVP1-BE-001 |
| KP-MVP1-BE-005 | 实现 RAG 搜索和问答应用服务/API | P0 | 2.5d | KP-MVP1-BE-004, KP-MVP1-AI-003 |
| KP-MVP1-BE-006 | 实现 RAG 调用日志、引用记录和敏感字段保护 | P0 | 2d | KP-MVP1-BE-005 |

### 3.2 RAG/AI

| 任务 ID | 任务标题 | 优先级 | 预估 | 依赖 |
| --- | --- | --- | --- | --- |
| KP-MVP1-AI-001 | 固定 embedding 模型、维度和 collection 策略 | P0 | 1d | MVP-0 RAG 风险闸口 |
| KP-MVP1-AI-002 | 实现文档读取、Markdown 化和 chunk pipeline | P0 | 2.5d | KP-MVP1-BE-001, KP-MVP1-AI-001 |
| KP-MVP1-AI-003 | 实现 embedding 生成和模型版本记录 | P0 | 2d | KP-MVP1-AI-001, KP-MVP1-AI-002 |
| KP-MVP1-AI-004 | 实现 `IKnowledgeVectorStore` Qdrant 适配层 | P0 | 2.5d | KP-MVP1-AI-001 |
| KP-MVP1-AI-005 | 创建 Qdrant `knowledge_chunks` collection 和 payload index | P0 | 1.5d | KP-MVP1-AI-004 |

### 3.3 Frontend

| 任务 ID | 任务标题 | 优先级 | 预估 | 依赖 |
| --- | --- | --- | --- | --- |
| KP-MVP1-FE-001 | 在知识版本页面展示索引状态和重建操作 | P1 | 1.5d | KP-MVP1-BE-002 |
| KP-MVP1-FE-002 | 实现基础 RAG 问答页面 | P1 | 2d | KP-MVP1-BE-005 |

### 3.4 QA

| 任务 ID | 任务标题 | 优先级 | 预估 | 依赖 |
| --- | --- | --- | --- | --- |
| KP-MVP1-QA-001 | 编写索引任务和失败定位测试 | P0 | 1.5d | KP-MVP1-BE-002, KP-MVP1-AI-002 |
| KP-MVP1-QA-002 | 编写权限化检索越权测试矩阵 | P0 | 2d | KP-MVP1-BE-004 |
| KP-MVP1-QA-003 | 编写 RAG 问答引用和无依据拒答测试 | P0 | 1.5d | KP-MVP1-BE-005 |

### 3.5 DevOps

| 任务 ID | 任务标题 | 优先级 | 预估 | 依赖 |
| --- | --- | --- | --- | --- |
| KP-MVP1-DEVOPS-001 | 补充 Qdrant collection 初始化和健康验证脚本 | P1 | 1d | KP-MVP1-AI-005 |
| KP-MVP1-DEVOPS-002 | 补充 LiteLLM 模型配置模板和本地校验 | P1 | 1d | KP-MVP1-AI-001 |

## 4. 详细任务

### KP-MVP1-BE-001 实现 KnowledgeIndexing 数据模型与迁移

- 阶段：MVP-1
- 模块：KnowledgeIndexing
- 类型：Backend
- 负责人角色：Backend
- 优先级：P0
- 预估：2 人日
- 依赖：MVP-0 完成
- 来源文档：`docs/technical-architecture.md` 第 6.3、7、10、15.2 节

任务说明：

实现索引任务、知识分块、embedding 记录和 embedding 模型版本等持久化模型。所有模型只保存业务事实、索引状态、失败定位和可追溯 metadata，不直接依赖 DataIngestion、VectorData、Qdrant 或模型 SDK 类型。

验收标准：

- 数据库包含 `AppKnowledgeIndexJobs`、`AppKnowledgeChunks`、`AppKnowledgeEmbeddingRecords`、`AppEmbeddingModelVersions`。
- 索引任务可记录 `Pending`、`Processing`、`Indexed`、`Failed`、`Outdated`、`Deleted` 状态。
- 索引失败必须能记录失败阶段、失败原因、重试次数和下一次重试时间。
- chunk 必须能追溯到知识条目、知识版本、索引任务、来源页码和内容 hash。
- embedding 记录必须能追溯到 chunk、模型版本、向量维度和外部向量记录 ID。
- 不新增对 Qdrant SDK、DataIngestion preview provider 或模型 SDK 的直接业务依赖。

完成定义：

- EF Core 迁移已生成或手写完成。
- `KnowPulseDbContext` 暴露对应 `DbSet` 并配置索引。
- Domain/Domain.Shared 编译通过。
- 领域测试或 EF Core 模型测试覆盖关键状态流转。

### KP-MVP1-BE-002 实现索引任务应用服务与手动重建入口

- 阶段：MVP-1
- 模块：KnowledgeIndexing
- 类型：Backend
- 负责人角色：Backend
- 优先级：P0
- 预估：2 人日
- 依赖：KP-MVP1-BE-001
- 来源文档：`docs/technical-architecture.md` 第 10.2、11、17.2 节

任务说明：

提供创建索引任务、查询索引任务、标记失败、重试和手动重建知识版本索引的应用服务与 REST API。

验收标准：

- 管理员可以对知识版本触发重建索引。
- 同一知识版本可以查询最近一次索引任务和历史任务。
- 失败任务能展示失败阶段、失败原因和重试次数。
- 手动重建必须产生新的任务记录，不覆盖旧任务事实。

完成定义：

- `POST /api/knowledge/versions/{id}/reindex` 可用。
- `GET /api/knowledge/index-jobs/{id}` 可用。
- 应用服务有显式权限检查。
- 审计日志可追踪手动重建操作。

### KP-MVP1-BE-003 发布、下架、归档和权限变更触发索引状态更新

- 阶段：MVP-1
- 模块：KnowledgeBase
- 类型：Backend
- 负责人角色：Backend
- 优先级：P0
- 预估：1.5 人日
- 依赖：KP-MVP1-BE-001, KP-MVP0-BE-010
- 来源文档：`docs/technical-architecture.md` 第 10.1、10.2 节

任务说明：

把知识版本生命周期和权限范围变更接入索引状态。发布创建索引任务；下架、归档、停用或权限收窄时使旧索引不可检索或标记过期。

验收标准：

- 发布版本后创建待处理索引任务。
- 下架或归档版本后旧 chunk 不再被 RAG 检索视为可用。
- 权限范围变更会标记相关索引 payload 过期或创建重建任务。
- 索引状态变化不影响 PostgreSQL 业务版本事实。

完成定义：

- 生命周期应用服务测试覆盖发布、下架、归档与索引状态联动。
- 权限范围新增、删除测试覆盖索引过期标记。

### KP-MVP1-AI-001 固定 embedding 模型、维度和 collection 策略

- 阶段：MVP-1
- 模块：RAG/AI
- 类型：RAG/AI
- 负责人角色：RAG/AI
- 优先级：P0
- 预估：1 人日
- 依赖：MVP-0 RAG 风险闸口
- 来源文档：`docs/rag-ai-boundary.md` 第 4 节

任务说明：

在进入真实索引前确认 embedding 模型、向量维度、模型网关名称、Qdrant collection 命名、灰度切换和重建策略。

验收标准：

- `EmbeddingModelVersion` 中可表达模型名称、版本、维度、collection 和是否当前默认。
- 维度变化必须使用新 collection 或明确迁移策略。
- LiteLLM 模型名、key、超时和 fallback 可配置。
- 本地开发配置不提交真实密钥。

完成定义：

- 文档和配置模板同步更新。
- 静态检查确认没有密钥落库或入仓。

### KP-MVP1-AI-002 实现文档读取、Markdown 化和 chunk pipeline

- 阶段：MVP-1
- 模块：KnowledgeIndexing
- 类型：RAG/AI
- 负责人角色：RAG/AI
- 优先级：P0
- 预估：2.5 人日
- 依赖：KP-MVP1-BE-001, KP-MVP1-AI-001
- 来源文档：`docs/technical-architecture.md` 第 7.1、7.2 节

任务说明：

实现从知识版本正文和附件读取内容、转换为 Markdown-centric 表示、分块并保存 `KnowledgeChunk` 的 pipeline。

验收标准：

- 支持 MVP-1 选定的初始文档类型。
- 每个 chunk 记录来源标题、页码、顺序号、内容 hash 和 token 估算。
- reader、chunker、processor 阶段失败能落到 `KnowledgeIndexJob`。
- 重新索引同一版本时可以替换旧 chunk 或标记旧 chunk 失效。

完成定义：

- pipeline 单元测试覆盖正文、附件、空内容、超长内容和失败输入。
- 不把完整大文件原文写入审计日志。

### KP-MVP1-AI-003 实现 embedding 生成和模型版本记录

- 阶段：MVP-1
- 模块：KnowledgeIndexing
- 类型：RAG/AI
- 负责人角色：RAG/AI
- 优先级：P0
- 预估：2 人日
- 依赖：KP-MVP1-AI-001, KP-MVP1-AI-002
- 来源文档：`docs/technical-architecture.md` 第 10.3 节

任务说明：

通过统一模型网关或 `Microsoft.Extensions.AI` 封装生成 embedding，并把模型版本、维度、content hash、外部向量 ID 写入 `KnowledgeEmbeddingRecord`。

验收标准：

- embedding 调用不直接散落在业务 Application Service。
- 每条 embedding 记录能追踪模型版本和 chunk。
- 模型调用异常能记录失败阶段和可重试状态。
- 支持按模型版本查询需要重建的 chunk。

完成定义：

- embedding client 有接口封装和测试替身。
- 本地无模型 key 时能跳过或返回明确错误，不阻塞非 RAG 测试。

### KP-MVP1-AI-004 实现 `IKnowledgeVectorStore` Qdrant 适配层

- 阶段：MVP-1
- 模块：RAG Service
- 类型：RAG/AI
- 负责人角色：RAG/AI
- 优先级：P0
- 预估：2.5 人日
- 依赖：KP-MVP1-AI-001
- 来源文档：`docs/rag-ai-boundary.md` 第 2、3、4 节

任务说明：

实现项目内 `IKnowledgeVectorStore` 的 Qdrant 适配层，封装 upsert、search、delete 和 payload filter 构建。

验收标准：

- 业务代码只依赖 `IKnowledgeVectorStore`。
- provider 或 SDK 类型不泄漏到 Application.Contracts、Application、Domain。
- 支持按知识版本删除向量记录。
- 支持按租户、发布状态、部门、岗位、角色、用户和安全等级构造前置过滤。

完成定义：

- 有集成测试或本地 Qdrant 验证脚本覆盖写入、检索、删除。
- 禁用依赖扫描脚本覆盖直接 SDK 泄漏。

### KP-MVP1-AI-005 创建 Qdrant `knowledge_chunks` collection 和 payload index

- 阶段：MVP-1
- 模块：RAG Service
- 类型：RAG/AI
- 负责人角色：RAG/AI
- 优先级：P0
- 预估：1.5 人日
- 依赖：KP-MVP1-AI-004
- 来源文档：`docs/technical-architecture.md` 第 7.3、7.4 节

任务说明：

创建 Qdrant collection 和 payload index，并确保高频权限过滤字段具备索引。

验收标准：

- collection 名称和向量维度来自 `EmbeddingModelVersion` 或配置。
- payload 至少包含 `tenantId`、`knowledgeItemId`、`knowledgeVersionId`、`chunkId`、`isPublished`、`departmentIds`、`positionIds`、`roleIds`、`userIds`、`securityLevel`。
- 初始化脚本可重复执行。
- collection 不保存业务最终状态，只保存可重建索引。

完成定义：

- 健康检查脚本能确认 collection 和关键 payload index 存在。
- 文档说明 Qdrant 可由 PostgreSQL 和 MinIO 重建。

### KP-MVP1-BE-004 实现权限化 `IKnowledgeRetriever`

- 阶段：MVP-1
- 模块：RAG Service
- 类型：Backend
- 负责人角色：Backend
- 优先级：P0
- 预估：2.5 人日
- 依赖：KP-MVP1-AI-004, KP-MVP1-BE-001
- 来源文档：`docs/technical-architecture.md` 第 7.4、9.2 节

任务说明：

把自然语言问题转换成向量检索请求，先用 Qdrant payload filter 做粗过滤，再回 PostgreSQL 读取知识版本、知识文档、权限范围和组织关系做二次校验。

验收标准：

- 同一问题在不同用户权限下只返回各自可见 chunk。
- 跨租户、跨部门、跨岗位、跨角色、指定用户和安全等级越权检索必须失败。
- Qdrant 索引落后于 PostgreSQL 权限变更时，二次校验必须阻断越权引用。
- 返回结果必须包含知识 ID、版本 ID、chunk ID、标题、页码和得分。

完成定义：

- 权限矩阵测试全部通过。
- 检索结果不会返回已下架、已归档、停用或未发布版本。

### KP-MVP1-BE-005 实现 RAG 搜索和问答应用服务/API

- 阶段：MVP-1
- 模块：RAG Service
- 类型：Backend
- 负责人角色：Backend
- 优先级：P0
- 预估：2.5 人日
- 依赖：KP-MVP1-BE-004, KP-MVP1-AI-003
- 来源文档：`docs/technical-architecture.md` 第 7.5、17.2 节

任务说明：

实现 RAG 搜索 API 和问答 API。问答先走 `RagAnswerService + LiteLLM chat alias` 路径，不把 Agent workflow 作为 MVP-1 必需链路。

验收标准：

- `POST /api/ai/rag/search` 返回通过二次校验的 chunk。
- `POST /api/ai/rag/answer` 返回答案、引用来源、置信度和是否建议人工复核。
- 无依据问题必须回答知识库依据不足。
- 每个答案至少返回一个可追溯 citation，除非明确无依据。
- prompt 不允许指示模型绕过权限或编造制度、流程、数字和日期。

完成定义：

- Swagger 可见 API。
- 未登录或无权限访问不返回业务数据。
- RAG 问答测试覆盖有依据、无依据和越权问题。

### KP-MVP1-BE-006 实现 RAG 调用日志、引用记录和敏感字段保护

- 阶段：MVP-1
- 模块：Audit
- 类型：Backend
- 负责人角色：Backend
- 优先级：P0
- 预估：2 人日
- 依赖：KP-MVP1-BE-005
- 来源文档：`docs/technical-architecture.md` 第 9.3、14.2 节

任务说明：

记录 RAG 搜索和问答调用的业务审计信息，包括问题摘要、检索结果、引用、模型、token、成本和异常，同时避免把完整文档原文或完整 prompt 写入审计日志。

验收标准：

- 可以按用户、知识版本、模型版本和时间查询 RAG 调用记录。
- citation 记录能回到知识版本和 chunk。
- 敏感字段扫描不包含完整正文、完整附件内容或完整 prompt。
- 模型失败、检索无结果、权限二次校验拒绝都有可观测记录。

完成定义：

- 审计检查脚本扩展 RAG 调用日志检查。
- 评测集可复用调用日志中的引用链路。

## 5. MVP-1 验收矩阵

| 验收项 | 关联任务 | 验证方式 | 阻塞风险 |
| --- | --- | --- | --- |
| 索引模型和迁移落库 | KP-MVP1-BE-001 | EF Core 迁移、DbContext 模型测试 | 后续任务无法记录状态 |
| 索引失败能定位阶段 | KP-MVP1-BE-001, KP-MVP1-AI-002 | 单元测试和失败输入测试 | 运维不可排查 |
| Qdrant provider 不泄漏业务层 | KP-MVP1-AI-004 | 静态扫描和代码评审 | preview 依赖污染 |
| collection 和 payload index 可重复初始化 | KP-MVP1-AI-005, KP-MVP1-DEVOPS-001 | 脚本执行和 Qdrant HTTP 检查 | 检索性能和权限过滤不稳定 |
| 权限化检索不越权 | KP-MVP1-BE-004, KP-MVP1-QA-002 | 权限矩阵测试 | 数据泄露 |
| PostgreSQL 二次校验阻断滞后索引 | KP-MVP1-BE-004 | 模拟 Qdrant 旧 payload 测试 | 权限收窄后仍可引用 |
| 答案必须带引用 | KP-MVP1-BE-005, KP-MVP1-QA-003 | API 测试和页面验收 | 回答不可追溯 |
| 无依据问题拒答 | KP-MVP1-BE-005, KP-MVP1-QA-003 | API 测试和评测集 | 模型编造 |
| RAG 调用日志可追溯且不落敏感原文 | KP-MVP1-BE-006 | 审计脚本和数据库检查 | 合规风险 |

## 6. 主要风险和处理

| 风险 | 影响 | 控制措施 | 关联任务 |
| --- | --- | --- | --- |
| DataIngestion preview API 变化 | pipeline 返工 | 包在 `IKnowledgeIngestionPipeline` 后；保留自研 reader/chunker 降级 | KP-MVP1-AI-002 |
| Qdrant provider 过滤能力不足 | 权限过滤不完整 | provider 不满足时直接 Qdrant SDK 适配，但仍包在 `IKnowledgeVectorStore` 后 | KP-MVP1-AI-004 |
| embedding 维度漂移 | collection 不兼容 | `EmbeddingModelVersion` 固定维度，维度变化建新 collection | KP-MVP1-AI-001 |
| 权限范围表达不完整 | RAG 越权 | Qdrant 粗过滤 + PostgreSQL 二次校验 + 权限矩阵测试 | KP-MVP1-BE-004 |
| 模型网关配置不稳定 | 问答失败或成本失控 | LiteLLM 配置模板、超时、fallback 和本地跳过策略 | KP-MVP1-DEVOPS-002 |
| 引用不可追溯 | 无法审计 | citation 强制携带知识、版本、chunk 和页码 | KP-MVP1-BE-005 |

## 7. 推荐执行顺序

1. 完成 KP-MVP1-BE-001，先把索引事实模型和迁移落库。
2. 完成 KP-MVP1-AI-001，固定模型、维度和 collection 策略。
3. 并行推进 KP-MVP1-BE-002、KP-MVP1-AI-002、KP-MVP1-AI-004。
4. 完成 KP-MVP1-AI-003 和 KP-MVP1-AI-005，形成真实索引写入闭环。
5. 完成 KP-MVP1-BE-004，先让权限化搜索可测。
6. 完成 KP-MVP1-BE-005 和 KP-MVP1-FE-002，形成问答演示闭环。
7. 补齐 KP-MVP1-BE-006、QA 和 DevOps 脚本，进入 MVP-1 验收。

## 8. MVP-1 完成判定

MVP-1 完成必须同时满足：

- 已发布知识版本可以被索引，索引状态和失败原因可追踪。
- Qdrant 可以检索 chunk，但不会成为权限最终裁决者。
- PostgreSQL 二次校验可以阻断跨租户、跨部门、跨岗位、跨角色、指定用户和安全等级越权。
- RAG 问答返回的答案有引用；无依据问题拒答。
- RAG 调用、引用和模型 metadata 可审计，且不保存完整文档原文或完整 prompt。
- 自动化测试和本地验证脚本覆盖索引、检索、问答、权限和审计核心路径。
