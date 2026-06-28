# KnowPulse 开发进度

## 2026-06-28

### 本轮完成

- 固化 MVP-3 学习与运营质量闭环交接验收：新增 `docs/mvp-3-acceptance-matrix.md`，统一覆盖学习路径、阅读进度、个人学习中心、知识反馈和 RAG 评测能力。
- 新增 `ops/verify-mvp3.ps1`，串联 MVP-3 静态验收、学习/反馈测试、RAG 评测专项、Blazor Client 构建、审计敏感字段扫描和可选运行态页面烟测。
- 完成 MVP-1 RAG 评测集和评测运行记录最小闭环：新增评测用例、运行记录和逐用例结果模型。
- 新增 RAG 评测应用服务和 `/api/ai/rag/evaluations` API，支持创建/编辑/查询用例、执行评测、查询运行记录。
- RAG 评测运行复用现有 `IRagAnswerService`，覆盖真实检索、PostgreSQL 二次权限校验、拒答和 citation 链路。
- 评测指标落库：命中率、引用准确率、无答案拒答率、越权召回率；越权召回率大于 0 时运行硬失败。
- 新增 `ops/verify-rag-evaluation.ps1` 和 `20260628133051_Mvp1RagEvaluation` 迁移。
- 完成 MVP-3 学习路径最小闭环后端：学习路径、组织分配、必学知识、推荐知识和必考考试模型。
- 完成 MVP-3 个人学习中心最小闭环：新增阅读记录、个人学习状态、Blazor “我的学习”页面和菜单。
- 新增 `LearningKnowledgeReadRecord`，并生成/应用 `20260628085925_Mvp3LearningCenter` 迁移。
- `/learning/my` 页面展示必学知识、推荐知识、必考考试、完成状态和最近阅读时间，支持跳转知识版本预览和考试入口。
- `/knowledge/items` 支持 `itemId` / `versionId` 查询参数，供学习中心直达指定知识版本。
- 新增 `LearningPath`、`LearningPathAssignment`、`LearningPathRequiredKnowledge`、`LearningPathRecommendedKnowledge`、`LearningPathRequiredExam`，并生成 `20260628083615_Mvp3LearningPath` 迁移。
- 新增学习路径应用服务和 REST API，支持创建、编辑、管理端查询、个人查询和停用。
- 新增 `KnowPulse.Learning.Manage` 和 `KnowPulse.Learning.View` 权限，个人查询只返回当前用户按部门、岗位或职级命中的启用路径。
- 个人学习内容中的知识项继续叠加 `KnowledgePermissionScope` 权限过滤，避免通过学习中心绕过知识库可见性。
- 新增 `docs/mvp-3-learning-path-acceptance.md`，记录本轮接口、模型、迁移、验证命令和剩余风险。
- 完成 `KP-MVP2-BE-006` 考试领域模型与迁移。
- 完成 `KP-MVP2-BE-007` 组卷策略和试卷快照。
- 完成 `KP-MVP2-BE-008` 考试计划、分配和开始考试。
- 完成 `KP-MVP2-BE-009` 答题自动保存、提交和幂等控制。
- 完成 `KP-MVP2-BE-010` 客观题自动评分、成绩和错题记录。
- 完成 `KP-MVP2-FE-003` 考试管理和组卷页面。
- 完成 `KP-MVP2-FE-004` 在线答题页和自动保存体验。
- 完成 `KP-MVP2-QA-003` 组卷、快照和考试状态测试。
- 完成 `KP-MVP2-QA-004` 答题页浏览器验收清单。
- 完成 `KP-MVP2-DEVOPS-001` MVP-2 演示考试种子脚本。
- 完成 `KP-MVP2-SEC-001` 考试权限点和审计证据整理。
- 新增 `docs/mvp-2-acceptance-matrix.md`，按 MVP-2 任务记录代码、测试、权限、审计、演示数据和答题页浏览器验收证据。
- 新增 `ops/verify-mvp2.ps1`，默认串联 MVP-2 静态范围检查、过滤测试和审计敏感字段扫描，并支持显式种子数据和演示考试运行态只读检查。
- 新增 `ops/seed-mvp2-demo-data.ps1`，通过本地 API 登录后创建两道已审核题、固定试卷和全员可见考试计划。
- `/exam/management` 页面支持考试计划、分配范围、组卷规则、试卷预览和发布入口。
- `/exam/take` 页面支持可见考试、开始考试、剩余时间、题目作答、自动保存、手动保存、失败重试和提交。
- 修正 `/exam/take` 手动保存和提交按钮的 Razor `disabled` 布尔绑定，避免按钮在非忙碌、未提交状态下仍被 HTML 属性禁用。
- 演示考试 `MVP2-DEMO 考试闭环 20260628141223` 已完成浏览器开始、作答、自动保存和提交触发；数据库确认 attempt 已评分，2 个答案，成绩 20/20，错题 0。
- 完成 `KP-MVP2-AI-002` AI 出题任务模型和执行管道。
- 完成 `KP-MVP2-AI-003` 候选题结构化校验和安全降级。
- 完成 `KP-MVP2-BE-005` AI 出题应用服务和 API。
- 完成 `KP-MVP2-FE-002` AI 候选题生成与审核页面。
- 完成 `KP-MVP2-QA-002` AI 出题安全和引用测试。
- 基于 MVP-1 `IKnowledgeRetriever` 的授权 RAG 检索结果生成候选题，模型上下文只来自二次校验后的 chunk。
- 新增/确认 AI 出题任务、候选题、候选题 citation、prompt 模板和模型调用日志持久化链路。
- 新增/确认任务创建、任务查询、候选题查看、候选题提交审核和失败重试 API，统一走 `KnowPulse.QuestionBank.AiGeneration` 权限入口。
- `/question-bank/ai-candidates` 页面支持创建生成任务、查看任务/候选题列表、展开 citation 来源、展示结构化校验错误和相似题提示，并可将候选题提交审核后执行审核通过或驳回。
- AI 候选题页面在审核通过前明确显示“未入正式题库”；提交审核后展示关联题库草稿状态，并按题目审核权限控制通过/驳回按钮。
- 候选题结果保存在 `AiQuestionCandidate` 隔离区；AI 生成题必须通过提交审核动作转换为题库待审核草稿，不能直接进入正式题库或考试。
- 失败路径覆盖无授权上下文、模型失败、模型坏格式和候选题结构校验失败，并写入任务状态和模型调用日志。
- 结构化校验已覆盖题干、选项、答案、解析、来源 citation、题型和难度；缺字段、非法枚举、不一致枚举或坏答案都会进入 `ValidationFailed`，不能提交审核。
- QA-002 测试覆盖有依据生成、无依据拒绝、越权知识不能出题、坏格式、模型失败，以及 citation 可追溯且不复制完整知识正文。

### 本轮验证记录

- `.\ops\verify-mvp3.ps1 -NoBuild -CheckRuntimePages`：通过。
  - 静态验收通过，确认 MVP-3 模型、迁移、权限、菜单、页面、API、审计脚本和测试入口存在。
  - MVP-3 领域测试 8 通过；MVP-3 EF 应用服务测试 7 通过。
  - RAG 评测专项通过：领域测试 2 通过，应用服务测试 3 通过，API 契约测试 8 通过。
  - Blazor Client 构建通过，0 警告，0 错误。
  - `ops/audit-check.ps1 -FailOnSensitiveHits -Limit 10` 通过，敏感字段扫描硬门禁为 0。
  - `https://localhost:44305/swagger/index.html`、`https://localhost:44307/learning/my`、`https://localhost:44307/knowledge/feedback`、`https://localhost:44307/_framework/dotnet.js` 均返回 HTTP 200。
- `.\ops\start-local.ps1 -HealthRetries 15 -HealthRetryDelaySeconds 4`：通过，PostgreSQL、MinIO、Qdrant、LiteLLM 健康检查通过，DbMigrator 完成。
- `dotnet build .\KnowPulse.slnx --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false -nr:false`：通过。
  - 13 个既有 NuGet 漏洞告警，0 错误。
- `dotnet test .\test\KnowPulse.Domain.Tests\KnowPulse.Domain.Tests.csproj --filter "FullyQualifiedName~RagEvaluationTests" --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false -nr:false`：通过。
  - 2 通过，0 失败，0 跳过。
- `dotnet test .\test\KnowPulse.EntityFrameworkCore.Tests\KnowPulse.EntityFrameworkCore.Tests.csproj --filter "FullyQualifiedName~Mvp1RagEvaluationAppServiceTests" --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false -nr:false`：通过。
  - 3 通过，0 失败，0 跳过。
- `.\ops\verify-rag-evaluation.ps1`：通过。
  - 领域测试 2 通过；应用服务测试 3 通过；API 契约测试 8 通过。
- `.\ops\verify-rag-evaluation.ps1 -NoBuild`：通过。
  - 使用既有编译产物验证，领域测试 2 通过；应用服务测试 3 通过；API 契约测试 8 通过。
- `dotnet test .\KnowPulse.slnx --no-build --no-restore --filter "FullyQualifiedName~Mvp1" -v:m -m:1 -nr:false`：通过。
  - `KnowPulse.Application.Tests` 中 8 个、`KnowPulse.Domain.Tests` 中 2 个、`KnowPulse.EntityFrameworkCore.Tests` 中 23 个 MVP-1 测试通过；总计 33 通过。
- `dotnet ef migrations add Mvp1RagEvaluation --no-build`：通过。
  - 生成 `20260628133051_Mvp1RagEvaluation.cs`、`20260628133051_Mvp1RagEvaluation.Designer.cs`，并更新 `KnowPulseDbContextModelSnapshot.cs`。
- `dotnet ef database update --no-build`：通过。
  - 已应用 `20260628133051_Mvp1RagEvaluation`；PostgreSQL 查询确认 `AppRagEvaluationCases`、`AppRagEvaluationRuns`、`AppRagEvaluationRunCaseResults` 表存在。
- `dotnet build src\KnowPulse.EntityFrameworkCore\KnowPulse.EntityFrameworkCore.csproj --no-restore -m:1 -p:UseSharedCompilation=false`：通过。
  - 2 个既有 `Scriban 7.0.0` NuGet 漏洞告警，0 错误。
- `dotnet ef migrations add Mvp3LearningPath --no-build`：通过。
  - 生成 `20260628083615_Mvp3LearningPath.cs`、`20260628083615_Mvp3LearningPath.Designer.cs`，并更新 `KnowPulseDbContextModelSnapshot.cs`。
- `dotnet ef migrations add Mvp3LearningCenter --no-build`：通过。
  - 生成 `20260628085925_Mvp3LearningCenter.cs`、`20260628085925_Mvp3LearningCenter.Designer.cs`，并更新 `KnowPulseDbContextModelSnapshot.cs`。
- `dotnet ef database update --no-build`：通过。
  - 已应用 `20260628085925_Mvp3LearningCenter`；迁移列表确认包含该迁移。
- `dotnet build src\KnowPulse.Application\KnowPulse.Application.csproj --no-restore -m:1 -p:UseSharedCompilation=false`：通过。
  - 2 个既有 `Scriban 7.0.0` NuGet 漏洞告警，0 错误。
- `dotnet build KnowPulse.slnx --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false -nr:false`：通过。
  - 13 个既有 NuGet 漏洞告警，0 错误。
- `dotnet test test\KnowPulse.Domain.Tests\KnowPulse.Domain.Tests.csproj --filter "FullyQualifiedName~LearningPathTests"`：通过。
  - 4 通过，0 失败，0 跳过。
  - 覆盖学习路径分配目标约束、全员分配目标归一化、停用、配置版本递增和阅读记录完成时间。
- `dotnet test test\KnowPulse.EntityFrameworkCore.Tests\KnowPulse.EntityFrameworkCore.Tests.csproj --filter "FullyQualifiedName~Mvp3LearningPathAppServiceTests"`：通过。
  - 4 通过，0 失败，0 跳过。
  - 覆盖学习路径创建、编辑、查询、停用、部门/岗位/职级分配过滤、知识权限范围二次过滤，以及个人学习阅读状态从未开始到进行中、已完成的闭环。
- `dotnet build .\src\KnowPulse.Blazor.Client\KnowPulse.Blazor.Client.csproj --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false -nr:false`：通过。
  - 0 警告，0 错误。
  - 覆盖 `/learning/my` 页面、学习菜单和知识文档深链查询参数的 Razor 编译。
- 本地运行态抽验：
  - `https://localhost:44305/swagger/index.html`：200。
  - `https://localhost:44307/learning/my`：200。
  - `https://localhost:44307/_framework/dotnet.js`：200，`text/javascript`。
- `dotnet test test\KnowPulse.EntityFrameworkCore.Tests\KnowPulse.EntityFrameworkCore.Tests.csproj --filter "FullyQualifiedName~Mvp2ExamAppServiceTests"`：通过。
  - 5 通过，0 失败，0 跳过。
  - 覆盖题目不足、状态过滤、固定题目只允许已审核题、试卷快照不可变、开始考试、保存幂等、提交幂等、提交后禁止修改、评分幂等、错题记录和过期禁止提交。
- `dotnet test .\KnowPulse.slnx --no-restore --filter "FullyQualifiedName~Mvp2" -v:m -m:1 -nr:false -p:BuildInParallel=false`：通过。
  - `KnowPulse.EntityFrameworkCore.Tests` 中 25 个 MVP-2 测试通过；其他测试程序集无匹配用例。
- `.\ops\verify-mvp2.ps1 -NoBuild`：通过。
  - 静态范围检查、25 个 MVP-2 过滤测试和审计敏感字段扫描均通过。
- `.\ops\verify-mvp2.ps1 -SkipDotnetTests -SkipStatic -SkipAudit -CheckExamRuntime -DemoPlanId 3a221f08-6a7d-053f-396b-87054ead4b71`：通过。
  - `plans=1 paper_questions=2 scored_attempts=1 answers=2 scores=1`。
- `.\ops\audit-check.ps1 -FailOnSensitiveHits -Limit 10`：通过。
  - 审计记录包含考试 `CreatePlanAsync`、`StartAttemptAsync`、`SaveAnswersAsync`、`SubmitAttemptAsync`，敏感字段命中为 0。
- `dotnet build .\KnowPulse.slnx`：通过。
  - 26 个既有 NuGet 漏洞告警，0 错误。
- `dotnet build .\src\KnowPulse.Blazor.Client\KnowPulse.Blazor.Client.csproj --no-restore -v:m -m:1 -p:BuildInParallel=false -nr:false`：通过。
  - 0 警告，0 错误。
  - 覆盖考试管理页面、在线答题页和修正后的按钮禁用绑定。
- `.\ops\start-local.ps1 -HealthRetries 15 -HealthRetryDelaySeconds 4`：通过。
  - PostgreSQL、MinIO、Qdrant、LiteLLM 健康检查通过，DbMigrator 完成。
- `.\ops\seed-mvp2-demo-data.ps1 -ApiBaseUrl https://localhost:44305`：通过。
  - 生成 `PlanId=3a221f08-6a7d-053f-396b-87054ead4b71`、`PaperId=3a221f08-6a32-e530-0327-7b15a94bd997`、2 道题、总分 20。
- `.\ops\verify-mvp2.ps1 -SkipDotnetTests -SkipAudit`：通过。
  - 静态检查确认考试领域模型、迁移、权限点、应用服务、页面、测试和演示脚本均存在并包含关键行为。
- 运行态端点抽验：
  - `https://localhost:44305/swagger`：200。
  - `https://localhost:44307/exam/take`：200。
- 答题页浏览器验收：
  - 登录态打开 `/exam/take`，可见演示考试、点击开始后显示进行中 attempt、2 道题、剩余时间和保存/提交按钮。
  - 选择答案后两题均显示“已答”，状态显示“已保存”，console/page error 为空。
  - 修复后浏览器确认“手动保存”和“提交试卷”均未禁用。
  - 提交动作触发后，数据库只读查询确认 attempt 状态为已评分、2 个答案、1 条成绩、20/20 分、0 条错题。
  - 重启 API/Blazor 后再次打开 `/exam/take`，登录态 admin 可见演示考试和开始按钮，console/page error 为空。
- `dotnet test test\KnowPulse.EntityFrameworkCore.Tests\KnowPulse.EntityFrameworkCore.Tests.csproj --filter "FullyQualifiedName~Mvp2AiQuestionGenerationAppServiceTests"`：通过。
  - 17 通过，0 失败，0 跳过。
  - 覆盖 prompt/模型配置与调用日志、正常候选题生成与 citation、提交审核后仍为 `PendingReview`、无上下文失败、越权向量命中丢弃、模型坏格式失败、模型调用失败和相似题提示。
  - 新增覆盖缺解析、缺来源、缺题型、非法题型、题型不一致、缺难度、非法难度、难度不一致和答案不匹配选项，确认坏输出只能作为失败候选题落库，不能提交审核。
  - 新增覆盖长知识正文 citation：候选题 citation 和提交后的题库来源均保存 `KnowledgeItemId`、`KnowledgeVersionId`、`ChunkId`、短摘录和 SHA-256 hash，且不复制完整 chunk 正文或向量 payload 正文。
- `dotnet test test\KnowPulse.EntityFrameworkCore.Tests\KnowPulse.EntityFrameworkCore.Tests.csproj --filter "FullyQualifiedName~Qa002_Citation_Should_Be_Traceable_And_Not_Copy_Full_Knowledge_Content"`：通过。
  - 1 通过，0 失败，0 跳过。
- `dotnet test test\KnowPulse.Application.Tests\KnowPulse.Application.Tests.csproj --filter "FullyQualifiedName~AiQuestionGenerationApiContractTests"`：通过。
  - 7 通过，0 失败，0 跳过。
  - 覆盖 AI 出题 Controller 根路由、权限策略，以及任务列表/详情、候选题详情、任务创建、失败重试和提交审核 API 路由契约。
- `dotnet build .\src\KnowPulse.Blazor.Client\KnowPulse.Blazor.Client.csproj --no-restore -v:m -m:1 -p:BuildInParallel=false -nr:false`：通过。
  - 0 警告，0 错误。
  - 覆盖 AI 候选题页面 Razor 编译、服务注入、权限检查、表单绑定、候选题状态展示和审核动作按钮。
- `dotnet build .\src\KnowPulse.Blazor\KnowPulse.Blazor.csproj --no-restore -v:m -m:1 -p:BuildInParallel=false -nr:false`：通过。
  - 0 警告，0 错误。
  - 重新生成 Blazor Host 静态资源映射，确认 `_framework/dotnet.js` 运行态返回 `text/javascript` 且非空。
- 本地运行态抽验：
  - `https://localhost:44307/question-bank/ai-candidates`：200。
  - `https://localhost:44305/swagger`：200。
  - 系统 Chrome headless 打开 `https://localhost:44307/question-bank/ai-candidates` 后进入未登录登录页，Blazor 启动无 console error / page error。

### 注意事项

- 本轮后端测试仍输出既有 NuGet 漏洞告警：`Scriban 7.0.0`、`SQLitePCLRaw.lib.e_sqlite3 2.1.11`。这些告警不是 `KP-MVP2-AI-002` / `KP-MVP2-AI-003` / `KP-MVP2-BE-005` / `KP-MVP2-FE-002` / `KP-MVP2-QA-002` 新增失败。
- 当前 AI 出题执行为同步管道；后续如切换后台任务，应保持现有任务状态、候选题隔离区和调用日志契约不变。

## 2026-06-27

### 本轮完成

- 完成 MVP-2 包 A：MVP-1 固化范围内的 S0 收尾，不进入题库、AI 出题、组卷或考试开发。
- 新增 `docs/mvp-1-acceptance-matrix.md`：
  - 按 MVP-1 任务列出验收证据、当前结论和剩余动作。
  - 固化模型 key 缺失、Qdrant 不可达、LiteLLM 不可达、无依据问题和越权命中的预期降级行为。
  - 写入本轮回归命令、验证结果和仍需普通浏览器手工补验的页面。
- 新增 `ops/verify-mvp1.ps1`：
  - 默认串联 MVP-1 过滤测试、RAG provider 边界扫描、Qdrant collection 验证、LiteLLM 配置验证和 RAG 审计敏感字段扫描。
  - 支持 `-EnsureQdrant`、`-LiveLiteLlm`、`-SkipQdrant`、`-SkipAudit`、`-NoBuild` 等本地回归开关。
- 更新 `docs/mvp-1-qa-test-plan.md` 和 `docs/development-environment.md`：
  - 将 MVP-1 过滤测试固定为 `dotnet test .\KnowPulse.slnx --no-restore --filter "FullyQualifiedName~Mvp1"`。
  - 增加提交前必须说明是否影响 MVP-1 RAG 回归范围的要求。
- 更新 `docs/rag-ai-boundary.md`：
  - 补充 MVP-1 RAG 审计字段规则，明确 hash、summary、preview 可保存字段和完整 prompt / 完整正文 / 完整附件内容禁存字段。
- 增强 `ops/audit-check.ps1`：
  - 增加 `-FailOnSensitiveHits`，用于把 ABP 审计和 RAG 审计敏感字段命中作为硬失败。
- 增强 `ops/verify-rag-provider-boundaries.ps1`：
  - 将 SemanticKernel、Microsoft.Extensions.AI、OpenAI、Azure.AI 的 `using` 和 PackageReference 泄漏纳入边界扫描。

### 本轮验证记录

- `.\ops\verify-mvp1.ps1`：通过。
  - MVP-1 过滤测试：20 通过，0 失败，0 跳过；匹配用例集中在 `KnowPulse.EntityFrameworkCore.Tests`，其他测试程序集无 `FullyQualifiedName~Mvp1` 匹配用例。
  - RAG provider boundary check：通过。
  - Qdrant collection check：通过，`knowledge_chunks_1536_mvp1` 存在，向量维度 `1536`，必需 payload index 无缺失。
  - LiteLLM RAG config check：通过，Host/DbMigrator appsettings、LiteLLM 模板和 `.env.example` alias/key 变量一致。
  - RAG audit sensitive field check：通过，ABP 审计参数、实体属性变更、RAG 调用日志和 citation 敏感命中均为 0。
- `.\ops\audit-check.ps1 -FailOnSensitiveHits`：单独执行通过；最近 RAG 调用日志能看到 LiteLLM embedding HTTP 401 被记录为 `embedding` / `search` 失败阶段，未出现敏感字段命中。

### 仍需人工验收

- `/knowledge/items` 索引状态和重建操作页面需要普通浏览器抽验。
- `/knowledge/index-jobs` 失败阶段、失败原因、重试次数和筛选分页需要普通浏览器抽验。
- `/rag/answer` 的回答、引用、置信度、人审提示和无依据拒答需要普通浏览器抽验。

### 注意事项

- 本轮 `dotnet test` 仍输出既有 NuGet 漏洞告警：`Scriban 7.0.0`、`SQLitePCLRaw.lib.e_sqlite3 2.1.11`。这些告警不是包 A 新增失败。
- 本地真实 RAG 页面调用存在 LiteLLM embedding HTTP 401 记录，说明当前模型网关 key/alias 运行态仍需在演示或真实问答前修复；自动化替身测试和静态配置验证已通过。

## 2026-06-26

### 本轮完成

- 修复 Blazor 表单自动化交互问题：MVP-0 主要文本输入和正文输入已使用 `@bind:event="oninput"`，避免浏览器自动化填值后模型仍为空导致表单无效。
- 修复知识版本号显示问题：`Items.razor` 中版本列表和附件版本下拉不再显示字面量 `v@version.VersionNumber`，Chrome 验证显示为 `v1` 和 `v1 - UI版本865207`。
- 补强 `ops/verify-mvp0-static.ps1`：
  - 检查标签表单使用 `oninput` 绑定。
  - 检查知识版本号使用正确 Razor 表达式。
  - 防止 `v@version.VersionNumber` 字面量回归。
- 扩展 `ops/verify-mvp0-runtime.ps1`：
  - 覆盖岗位、职级、用户组织关系的创建和更新。
  - 覆盖知识分类、标签的新增、编辑和删除。
  - 覆盖 Department、Position、Role、User 权限范围创建，并分别验证安全等级。
  - 保持原有知识版本生命周期、附件上传/下载/删除、MinIO 对象检查和审计敏感字段扫描。
- 继续补强 `ops/verify-mvp0-static.ps1`：
  - 检查运行时脚本包含分类/标签编辑删除、组织关系、权限范围多类型覆盖。
  - 检查权限范围页面提供部门、岗位、角色、用户和安全等级控件。
- 使用系统 Chrome 完成登录后 UI 验收补强：
  - 通过本地 Account 登录进入 `https://localhost:44307`。
  - 8 个 MVP-0 页面均在登录态渲染成功，关键控件存在且无新增应用错误：部门、岗位、职级、用户组织关系、知识分类、标签、知识文档、权限范围。
  - 标签页面新增 `UiTagChrome-*` 成功，列表出现新标签。
  - 部门页面新增 `UI部门*` 成功，列表出现新部门。
  - 组织管理页面完成深度 CRUD 验证并清理测试数据：部门、岗位、职级、用户组织关系均通过新增、编辑、删除。
  - 组织 CRUD 验证覆盖数字和布尔字段：部门安全等级 `2 -> 3`，岗位安全等级 `4 -> 5`，职级排序/安全等级 `1/6 -> 2/7`，用户组织关系安全等级 `8 -> 9` 且可取消“主组织关系”。
  - 知识分类页面新增 `UI分类*` 成功，列表出现新分类。
  - 知识文档页面新增 `UI知识865207` 成功，创建 `UI版本865207` 成功，发布后页面显示 `当前发布`。
  - 权限范围页面为 `UI知识865207` 新增“全员”范围成功，列表出现“全员 / 启用”。
- 补齐后台任务预留验收口径：MVP-0 接入 ABP BackgroundJobs EF Core 模块，PostgreSQL 迁移包含 `AbpBackgroundJobs` 表；Hangfire 在 MVP-0 按文档任务拆分作为 PostgreSQL 后台任务存储预留，不单独启动 Dashboard/Worker。
- 补强 `ops/verify-mvp0-static.ps1`：
  - 检查 `AbpBackgroundJobsEntityFrameworkCoreModule` 已接入。
  - 检查 `KnowPulseDbContext` 调用 `ConfigureBackgroundJobs()`。
  - 检查初始迁移包含 `AbpBackgroundJobs` 表。
  - 检查开发环境文档说明 Hangfire 预留存储。
- 使用数据库复核 UI 发布结果：`UI知识865207` 的 `CurrentPublishedVersionId` 已写入，`UI版本865207` 的 `Status=1` 且 `IsCurrentPublished=true`。
- 确认 Codex 内置浏览器失败原因是工具兼容问题：Blazor `_framework/*.wasm`、PDB 等资源由本地 Host 返回 200，但内置浏览器/扩展侧 `fetch` 报失败；系统 Chrome 可正常运行 Blazor。
- 确认本轮系统 Chrome 扩展后续连接异常原因：Codex Chrome Extension 在选中 Chrome Profile 中已安装启用，但 Windows native messaging host registry key 缺失，导致标签列举/新建命令超时；按插件约束不能由 Codex 直接修复 native host。
- 清理本轮临时启动的 `http://localhost:5007` Blazor 验证进程，保留 `https://localhost:44307` 前端运行。
- 补充稳定基础演示数据脚本 `ops/seed-demo-data.ps1`：
  - 通过本地 `admin` 登录和真实 API 幂等创建或更新 `DEMO-*` 部门、岗位、职级、用户组织关系、知识分类、标签、知识文档、发布版本和权限范围。
  - 使用脚本生成的 `artifacts\demo-seed\knowpulse-demo-attachment.txt` 上传演示附件，并执行下载内容校验；不上传用户个人文件。
  - 保留 `ops/verify-mvp0-runtime.ps1` 作为随机运行闭环验证脚本，`ops/seed-demo-data.ps1` 作为可重复演示数据脚本，并可通过 `ops/verify-mvp0.ps1 -WithDemoSeed` 统一调用。
- 补充页面级附件验收前置检查脚本 `ops/check-page-attachment-prereqs.ps1`：
  - 生成安全演示附件，检查 Blazor 知识文档页面、API 登录页、Codex Chrome native host 注册状态。
  - 输出普通浏览器手工上传、下载、删除验收路径，并可通过 `-EnsureDemoSeed` 先补齐 `DEMO-*` 演示数据。
  - `ops/verify-mvp0.ps1` 增加 `-WithPageAttachmentPreflight` 可选入口。

### 本轮验证记录

- `dotnet msbuild .\src\KnowPulse.Blazor.Client\KnowPulse.Blazor.Client.csproj /t:CoreCompile /p:NoRestore=true /p:BuildInParallel=false /m:1 /nr:false /v:m`：通过。
- `dotnet build .\src\KnowPulse.Blazor.Client\KnowPulse.Blazor.Client.csproj --no-restore -v:m -m:1 -p:BuildInParallel=false -nr:false`：通过，Blazor WebAssembly 输出生成成功。
- `dotnet build .\src\KnowPulse.Blazor\KnowPulse.Blazor.csproj --no-restore -v:m -m:1 -p:BuildInParallel=false -nr:false -p:UseAppHost=false -p:DebugType=none -p:DebugSymbols=false`：通过，用于系统 Chrome UI 验证。
- `https://localhost:44307/`：HTTP 200；当前监听进程为本轮重启后的 `dotnet` Blazor Host。
- 系统 Chrome UI 验证：
  - 登录后路由烟测：8/8 页面通过。
  - 标签新增、部门新增、知识分类新增、知识文档新增、版本创建、版本发布、权限范围新增通过。
  - 组织管理深度 CRUD：部门、岗位、职级、用户组织关系新增/编辑/删除均通过；临时数据 `000QADEP547054`、`000QAPOS547054`、`000QALVL547054` 及对应用户组织关系已清理。
  - 表单数字字段使用真实键盘路径验证：部门、岗位、职级和用户组织关系安全等级均按预期保存；用户组织关系“主”标记可创建并可取消。
  - 控制台无新增应用错误；仅保留 ABP `AbpMvcClient` IdentityClientConfiguration 警告。
- PostgreSQL 复核：
  - `UI知识865207` 已有 `CurrentPublishedVersionId`。
  - `UI版本865207`：`Status=1`、`IsCurrentPublished=true`、`PublishedTime=2026-06-26 04:25:16.91787`。
- `.\ops\verify-mvp0.ps1 -WithRuntime`：通过。
  - Domain.Tests：14 通过。
  - EntityFrameworkCore.Tests：7 通过。
  - 依赖健康检查、静态契约检查、端点检查、登录后真实 API/MinIO 闭环和审计检查全部通过。
  - 运行时业务闭环已扩展到组织岗位/职级/用户关系、分类/标签编辑删除，以及权限范围 All/Department/Position/Role/User 多类型创建。
- `.\ops\seed-demo-data.ps1`：通过，并已二次执行验证幂等。
  - 第一次补齐演示附件 `knowpulse-demo-attachment.txt`，第二次输出 `Created: (none)`，全部 `DEMO-*` 数据复用或更新。
  - PostgreSQL 复核 `DEMO-*` 数据：Departments=3、Positions=3、JobLevels=3、Categories=3、Tags=3、Items=3、Versions=3、PermissionScopes=5、Attachments=1。
- `.\ops\verify-mvp0.ps1 -SkipDotnetTests -SkipHealth -SkipEndpoints -SkipAudit -WithDemoSeed`：通过，确认综合验证入口可调用演示数据生成。
- `.\ops\check-page-attachment-prereqs.ps1`：通过；已生成页面补验用演示附件，默认将端点、Chrome native host 和本地文件访问权限异常以 Warn 输出，不阻断普通浏览器手工补验。
- `.\ops\check-page-attachment-prereqs.ps1 -RequireBrowserAutomation`：当前失败；脚本已拆分“本机注册表/manifest 结构可见”和“插件官方 native host 检查失败”两个信号，用于判断是否能执行页面级自动化上传。
- `.\ops\check-page-attachment-prereqs.ps1 -RequireEndpoints -RequireBrowserAutomation`：当前失败，正确暴露本地 HTTPS/curl 握手和 Chrome native host 官方检查问题；用于严格自动化前置门禁。
- `.\ops\verify-mvp0.ps1 -SkipDotnetTests -SkipHealth -SkipEndpoints -SkipAudit -WithPageAttachmentPreflight`：通过，确认综合验证入口可调用页面附件前置检查。

### 仍需人工验收

- Chrome 扩展文件选择器调用 `fileChooser.setFiles` 被本地文件访问权限阻止，未能自动化完成页面级附件上传；API 层的上传、下载、删除、MinIO 写入/删除和审计已经由 `ops/verify-mvp0.ps1 -WithRuntime` 覆盖。
- 文件上传授权已给，API 层已上传脚本生成的演示附件，页面补验文件和步骤已固化在 `ops/check-page-attachment-prereqs.ps1`；本轮系统 Chrome 控制曾能列出标签页，但接管知识文档标签页超时，随后轻量 `openTabs` 重试也超时，仍未完成页面级自动化上传。如需把页面级附件上传也纳入自动化，下一步需要用户确认是否打开所选 Chrome 配置的新窗口重试；若仍失败，需要从 Codex 插件 UI 修复或重装 Chrome 插件，并在 Chrome 扩展详情中启用本地文件访问权限。也可使用普通浏览器手工在知识文档页面选择附件上传、下载、删除。

## 2026-06-25

### 本轮完成

- 验证完整本地依赖环境已可运行：PostgreSQL、MinIO、Qdrant、LiteLLM 均通过健康检查。
- 修正并验证 `ops/start-local.ps1` 的 DbMigrator 执行路径和本地 OpenIddict 客户端 URL 注入。
- 运行 DbMigrator 后确认本地 OpenIddict 客户端已写入：
  - `KnowPulse_Blazor`：`https://localhost:44307/authentication/login-callback`
  - `KnowPulse_Swagger`：`https://localhost:44305/swagger/oauth2-redirect.html`
- 确认默认管理员 `admin / admin@abp.io` 已种子写入，默认角色已授予 `KnowPulse.Organization.*` 和 `KnowPulse.KnowledgeBase.*` 权限。
- 安装 API Host 的 ABP MVC 静态资源：`src/KnowPulse.HttpApi.Host/wwwroot/libs`，修复 Swagger / Account 页面缺少 libs 时的 500 问题。
- 补强 Organization / KnowledgeBase 应用服务显式权限检查：未认证用户先被 `CheckMvpPermissionAsync` 拦截，避免只依赖前端菜单隐藏。
- 修正测试基座中的伪当前用户认证状态，使 `CurrentUser.IsAuthenticated` 在集成测试中为 true。
- 增加 EF Core 应用服务集成测试：覆盖组织基础数据、知识分类/标签、知识版本发布/下架/归档、权限范围可见性、附件上传/下载/删除。
- 扩展 EF Core 应用服务集成测试：覆盖默认全员可见、岗位、角色、指定用户、安全等级拒绝，以及隐藏知识附件下载的后端越权拦截。
- 为 EF Core 测试宿主接入 `Volo.Abp.BlobStoring.FileSystem`，用临时文件目录验证附件应用服务闭环。
- 增加 MVP-0 静态契约检查脚本：`ops/verify-mvp0-static.ps1`。
  - 检查架构文档、Blazor 页面授权、菜单权限绑定、后端应用服务权限守卫、本地 OIDC 配置、审计配置，以及禁用 RAG/AI provider 边界。
- 增加 MVP-0 综合验证脚本：`ops/verify-mvp0.ps1`。
  - 串联 `dotnet test --no-build --no-restore`、依赖健康检查、静态契约检查、本地 API/Blazor 端点检查和审计检查。
- 增加 MVP-0 登录后真实业务闭环验证脚本：`ops/verify-mvp0-runtime.ps1`。
  - 通过 Account 登录页获取 admin cookie 和 XSRF token。
  - 调用真实 API 完成部门创建、知识分类创建、知识条目创建、版本创建/发布/下架/归档、权限范围创建、附件上传/下载/删除。
  - 检查 MinIO 对象写入和删除，检查业务审计动作落库和敏感字段排除。
- 增加 MVP-0 验收矩阵：`docs/mvp-0-acceptance-matrix.md`，逐项记录任务证据、当前结论和剩余手工验收缺口。
- 验证 API Host 和 Blazor 本地端点可访问。
- 更新 `docs/development-environment.md`，补充 `abp install-libs`、OpenIddict 本地 URL 参数、DLL 运行兜底说明、MVP-0 综合验证入口和登录后真实业务闭环验证入口。

### 本轮验证记录

- `docker compose ps`：`knowpulse-postgres`、`knowpulse-minio`、`knowpulse-qdrant`、`knowpulse-litellm` 均为 Up。
- `.\ops\health-check.ps1 -Retries 3 -RetryDelaySeconds 2`：PostgreSQL、MinIO、Qdrant、LiteLLM 全部通过。
- `.\ops\audit-check.ps1`：可执行；运行时业务闭环后可看到 KnowledgeBase / Organization 应用服务动作，敏感字段扫描 `ActionParameters=0`、`EntityPropertyChanges=0`。
- `https://localhost:44305/.well-known/openid-configuration`：HTTP 200。
- `https://localhost:44305/swagger/index.html`：HTTP 200。
- `https://localhost:44305/swagger/v1/swagger.json`：HTTP 200。
- `https://localhost:44307/`、`https://localhost:44307/authentication/login`、`https://localhost:44307/_framework/dotnet.js`：HTTP 200。
- 未登录直接访问 `/api/app/department`、`/api/app/position`、`/api/app/knowledge-category`、`/api/app/knowledge-item`：返回 302 到 `/Account/Login`。
- Swagger 路由检查：
  - `/api/app/department`、`/api/app/position`、`/api/app/job-level`、`/api/app/user-organization-assignment` 均存在。
  - `/api/app/knowledge-category`、`/api/app/knowledge-tag`、`/api/app/knowledge-item` 均存在。
- `dotnet test .\test\KnowPulse.Application.Tests\KnowPulse.Application.Tests.csproj --no-restore -v:m -m:1 -nr:false -p:BuildInParallel=false`：无可发现测试，命令本身通过；Application.Tests 尚未形成有效应用服务测试。
- `dotnet test .\KnowPulse.slnx --no-build --no-restore -v:m -m:1 -nr:false`：通过现有测试程序集。
  - Domain.Tests：14 通过。
  - EntityFrameworkCore.Tests：7 通过。
  - Application.Tests / TestBase：当前无可发现测试。
- `dotnet test .\test\KnowPulse.EntityFrameworkCore.Tests\KnowPulse.EntityFrameworkCore.Tests.csproj -v:m -m:1 -nr:false -p:BuildInParallel=false`：通过，7 个测试通过；包含新增 MVP-0 应用服务集成测试和权限边界扩展。
- `dotnet build .\KnowPulse.slnx --no-restore -v:m -m:1 -p:BuildInParallel=false -nr:false -p:UseAppHost=false`：通过，9 个 NuGet 漏洞告警，0 错误。首次执行时被正在运行的 `KnowPulse.Blazor` 锁定 DLL，停止 44307 Blazor 进程后重跑通过。
- 构建后已使用 DLL 模式恢复 Blazor：`https://localhost:44307/` 和 `https://localhost:44307/_framework/dotnet.js` 均返回 HTTP 200。
- `.\ops\verify-mvp0-static.ps1`：通过。
- `.\ops\verify-mvp0.ps1 -SkipDotnetTests -SkipHealth -SkipStatic -SkipAudit`：通过，端点检查确认 API/Swagger/Blazor 为 HTTP 200，未登录核心 API 为 HTTP 302。
- `.\ops\verify-mvp0.ps1`：通过，测试、依赖健康检查、静态契约检查、端点检查和审计检查全部成功。
- `.\ops\verify-mvp0-runtime.ps1`：通过。
  - admin cookie 登录成功。
  - 创建部门、知识分类、知识条目、知识版本，发布版本，创建 All 权限范围，上传/下载/删除附件，下架和归档版本均成功。
  - MinIO 对象计数：`Before=4`、`AfterUpload=65`、`AfterDelete=4`。
  - 运行时审计动作：9 类业务方法落库，敏感字段扫描 `Hits=0`。
- `.\ops\verify-mvp0.ps1 -WithRuntime`：通过，综合入口已覆盖测试、健康检查、静态契约、端点检查、真实业务闭环和最终审计检查。
- 调整 `ops/verify-mvp0.ps1` 顺序后执行 `.\ops\verify-mvp0.ps1 -SkipDotnetTests -SkipHealth -SkipStatic -SkipEndpoints -WithRuntime`：通过，真实业务闭环后再执行 `audit-check`，可看到最新业务操作审计行。

### 仍需人工验收

- 使用普通浏览器完成 Blazor 登录后的菜单权限、组织管理页面、知识管理页面、权限范围页面验收。
- 登录后通过页面验证知识创建、版本发布、附件上传、下载、删除闭环；API 层和审计层已由 `ops/verify-mvp0-runtime.ps1` 验证通过。
- 当前 Codex 内置浏览器会拦截 Blazor `_framework/dotnet.js` 动态加载，页面视觉验收需使用普通浏览器；Python/HTTPS 检查已确认该静态资源本身返回 200。
- 2026-06-25 再次使用 Codex 内置浏览器复验 Blazor：首页标题为 `KnowPulse.Blazor`，但控制台仍报 `Failed to fetch dynamically imported module: https://localhost:44307/_framework/dotnet.js`，页面停留在加载 spinner。
- 2026-06-25 构建后再次使用 Codex 内置浏览器复验 Blazor：可以看到 Blazor 壳、导航和 `Authorizing...`，但仍无法完成登录后 UI 验收；普通浏览器仍是最终 UI 验收入口。
- Application.Tests 目前仍无可发现测试；应用服务集成测试已放在 EF Core Tests 中运行。
- `git status --ignored` 当前会因历史嵌套 `artifacts/.../artifacts` 输出目录出现路径过长警告；这些目录被 `.gitignore` 忽略，提交前建议清理构建产物。

## 2026-06-24

### 已完成

- 初始化本地 Git 仓库。
- 输出并沉淀架构文档：
  - `docs/technical-architecture.md`
  - `docs/system-architecture.md`
  - `docs/mvp-0-task-breakdown.md`
- 基于 ABP CLI `10.3.0` 生成 KnowPulse 分层单体工程骨架。
- 固定 .NET SDK 基线：`10.0.301`。
- 建立 MVP-0 本地依赖环境：
  - PostgreSQL
  - MinIO
  - Qdrant
  - LiteLLM
- 增加 `.env.example`，并忽略本地 `.env` 和脚手架临时目录。
- 增加本地健康检查脚本：`ops/health-check.ps1`。
- 增加审计日志检查脚本：`ops/audit-check.ps1`。
  - 查询最近审计动作、应用服务方法统计和 `ContentText`/文件流敏感字段扫描。
- 增加本地启动初始化脚本：`ops/start-local.ps1`。
  - 串联 Docker Compose 启动、健康检查和 DbMigrator。
- 修正 LiteLLM 本地镜像默认标签为 `ghcr.io/berriai/litellm:main-stable`，避免不存在的版本标签阻塞 Docker Compose 启动。
- 修正 PostgreSQL 本地镜像默认标签为 `postgres:18-alpine`，匹配本机已有镜像并减少拉取体积。
- `ops/start-local.ps1` 和 `ops/health-check.ps1` 增加 `CoreOnly` 模式，可先验证 PostgreSQL + MinIO + DbMigrator 核心链路。
- `ops/start-local.ps1` 运行 DbMigrator 前显式设置 `ConnectionStrings__Default`，避免从仓库根目录执行 `dotnet run --project` 时读取不到项目目录 `appsettings.json`。
- 按 PostgreSQL 18 镜像要求，将 compose 中 PostgreSQL volume target 从 `/var/lib/postgresql/data` 修正为 `/var/lib/postgresql`。
- 增加开发环境说明：`docs/development-environment.md`。
  - 包含 Docker volume、PostgreSQL 备份/恢复、MinIO/Qdrant 持久化说明。
- 增加模块边界说明：`docs/module-boundaries.md`。
- 增加 RAG/AI 边界与风险闸口：`docs/rag-ai-boundary.md`。
- 增加 RAG/AI 项目内接口占位：`src/KnowPulse.Application.Contracts/RagAI/RagContracts.cs`。
  - 包含 `IKnowledgeIngestionPipeline`、`IKnowledgeVectorStore`、`IKnowledgeRetriever`、`IRagAnswerService`。
  - 仅使用项目内 DTO/BCL 类型，不直接依赖 DataIngestion、VectorData provider 或 Qdrant SDK。
- 建立 Blazor MVP-0 菜单和权限导航占位：
  - 组织管理：部门、岗位、职级。
  - 知识库：知识文档、知识分类、标签、权限范围。
- 增加 MVP-0 权限点：
  - `KnowPulse.Organization.*`
  - `KnowPulse.KnowledgeBase.*`
- 实现 Organization 领域模型：
  - `Department`
  - `Position`
  - `JobLevel`
  - `UserOrganizationAssignment`
- 实现 KnowledgeBase 领域模型：
  - `KnowledgeCategory`
  - `KnowledgeTag`
  - `KnowledgeItem`
  - `KnowledgeVersion`
  - `KnowledgeAttachment`
  - `KnowledgePermissionScope`
- 生成迁移：`20260624154953_Mvp0OrganizationKnowledgeBase`。
- 生成迁移：`20260624163432_EnforceSingleCurrentKnowledgeVersion`，通过过滤唯一索引约束同一知识条目只允许一个当前发布版本。
- 实现 Organization 应用服务和 REST API：
  - 部门 CRUD
  - 岗位 CRUD
  - 职级 CRUD
  - 用户组织关系 CRUD，支持更新用户、部门、岗位、职级、安全等级、主关系和启用状态。
- 实现 KnowledgeBase 应用服务和 REST API：
  - 分类 CRUD
  - 标签 CRUD
  - 知识条目 CRUD
  - 版本创建、发布、下架、归档
  - 权限范围查询、创建、删除
  - 知识列表、详情、版本和附件下载的权限范围可见性校验
  - 附件列表、上传、下载、删除
- 接入 ABP BlobStoring + MinIO：
  - 新增 `KnowledgeAttachmentBlobContainer`
  - Host 配置 MinIO provider 和 `knowpulse-knowledge` bucket
  - 附件 DTO 不返回内部 `BlobName`，下载必须通过应用服务
- 实现 Blazor MVP-0 基础管理页面：
  - 部门、岗位、职级 CRUD。
  - 用户组织关系 CRUD，支持用户 ID、部门、岗位、职级、安全等级、主关系和启用状态。
  - 知识分类、标签 CRUD。
  - 知识文档 CRUD、版本创建、发布、下架、归档，以及版本附件上传、列表、下载、删除。
  - 知识版本发布、下架、归档、知识文档删除和附件删除均增加确认对话框。
  - 附件上传增加上传中按钮状态和进度条反馈。
  - 知识权限范围配置，支持全员、部门、岗位、角色、指定用户和安全等级；部门、岗位、角色、用户均提供选择控件。
  - 知识权限范围空配置时，页面明确提示默认全员可见规则。
  - MVP-0 页面错误提示统一为中文业务文案；403 显示无权限提示，失败状态提供重试入口。
  - 部门、岗位、职级、用户组织关系、知识分类、标签和知识文档主列表接入分页组件。
  - 页面包含加载、空数据、错误、保存中禁用等基础状态。
- 增加 Blazor 附件下载 JS 互操作：`src/KnowPulse.Blazor.Client/wwwroot/knowpulse.js`。
- 补强审计配置：
  - 启用 GET 请求审计以覆盖下载动作。
  - 审计忽略 `CreateKnowledgeVersionDto`、`IRemoteStreamContent`、`RemoteStreamContent`、`KnowledgeVersion`，避免知识正文和文件流进入审计参数或实体变更。
- 增加 MVP-0 QA 验收测试计划：`docs/mvp-0-qa-test-plan.md`。
- 增加领域测试：
  - 部门安全等级约束。
  - 用户组织关系用户变更与安全等级约束。
  - 知识版本状态流转。
  - 知识权限范围目标约束。
  - 知识可见性 evaluator：空范围、部门、安全等级、指定用户、禁用范围。
- 增加 EF Core 测试：验证数据库层拒绝同一知识条目的多个当前发布版本。

### 当前任务状态

| 任务 | 状态 | 说明 |
| --- | --- | --- |
| KP-MVP0-BE-001 | 完成 | ABP 工程骨架、版本基线、模块边界和构建验证已完成 |
| KP-MVP0-BE-002 | 完成 | PostgreSQL 连接、迁移链路和 DbMigrator 已在 Docker PostgreSQL 环境验证通过 |
| KP-MVP0-BE-003 | 完成 | Identity/租户来自 ABP 模板；OpenIddict Blazor/Swagger 客户端、默认管理员和 MVP-0 角色权限种子已验证；系统 Chrome 登录态已看到 admin 和 MVP-0 菜单 |
| KP-MVP0-BE-004 | 完成 | Organization 领域模型和 EF 映射已完成 |
| KP-MVP0-BE-005 | 运行验证完成 | Department、Position、JobLevel、UserOrganizationAssignment 应用服务和 Swagger 路由已验证；未登录 API 返回登录重定向；登录后运行时脚本已创建部门并产生审计 |
| KP-MVP0-BE-006 | 完成 | KnowledgeBase 基础领域模型和 EF 映射已完成 |
| KP-MVP0-BE-007 | 运行验证完成 | 版本状态流转领域方法和 API 已完成；通过过滤唯一索引和 EF 测试保护同一知识条目只允许一个当前发布版本；登录后运行时脚本已验证发布、下架和归档 |
| KP-MVP0-BE-008 | 完成 | 权限范围模型、配置 API、列表/详情/版本/附件下载可见性校验、领域测试和 EF Core 应用服务集成测试已完成；覆盖默认全员、部门、岗位、角色、用户和安全等级 |
| KP-MVP0-BE-009 | 运行验证完成，页面附件补验受浏览器链路限制 | ABP BlobStoring + MinIO provider、附件上传/下载/删除 API 已完成；MinIO 健康检查通过，EF Core 集成测试已用 FileSystem provider 验证附件应用服务闭环；运行时脚本已验证真实 MinIO 对象写入和删除 |
| KP-MVP0-BE-010 | 运行验证完成，页面附件补验受浏览器链路限制 | 知识库基础 API、版本生命周期、权限范围和附件 API 已完成；Swagger 路由、未登录拦截、版本生命周期、权限范围可见性、分类/标签编辑删除和权限范围多类型已验证；运行时脚本已验证登录后 API 闭环 |
| KP-MVP0-BE-011 | 运行验证完成 | ABP 审计表、`UseAuditing()`、GET 审计、敏感类型排除和 `ops/audit-check.ps1` 已配置；运行时脚本已验证 9 类业务方法审计落库且敏感字段扫描为 0 |
| KP-MVP0-FE-001 | 页面验收完成 | Blazor 菜单、权限导航和基础页面壳已完成；Blazor 路由和 `_framework/dotnet.js` 返回 200；系统 Chrome 登录态菜单和 admin 状态已验证 |
| KP-MVP0-FE-002 | 页面验收完成 | 部门、岗位、职级、用户组织关系 CRUD 和主列表分页已完成；系统 Chrome 已验证新增、编辑、删除、数字字段和主组织关系布尔字段 |
| KP-MVP0-FE-003 | 主要验收完成 | 知识分类、标签 CRUD 和主列表分页已完成；系统 Chrome 已验证新增和列表，运行时脚本已验证分类/标签新增、编辑、删除 |
| KP-MVP0-FE-004 | 主要验收完成，页面附件补验受浏览器链路限制 | 知识文档 CRUD、主列表分页、版本生命周期、版本附件上传/列表/下载/删除、关键操作确认和上传中反馈已完成；系统 Chrome 已验证知识新增、版本创建和发布，API/MinIO 已验证附件闭环 |
| KP-MVP0-FE-005 | 主要验收完成 | 权限范围配置页面已完成，支持全员、部门、岗位、角色、指定用户和安全等级；系统 Chrome 已验证全员范围新增，运行时脚本已验证多类型范围创建 |
| KP-MVP0-FE-006 | 基础状态验收完成，页面附件补验受浏览器链路限制 | 已覆盖页面加载、空数据、保存禁用、中文失败提示、403 无权限提示和重试入口；系统 Chrome 8 个登录态页面烟测无错误，附件上传控件需本地文件访问权限后补验 |
| KP-MVP0-DEVOPS-001 | 完成 | Docker Compose 已补充并运行通过；PostgreSQL、MinIO、Qdrant、LiteLLM 均通过健康检查 |
| KP-MVP0-DEVOPS-002 | 完成 | `.env.example`、忽略规则、secrets 条件引用和敏感文件扫描已完成 |
| KP-MVP0-DEVOPS-003 | 完成 | 本地启动、健康检查、审计检查、运行时业务闭环和 MVP-0 综合验证脚本已补充；完整依赖健康检查、端点检查、真实业务闭环和审计检查均已验证 |
| KP-MVP0-DEVOPS-004 | 完成 | `docs/development-environment.md` 已补充 PostgreSQL、MinIO、Qdrant 持久化和备份说明 |
| KP-MVP0-AI-001 | 完成 | 已形成文档边界和项目内接口占位，MVP-0 业务代码不直接依赖 preview provider |
| KP-MVP0-AI-002 | 完成 | Qdrant 与 LiteLLM 已在 Docker Compose 和健康检查脚本中配置，并通过实际健康检查 |
| KP-MVP0-AI-003 | 完成 | 已形成 MVP-1 风险闸口，覆盖 preview 依赖、降级方案、embedding 模型选择和权限越权测试要求 |
| KP-MVP0-QA-001 | 完成 | `docs/mvp-0-qa-test-plan.md` 和 `docs/mvp-0-acceptance-matrix.md` 覆盖 MVP-0 环境、组织、知识、权限、前端状态、文件和审计验收范围 |
| KP-MVP0-QA-002 | 完成 | 测试计划和 EF Core 集成测试覆盖无范围默认全员可见、部门、岗位、角色、用户、安全等级和 API/下载越权用例 |
| KP-MVP0-QA-003 | 完成 | 测试计划包含知识创建、版本创建、发布、下架、归档用例；EF Core 集成测试和运行时脚本已覆盖版本生命周期 |
| KP-MVP0-QA-004 | 完成 | 测试计划包含上传、下载、越权下载、审计检查脚本和审计敏感内容排除用例；EF Core 集成测试已覆盖附件上传、下载、删除和越权下载；运行时脚本已覆盖真实 MinIO 和审计落库 |

### MVP-3 知识反馈和纠错闭环

- 新增 `KnowledgeFeedback` 聚合根和 `AppKnowledgeFeedbacks` 表，覆盖反馈类型、状态流转、来源知识、来源版本、提交人、处理人、处理结论、关闭原因和关联修订版本。
- 新增 `KnowledgeFeedbackType`、`KnowledgeFeedbackStatus`、`KnowledgeBase.Feedback` 权限和本地化菜单文本。
- 新增 `IKnowledgeFeedbackAppService`、`KnowledgeFeedbackAppService` 和 `/api/knowledge/feedback` 显式 HTTP API。
- 用户提交反馈时复用知识文档功能权限，并叠加 `KnowledgePermissionScope` 业务可见性校验。
- 管理员处理反馈时只更新反馈状态和关联版本，不直接修改已发布知识版本正文或发布状态。
- 新增 Blazor `/knowledge/feedback` 管理页，支持筛选、查看、接手处理、关联版本、标记已处理和关闭。
- 知识文档页新增当前知识/版本的反馈提交入口。
- 审计脚本 `ops/audit-check.ps1` 增加知识反馈生命周期、反馈审计动作和敏感字段扫描。
- 验收记录：`docs/mvp-3-knowledge-feedback-acceptance.md`。
- EF 迁移：`20260628100956_Mvp3KnowledgeFeedback`，已应用到本地 PostgreSQL 并确认 `AppKnowledgeFeedbacks` 表存在。

### 验证记录

- `docker compose config`：通过。
- `dotnet restore .\KnowPulse.slnx --disable-parallel -v:m`：通过，需非沙箱网络访问 NuGet。
- `dotnet build .\src\KnowPulse.Blazor.Client\KnowPulse.Blazor.Client.csproj --no-restore -v:m -m:1 -p:BuildInParallel=false -nr:false`：通过，0 警告，0 错误。
- `dotnet build .\KnowPulse.slnx --no-restore -v:m -m:1 -p:BuildInParallel=false -nr:false`：通过，9 个 NuGet 漏洞告警，0 错误。
- `dotnet test .\KnowPulse.slnx --no-build --no-restore -v:m -m:1 -nr:false`：通过。
  - Domain.Tests：12 通过。
  - EntityFrameworkCore.Tests：4 通过；2026-06-25 已扩展为 7 通过。
  - Application.Tests / TestBase：当前模板无可发现测试。
- RAG/AI 接口占位加入后已重新执行解决方案构建通过；完整测试重跑被当前 Codex 审批层额度限制拒绝，未再次执行。
- 权限范围角色/用户选择控件加入后，普通沙箱执行 `dotnet build .\src\KnowPulse.Blazor.Client\KnowPulse.Blazor.Client.csproj --no-restore -v:m -m:1 -p:BuildInParallel=false -nr:false`：Domain.Shared、Application.Contracts、HttpApi.Client 和 Blazor.Client 程序集生成完成，随后在 WebAssembly 资产阶段因 MSBuild 无法创建 `ComputeWasmBuildAssets` 任务宿主失败；未发现 Razor/C# 编译错误，但本轮未得到完整 Blazor Client 构建通过结果。
- 知识版本确认对话框和上传中状态加入后，再次执行 `dotnet build .\src\KnowPulse.Blazor.Client\KnowPulse.Blazor.Client.csproj --no-restore -v:m -m:1 -p:BuildInParallel=false -nr:false`，仍在 WebAssembly 资产阶段因 `ComputeWasmBuildAssets` 任务宿主创建失败中断；随后执行 `dotnet msbuild .\src\KnowPulse.Blazor.Client\KnowPulse.Blazor.Client.csproj /t:CoreCompile /p:NoRestore=true /p:BuildInParallel=false /m:1 /nr:false /v:m` 通过。
- 权限范围空配置默认规则补充后，已静态检查页面文案和 QA 用例；本轮未重跑 Blazor Client 构建。
- FE-006 失败、无权限和重试状态补强后，执行 `dotnet msbuild .\src\KnowPulse.Blazor.Client\KnowPulse.Blazor.Client.csproj /t:CoreCompile /p:NoRestore=true /p:BuildInParallel=false /m:1 /nr:false /v:m` 通过。
- 增加 `ops/audit-check.ps1` 后，已执行 PowerShell 静态解析检查；2026-06-25 已补充真实审计查询验证。
- 增加 `ops/start-local.ps1` 后，已执行 PowerShell 静态解析检查；2026-06-25 已补充真实启动链路和健康检查验证。
- MVP-3 知识反馈闭环加入后，执行 `dotnet build .\KnowPulse.slnx --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false -nr:false` 通过，13 个既有 NuGet 漏洞告警，0 错误。
- MVP-3 知识反馈迁移已执行 `dotnet ef migrations add Mvp3KnowledgeFeedback --no-build`；首次 `database update --no-build` 因迁移未重新编译误判最新，重新构建 EF Core 项目后已应用 `20260628100956_Mvp3KnowledgeFeedback`，并通过 PostgreSQL 查询确认 `AppKnowledgeFeedbacks` 表存在。
- MVP-3 知识反馈领域测试：`dotnet test .\test\KnowPulse.Domain.Tests\KnowPulse.Domain.Tests.csproj --filter "FullyQualifiedName~KnowledgeFeedbackTests" --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false -nr:false`，4 通过。
- MVP-3 知识反馈应用服务测试：`dotnet test .\test\KnowPulse.EntityFrameworkCore.Tests\KnowPulse.EntityFrameworkCore.Tests.csproj --filter "FullyQualifiedName~Mvp3KnowledgeFeedbackAppServiceTests" --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false -nr:false`，3 通过。
- MVP-3 知识反馈审计检查：`.\ops\audit-check.ps1 -FailOnSensitiveHits -Limit 10` 通过；反馈生命周期当前为空态，反馈敏感字段扫描 0 命中。
- MVP-3 知识反馈页面烟测：API Host 和 Blazor 已启动，`GET https://localhost:44305/swagger/index.html` 与 `GET https://localhost:44307/knowledge/feedback` 均返回 200。
- AI-003 风险闸口补齐后，执行 `rg -n "DataIngestion|VectorData|Qdrant|LiteLLM|Microsoft.Extensions.AI|SemanticKernel|Agent Framework|OpenAI|Azure.AI" src test -g "*.cs" -g "*.csproj"` 无命中，确认 MVP-0 业务代码未直接引入禁用 RAG/AI provider。
- Blazor 主列表分页接入后，执行 `dotnet msbuild .\src\KnowPulse.Blazor.Client\KnowPulse.Blazor.Client.csproj /t:CoreCompile /p:NoRestore=true /p:BuildInParallel=false /m:1 /nr:false /v:m` 通过。
- 首次执行 `.\ops\start-local.ps1` 时 Docker 可访问，但 LiteLLM `ghcr.io/berriai/litellm:v1.75.0-stable` 标签不存在导致启动失败；已用 `docker manifest inspect` 验证 `main-stable` 可用并修正默认配置。
- 用户组织关系领域测试加入后，普通沙箱执行 `dotnet test .\test\KnowPulse.Domain.Tests\KnowPulse.Domain.Tests.csproj --no-restore -v:m -m:1 -nr:false` 未完成：构建写入 `test\KnowPulse.Domain.Tests\obj\Debug\net10.0\*.cache` 被拒绝。改用独立中间输出目录后，NuGet 漏洞数据访问因网络受限告警，且 `artifacts\obj-domain-tests\*.tmp` 仍被拒绝写入；本轮未得到新的 Domain.Tests 结果。
- 本轮测试失败遗留的 `src/*/artifacts`、`test/*/artifacts` 目录被 `.gitignore` 忽略；普通沙箱清理时仍被访问拒绝阻断，需后续在权限正常的本机 shell 中清理。

### 待验证

以下为 2026-06-24 遗留清单；环境启动、健康检查、DbMigrator、Swagger 路由和未登录 API 授权拦截已在 2026-06-25 验证通过。

- 登录 Blazor 后验证权限菜单与页面授权。
- 登录后通过页面或 Swagger 验证 Organization / KnowledgeBase 业务 API 的 CRUD 闭环。
- 登录后验证知识版本附件上传、下载和删除闭环，并复查审计日志。

### 注意事项

- ABP CLI 生成阶段的 bundling 步骤曾报 `src\KnowPulse.Blazor.Client\wwwroot\index.html file could not be found`，工程文件已生成并复制到仓库根目录，后续以实际 `dotnet build` 为准。
- Docker Desktop 不可用的问题为 2026-06-24 历史阻塞；2026-06-25 已完成 Docker Compose 运行和健康检查验证。
- NuGet 当前告警：
  - `Scriban 7.0.0`：高危，来自 ABP 依赖链。
  - `SQLitePCLRaw.lib.e_sqlite3 2.1.11`：高危，出现在 EF Core 测试项目依赖链。
- MVP-0 不实现真实 RAG、真实向量写入和 AI 出题。
