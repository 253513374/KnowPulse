# KnowPulse MVP-1 QA 验收清单

版本：v0.1
日期：2026-06-27
范围：MVP-1 权限化 RAG 问答闭环

## 1. 验收入口

本清单用于覆盖 `docs/mvp-1-task-breakdown.md` 中的 QA 与 DevOps 验收项：

- KP-MVP1-QA-001：索引任务和失败定位测试。
- KP-MVP1-QA-002：权限化检索越权测试矩阵。
- KP-MVP1-QA-003：RAG 问答引用和无依据拒答测试。
- KP-MVP1-QA-004：RAG 评测集和评测运行记录测试。
- KP-MVP1-DEVOPS-001：Qdrant collection 初始化和健康验证。
- KP-MVP1-DEVOPS-002：LiteLLM 模型配置模板和本地校验。

## 2. 自动化验证命令

在仓库根目录执行：

```powershell
.\ops\verify-mvp1.ps1
```

单项命令：

```powershell
.\ops\verify-rag-provider-boundaries.ps1
.\ops\verify-qdrant-knowledge-collection.ps1
.\ops\verify-litellm-rag-config.ps1
.\ops\verify-rag-evaluation.ps1
.\ops\audit-check.ps1 -FailOnSensitiveHits
dotnet test .\KnowPulse.slnx --no-restore --filter "FullyQualifiedName~Mvp1" -v:m -m:1 -nr:false -p:BuildInParallel=false
```

如果本地 Qdrant 已启动并允许初始化：

```powershell
.\ops\verify-qdrant-knowledge-collection.ps1 -Ensure
```

如果本地 LiteLLM 已启动并配置了 `KNOWPULSE_LITELLM_API_KEY` 或 `LITELLM_MASTER_KEY`：

```powershell
.\ops\verify-litellm-rag-config.ps1 -Live
```

## 3. QA-001 索引任务与失败定位

| 场景 | 前置条件 | 操作 | 期望结果 |
| --- | --- | --- | --- |
| 发布版本创建索引任务 | 知识文档存在草稿版本 | 发布版本 | 生成 `Pending` 索引任务，关联知识条目和版本 |
| 手动重建不覆盖旧事实 | 已存在同版本索引任务 | 调用重建索引 | 新增任务记录，旧任务保留 |
| 空正文失败定位 | 知识版本正文和附件均为空 | 执行摄取管道 | 任务失败，`FailureStage` 指向 reader 或 chunker 阶段 |
| embedding key 缺失 | 未配置 LiteLLM key | 执行 embedding pipeline | 任务失败，`FailureStage=embedding`，非 RAG 测试不被阻塞 |
| 重试失败任务 | 存在 `Failed` 任务 | 调用重试 | 任务回到 `Pending` 或重新进入处理状态，重试次数可追踪 |

重点检查表：

- `AppKnowledgeIndexJobs` 保存状态、失败阶段、失败原因、重试次数和下次重试时间。
- `AppKnowledgeChunks` 可追溯知识条目、知识版本、索引任务、来源页码和内容 hash。
- `AppKnowledgeEmbeddingRecords` 可追溯 chunk、模型版本、向量维度和外部向量记录 ID。

## 4. QA-002 权限化检索越权矩阵

| 权限边界 | 用户 A 可见 | 用户 B 不可见 | 验证点 |
| --- | --- | --- | --- |
| 租户 | Tenant A 知识 | Tenant B 用户 | Qdrant payload filter 和 PostgreSQL 二次校验均不返回 |
| 部门 | 部门甲知识 | 部门乙用户 | 不返回候选 chunk，不生成 citation |
| 岗位 | 岗位甲知识 | 岗位乙用户 | 二次校验阻断旧 payload 命中 |
| 角色 | 指定角色知识 | 无角色用户 | 搜索结果为空 |
| 指定用户 | 指定用户知识 | 其他用户 | 搜索结果为空 |
| 安全等级 | 安全等级 1 用户 | 安全等级不足用户 | 高等级知识不返回 |
| 生命周期 | 已发布版本 | 下架、归档、停用版本 | 不返回不可用版本 chunk |

必须覆盖的回归点：

- Qdrant payload 仍命中但 PostgreSQL 权限已收窄时，最终结果必须为空。
- `POST /api/ai/rag/search` 只返回二次校验后的 chunk。
- 未登录或无 `KnowPulse.Rag.Search` 权限时，不返回业务数据。

## 5. QA-003 RAG 问答、引用与拒答

| 场景 | 输入 | 期望结果 |
| --- | --- | --- |
| 有依据回答 | 命中已发布且可访问知识 | 返回答案、置信度、至少一个 citation |
| 无依据问题 | 知识库没有相关内容 | 回答“知识库依据不足”，`NeedHumanReview=true`，citation 为空 |
| 越权问题 | 问题命中无权限知识 | 不泄露答案或引用，按无依据处理 |
| 引用可追溯 | 返回 citation | 包含知识条目、知识版本、chunk、标题、页码和受控摘录 |
| 审计可追溯 | 完成搜索或问答 | 写入调用日志、引用记录、模型 metadata、状态和耗时 |

敏感字段检查：

- 调用日志不得保存完整 prompt。
- 调用日志不得保存完整文档原文或完整附件内容。
- citation 只保存 quote hash 和受控长度 preview。

## 6. QA-004 RAG 评测集和运行记录

| 场景 | 输入 | 期望结果 |
| --- | --- | --- |
| 有依据用例 | 问题、期望 citation、当前用户权限上下文 | 运行记录保存命中率和引用准确率，逐用例结果可追溯实际 citation |
| 期望拒答用例 | 无依据问题且 `ExpectedRefusal=true` | 无答案拒答率计入通过，chat client 不应被调用 |
| 越权召回硬失败 | 实际 citation 命中禁止 citation | `UnauthorizedRecallRate > 0`，运行状态为 `Failed`，`HardFailed=true` |
| 权限上下文不匹配 | 用例声明非当前用户或越界组织上下文 | 用例失败，不执行替人问答，不产生伪造权限召回 |

指标口径：

- `HitRate`：非拒答用例中，命中至少一个期望 citation 的比例；没有期望 citation 时以返回授权 citation 作为命中。
- `CitationAccuracyRate`：非拒答用例中，实际 citation 命中期望 citation 的比例。
- `NoAnswerRejectionRate`：期望拒答用例实际拒答的比例。
- `UnauthorizedRecallRate`：实际 citation 命中禁止 citation 的比例；该指标大于 0 必须作为硬失败。

本地验证入口：

```powershell
.\ops\verify-rag-evaluation.ps1
```

## 7. 前端冒烟

| 页面 | 操作 | 期望结果 |
| --- | --- | --- |
| `/knowledge/items` | 选择知识文档 | 版本列表展示最新索引状态 |
| `/knowledge/items` | 点击重建索引 | 生成新索引任务并刷新状态 |
| `/knowledge/index-jobs` | 按状态筛选 | 正确分页和展示失败阶段、原因、重试次数 |
| `/knowledge/index-jobs` | 重试失败任务 | 任务状态刷新 |
| `/rag/answer` | 输入问题并提问 | 显示回答、置信度、人审标记和引用 |
| `/rag/answer` | 清空 | 清除问题、回答和错误状态 |

## 8. MVP-1 完成判定

MVP-1 进入验收时必须同时满足：

- 已发布知识版本能进入索引任务，并能追踪成功、失败、重试和手动重建。
- Qdrant collection、payload index 和 LiteLLM alias 可通过脚本校验。
- RAG 检索只返回当前用户可访问的已发布 chunk。
- RAG 问答有引用；无依据或越权时拒答。
- RAG 调用日志和 citation 可审计，且不保存完整敏感正文或完整 prompt。
- RAG 评测集可以重复运行，越权召回率作为硬失败指标落库。

## 9. 提交前回归要求

后续进入 MVP-2 题库、AI 出题和考试开发前，每个 PR 或提交说明至少记录：

- 是否影响 MVP-1 RAG 索引、检索、问答或审计链路。
- 是否已执行 `.\ops\verify-mvp1.ps1`；若跳过 Qdrant、LiteLLM live 或审计数据库扫描，需要说明本地依赖缺失原因。
- 如果修改权限、知识生命周期、RAG prompt、citation 或模型调用日志，必须执行 `dotnet test .\KnowPulse.slnx --no-restore --filter "FullyQualifiedName~Mvp1"`。
- 如果修改 RAG 评测指标、评测用例模型或运行记录，必须执行 `.\ops\verify-rag-evaluation.ps1`。
