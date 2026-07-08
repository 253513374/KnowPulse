# KnowPulse MVP-1 验收矩阵和完成记录

版本：v0.1
日期：2026-06-27
来源：`docs/mvp-1-task-breakdown.md`、`docs/mvp-1-qa-test-plan.md`、`docs/rag-ai-boundary.md`、当前仓库验证结果

## 1. 验收口径

MVP-1 验收目标是权限化 RAG 问答闭环：

- 已发布知识版本可以进入索引任务，索引状态、失败阶段、失败原因和重试次数可追踪。
- 知识正文和 MVP-1 支持的文本附件可以被读取、分块、生成 embedding 记录，并写入 Qdrant 可重建索引。
- Qdrant 只做 payload 粗过滤和向量召回，PostgreSQL 仍是业务事实库和最终权限裁决者。
- RAG 检索必须经过租户、发布态、部门、岗位、角色、指定用户和安全等级校验。
- RAG 问答必须基于二次校验后的 chunk 生成；无依据、越权或模型不可用时按预期降级。
- RAG 调用日志和 citation 可审计，但不保存完整 prompt、完整知识正文或完整附件内容。
- Qdrant provider、DataIngestion/VectorData preview provider 和模型 SDK 类型不得泄漏到业务边界。

不纳入 MVP-1 的内容：题库、AI 出题、审核、组卷、在线考试、成绩、错题和学习路径。

## 2. 当前验证命令

| 验证项 | 命令 |
| --- | --- |
| MVP-1 综合回归 | `.\ops\verify-mvp1.ps1` |
| RAG 评测集定向回归 | `.\ops\verify-rag-evaluation.ps1` |
| MVP-1 过滤测试 | `dotnet test .\KnowPulse.slnx --no-restore --filter "FullyQualifiedName~Mvp1" -v:m -m:1 -nr:false -p:BuildInParallel=false` |
| RAG provider 边界扫描 | `.\ops\verify-rag-provider-boundaries.ps1` |
| Qdrant collection 只读健康验证 | `.\ops\verify-qdrant-knowledge-collection.ps1` |
| Qdrant collection 幂等初始化 | `.\ops\verify-qdrant-knowledge-collection.ps1 -Ensure` |
| LiteLLM alias 和密钥变量静态验证 | `.\ops\verify-litellm-rag-config.ps1` |
| LiteLLM 本地网关运行验证 | `.\ops\verify-litellm-rag-config.ps1 -Live` |
| RAG 调用日志和敏感字段扫描 | `.\ops\audit-check.ps1 -FailOnSensitiveHits` |

说明：`verify-mvp1.ps1` 默认执行 MVP-1 过滤测试、provider 边界扫描、Qdrant collection 验证、LiteLLM 静态配置验证和审计敏感字段扫描。若本地尚未启动 Qdrant、LiteLLM 或 PostgreSQL，可先执行单项命令定位缺失依赖；不应把依赖不可达误判为业务逻辑通过。

## 3. 任务到证据矩阵

| 任务 | 验收证据 | 当前结论 | 剩余动作 |
| --- | --- | --- | --- |
| KP-MVP1-BE-001 实现 KnowledgeIndexing 数据模型与迁移 | `AppKnowledgeIndexJobs`、`AppKnowledgeChunks`、`AppKnowledgeEmbeddingRecords`、`AppEmbeddingModelVersions` 迁移和 DbContext；`KnowledgeIndexingDomainTests`；`Mvp1KnowledgeIndexingAppServiceTests` | 自动化覆盖已具备 | 继续在后续变更中保持 `FullyQualifiedName~Mvp1` 回归 |
| KP-MVP1-BE-002 实现索引任务应用服务与手动重建入口 | `KnowledgeIndexJobAppService`、`KnowledgeIndexJobsController`、`/knowledge/index-jobs` 页面和 EF Core 集成测试 | 自动化和页面入口已具备 | 页面级筛选/重试仍需普通浏览器手工抽验 |
| KP-MVP1-BE-003 发布、下架、归档和权限变更触发索引状态更新 | `KnowledgeItemAppService` 生命周期联动、索引任务和 chunk 状态测试 | 自动化覆盖已具备 | 权限范围变化后的页面提示可在 MVP-1 FE 补验中继续观察 |
| KP-MVP1-AI-001 固定 embedding 模型、维度和 collection 策略 | `docs/rag-ai-boundary.md`、`RagAiOptions`、Host/DbMigrator appsettings、LiteLLM 模板和 `.env.example` | 配置和文档已固化 | 生产环境真实供应商 key 只允许通过安全配置源注入 |
| KP-MVP1-AI-002 实现文档读取、Markdown 化和 chunk pipeline | `KnowledgeIngestionPipeline`、正文/文本附件/MarkItDown 可转换附件/空内容/失败阶段测试 | 自动化覆盖已具备 | 图片 OCR、音频、视频解析仍不进入当前默认范围 |
| KP-MVP1-AI-003 实现 embedding 生成和模型版本记录 | `LiteLlmKnowledgeEmbeddingClient`、`KnowledgeEmbeddingPipeline`、模型 key 缺失失败测试 | 自动化覆盖已具备 | 真实模型 key 缺失时按 embedding 阶段失败处理 |
| KP-MVP1-AI-004 实现 `IKnowledgeVectorStore` Qdrant 适配层 | `QdrantKnowledgeVectorStore` REST 适配、`QdrantKnowledgeVectorStoreTests`、`verify-rag-provider-boundaries.ps1` | 边界和请求结构已具备 | 禁止后续业务服务直接引用 provider/SDK 类型 |
| KP-MVP1-AI-005 创建 Qdrant collection 和 payload index | `verify-qdrant-knowledge-collection.ps1`、`knowledge_chunks_1536_mvp1`、必需 payload index 清单 | 验证入口已固化 | 本地 collection 缺失时执行 `-Ensure` 幂等创建 |
| KP-MVP1-BE-004 实现权限化 `IKnowledgeRetriever` | `KnowledgeRetriever`、`Mvp1KnowledgeRetrieverTests`，覆盖 Qdrant 粗过滤和 PostgreSQL 二次校验 | 自动化覆盖已具备 | 后续任何权限模型扩展必须补充越权矩阵 |
| KP-MVP1-BE-005 实现 RAG 搜索和问答应用服务/API | `RagController`、`RagAnswerService`、`Mvp1RagAnswerServiceTests` | 自动化覆盖有依据、无依据和越权拒答 | `/rag/answer` 页面仍需普通浏览器手工抽验 |
| KP-MVP1-BE-006 实现 RAG 调用日志、引用记录和敏感字段保护 | `RagCallLog`、`RagCitationRecord`、`RagCallLogWriter`、`Mvp1RagAuditTests`、`audit-check.ps1 -FailOnSensitiveHits` | 自动化和扫描入口已具备 | 本地数据库中已有历史数据时以扫描结果为准 |
| KP-MVP1-QA-004 实现 RAG 评测集和运行记录 | `RagEvaluationCase`、`RagEvaluationRun`、`RagEvaluationRunCaseResult`、`Mvp1RagEvaluationAppServiceTests`、`ops/verify-rag-evaluation.ps1` | 自动化覆盖命中率、引用准确率、拒答率和越权召回硬失败 | 后续可从审计日志沉淀更多真实评测用例 |
| KP-MVP1-FE-001 在知识版本页面展示索引状态和重建操作 | `Pages/Knowledge/Items.razor`、`Pages/Knowledge/IndexJobs.razor` | 页面实现已具备 | 需要普通浏览器手工记录索引状态、重建和失败展示 |
| KP-MVP1-FE-002 实现基础 RAG 问答页面 | `Pages/Rag/Answer.razor` | 页面实现已具备 | 需要普通浏览器手工记录回答、引用、置信度和无依据拒答 |
| KP-MVP1-QA-001 编写索引任务和失败定位测试 | `Mvp1KnowledgeIndexingAppServiceTests`、`KnowledgeIndexingDomainTests` | 已纳入 MVP-1 过滤测试 | 无 |
| KP-MVP1-QA-002 编写权限化检索越权测试矩阵 | `Mvp1KnowledgeRetrieverTests` | 已纳入 MVP-1 过滤测试 | 无 |
| KP-MVP1-QA-003 编写 RAG 问答引用和无依据拒答测试 | `Mvp1RagAnswerServiceTests`、`Mvp1RagAuditTests` | 已纳入 MVP-1 过滤测试 | 无 |
| KP-MVP1-DEVOPS-001 补充 Qdrant collection 初始化和健康验证脚本 | `ops/verify-qdrant-knowledge-collection.ps1` | 已固化 | 本地依赖未启动时脚本应失败并提示执行 `-Ensure` 或启动 Qdrant |
| KP-MVP1-DEVOPS-002 补充 LiteLLM 模型配置模板和本地校验 | `ops/verify-litellm-rag-config.ps1`、`deploy/litellm/config.example.yaml` | 已固化 | `-Live` 需要本地 LiteLLM 运行并加载 alias |

## 4. 预期降级行为

| 场景 | 预期行为 | 验证入口 |
| --- | --- | --- |
| 模型 key 缺失 | embedding/chat 阶段记录明确失败原因；非 RAG 基础测试不应因此失败 | `Mvp1KnowledgeIndexingAppServiceTests`、`Mvp1RagAuditTests` |
| Qdrant 不可达或 collection 缺失 | collection 验证失败并提示使用 `-Ensure`；检索阶段记录 `vector-store` 失败 | `verify-qdrant-knowledge-collection.ps1`、MVP-1 过滤测试替身 |
| LiteLLM 不可达 | 静态配置仍可验证；`-Live` 失败指向网关健康或 alias 缺失；问答服务使用保守降级答案并保留 citation | `verify-litellm-rag-config.ps1 -Live`、`Mvp1RagAnswerServiceTests` |
| 无依据问题 | 返回“知识库依据不足”，citation 为空，`NeedHumanReview=true` | `Mvp1RagAnswerServiceTests` |
| 越权命中 | Qdrant 候选被 PostgreSQL 二次校验丢弃，不返回答案或 citation | `Mvp1KnowledgeRetrieverTests`、`Mvp1RagAnswerServiceTests` |
| 评测越权召回 | 命中禁止 citation 时评测运行 `HardFailed=true` 且状态为 `Failed` | `Mvp1RagEvaluationAppServiceTests` |

## 5. 完成记录

| 日期 | 范围 | 结论 | 证据 |
| --- | --- | --- | --- |
| 2026-06-27 | S0 包 A：KP-S0-DOC-001、KP-S0-QA-001、KP-S0-SEC-001 | MVP-1 验收矩阵、回归入口和安全扫描入口已固化；不进入 MVP-2 题库开发 | `docs/mvp-1-acceptance-matrix.md`、`docs/mvp-1-qa-test-plan.md`、`ops/verify-mvp1.ps1`、`ops/verify-rag-provider-boundaries.ps1`、`ops/audit-check.ps1 -FailOnSensitiveHits` |

## 6. 本轮验证结果

| 命令 | 结果 | 备注 |
| --- | --- | --- |
| `.\ops\verify-mvp1.ps1` | 通过 | 串联 5 个步骤全部通过 |
| `dotnet test .\KnowPulse.slnx --no-restore --filter "FullyQualifiedName~Mvp1" -v:m -m:1 -nr:false -p:BuildInParallel=false` | 通过 | `KnowPulse.EntityFrameworkCore.Tests` 中 20 个 MVP-1 测试通过；其他测试程序集无匹配用例 |
| `.\ops\verify-rag-provider-boundaries.ps1` | 通过 | 未发现 Qdrant SDK、SemanticKernel、VectorData、DataIngestion、Microsoft.Extensions.AI、OpenAI 或 Azure.AI provider/SDK 直接泄漏到扫描边界 |
| `.\ops\verify-qdrant-knowledge-collection.ps1` | 通过 | `knowledge_chunks_1536_mvp1` 存在，维度为 1536，必需 payload index 无缺失 |
| `.\ops\verify-litellm-rag-config.ps1` | 通过 | Host/DbMigrator appsettings、LiteLLM 模板和 `.env.example` alias/key 变量一致 |
| `.\ops\audit-check.ps1 -FailOnSensitiveHits` | 通过 | ABP 审计参数、实体属性变更、RAG 调用日志和 citation 敏感命中均为 0 |

本轮回归仍有两个非阻断提示：

- `dotnet test` 输出 NuGet 漏洞告警：`Scriban 7.0.0`、`SQLitePCLRaw.lib.e_sqlite3 2.1.11`。这些是当前依赖链告警，不是本次包 A 新增失败。
- 审计记录中最近一次真实 RAG 页面调用因 LiteLLM embedding HTTP 401 失败，已记录为 `embedding` / `search` 失败阶段，敏感字段扫描为 0。该问题属于本地模型网关 key/alias 配置风险，不影响静态配置和自动化替身测试结论。

## 7. 仍需人工验收

| 缺口 | 影响任务 | 原因 | 建议验证方式 |
| --- | --- | --- | --- |
| `/knowledge/items` 索引状态和重建操作页面未在本轮普通浏览器复验 | KP-MVP1-FE-001 | 包 A 只固化 MVP-1 验收和回归，不扩展前端自动化 | 启动 API/Blazor 后使用普通浏览器检查发布版本索引状态、点击重建、观察任务变化 |
| `/knowledge/index-jobs` 失败阶段、失败原因和重试筛选未在本轮普通浏览器复验 | KP-MVP1-FE-001 | 需要已有失败任务或手工构造失败任务 | 通过索引任务页按状态筛选，确认失败阶段、失败原因、重试次数和分页 |
| `/rag/answer` 回答、引用、置信度、人审提示和无依据拒答未在本轮普通浏览器复验 | KP-MVP1-FE-002 | 真实问答依赖本地登录态、Qdrant collection 和模型网关配置 | 使用已发布并索引的演示知识提问，再使用无依据问题验证拒答 |
