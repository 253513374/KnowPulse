# KnowPulse MVP-3 学习与运营质量闭环验收矩阵

版本：v0.2
日期：2026-06-30
范围：学习路径、阅读进度、个人学习中心、知识反馈、RAG 评测集、审计追溯和运营质量最小证据链

## 1. 验收结论

当前 MVP-3 已形成可交接、可复跑的最小闭环：

- 管理端可配置岗位、部门、职级、全员学习路径，绑定必学知识、推荐知识和必考考试。
- 个人学习中心只展示当前用户被分配且有知识可见权限的内容，并记录阅读、完成状态。
- 知识反馈闭环支持用户提交、管理员处理、关闭和关联修订版本，反馈不会直接修改已发布知识。
- RAG 评测支持可重复运行的用例、运行记录、命中率、引用准确率、无答案拒答率和越权召回率，越权召回作为硬失败指标。
- 审计脚本可检查 RAG、AI 出题、考试评分和知识反馈的追溯链路与敏感字段边界。

需要如实保留的缺口：

- `docs/post-mvp-1-task-breakdown.md` 中 `KP-MVP3-BE-004` 题目质量反馈和错题聚合、`KP-MVP3-BE-005` 运营报表聚合任务尚未形成独立模块或页面。
- 当前运营质量能力是最小证据链：知识反馈、考试错题记录、RAG 评测运行和审计查询已经可追溯，但还不是完整运营报表闭环。
- 本次只固化 MVP-3 验收结果，不启动 MVP-4 生产化功能，也不临时扩展大范围报表功能。

## 2. 验收矩阵

| 验收项 | 交付证据 | 自动或运行态验证 | 当前结论 | 剩余风险 |
| --- | --- | --- | --- | --- |
| 学习路径数据模型 | `LearningPath`、`LearningPathAssignment`、`LearningPathRequiredKnowledge`、`LearningPathRecommendedKnowledge`、`LearningPathRequiredExam` | `LearningPathTests`、`Mvp3LearningPathAppServiceTests`、EF 迁移检查 | 通过 | 学习路径配置只保存 `ConfigVersion`，不保存完整历史配置快照 |
| 阅读进度数据追溯 | `LearningKnowledgeReadRecord` 按路径、用户、知识、版本唯一记录 | `My_Learning_Should_Record_Read_And_Completion_Status`、唯一索引静态检查 | 通过 | 阅读完成仍为用户主动标记，未做阅读时长或滚动深度判断 |
| 个人学习中心权限 | `GetMyListAsync`、`GetMyAsync`、`MarkKnowledgeReadAsync` 叠加分配规则和 `KnowledgePermissionScope` | `My_List_Should_Only_Return_Active_Paths_Assigned_By_Department_Position_Or_JobLevel`、`My_List_Should_Filter_Knowledge_Content_By_Knowledge_Permission_Scopes` | 通过 | 考试入口仍由考试模块二次校验，学习路径只提示不可参加 |
| 学习中心页面 | `/learning/my`、学习菜单、知识详情深链、考试入口跳转 | Blazor Client 构建、HTTP 200 烟测、浏览器渲染抽验 | 通过 | 已验证管理员登录态空状态；仍建议补普通用户真实数据态抽验 |
| 知识反馈数据模型 | `KnowledgeFeedback`、反馈类型、状态流转、来源版本、处理人、处理结论、关联修订版本 | `KnowledgeFeedbackTests`、`Mvp3KnowledgeFeedbackAppServiceTests`、EF 迁移检查 | 通过 | 反馈描述是用户输入，后续可增加敏感词脱敏 |
| 反馈不直接改已发布知识 | `KnowledgeFeedbackAppService` 只更新反馈聚合；修订仍走知识版本流程 | `Should_Submit_Feedback_Without_Modifying_Published_Version`、静态检查 `_versionRepository` 不写入 | 通过 | 仍需要运营流程约束管理员先创建修订草稿再关联 |
| 反馈提交和处理页面 | `/knowledge/items` 反馈提交入口、`/knowledge/feedback` 管理页 | Blazor Client 构建、HTTP 200 烟测、浏览器渲染抽验 | 通过 | 关联版本当前为 GUID 输入，易用性后续可增强 |
| RAG 评测用例 | `RagEvaluationCase` 保存问题、期望引用、期望拒答、权限上下文和禁止引用 | `Mvp1RagEvaluationTests`、`Mvp1RagEvaluationAppServiceTests`、API 契约测试 | 通过 | 真实评测质量依赖后续持续维护用例集 |
| RAG 评测运行记录 | `RagEvaluationRun`、`RagEvaluationRunCaseResult` 保存指标和逐用例结果 | `ops/verify-rag-evaluation.ps1`、`ops/verify-mvp3.ps1` | 通过 | 本轮为项目内最小闭环，未接入外部评测平台 |
| 越权召回硬失败 | `UnauthorizedRecallRate`、`UnauthorizedRecallCount`、`HardFailed` | `RunAsync_Should_Hard_Fail_When_Unauthorized_Recall_Detected` | 通过 | 只能覆盖用例声明的 forbidden citations，需持续补充负例 |
| 审计和敏感字段保护 | `ops/audit-check.ps1` 覆盖 RAG、AI 出题、考试、知识反馈和敏感字段扫描 | `ops/audit-check.ps1 -FailOnSensitiveHits -Limit 10` 由综合脚本串联通过 | 通过 | 审计动作丰富度依赖实际运行态用户操作量 |
| 考试错题追溯 | `WrongQuestionRecord` 保存用户、考试、试卷题快照、用户答案、正确答案和得分 | `Mvp2ExamAppServiceTests.Scoring_Should_Record_Wrong_Questions_And_Remain_Idempotent`、审计脚本考试评分查询 | 已具备追溯底座 | 尚未形成题目质量聚合服务或待复核列表 |
| 运营质量最小能力 | 知识反馈、RAG 评测、考试成绩错题、AI 出题审核和审计查询 | `verify-mvp3`、`verify-mvp2` 既有证据、`audit-check` SQL | 部分闭环 | 缺少独立运营报表聚合任务、报表首页、题目质量反馈页面 |
| 综合验收入口 | `ops/verify-mvp3.ps1` 串联静态检查、MVP-3 测试、RAG 评测、Blazor 构建、审计和可选页面烟测 | `.\ops\verify-mvp3.ps1 -WithBuild`、页面烟测命令 | 通过 | 页面数据态仍建议以真实普通用户和管理员账号补充人工记录 |

## 3. 接口清单

### 学习路径与个人学习

| 方法 | 路由 | 权限 | 说明 |
| --- | --- | --- | --- |
| `GET` | `/api/learning/paths` | `KnowPulse.Learning.Manage` | 管理端分页查询学习路径 |
| `GET` | `/api/learning/paths/{id}` | `KnowPulse.Learning.Manage` | 管理端查询学习路径详情 |
| `POST` | `/api/learning/paths` | `KnowPulse.Learning.Manage` | 创建学习路径 |
| `PUT` | `/api/learning/paths/{id}` | `KnowPulse.Learning.Manage` | 编辑学习路径配置 |
| `POST` | `/api/learning/paths/{id}/deactivate` | `KnowPulse.Learning.Manage` | 停用学习路径 |
| `GET` | `/api/learning/paths/my` | `KnowPulse.Learning.View` | 当前用户查询被分配的启用路径 |
| `GET` | `/api/learning/paths/my/{id}` | `KnowPulse.Learning.View` | 当前用户查询单条被分配路径 |
| `POST` | `/api/learning/paths/my/knowledge-read` | `KnowPulse.Learning.View` | 记录阅读或标记完成 |

### 知识反馈

| 方法 | 路由 | 权限 | 说明 |
| --- | --- | --- | --- |
| `POST` | `/api/knowledge/feedback` | `KnowPulse.KnowledgeBase.Items` | 用户提交反馈，并叠加知识可见性校验 |
| `GET` | `/api/knowledge/feedback` | `KnowPulse.KnowledgeBase.Feedback` | 管理端反馈列表 |
| `GET` | `/api/knowledge/feedback/{id}` | `KnowPulse.KnowledgeBase.Feedback` | 管理端反馈详情 |
| `POST` | `/api/knowledge/feedback/{id}/process` | `KnowPulse.KnowledgeBase.Feedback` | 接手处理 |
| `POST` | `/api/knowledge/feedback/{id}/link-version` | `KnowPulse.KnowledgeBase.Feedback` | 关联修订版本 |
| `POST` | `/api/knowledge/feedback/{id}/resolve` | `KnowPulse.KnowledgeBase.Feedback` | 标记已处理 |
| `POST` | `/api/knowledge/feedback/{id}/close` | `KnowPulse.KnowledgeBase.Feedback` | 关闭反馈 |

### RAG 评测

| 方法 | 路由 | 权限 | 说明 |
| --- | --- | --- | --- |
| `POST` | `/api/ai/rag/evaluations/cases` | `KnowPulse.Rag.Evaluations` | 创建评测用例 |
| `PUT` | `/api/ai/rag/evaluations/cases/{id}` | `KnowPulse.Rag.Evaluations` | 编辑评测用例 |
| `GET` | `/api/ai/rag/evaluations/cases/{id}` | `KnowPulse.Rag.Evaluations` | 查询评测用例 |
| `GET` | `/api/ai/rag/evaluations/cases` | `KnowPulse.Rag.Evaluations` | 分页查询评测用例 |
| `POST` | `/api/ai/rag/evaluations/runs` | `KnowPulse.Rag.Evaluations` | 执行评测 |
| `GET` | `/api/ai/rag/evaluations/runs/{id}` | `KnowPulse.Rag.Evaluations` | 查询评测运行详情 |
| `GET` | `/api/ai/rag/evaluations/runs` | `KnowPulse.Rag.Evaluations` | 分页查询评测运行 |

## 4. 当前验证记录

| 验证项 | 命令或方式 | 结果 |
| --- | --- | --- |
| 本地依赖和迁移 | `.\ops\start-local.ps1 -SkipDocker -HealthRetries 10 -HealthRetryDelaySeconds 3` | 通过；PostgreSQL、MinIO、Qdrant、LiteLLM 健康检查通过，DbMigrator 完成 |
| MVP-3 综合验收 | `.\ops\verify-mvp3.ps1 -WithBuild` | 通过；解决方案构建、静态验收、MVP-3 领域测试、MVP-3 EF 应用服务测试、RAG 评测、Blazor Client 构建、审计敏感字段硬门禁均通过 |
| MVP-3 领域测试 | 综合脚本内执行 `FullyQualifiedName~LearningPathTests|FullyQualifiedName~KnowledgeFeedbackTests` | 8 通过，0 失败 |
| MVP-3 EF 应用服务测试 | 综合脚本内执行 `FullyQualifiedName~Mvp3LearningPathAppServiceTests|FullyQualifiedName~Mvp3KnowledgeFeedbackAppServiceTests` | 7 通过，0 失败 |
| RAG 评测专项 | 综合脚本内执行 `ops/verify-rag-evaluation.ps1` | 通过；覆盖评测领域、应用服务和 API 契约 |
| 审计敏感字段硬门禁 | 综合脚本内执行 `ops/audit-check.ps1 -FailOnSensitiveHits -Limit 10` | 通过；RAG、AI 出题、知识反馈、MVP-2 敏感字段扫描均 0 命中 |
| Blazor 页面 HTTP 烟测 | `.\ops\verify-mvp3.ps1 -SkipStatic -SkipDotnetTests -SkipRagEvaluation -SkipBlazorBuild -SkipAudit -CheckRuntimePages` | 通过；Swagger、`/learning/my`、`/knowledge/feedback`、`/_framework/dotnet.js` 均 HTTP 200 |
| 浏览器渲染抽验 | Codex 内置浏览器访问 `/learning/my` 和 `/knowledge/feedback` | 通过；页面标题分别为“我的学习”“知识反馈”，渲染出空态、筛选表格和处理详情，无 Blazor 错误、无页面 console error |

构建和测试仍输出既有 NuGet 漏洞告警：`Scriban 7.0.0`、`SQLitePCLRaw.lib.e_sqlite3 2.1.11`。这些告警不阻塞当前 MVP-3 功能验收，但应作为依赖升级风险单独处理。

## 5. 权限、审计、追溯和页面可用性

| 检查面 | 当前设计 | 验收方式 |
| --- | --- | --- |
| 学习路径管理权限 | 管理端接口和菜单使用 `KnowPulse.Learning.Manage` | 静态验收、控制器授权、应用服务权限检查 |
| 个人学习权限 | 个人接口和菜单使用 `KnowPulse.Learning.View`，再叠加路径分配和知识权限范围 | 应用服务测试、浏览器渲染抽验 |
| 阅读追溯 | 读记录保存用户、路径、知识、版本、最近阅读、完成时间和阅读次数 | 应用服务测试和 EF 唯一索引检查 |
| 反馈提交权限 | 用户必须具备 `KnowledgeBase.Items`，且只能对可访问知识提交反馈 | 应用服务测试、反馈服务静态检查 |
| 反馈处理权限 | 管理端处理接口和菜单使用 `KnowledgeBase.Feedback` | 控制器授权、菜单权限、页面构建和渲染抽验 |
| 反馈修订边界 | 反馈处理不写入 `KnowledgeVersion`，只关联版本 ID | 应用服务测试和静态检查 |
| RAG 评测权限 | 评测接口统一使用 `KnowPulse.Rag.Evaluations` | 控制器授权、API 契约测试 |
| RAG 评测追溯 | 运行记录保存操作者、开始/完成时间、整体指标和逐用例结果 | RAG 评测测试和迁移检查 |
| 越权召回 | 禁止引用命中会导致用例失败，运行 `HardFailed=true` | RAG 评测领域和应用服务测试 |
| 页面可用性 | `/learning/my`、`/knowledge/feedback` 具备加载、空态、错误和无权限提示 | Blazor 构建、HTTP 200 烟测、浏览器渲染抽验 |

## 6. 剩余风险

| 风险 | 影响 | 建议 |
| --- | --- | --- |
| 题目质量聚合未独立实现 | 管理员暂时不能直接看到题目答错率、反馈次数、最近考试使用次数和待复核题列表 | 后续按 `KP-MVP3-BE-004` 单独实现，不和 MVP-4 生产化任务混做 |
| 运营报表聚合任务和首页未实现 | 当前只有审计 SQL、错题记录、反馈列表、RAG 评测等分散证据，未形成统一运营首页 | 后续按 `KP-MVP3-BE-005`、`KP-MVP3-FE-004` 单独交付 |
| 登录态页面数据态未完全自动化 | HTTP 200 和浏览器渲染能证明路由/页面可用，不能覆盖所有真实账号数据状态 | 用普通用户、内容管理员、系统管理员各补一次数据态、空态、无权限态抽验 |
| 学习路径配置缺少历史快照 | 后续审计只能通过 `ConfigVersion` 和读记录回溯到知识版本 ID | 如需强审计，增加学习路径配置快照或配置变更日志 |
| 考试分配和学习路径考试配置可能不一致 | 学习中心可展示考试，但考试模块仍会按自身分配拒绝未分配用户 | 管理端后续增加考试分配范围提示和冲突校验 |
| 反馈关联版本输入体验偏弱 | 管理员需要复制 GUID，容易输错 | 后续增加同知识版本选择器或从反馈创建修订草稿入口 |
| RAG 评测集初始规模有限 | 指标可运行，但质量代表性依赖用例维护 | 建立评测用例维护流程和发布前回归门禁 |
| NuGet 既有漏洞告警 | 构建和测试会继续输出依赖漏洞提示 | 单独排期升级 `Scriban`、`SQLitePCLRaw`，避免混入本次验收固化范围 |

## 7. MVP-3 完成证据摘要

1. 学习闭环：路径配置、分配过滤、知识权限过滤、阅读记录、个人学习页面均有代码、测试和页面证据。
2. 反馈闭环：知识页可提交反馈，管理页可处理、关联版本、完成和关闭；测试证明不会直接修改已发布知识。
3. RAG 评测闭环：评测用例和运行记录可持久化，命中率、引用准确率、拒答率和越权召回硬失败均有自动测试。
4. 审计追溯：审计脚本覆盖知识反馈、RAG、AI 出题、考试评分、错题和敏感字段扫描，硬门禁通过。
5. 页面可用性：本地 API/Blazor 运行后，Swagger、学习中心、知识反馈页和 Blazor runtime 资源均可达；浏览器可渲染关键页面。
