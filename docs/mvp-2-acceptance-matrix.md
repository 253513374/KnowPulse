# KnowPulse MVP-2 验收矩阵和完成记录

版本：v0.2
日期：2026-06-28
来源：`docs/post-mvp-1-task-breakdown.md`、当前仓库实现、自动化测试、运行态演示数据和浏览器验收记录

## 1. 验收口径

MVP-2 验收目标是完成题库、AI 出题、人审入库和考试闭环：

- 正式题库支持手工题、AI 候选题、人审流、题目来源和可考试状态。
- AI 生成题必须先进入候选区，再提交审核成为待审核题；审核通过前不得进入正式考试。
- 考试计划可以按全员、部门、岗位或指定用户分配。
- 组卷支持岗位、知识点、题型、难度、数量和固定题目；生成的试卷必须保存题干、选项、答案、解析、知识点和来源快照。
- 用户可以查看可参加考试、开始考试、自动保存、手动保存和提交。
- 提交必须幂等；提交后不得继续修改答案。
- 客观题自动评分，生成成绩、通过状态和错题记录；重复评分不能产生重复成绩或重复错题。
- 权限点、审计、演示数据和答题页浏览器验收必须有可复查证据。

不纳入 MVP-2 的内容：主观题人工阅卷、复杂防作弊、学习路径、BI 报表、考试高峰独立扩容和移动端离线答题。

## 2. 当前验证命令

| 验证项 | 命令 |
| --- | --- |
| MVP-2 综合回归 | `.\ops\verify-mvp2.ps1` |
| MVP-2 静态范围检查 | `.\ops\verify-mvp2.ps1 -SkipDotnetTests -SkipAudit` |
| MVP-2 过滤测试 | `dotnet test .\KnowPulse.slnx --no-restore --filter "FullyQualifiedName~Mvp2" -v:m -m:1 -nr:false -p:BuildInParallel=false` |
| 题库领域定向测试 | `dotnet test .\test\KnowPulse.Domain.Tests\KnowPulse.Domain.Tests.csproj --no-restore --filter "FullyQualifiedName~QuestionTests" -v:m -m:1 -nr:false -p:BuildInParallel=false` |
| 题库应用服务定向测试 | `dotnet test .\test\KnowPulse.EntityFrameworkCore.Tests\KnowPulse.EntityFrameworkCore.Tests.csproj --no-restore --filter "FullyQualifiedName~Mvp2QuestionBankAppServiceTests" -v:m -m:1 -nr:false -p:BuildInParallel=false` |
| AI 出题应用服务定向测试 | `dotnet test .\test\KnowPulse.EntityFrameworkCore.Tests\KnowPulse.EntityFrameworkCore.Tests.csproj --no-restore --filter "FullyQualifiedName~Mvp2AiQuestionGenerationAppServiceTests" -v:m -m:1 -nr:false -p:BuildInParallel=false` |
| AI 出题 API 契约测试 | `dotnet test .\test\KnowPulse.Application.Tests\KnowPulse.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~AiQuestionGenerationApiContractTests" -v:m -m:1 -nr:false -p:BuildInParallel=false` |
| 考试闭环定向测试 | `dotnet test .\test\KnowPulse.EntityFrameworkCore.Tests\KnowPulse.EntityFrameworkCore.Tests.csproj --filter "FullyQualifiedName~Mvp2ExamAppServiceTests"` |
| Blazor Client 构建 | `dotnet build .\src\KnowPulse.Blazor.Client\KnowPulse.Blazor.Client.csproj --no-restore -v:m -m:1 -p:BuildInParallel=false -nr:false` |
| 演示考试种子数据 | `.\ops\seed-mvp2-demo-data.ps1 -ApiBaseUrl https://localhost:44305` |
| 演示考试运行态闭环只读检查 | `.\ops\verify-mvp2.ps1 -SkipDotnetTests -SkipStatic -SkipAudit -CheckExamRuntime -DemoPlanId <PlanId>` |
| 审计敏感字段扫描 | `.\ops\audit-check.ps1 -FailOnSensitiveHits` |

说明：`verify-mvp2.ps1` 默认执行静态范围检查、MVP-2 过滤测试和审计敏感字段扫描。静态检查覆盖题库、AI 出题、考试模型、应用服务、接口契约、页面、迁移、权限常量、权限定义和关键测试用例。`-SeedDemoData` 会写入演示题目和考试计划，因此只在需要刷新演示数据时显式传入。`-CheckExamRuntime` 为只读数据库检查，用于确认演示考试存在试卷快照、已评分作答、答案和成绩。

## 3. 任务到证据矩阵

| 任务 | 验收证据 | 当前结论 | 剩余动作 |
| --- | --- | --- | --- |
| KP-MVP2-BE-001 实现题库领域模型与迁移 | `src/KnowPulse.Domain/QuestionBank/*`、`Mvp2QuestionBank` 迁移、`Mvp2QuestionBankAppServiceTests` | 已完成 | 后续题型扩展时补充领域测试 |
| KP-MVP2-BE-002 实现题库应用服务和 REST API | `QuestionAppService`、题库 Controller、题库页面 | 已完成 | 无 |
| KP-MVP2-BE-003 实现题目审核流和正式入库规则 | `SubmitReviewAsync`、`ApproveAsync`、`RejectAsync`、`ReturnForRevisionAsync`、`QuestionStatus.Approved` 入考过滤 | 已完成 | 无 |
| KP-MVP2-BE-004 实现题目知识点、来源引用和版本快照 | 题目知识点、来源模型；考试试卷 `ExamPaperQuestion` 快照字段 | 已完成 | 无 |
| KP-MVP2-AI-001 建立 AI 出题 prompt 模板、版本和模型调用日志 | `AiQuestionPromptTemplate`、`AiQuestionModelCallLog`、AI 出题测试 | 已完成 | 无 |
| KP-MVP2-AI-002 实现 AI 出题任务模型和执行管道 | `AiQuestionGenerationTask`、候选题生成管道、`Mvp2AiQuestionGenerationAppServiceTests` | 已完成 | 后台任务化属于后续演进 |
| KP-MVP2-AI-003 实现候选题结构化校验和安全降级 | 候选题校验测试覆盖坏格式、越权、无依据、模型失败和缺字段 | 已完成 | 无 |
| KP-MVP2-AI-004 实现相似题检测 | `QuestionSimilarityCheck`、相似题提示测试 | 已完成 | 可后续替换为向量相似度 |
| KP-MVP2-BE-005 暴露 AI 出题应用服务和 API | `AiQuestionGenerationController`、`AiQuestionGenerationApiContractTests` | 已完成 | 无 |
| KP-MVP2-FE-001 实现题库管理页面 | `Pages/QuestionBank/*` | 已完成 | 普通浏览器可继续补充题库 CRUD 人工录像或截图 |
| KP-MVP2-FE-002 实现 AI 候选题生成与审核页面 | `Pages/QuestionBank/AiCandidates.razor`、Blazor 构建、运行态页面 200 | 已完成 | 无 |
| KP-MVP2-BE-006 实现考试领域模型与迁移 | `ExamPlan`、`ExamPaper`、`ExamPaperQuestion`、`ExamAssignment`、`ExamAttempt`、`ExamAnswer`、`ExamScore`、`WrongQuestionRecord`、`20260627152649_Mvp2ExamLoop` | 已完成 | 无 |
| KP-MVP2-BE-007 实现组卷策略和试卷快照 | `ExamAppService.PreviewPaperAsync`、`CreatePlanAsync`、规则组卷、固定题目、`QuestionStatus.Approved` 过滤、快照不可变测试 | 已完成 | 无 |
| KP-MVP2-BE-008 实现考试计划、分配和开始考试 | `CreatePlanAsync`、`GetMyPlanListAsync`、`StartAttemptAsync`、全员/部门/岗位/用户分配、次数限制 | 已完成 | 无 |
| KP-MVP2-BE-009 实现答题自动保存、提交和幂等控制 | `SaveAnswersAsync`、`SubmitAttemptAsync`、答案保存幂等键、提交幂等键、提交后禁止修改测试 | 已完成 | 无 |
| KP-MVP2-BE-010 实现客观题自动评分、成绩和错题记录 | `GradeAttemptCoreAsync`、`ExamScore`、`WrongQuestionRecord`、评分幂等测试 | 已完成 | 主观题评分不进 MVP-2 |
| KP-MVP2-FE-003 实现考试管理和组卷页面 | `Pages/Exam/Management.razor`，包含考试计划、分配范围、组卷规则、试卷预览和发布入口 | 已完成 | 无 |
| KP-MVP2-FE-004 实现在线答题页和自动保存体验 | `Pages/Exam/Take.razor`，包含题目列表、剩余时间、答案编辑、自动保存、手动保存、失败重试和提交入口 | 已完成 | 无 |
| KP-MVP2-QA-001 编写题库领域和审核流测试 | `Mvp2QuestionBankAppServiceTests` | 已完成 | 无 |
| KP-MVP2-QA-002 编写 AI 出题安全和引用测试 | `Mvp2AiQuestionGenerationAppServiceTests`、`AiQuestionGenerationApiContractTests` | 已完成 | 无 |
| KP-MVP2-QA-003 编写组卷、快照和考试状态测试 | `Mvp2ExamAppServiceTests` 覆盖题目不足、状态过滤、快照不可变、开始、保存、提交、重复提交、评分幂等和过期禁止提交 | 已完成 | 无 |
| KP-MVP2-QA-004 编写答题页浏览器验收清单 | 本文第 6 节验收清单；本轮已完成 `/exam/take` 浏览器验收 | 已完成 | 后续可补充自动化 E2E |
| KP-MVP2-DEVOPS-001 扩展种子数据和演示考试脚本 | `ops/seed-mvp2-demo-data.ps1`、`ops/verify-mvp2.ps1 -CheckExamRuntime` | 已完成 | 无 |
| KP-MVP2-SEC-001 补充考试和题库权限点与审计检查 | `KnowPulsePermissions.QuestionBank.*`、`KnowPulsePermissions.Exam.*`、权限定义、服务端授权、菜单授权、页面授权、`audit-check.ps1 -FailOnSensitiveHits` | 已完成 | 无 |

## 4. 考试闭环自动化测试覆盖

| 测试 | 覆盖点 |
| --- | --- |
| `Paper_Generation_Should_Filter_Status_And_Preserve_Question_Snapshot` | 题目不足失败、只取已审核题、题库修改后试卷快照不可变 |
| `Fixed_Paper_Generation_Should_Reject_Not_Approved_Questions` | 草稿、待审、驳回、停用、归档题均不能进入固定试卷 |
| `Attempt_Should_Start_Save_Submit_And_Keep_Submit_Idempotent` | 开始考试、恢复进行中作答、保存幂等、提交幂等、提交后禁止修改、成绩唯一 |
| `Scoring_Should_Record_Wrong_Questions_And_Remain_Idempotent` | 单选、多选、判断、填空客观评分，生成错题，重复评分不重复错题 |
| `Expired_Attempt_Should_Reject_Save_And_Submit` | 已过期作答不能保存或提交 |

## 5. 权限和审计证据

| 项目 | 证据 |
| --- | --- |
| 题库权限 | `KnowPulsePermissions.QuestionBank.Questions`、`Reviews`、`AiGeneration`；题库服务、Controller、菜单和页面按权限控制 |
| 考试权限 | `KnowPulsePermissions.Exam.Manage`、`Participate`、`Results`；考试服务、接口契约、菜单和页面按权限控制 |
| 服务端授权 | `IExamAppService` 和 `ExamAppService` 对管理、参加和成绩查询分别检查权限 |
| 页面授权 | `/exam/management` 使用 `Exam.Manage`，`/exam/take` 使用 `Exam.Participate` |
| 审计扫描 | `ops/audit-check.ps1 -FailOnSensitiveHits` 扫描 ABP 审计参数、实体属性变更、RAG 调用日志、RAG citation、AI 出题模型调用日志、AI 候选题 citation 和题目来源摘录敏感字段 |
| 运行态审计 | 本轮演示题目创建、审核、考试计划创建、开始考试、保存和提交均通过 ABP 应用服务/API 路径执行，可在 `AbpAuditLogs` / `AbpAuditLogActions` 查询 |
| MVP-2 审计只读输出 | `audit-check.ps1` 输出 `Recent AI question model calls`、`Question review trail`、`Exam scoring closure` 和 `MVP-2 sensitive field scan`；当前本地运行库有题目审核和考试评分记录，AI 出题运行态表本轮未产生真实模型调用记录 |

## 6. 答题页浏览器验收清单

| 场景 | 证据状态 |
| --- | --- |
| 未选择考试时展示空状态 | 已在浏览器打开 `/exam/take` 看到“暂无/请选择考试”空状态 |
| 演示考试可见 | 运行 `ops/seed-mvp2-demo-data.ps1` 后，浏览器看到 `MVP2-DEMO 考试闭环 20260628141223`、2 题、开始按钮 |
| 重启后页面可用 | 重启 API/Blazor 后，浏览器再次打开 `/exam/take`，登录态 admin 可见演示考试和开始按钮，console/page error 为空 |
| 开始考试 | 浏览器点击开始后显示进行中 attempt、2 道题、剩余时间和保存/提交按钮 |
| 自动保存 | 浏览器选择两个答案后，两题均显示“已答”，状态显示“已保存” |
| 手动保存和提交按钮 | 修正 Razor `disabled` 绑定后，浏览器确认“手动保存”和“提交试卷”均可点击 |
| 提交和评分 | 浏览器提交动作触发后端提交；数据库只读检查确认 attempt 状态为已评分、2 个答案、1 条成绩、20/20 分、0 条错题 |
| 控制台错误 | 浏览器验收期间 console log/page error 为空 |

## 7. 演示数据证据

本轮演示脚本生成的数据：

| 字段 | 值 |
| --- | --- |
| RunTag | `20260628141223` |
| KnowledgePoint | `MVP2-DEMO-20260628141223` |
| PlanId | `3a221f08-6a7d-053f-396b-87054ead4b71` |
| PlanName | `MVP2-DEMO 考试闭环 20260628141223` |
| PaperId | `3a221f08-6a32-e530-0327-7b15a94bd997` |
| QuestionCount | `2` |
| TotalScore | `20` |
| Runtime result | 1 次作答，2 个答案，1 条成绩，20/20 分，0 条错题 |

## 8. 本轮验证结果

| 命令 | 结果 | 备注 |
| --- | --- | --- |
| `dotnet test .\test\KnowPulse.Domain.Tests\KnowPulse.Domain.Tests.csproj --no-restore --filter "FullyQualifiedName~QuestionTests" -v:m -m:1 -nr:false -p:BuildInParallel=false` | 通过 | 16 通过，0 失败，0 跳过；覆盖题型、答案、来源、审核状态和 `IsUsableForExam` |
| `dotnet test .\test\KnowPulse.EntityFrameworkCore.Tests\KnowPulse.EntityFrameworkCore.Tests.csproj --no-restore --filter "FullyQualifiedName~Mvp2QuestionBankAppServiceTests" -v:m -m:1 -nr:false -p:BuildInParallel=false` | 通过 | 3 通过，0 失败，0 跳过；覆盖题库 CRUD、审核、正式题筛选和审核记录 |
| `dotnet test .\test\KnowPulse.EntityFrameworkCore.Tests\KnowPulse.EntityFrameworkCore.Tests.csproj --no-restore --filter "FullyQualifiedName~Mvp2AiQuestionGenerationAppServiceTests" -v:m -m:1 -nr:false -p:BuildInParallel=false` | 通过 | 17 通过，0 失败，0 跳过；覆盖 AI 出题上下文、citation、安全降级、结构校验、相似题和提交审核 |
| `dotnet test .\test\KnowPulse.EntityFrameworkCore.Tests\KnowPulse.EntityFrameworkCore.Tests.csproj --no-restore --filter "FullyQualifiedName~Mvp2ExamAppServiceTests" -v:m -m:1 -nr:false -p:BuildInParallel=false` | 通过 | 5 通过，0 失败，0 跳过 |
| `dotnet test .\test\KnowPulse.Application.Tests\KnowPulse.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~AiQuestionGenerationApiContractTests" -v:m -m:1 -nr:false -p:BuildInParallel=false` | 通过 | 7 通过，0 失败，0 跳过 |
| `dotnet test .\KnowPulse.slnx --no-restore --filter "FullyQualifiedName~Mvp2" -v:m -m:1 -nr:false -p:BuildInParallel=false` | 通过 | `KnowPulse.EntityFrameworkCore.Tests` 中 25 个 MVP-2 测试通过；其他测试程序集无 `Mvp2` 命名匹配 |
| `.\ops\verify-mvp2.ps1 -NoBuild` | 通过 | 静态范围检查、25 个 MVP-2 过滤测试和审计敏感字段扫描均通过；运行态审计可见 4 条题目审核记录和 1 条已评分考试闭环记录，AI 出题模型调用表本轮为 0 行 |
| `.\ops\verify-mvp2.ps1 -SkipDotnetTests -SkipStatic -SkipAudit -CheckExamRuntime -DemoPlanId 3a221f08-6a7d-053f-396b-87054ead4b71` | 通过 | `plans=1 paper_questions=2 scored_attempts=1 answers=2 scores=1` |
| `.\ops\audit-check.ps1 -FailOnSensitiveHits` | 通过 | 审计记录包含题目创建、提交审核、审核通过、考试创建、开始、保存、提交和评分成绩；ABP/RAG/AI 出题敏感字段命中为 0 |
| `dotnet build .\KnowPulse.slnx --no-restore -v:m -m:1 -p:BuildInParallel=false -nr:false` | 通过 | 13 个既有 NuGet 漏洞告警，0 错误 |
| `dotnet build .\src\KnowPulse.Blazor.Client\KnowPulse.Blazor.Client.csproj --no-restore -v:m -m:1 -p:BuildInParallel=false -nr:false` | 通过 | 0 警告，0 错误 |
| `.\ops\start-local.ps1 -HealthRetries 15 -HealthRetryDelaySeconds 4` | 通过 | PostgreSQL、MinIO、Qdrant、LiteLLM 健康检查通过，DbMigrator 完成 |
| `https://localhost:44305/swagger` | 通过 | HTTP 200 |
| `https://localhost:44307/exam/take` | 通过 | HTTP 200，浏览器验收见第 6 节 |

本轮仍有一个工具层提示：浏览器点击提交后的 JS 工具调用曾因确认框/执行超时中断，但后端数据库已确认同一演示 attempt 完成提交和评分。该问题不影响服务端闭环结果；后续引入正式 E2E 依赖后可把提交后得分页状态纳入自动化截图。

## 9. MVP-2 剩余风险

| 风险 | 当前处理 | 后续建议 |
| --- | --- | --- |
| AI 出题运行态演示未在本轮产生真实模型调用记录 | 通过可控替身集成测试验证模型成功、失败、坏格式、无上下文、越权和 citation；`audit-check.ps1` 已能查询真实库 `AppAiQuestionModelCallLogs` | 配置真实 `KNOWPULSE_LITELLM_API_KEY` 后补一次 AI 出题运行态审计截图或记录 |
| 答题页浏览器验收主要依赖手工记录 | 服务端闭环、Blazor 构建和历史浏览器记录已通过 | 后续引入 Playwright/E2E 后固化刷新恢复、自动保存失败提示、提交后结果页截图 |
| 依赖漏洞告警仍存在 | 本轮测试均通过，但 NuGet 输出仍提示 `Scriban` 和 `SQLitePCLRaw.lib.e_sqlite3` 既有漏洞告警 | 进入 MVP-4 安全加固或单独依赖治理任务处理，不阻塞 MVP-2 功能验收 |
