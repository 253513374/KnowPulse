# KnowPulse MVP-3 学习与运营质量闭环验收矩阵

版本：v0.1
日期：2026-06-28
范围：学习路径、阅读进度、个人学习中心、知识反馈、RAG 评测集和评测运行记录

## 1. 验收结论

MVP-3 已形成可交接的最小闭环：

- 管理端可配置岗位/部门/职级/全员学习路径，绑定必学知识、推荐知识和必考考试。
- 个人学习中心只展示当前用户被分配且有知识可见权限的内容，并记录阅读/完成状态。
- 知识反馈闭环支持用户提交、管理员处理、关闭和关联修订版本，反馈不直接修改已发布知识。
- RAG 评测支持可重复运行的用例、运行记录、命中率、引用准确率、无答案拒答率和越权召回率，越权召回是硬失败指标。
- 权限、审计、数据追溯和页面路由均有自动或半自动验证入口。

不纳入本轮：复杂运营报表、AI 推荐、移动端专项适配、MVP-4 生产化治理。

## 2. 验收矩阵

| 验收项 | 交付证据 | 自动验证 | 当前结论 | 剩余风险 |
| --- | --- | --- | --- | --- |
| 学习路径数据模型 | `LearningPath`、`LearningPathAssignment`、`LearningPathRequiredKnowledge`、`LearningPathRecommendedKnowledge`、`LearningPathRequiredExam` | `LearningPathTests`、`Mvp3LearningPathAppServiceTests`、EF 迁移检查 | 已具备 | 学习路径配置只保存当前版本号，不保存完整历史快照 |
| 阅读进度数据追溯 | `LearningKnowledgeReadRecord` 唯一索引按路径、用户、知识和版本记录 | `My_Learning_Should_Record_Read_And_Completion_Status` | 已具备 | 阅读完成仍为用户主动标记，未做阅读时长或滚动深度判断 |
| 个人学习中心权限 | `GetMyListAsync`、`GetMyAsync`、`MarkKnowledgeReadAsync` 叠加分配规则和 `KnowledgePermissionScope` | `My_List_Should_Only_Return_Active_Paths_Assigned_By_Department_Position_Or_JobLevel`、`My_List_Should_Filter_Knowledge_Content_By_Knowledge_Permission_Scopes` | 已具备 | 考试入口仍由考试模块二次校验，学习路径只提示不可参加 |
| 学习中心页面 | `/learning/my`、学习菜单、知识详情深链、考试入口跳转 | Blazor Client 构建、运行态路由烟测 | 已具备 | 登录态浏览器数据态仍建议用真实账号补充抽验 |
| 知识反馈数据模型 | `KnowledgeFeedback`、反馈类型、状态流转、来源版本、处理人、处理结论、关联修订版本 | `KnowledgeFeedbackTests`、`Mvp3KnowledgeFeedbackAppServiceTests`、EF 迁移检查 | 已具备 | 管理页关联版本当前为 GUID 输入，易用性后续可增强 |
| 反馈不直接改已发布知识 | `KnowledgeFeedbackAppService` 只更新反馈聚合；修订仍走知识版本流程 | `Should_Submit_Feedback_Without_Modifying_Published_Version`、静态检查 `_versionRepository` 不写入 | 已具备 | 仍需要运营流程约束管理员先创建修订草稿再关联 |
| 反馈管理页面 | `/knowledge/feedback` 支持筛选、查看、接手、关联版本、完成和关闭 | Blazor Client 构建、运行态路由烟测 | 已具备 | 登录态浏览器交互验收建议在交付前用管理员账号补一轮 |
| RAG 评测用例 | `RagEvaluationCase` 保存问题、期望引用、期望拒答、权限上下文和禁止引用 | `Mvp1RagEvaluationTests`、`Mvp1RagEvaluationAppServiceTests`、API 契约测试 | 已具备 | 真实评测质量依赖后续持续维护用例集 |
| RAG 评测运行记录 | `RagEvaluationRun`、`RagEvaluationRunCaseResult` 保存指标和逐用例结果 | `ops/verify-rag-evaluation.ps1` | 已具备 | 本轮为项目内最小闭环，未接入外部评测平台 |
| 越权召回硬失败 | `UnauthorizedRecallRate`、`UnauthorizedRecallCount`、`HardFailed` | `RunAsync_Should_Hard_Fail_When_Unauthorized_Recall_Detected` | 已具备 | 只能覆盖用例声明的 forbidden citations，需持续补充负例 |
| 审计和敏感字段保护 | `ops/audit-check.ps1` 覆盖 RAG、AI 出题、考试和知识反馈敏感字段扫描 | `ops/audit-check.ps1 -FailOnSensitiveHits -Limit 10` | 已具备 | 审计动作丰富度依赖实际运行态用户操作量 |
| 综合验收入口 | `ops/verify-mvp3.ps1` 串联静态检查、MVP-3 测试、RAG 评测、Blazor 构建、审计和可选页面烟测 | `.\ops\verify-mvp3.ps1` | 已具备 | 页面烟测需要本地 API/Blazor 已启动 |

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
| `POST` | `/api/knowledge/feedback` | `KnowPulse.KnowledgeBase.Items` | 用户提交反馈 |
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

## 4. 验证记录

| 验证项 | 命令 | 结果 |
| --- | --- | --- |
| 本地依赖和迁移 | `.\ops\start-local.ps1 -HealthRetries 15 -HealthRetryDelaySeconds 4` | 通过；PostgreSQL、MinIO、Qdrant、LiteLLM 健康检查通过，DbMigrator 完成 |
| MVP-3 综合验收 | `.\ops\verify-mvp3.ps1 -NoBuild -CheckRuntimePages` | 通过；静态验收、MVP-3 领域测试 8 个、MVP-3 EF 应用服务测试 7 个、RAG 评测专项、Blazor Client 构建、审计敏感字段扫描和页面烟测均通过 |
| RAG 评测专项 | `.\ops\verify-rag-evaluation.ps1 -NoBuild` | 已由综合验收脚本串联通过；领域测试 2 个、应用服务测试 3 个、API 契约测试 8 个 |
| Blazor 页面烟测 | `.\ops\verify-mvp3.ps1 -SkipStatic -SkipDotnetTests -SkipRagEvaluation -SkipBlazorBuild -SkipAudit -CheckRuntimePages` | 通过；Swagger、`/learning/my`、`/knowledge/feedback`、`/_framework/dotnet.js` 均 HTTP 200 |
| 解决方案构建 | `dotnet build .\KnowPulse.slnx --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false -nr:false` | 通过；13 个既有 NuGet 漏洞告警，0 错误 |

说明：第一次运行 `.\ops\verify-mvp3.ps1 -NoBuild -CheckRuntimePages` 时，API/Blazor 端口未启动导致页面烟测失败；启动本地 Host 后复跑综合脚本已通过。页面烟测要求本地 API 和 Blazor Host 正在监听 `https://localhost:44305` 和 `https://localhost:44307`。

## 5. 权限、审计、追溯和页面可用性

| 检查面 | 当前设计 | 验收方式 |
| --- | --- | --- |
| 学习内容可见性 | 个人接口先校验学习路径分配，再过滤知识权限范围 | 应用服务测试和 `ops/verify-mvp3.ps1` 静态检查 |
| 阅读追溯 | 读记录保存用户、路径、知识、版本、最近阅读、完成时间和阅读次数 | 应用服务测试和 EF 唯一索引检查 |
| 反馈追溯 | 反馈保存来源知识/版本、提交人、处理人、处理时间、关联修订版本和关闭原因 | 领域测试、应用服务测试和审计脚本 |
| 反馈修订边界 | 反馈处理不写入 `KnowledgeVersion`，只关联版本 ID | 应用服务测试和静态检查 |
| RAG 评测追溯 | 运行记录保存操作者、开始/完成时间、整体指标和逐用例结果 | RAG 评测测试和迁移检查 |
| 越权召回 | 禁止引用命中会导致用例失败，运行 `HardFailed=true` | RAG 评测领域/应用服务测试 |
| 页面可用性 | `/learning/my`、`/knowledge/feedback` 具备加载、空态、错误和无权限提示 | Blazor 构建、运行态 HTTP 烟测、人工登录态补验 |

## 6. 剩余风险

| 风险 | 影响 | 建议 |
| --- | --- | --- |
| 登录态页面数据态未完全自动化 | HTTP 200 和 Razor 编译能证明路由/构建，不能覆盖所有真实登录交互 | 交付前用普通浏览器账号抽验学习路径有数据、无数据、无权限和反馈处理动作 |
| 学习路径配置缺少历史快照 | 后续审计只能通过路径 `ConfigVersion` 和读记录回溯到版本 ID | 下一阶段如要强审计，增加学习路径配置快照或配置变更日志 |
| 考试分配和学习路径考试配置可能不一致 | 学习中心可展示考试，但考试模块仍会按自身分配拒绝未分配用户 | 管理端后续增加考试分配范围提示和冲突校验 |
| 反馈关联版本输入体验偏弱 | 管理员需要复制 GUID，容易输错 | 后续增加同知识版本选择器或从反馈创建修订草稿入口 |
| RAG 评测集初始规模有限 | 指标可运行，但质量代表性依赖用例维护 | 下一阶段建立评测用例维护流程和发布前回归门禁 |
| NuGet 既有漏洞告警 | 构建/测试会继续输出 `Scriban`、`SQLitePCLRaw` 告警 | 单独排期依赖升级，不并入 MVP-4 生产化任务启动 |

## 7. 下一阶段建议

1. 将 `ops/verify-mvp3.ps1 -NoBuild` 作为 MVP-3 交付前固定回归入口。
2. 使用真实管理员和普通用户账号补一次浏览器手工验收，记录数据态、空态、无权限态和反馈处理态。
3. 为 RAG 评测补充最小基线用例集，至少覆盖有答案、无答案、部门越权、岗位越权和安全等级越权。
4. 在进入生产化前再评估学习路径配置快照、反馈处理 SLA 和评测结果趋势报表；本轮不启动这些 MVP-4 范围。
