# KnowPulse 开发进度

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
