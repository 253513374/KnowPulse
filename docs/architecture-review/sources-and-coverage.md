# 来源、覆盖范围与证据等级

## 1. 审查方法

先读取根开发指引、词汇表、README、解决方案、项目引用、部署配置和 ADR 目录，再追踪高风险路径。全树检索用于检查调用方、生产入口与测试；代码清单由实际文件解析生成。对大仓库采用风险优先抽查，没有声称每一行都被人工审阅。

审查以当前文件为准。Git worktree 元数据目录失效是本地环境事实，未擅自修复或重建 Git，也不能借此判断文件已经提交或未提交。

## 2. 覆盖矩阵

| 范围 | 本轮做了什么 | 深度与限制 |
| --- | --- | --- |
| 结构／依赖 | 解析 16 个项目、全部直接 ProjectReference／PackageReference；目录和体量统计 | 自动扫描；不是 NuGet 传递依赖漏洞审查 |
| 启动／部署 | Host、Web、Client 入口；模块注册；Compose；主要运维入口 | 本地配置与源码核对；没有启动业务服务 |
| 身份与权限 | 微信签码、交换、令牌、逐请求校验；个人所有权；学校访问 Guard | 关键路径审阅；不是渗透测试 |
| 知识与 RAG | 发布关键事务；摄取、分块、Embedding、向量写入、候选和二次授权、日志 | 完整调用链追踪，重点语句核对；未真实调用供应商 |
| 企业旧考试 | 开始／恢复、保存、提交、评分、领域时间与版本规则、EF 索引 | 深入审查并运行两个领域探针 |
| 教育考试 | 固定名单、答卷、封存、评分初始化、关闭、发布、当前结果 | 主要服务路径和 ADR；未逐方法重审全部 partial |
| 分析与方案 | 报告认领／完成、来源指纹、AI 投影、方案发布／执行 | 关键一致性与隐私路径；没有教学质量评估 |
| 导出与任务 | 导出 RunId／租约／清理，以及出题、方案、报告任务模式 | 认领、失败与完成逻辑；未跑多 Host 故障注入 |
| Web／小程序 | 路由、项目依赖、页面注册、会话与恢复、账号帮助 | Web 页面以结构和相关入口抽查为主；非逐页视觉审计 |
| 数据库 | DbSet、重要唯一约束和映射、迁移数量及快照 | 没有逐条审阅 49 个迁移，也未读取真实业务数据 |
| 测试 | 测试模块机制、相关现有用例、选择性运行 | 明确 SQLite 无事务；未跑全量回归 |
| 文档 | 当前 ADR、PRD 决策与早期规划的差异 | 旧实施表部分待办已过时，以后续记录和代码为准 |

## 3. 主要源码入口

| 主题 | 入口 |
| --- | --- |
| 应用装配 | [KnowPulseHttpApiHostModule](../../src/KnowPulse.HttpApi.Host/KnowPulseHttpApiHostModule.cs)、[KnowPulseApplicationModule](../../src/KnowPulse.Application/KnowPulseApplicationModule.cs) |
| 数据映射 | [KnowPulseDbContext](../../src/KnowPulse.EntityFrameworkCore/EntityFrameworkCore/KnowPulseDbContext.cs)、[Education 映射目录](../../src/KnowPulse.EntityFrameworkCore/Education/) |
| 知识业务 | [KnowledgeItemAppService](../../src/KnowPulse.Application/KnowledgeBase/KnowledgeItemAppService.cs)、[KnowledgeSchoolSharingAppService](../../src/KnowPulse.Application/KnowledgeBase/KnowledgeSchoolSharingAppService.cs) |
| 索引 | [Processor](../../src/KnowPulse.Application/KnowledgeIndexing/KnowledgeIndexJobProcessor.cs)、[Ingestion](../../src/KnowPulse.Application/RagAI/KnowledgeIngestionPipeline.cs)、[Embedding](../../src/KnowPulse.Application/RagAI/KnowledgeEmbeddingPipeline.cs) |
| 检索 | [KnowledgeRetriever](../../src/KnowPulse.Application/RagAI/KnowledgeRetriever.cs)、[Qdrant](../../src/KnowPulse.Application/RagAI/QdrantKnowledgeVectorStore.cs)、[RagAnswerService](../../src/KnowPulse.Application/RagAI/RagAnswerService.cs) |
| 企业考试 | [ExamAppService](../../src/KnowPulse.Application/Exam/ExamAppService.cs)、[ExamAnswer](../../src/KnowPulse.Domain/Exam/ExamAnswer.cs)、[ExamAttempt](../../src/KnowPulse.Domain/Exam/ExamAttempt.cs) |
| 教育考试 | [EducationAssessmentAppService](../../src/KnowPulse.Application/Education/EducationAssessmentAppService.cs)、[Results partial](../../src/KnowPulse.Application/Education/EducationAssessmentAppService.Results.cs) |
| 后台消费者 | [Host worker 目录](../../src/KnowPulse.HttpApi.Host/KnowledgeIndexing/) |
| 微信 | [Application/WeChat](../../src/KnowPulse.Application/WeChat/)、[Host/WeChat](../../src/KnowPulse.HttpApi.Host/WeChat/)、[原生会话](../../miniprogram/utils/session.js) |
| 测试基础 | [EF 测试模块](../../test/KnowPulse.EntityFrameworkCore.Tests/EntityFrameworkCore/KnowPulseEntityFrameworkCoreTestModule.cs)、[TestBase](../../test/KnowPulse.TestBase/) |

## 4. ADR 分组导航

| ADR | 当前设计主题 |
| --- | --- |
| 0001–0006 | 知识 UI、转换适配、管理投影、原子发布、可恢复索引 |
| 0007–0010 | 个人归属、学校共享与来源链、导出 |
| 0011–0013 | 教材与知识点映射、课程出题、评分定义和审核 |
| 0014–0017 | 教育任务、结构化响应、冻结评分、统一成绩发布 |
| 0018–0024 | 私有观察与评语、证据、报告、学生成长、教学方案与提示资格 |
| 0025–0036 | 活动、正式复测、图片冲突、提交确认、撤回、材料、订正、工作量、笔记与复练 |
| 0037–0046 | 微信绑定、常用语、收藏、移动端笔记／成长、趋势、AI方案、执行与移动跟进 |
| 0047–0053 | 催办、自动消息、统一收件箱、学校邀请、任教更正、账号自助入口 |

其中深入核对 0003、0005、0006、0008、0009、0017 与对应实现；其余结合词汇表、关键服务和此前实施记录阅读，未重新逐条验收所有决策。

## 5. 已确认与尚待实现

- 初中多学科；教师个人库默认私有，主动共享到学校。
- D02：一个 AppID、师生两个独立工作区；真实微信配置尚未提供，先代码与本地验证。
- D03：共享撤回后停止后续在线正文／附件读取，保留历史答卷和已上交记录。
- D06／D10 的账号核验责任：用户已于 2026-10-18 选择本人验证原账号后自助，无法验证时平台人工复核；学校不直接接管。**这项责任已确认，人工申请和受控执行代码尚未实现。**
- D06 的其他内容如留存、删除、离校历史访问，及 D07 考试例外、D08 教研参数、D09 容量和设备等仍需后续落实。

## 6. 本轮证据文件

以下日志留在忽略的 `TestResults/`，避免把构建产物并入文档包；结果摘要已经写入正文。

| 文件 | 内容 |
| --- | --- |
| `repository-audit-build.log` | 完整解决方案构建：0 警告、0 错误，39.10 秒 |
| `repository-audit-probes.log` | 两个实际领域代码问题探针 |
| `repository-audit-focused-tests.log` | 三个选择性 EF 测试：3/3 通过，9 秒 |
| `repository-audit-native.log` | 两个原生会话边界测试 |
| `repository-audit-provider-boundary.log` | Provider 检查与 ADR 的冲突失败 |
| `repository-audit-doc-validation.json` | 109 个本地链接、12 个 SVG XML 与 Mermaid 节点引用检查 |
| `repository-audit-visual-validation.json` | 桌面／窄屏、章节筛选、图表放大及 12 张 SVG 实际渲染检查 |
| `repository-audit-inventory.json` | 初始结构扫描；最终可交付清单在本目录 `inventory.json` |

ABP 事务默认行为另外核对了 **10.6.0 官方源码**：[UnitOfWorkInterceptor](https://raw.githubusercontent.com/abpframework/abp/10.6.0/framework/src/Volo.Abp.Uow/Volo/Abp/Uow/UnitOfWorkInterceptor.cs)、[AbpUnitOfWorkDefaultOptions](https://raw.githubusercontent.com/abpframework/abp/10.6.0/framework/src/Volo.Abp.Uow/Volo/Abp/Uow/AbpUnitOfWorkDefaultOptions.cs)。没有以最新主分支替代当前包版本。

## 7. 未检查

真实账号、个人文件、生产数据库和密钥未读取；没有发送邮件／微信消息、调用付费模型或执行迁移。没有检查所有第三方实现、所有历史迁移、所有 Web／小程序页面的视觉和无障碍表现，也未证明任何生产容量指标。

没有把历史“已跑 1000 多项”的数字作为本轮通过结果；本轮结论严格区分静态推导、实际代码探针、选择性测试和未验收项目。

SVG 与 Mermaid 文件由同一组图结构生成，离线阅读版内嵌 SVG，不依赖联网渲染。Mermaid 图源检查了节点引用，未用 Mermaid 引擎逐图渲染；图中的数据关系是概念模型，不是数据库物理外键的完整导出。
