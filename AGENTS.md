# KnowPulse 开发指引

## 项目与文档

KnowPulse 是企业知识、学习与考试系统，采用 ABP 分层单体。当前已包含组织权限、知识发布与索引、权限感知 RAG、AI 模型管理、题库审核与 AI 出题、考试、学习路径及知识反馈。

- 先读根目录 `CONTEXT.md` 和 `docs/adr/`；领域术语沿用词汇表。与 ADR 冲突的改动应明确指出并更新决策，不能静默覆盖。
- 开发环境见 [docs/development-environment.md](docs/development-environment.md)，RAG 边界见 [docs/rag-ai-boundary.md](docs/rag-ai-boundary.md)，操作流程见 [docs/user-operation-guide.md](docs/user-operation-guide.md)。
- `docs/module-boundaries.md`、`docs/technical-architecture.md` 等含早期 MVP 规划。判断已实现能力、依赖版本和运行模式时核对当前代码与 ADR，勿将历史“占位/暂缓”直接当作现状。
- SDK 基线为 `global.json` 中的 .NET `10.0.301`（`latestFeature` roll-forward）；项目目标为 `net10.0`，当前 `Volo.Abp.*` 包为 `10.6.0`。包版本以各 `.csproj` 为准，公共构建属性在 `common.props`。

## 代码位置与分层

| 路径 | 职责 |
| --- | --- |
| `src/KnowPulse.Domain.Shared` | 枚举、共享常量、错误码、本地化资源 |
| `src/KnowPulse.Domain` | 实体、领域规则、访问策略与状态判断 |
| `src/KnowPulse.Application.Contracts` | 应用服务接口、DTO、验证和权限定义 |
| `src/KnowPulse.Application` | 用例编排、授权、仓储访问；`RagAI/` 中封装摄取、模型和向量库适配 |
| `src/KnowPulse.EntityFrameworkCore` | `KnowPulseDbContext`、实体映射及 `Migrations/` |
| `src/KnowPulse.HttpApi` / `src/KnowPulse.HttpApi.Client` | HTTP 控制器与客户端代理 |
| `src/KnowPulse.HttpApi.Host` | API、认证、依赖配置和知识索引后台 worker |
| `src/KnowPulse.Blazor.Client` | Blazor WebAssembly 页面、组件、菜单及样式；业务页面在 `Pages/` |
| `src/KnowPulse.Blazor` | 承载 WebAssembly 客户端的 ASP.NET Core 主机 |
| `src/KnowPulse.DbMigrator` | 数据库迁移与种子数据 |
| `test/` | 领域、契约和 EF Core 集成测试及共享测试基类 |
| `ops/` / `deploy/` | PowerShell 验证与运维脚本、部署配置 |

保持现有依赖方向：UI 使用应用契约/HTTP 代理，控制器保持轻量，应用服务编排用例，领域对象维护业务规则，EF Core 负责持久化。不要把数据库或外部 provider 类型泄漏到 DTO 和领域模型。

## 常用命令

以下 PowerShell 命令默认从仓库根目录执行；解决方案文件是 `KnowPulse.slnx`。

```powershell
dotnet restore .\KnowPulse.slnx --disable-parallel
dotnet build .\KnowPulse.slnx --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false -nr:false
dotnet test .\KnowPulse.slnx --no-build --no-restore -m:1 -nr:false
```

只有当前代码已成功构建时才使用 `--no-build`，还原完成后才使用 `--no-restore`。优先按改动范围运行测试，例如：

```powershell
dotnet test .\test\KnowPulse.Domain.Tests\KnowPulse.Domain.Tests.csproj --filter "FullyQualifiedName~KnowledgeAccessEvaluatorTests"
dotnet test .\test\KnowPulse.Application.Tests\KnowPulse.Application.Tests.csproj
dotnet test .\test\KnowPulse.EntityFrameworkCore.Tests\KnowPulse.EntityFrameworkCore.Tests.csproj --filter "FullyQualifiedName~KnowledgeManagementAppServiceTests"
dotnet build .\src\KnowPulse.Blazor.Client\KnowPulse.Blazor.Client.csproj --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false -nr:false
```

本地运行需 Docker、Node（README 基线为 20.11+）和 ABP CLI。首次创建 `.env` 时从 `.env.example` 复制，保留已有本地配置。

```powershell
# 启动 PostgreSQL、MinIO、Qdrant、LiteLLM，健康检查并运行 DbMigrator
.\ops\start-local.ps1
# 仅基础依赖可用 -CoreOnly；仅启动依赖可用 -SkipMigrator

# 首次运行 API 的 Account / Swagger 页面前安装静态资源
Push-Location .\src\KnowPulse.HttpApi.Host
abp install-libs
Pop-Location
```

分别在两个终端启动 API 与前端：

```powershell
dotnet run --project .\src\KnowPulse.HttpApi.Host\KnowPulse.HttpApi.Host.csproj
dotnet run --project .\src\KnowPulse.Blazor\KnowPulse.Blazor.csproj
```

默认 API 为 `https://localhost:44305`，前端为 `https://localhost:44307`。`start-local.ps1` 会配置本地 OIDC 客户端地址并从正确目录启动迁移器；它不启动 API 和 Blazor。

## 验证与数据库变更

- 测试使用 xUnit、Shouldly、NSubstitute。领域规则放在 `Domain.Tests`，路由/DTO/授权特性契约放在 `Application.Tests`，仓储和应用服务集成行为放在 `EntityFrameworkCore.Tests`。
- EF 测试使用内存 SQLite、临时文件 Blob 和 embedding/vector/chat/Markdown 转换替身，不要求真实 PostgreSQL、MinIO、Qdrant 或模型服务。测试默认关闭 UoW 事务；测试基类默认放行授权并提供管理员身份。权限测试须显式设置拒绝/受限上下文，事务与 PostgreSQL 特有行为须另行验证。
- `ops/verify-mvp0-static.ps1` 检查基础契约；`ops/verify-mvp1.ps1 -SkipQdrant -SkipAudit` 串联 RAG 测试及静态检查；题库/考试和学习/反馈分别有 `ops/verify-mvp2.ps1`、`ops/verify-mvp3.ps1`。先读脚本参数，再选择与改动相关的检查。
- 已知检查差异：`ops/verify-rag-provider-boundaries.ps1` 当前仍全面匹配 DataIngestion 引用，而 ADR 0003 已允许隔离后的 MarkItDown 适配。遇到此项失败应指出脚本与 ADR 的差异，不能为通过扫描而破坏已批准的边界。
- 运行时验收、审计与演示数据脚本依赖本地服务；`seed-*`、`verify-mvp0-runtime.ps1` 会写入数据库。页面返回 HTTP 200 或契约测试通过不代表登录后业务流程已验收。
- 数据模型变化需同步 EF 映射并生成迁移。设计时工厂按当前目录读取 `../KnowPulse.DbMigrator/appsettings.json`，因此在 `src/KnowPulse.EntityFrameworkCore` 目录执行 `dotnet ef migrations add <Name>`。生成后重新构建，再运行 DbMigrator；不要用旧程序集配合 `--no-build` 判断新迁移是否已应用。
- 若运行中的本地 Host 锁定构建输出，先识别相关进程或改用独立输出目录。报告实际执行的检查及未完成原因，不把历史验证记录作为本轮通过结果。

## 业务边界

- PostgreSQL 保存业务事实，MinIO/BlobStoring 保存原始附件；Qdrant 仅保存可重建索引。检索需先过滤候选，再由 .NET 根据数据库中的租户、权限、知识状态和版本二次校验；仅经校验的内容可送入模型。
- 外部模型、向量存储及转换实现通过项目自有接口隔离，如 `IKnowledgeVectorStore`、`IKnowledgeIngestionPipeline`、`IKnowledgeDocumentMarkdownConverter`。MarkItDown/DataIngestion preview 类型仅留在适配实现，不进入契约、领域或 UI；转换失败归入 reader 阶段，原因不得包含完整附件正文。
- 原子发布须在同一关系型事务中保存完整显式访问策略、切换发布版本、使无有效学校共享的旧索引/分块失效并创建新索引任务。有效指定版本共享可按 ADR 0009 保留其旧索引，保留不授予读者或新用途权限；撤回后即使向量仍在也须由数据库复核阻断新使用。策略为单条 `All` 或非重复的定向规则，不能混用；发布同时检查版本管理及权限范围权限。附件预先保存，失败后保留可修复草稿（ADR 0005、0009）。
- 发布成功只表示任务已持久化。索引按 `reader -> processor -> chunker -> persistence -> embedding -> vector-store` 执行，分块、embedding 审计与向量写入全部完成后才能标为 `Indexed`；保留任务幂等、进度/心跳、指数退避及 `RetryOfJobId` 恢复链路（ADR 0006）。
- 知识管理状态由查询时的生命周期与索引事实计算，不另存 UI 状态。`/knowledge` 为管理列表，`/knowledge/{id}` 为详情，`/knowledge/upload` 为上传引导，`/knowledge/items` 保留高级操作（ADR 0001–0004）。
- RAG/AI 审计仅保存受控摘要、hash、引用标识、模型元数据及失败阶段；不写完整正文、附件流、完整 prompt 或密钥。Embedding 维度变化需新建 collection 或显式迁移，同一 collection 不混写不同维度。

## 编码与配置约定

- 沿用相邻 C# 文件的 file-scoped namespace、4 空格缩进、nullable 和 `Async` 命名；公共设置见 `common.props`，前端文本格式见 `.prettierrc`。避免无关格式化和依赖升级。
- 新增业务接口同步应用契约、服务实现、必要的 HTTP 路由/代理和 UI。权限常量与定义在 `Application.Contracts/Permissions`；应用服务沿用 `KnowPulseAppService`、`[Authorize]` 与 `CheckMvpPermissionAsync` 显式检查，页面和菜单授权不能替代服务端校验。功能权限与知识可见范围分别验证。
- UI 延续现有 Blazor、MudBlazor LeptonX Lite 与原生 HTML 组件体系；复用 `KnowPulseComponentBase`、已有组件与页面样式。本地化资源在 `Domain.Shared/Localization/KnowPulse`。
- 不提交 `.env`、`appsettings.secrets.json`、签名证书、真实模型/存储密钥，或 `bin/`、`obj/`、日志和验证产物。配置示例只保留占位值及环境变量名。
- 业务流程变化同步相关操作说明；术语与架构决策变化更新 `CONTEXT.md` / `docs/adr/`。

## Agent skills

### Issue tracker

Issues are tracked in GitHub Issues for `https://github.com/253513374/KnowPulse.git`; external PRs are not a triage surface. See `docs/agents/issue-tracker.md`.

### Triage labels

Use the default five-label triage vocabulary: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, and `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

This is a single-context repo: read root `CONTEXT.md` and root `docs/adr/` when present. See `docs/agents/domain.md`.
