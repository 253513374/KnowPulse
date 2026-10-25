# 解决方案目录与源码导航

[返回导读](README.md)

## 工作区与仓库边界

```text
KnowPulse/                         工作区目录，本身没有 .git
├─ AGENTS.md                       工作区贡献规范
├─ KnowPulse/                      后端与 Web 独立 Git 仓库
│  ├─ KnowPulse.slnx               .NET 解决方案
│  ├─ global.json                 SDK 选择规则
│  ├─ common.props                公共构建属性
│  ├─ src/                        11 个源码项目，见下表
│  ├─ test/                       5 个测试或测试辅助项目
│  ├─ docs/                       手册、架构规划、验收记录
│  │  ├─ project-guide/           当前代码导读与图表
│  │  ├─ adr/                     架构决策
│  │  └─ agents/                  代理协作、问题跟踪规则
│  ├─ ops/                        PowerShell 启动和验证脚本
│  ├─ deploy/                     模型网关等部署配置
│  └─ docker-compose.yml          PostgreSQL、MinIO、Qdrant、LiteLLM
└─ KnowPulse.MiniProgram/          微信小程序独立 Git 仓库
   ├─ project.config.json         微信开发者工具入口
   ├─ package.json                Node 内置测试命令
   ├─ miniprogram/
   │  ├─ app.js / app.json / app.wxss
   │  ├─ pages/                   home、learning、knowledge、assistant、
   │  │                           profile、document、exam
   │  ├─ components/              page-header、task-card、knowledge-card
   │  ├─ custom-tab-bar/           五个导航入口
   │  ├─ data/demo.js             演示数据与状态计算
   │  └─ assets/                  本地品牌和图标资源
   ├─ tests/prototype.test.js      交互逻辑与资源完整性测试
   ├─ docs/adr/                   小程序边界决策
   └─ design-qa.md                模拟器、视觉检查和未通过项
```

`bin/`、`obj/`、`artifacts/` 和 `Logs/` 是构建或运行产物，不是业务模块。Git 状态、提交与分支操作必须在对应子仓库中进行。

## 11 个源码项目

以下路径均相对于后端仓库 `src/`。

| 项目 | 职责 | 首先看什么 |
| --- | --- | --- |
| `KnowPulse.Domain.Shared` | 公共常量、枚举、错误码、本地化、多租户开关 | `MultiTenancy/MultiTenancyConsts.cs` |
| `KnowPulse.Domain` | 领域实体及状态规则，ABP 身份与租户模块 | `KnowledgeBase/KnowledgeVersion.cs`、`KnowledgeAccessEvaluator.cs` |
| `KnowPulse.Application.Contracts` | 应用服务接口、DTO、权限定义与 RAG 契约 | `Permissions/KnowPulsePermissions.cs`、各模块接口 |
| `KnowPulse.Application` | 业务编排、读模型、索引与外部服务适配 | `KnowPulseApplicationModule.cs` |
| `KnowPulse.EntityFrameworkCore` | DbContext、实体映射、PostgreSQL 与迁移 | `EntityFrameworkCore/KnowPulseDbContext.cs`、`Migrations/` |
| `KnowPulse.HttpApi` | 显式 HTTP 控制器 | `Controllers/` |
| `KnowPulse.HttpApi.Host` | API、认证服务、审计、MinIO、索引 worker 的运行宿主 | `Program.cs`、`KnowPulseHttpApiHostModule.cs` |
| `KnowPulse.HttpApi.Client` | ABP HTTP 客户端代理 | `KnowPulseHttpApiClientModule.cs` |
| `KnowPulse.Blazor.Client` | 浏览器端交互、页面、菜单和共享组件 | `Pages/`、`Menus/`、`Components/`、`wwwroot/` |
| `KnowPulse.Blazor` | Blazor Web App 宿主与静态资源分发 | `KnowPulseBlazorModule.cs` |
| `KnowPulse.DbMigrator` | 一次性数据库迁移及种子初始化 | `Program.cs`、`KnowPulseDbMigratorModule.cs` |

这里的“模块”主要是业务目录和命名空间边界，不代表独立进程或微服务。Application 中的 REST 适配器可访问 LiteLLM/Qdrant；Domain 不应依赖这些供应商类型。

## 业务模块速查

同一业务通常同时出现在 Domain、Application.Contracts 和 Application 中。

| 模块目录 | 管什么 | 应用入口 | Web 入口示例 |
| --- | --- | --- | --- |
| `Organization` | 部门、岗位、职级、用户任职 | `DepartmentAppService`、`UserOrganizationAssignmentAppService` | `/organization/departments` |
| `KnowledgeBase` | 知识、版本、附件、权限、反馈和管理读模型 | `KnowledgeItemAppService`、`KnowledgeManagementAppService` | `/knowledge`、`/knowledge/upload` |
| `KnowledgeIndexing` | 索引任务状态、重试、恢复及共享执行器 | `KnowledgeIndexJobAppService`、`KnowledgeIndexJobProcessor` | `/knowledge/index-jobs` |
| `RagAI` | 摄取、分块、向量、问答、调用审计和评测 | `KnowledgeRetriever`、`RagAnswerService`、`RagEvaluationAppService` | `/rag/answer` |
| `AiModelManagement` | 模型部署目录、用途绑定、健康快照 | `AiModelCatalogAppService`、`AiModelResolver` | `/rag/models` |
| `AiQuestioning` | 提示模板、生成任务、候选题、来源和调用日志 | `AiQuestionGenerationAppService` | `/question-bank/ai-candidates` |
| `QuestionBank` | 人工草稿、采纳候选、审核、正式题和停用 | `QuestionAppService` 同时实现题库与工作流接口 | `/question-bank`、`/question-bank/reviews` |
| `Exam` | 组卷、分配、作答、评分和错题 | `ExamAppService` | `/exam/management`、`/exam/take` |
| `Learning` | 路径配置、分配、阅读记录与考试进度 | `LearningPathAppService` | `/learning/my`；路径配置另有管理 API |

## 五个测试与辅助项目

| 项目 | 用途 |
| --- | --- |
| `KnowPulse.Domain.Tests` | 实体状态、知识可见范围、访问策略等领域规则 |
| `KnowPulse.Application.Tests` | API 契约、Qdrant 请求格式和应用测试基类 |
| `KnowPulse.EntityFrameworkCore.Tests` | SQLite 内存数据库上的应用集成测试，模型/向量/转换器均有替身 |
| `KnowPulse.TestBase` | ABP 测试初始化、种子和公共基础设施；本身没有测试用例 |
| `KnowPulse.HttpApi.Client.ConsoleTestApp` | 手动运行的 API 客户端示例，不是自动化测试套件 |

## 一次改动通常会经过哪里

| 需求 | 修改与检查路径 |
| --- | --- |
| 增加接口字段 | Contracts DTO → Application 映射 → HttpApi/客户端 → Razor 页面 → 契约和集成测试 |
| 增加业务状态 | Domain.Shared 枚举 → Domain 状态方法 → Application 编排 → UI 文案和操作 → 领域测试 |
| 增加持久化字段 | Domain → DbContext 映射 → EF 迁移 → DTO/映射 → DbMigrator |
| 调整知识权限 | AccessContextProvider → AccessEvaluator → 列表/附件/RAG/学习消费者 → 权限矩阵测试 |
| 调整索引 | Host worker → Processor → Ingestion/Embedding → VectorStore → 任务和管道测试 |

证据入口：[解决方案](../../KnowPulse.slnx)、[应用注册](../../src/KnowPulse.Application/KnowPulseApplicationModule.cs)、[数据库映射](../../src/KnowPulse.EntityFrameworkCore/EntityFrameworkCore/KnowPulseDbContext.cs)。
