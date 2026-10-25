# 项目接手代码审计

[返回导读](README.md)

## 这个项目做什么

KnowPulse 为企业提供知识发布、授权问答、题库考试与个人学习。后端和 Web 已有真实业务链路；独立微信小程序目前只演示交互。

本次按“一个 API 实例、一个 Web 实例、同一企业有多名员工同时使用”的负载假设审读，并用知识/路径历史持续增长的情形检查分页。没有假定已具备多实例高可用或做过容量验收。以下问题来自源码路径；未做 HTTP 攻击演示或生产操作。

## 必须修复

### 1. 考生响应直接包含标准答案和解析

证据：[ExamAppService](../../src/KnowPulse.Application/Exam/ExamAppService.cs#L147) 的 `GetMyPlanListAsync`、`MapPlanAsync`、`MapPaperQuestion`（约 696、752 行）、`MapAttemptAsync`（约 787 行）；[结果可见性枚举](../../src/KnowPulse.Domain.Shared/Exam/ExamResultVisibility.cs)。

- **这是什么：** 管理员和考生共用包含完整试卷快照的 DTO 映射，快照中含正确答案与解析。
- **问题：** 被分配的普通考生查询我的考试或开始作答时，响应的 `Paper.Questions[].CorrectAnswers` 和 `Analysis` 就已填入；`Plan.Paper` 也包含一份。结果映射没有按 Hidden、ScoreOnly、FullReview 裁剪，页面不显示不能阻止客户端读取。
- **最小修复：** 在考生共用映射入口按作答状态与结果策略去除答案、解析和不可见结果，管理预览保留完整内容；两条嵌套 Paper 路径都要处理。补一个未交卷及结果策略的响应内容测试。
- **不修会怎样：** 考试开始前或作答中即可从响应得到答案，“隐藏结果/仅分数”设置也不能实现承诺。

### 2. 已过期但仍为 InProgress 的作答阻塞下一次尝试

证据：[StartAttemptAsync](../../src/KnowPulse.Application/Exam/ExamAppService.cs#L169)、[ExamAttempt.Submit](../../src/KnowPulse.Domain/Exam/ExamAttempt.cs#L99)、[现有到期测试](../../test/KnowPulse.EntityFrameworkCore.Tests/EntityFrameworkCore/Applications/Mvp2ExamAppServiceTests.cs#L251)。

- **这是什么：** 开始考试时优先返回已有 InProgress 作答，目的是断线后继续作答。
- **问题：** 该分支不比较 `ExpiresTime`。例如计划仍开放、允许两次尝试，用户第一次超时后再次点击开始，仍得到旧的过期记录；保存和提交都会报到期，无法进入第二次。`Submit` 改为 Expired 后抛异常，也不能依赖这一写入在生产事务中提交。
- **最小修复：** 在开始考试的共享入口先持久化清理过期作答，再选择有效作答并计算剩余次数；补“到期后重新开始”的集成测试。
- **不修会怎样：** 有剩余次数的员工仍可能卡在不可保存、不可提交的旧考试中。现有测试只断言超时保存/提交报错，没有覆盖恢复入口。

### 3. 公开状态接口可把未执行任务直接标为 Indexed

证据：[indexed 控制器入口](../../src/KnowPulse.HttpApi/Controllers/KnowledgeIndexing/KnowledgeIndexJobsController.cs#L136)、[MarkIndexedAsync](../../src/KnowPulse.Application/KnowledgeIndexing/KnowledgeIndexJobAppService.cs#L241)、[完整执行 ADR](../adr/0006-recoverable-knowledge-index-job-execution.md)。

- **这是什么：** 保留了直接开始、失败和完成任务的管理接口；正常执行已有共享 Processor。
- **问题：** 有索引管理权限的调用者对 Pending 任务调用 `POST /api/knowledge/index-jobs/{id}/indexed`，服务只改状态，不验证分块、Embedding 或向量。管理页可显示完成，但检索实际没有新内容。
- **最小修复：** 去掉对外“直接标完成”的入口和远程能力，让完成状态只由真实管道写入；同步调整依赖该接口布置状态的测试。
- **不修会怎样：** “已索引”不能作为运维或用户判断知识可用的事实，排障会被假完成状态误导。

## 应该修复

### 4. 不支持的附件会使整份知识反复索引失败

证据：[reader 遍历附件](../../src/KnowPulse.Application/RagAI/KnowledgeIngestionPipeline.cs#L233)、[ThrowUnsupportedAttachment](../../src/KnowPulse.Application/RagAI/KnowledgeIngestionPipeline.cs#L324)、[上传引导](../../src/KnowPulse.Blazor.Client/Pages/Knowledge/Upload.razor) 的 `AttachmentOnly` 与发布操作。

- **这是什么：** 上传界面允许图片、音视频等“仅附件”资料；摄取读取版本正文和所有附件。
- **问题：** 例如有效正文所在版本再附一张 PNG，reader 在不支持附件处直接抛异常，正文也无法进入后续阶段。只上传“仅附件”再点击“发布并创建索引”同样进入确定失败和重试。
- **最小修复：** reader 跳过明确不索引的附件，仅在没有任何可读内容时失败；界面明确区分附件发布与可检索内容，补“正文 + PNG”用例。
- **不修会怎样：** 用户合理保存附件就可能打断已有正文索引，失败任务还会消耗重试预算。

### 5. 列表分页之前加载整租户历史，路径与考试还逐条查询分配

证据：[知识快照加载](../../src/KnowPulse.Application/KnowledgeBase/KnowledgeManagementAppService.cs#L131)、[知识内存分页](../../src/KnowPulse.Application/KnowledgeBase/KnowledgeManagementAppService.cs#L50)、[学习路径查询](../../src/KnowPulse.Application/Learning/LearningPathAppService.cs#L163)、[我的考试查询](../../src/KnowPulse.Application/Exam/ExamAppService.cs#L147)。

- **这是什么：** 列表先加载并计算可见对象，再在内存里 `Skip/Take`。
- **问题：** 即使只取 10 条，知识管理仍读取全部条目、版本、权限、任务和反馈；每个条目又扫描相关集合。500 条启用路径即会逐条检查分配，再做分页。这是调用次数和读取规模的源码结论，不是已测得的延迟数字。
- **最小修复：** 在数据库下推可见范围、过滤和分页，分配记录按用户/候选 ID 批量查询；汇总统计与当前页明细分开读取，避免每行重扫全表集合。
- **不修会怎样：** 历史越多，首屏消耗越大；多名员工并发打开列表时，数据库请求和应用内存一起增长。

### 6. RAG 依赖检查与已采纳 MarkItDown 边界冲突

证据：[验证脚本](../../ops/verify-rag-provider-boundaries.ps1#L20)、[适配实现](../../src/KnowPulse.Application/RagAI/KnowledgeDocumentMarkdownConverter.cs#L10)、[项目依赖](../../src/KnowPulse.Application/KnowPulse.Application.csproj)、[ADR 0003](../adr/0003-markitdown-backed-document-ingestion.md)。

- **这是什么：** 静态脚本防止预览 SDK 进入核心业务边界；ADR 已允许在 Application 内通过项目接口封装 MarkItDown。
- **问题：** 本次实际执行脚本退出码为 1，命中转换器的 DataIngestion 命名空间和 MarkItDown 包引用。这是仓库规则与实现冲突，不能解释为全部 RAG 测试失败。
- **最小修复：** 仅为已批准的适配文件与准确包名设置窄例外，仍禁止 Contracts/Domain 和其他业务位置引用供应商类型；增加合法适配及非法泄漏的检查样本。
- **不修会怎样：** 开发者按文档运行验证必然失败，难以区分真实边界泄漏和允许的适配实现。

## 已执行与检查边界

| 检查 | 本次结果 |
| --- | --- |
| 解决方案、项目引用、注册、运行入口、Compose | 已核对 |
| 知识发布、索引、检索、问答、考试、路径关键读写与权限 | 已沿主链路阅读；没有逐行阅读全部辅助方法 |
| .NET 测试 | 166 通过，0 失败；SQLite 集成测试关闭了工作单元事务 |
| 小程序测试 | 8 通过，0 失败 |
| RAG 静态边界扫描 | 失败，见第 6 项 |
| 问题验证性质 | 源码可达路径和已有测试核对；没有新增漏洞复现代码或修改业务逻辑 |

**结论：** 知识链路的事实与权限边界已经成形；先修考生响应中的答案披露和到期恢复，再收紧索引完成入口。

**未检查：** 生产数据、真实 PostgreSQL 事务/锁/崩溃恢复、外部模型与费用准确性、真实 Qdrant/MinIO、浏览器和微信真机、多实例及负载测试、全部 ABP 包内部实现、最新依赖漏洞数据库。没有对未读代码作“无风险”结论；小程序进程内状态符合已声明原型边界，不作为缺陷。
