# KnowPulse MVP-0 验收矩阵

版本：v0.1  
日期：2026-06-26  
来源：`docs/mvp-0-task-breakdown.md`、`docs/mvp-0-qa-test-plan.md`、当前仓库验证结果

## 1. 验收口径

MVP-0 验收目标是工程基础与知识库闭环：

- 本地开发环境可启动 PostgreSQL、MinIO、Qdrant、LiteLLM，并在 PostgreSQL 中预留 ABP BackgroundJobs/Hangfire 后台任务存储。
- ABP + ASP.NET Core + Blazor 工程可构建、可测试、可启动。
- Organization 与 KnowledgeBase 基础模型、应用服务、权限点和 REST API 可用。
- 知识条目、版本、权限范围、附件上传下载形成最小闭环。
- 权限范围在后端强制执行，不依赖前端隐藏按钮。
- 关键操作进入 ABP 审计，且审计不保存知识正文和文件流内容。
- RAG/AI 在 MVP-0 只保留边界、禁用清单、健康检查和 MVP-1 风险闸口。

不纳入 MVP-0 的内容：真实 RAG 问答、向量索引写入、AI 出题、考试、学习记录和生产级部署。

## 2. 当前验证命令

| 验证项 | 命令 |
| --- | --- |
| 依赖健康检查 | `.\ops\health-check.ps1 -Retries 3 -RetryDelaySeconds 2` |
| MVP-0 静态契约检查 | `.\ops\verify-mvp0-static.ps1` |
| 审计日志和敏感字段检查 | `.\ops\audit-check.ps1` |
| 综合验证 | `.\ops\verify-mvp0.ps1` |
| 登录后真实业务闭环验证 | `.\ops\verify-mvp0-runtime.ps1` |
| 综合验证 + 真实业务闭环 | `.\ops\verify-mvp0.ps1 -WithRuntime` |
| 基础演示数据生成 | `.\ops\seed-demo-data.ps1` |
| 综合验证 + 基础演示数据 | `.\ops\verify-mvp0.ps1 -WithDemoSeed` |
| 页面级附件验收前置检查 | `.\ops\check-page-attachment-prereqs.ps1` |
| 页面级附件严格前置检查 | `.\ops\check-page-attachment-prereqs.ps1 -RequireEndpoints -RequireBrowserAutomation` |
| 综合验证 + 页面级附件前置检查 | `.\ops\verify-mvp0.ps1 -WithPageAttachmentPreflight` |
| 完整构建 | `dotnet build .\KnowPulse.slnx --no-restore -v:m -m:1 -p:BuildInParallel=false -nr:false -p:UseAppHost=false` |
| 测试 | `dotnet test .\KnowPulse.slnx --no-build --no-restore -v:m -m:1 -nr:false` |
| EF Core 集成测试 | `dotnet test .\test\KnowPulse.EntityFrameworkCore.Tests\KnowPulse.EntityFrameworkCore.Tests.csproj -v:m -m:1 -nr:false -p:BuildInParallel=false` |

说明：`ops/verify-mvp0.ps1 -WithBuild` 会先执行完整构建；如果 API Host 或 Blazor 正在从 `bin\Debug` DLL 运行，Windows 文件锁可能导致构建失败，应先停止运行中的应用再执行。`ops/verify-mvp0-runtime.ps1` 会写入本地开发数据库测试数据，并通过 API 上传和删除一个临时附件。`ops/check-page-attachment-prereqs.ps1` 会生成安全演示附件并检查页面补验前置条件；默认模式把端点或 Chrome 自动化问题作为 Warn，便于普通浏览器手工补验；`-RequireEndpoints -RequireBrowserAutomation` 会把这些问题作为硬失败。该脚本包含 `native host official check` 信号。它不等同于页面已自动完成上传、下载和删除。

## 3. 任务到证据矩阵

| 任务 | 验收证据 | 当前结论 | 剩余动作 |
| --- | --- | --- | --- |
| KP-MVP0-BE-001 初始化 ABP 解决方案与模块边界 | `KnowPulse.slnx`、`src/*` 模块、`docs/system-architecture.md`、`dotnet build`、ABP BackgroundJobs 模块引用 | 已通过自动验证 | 无 |
| KP-MVP0-BE-002 配置 PostgreSQL 与基础迁移链路 | `docker-compose.yml`、`src/KnowPulse.DbMigrator`、DbMigrator 可执行、数据库基础表已验证 | 已通过运行验证 | 无 |
| KP-MVP0-BE-003 配置 Identity、租户与权限基础 | OpenIddict Blazor/Swagger 客户端、默认 admin、角色权限授权、未登录 API 非 200；系统 Chrome 登录后显示 admin 和 MVP-0 菜单 | 已通过后端和 Chrome 登录验证 | 无 |
| KP-MVP0-BE-004 实现 Organization 领域模型 | `src/KnowPulse.Domain/Organization`、领域测试、EF Core 集成测试 | 已通过自动验证 | 无 |
| KP-MVP0-BE-005 实现 Organization 应用服务与权限点 | `src/KnowPulse.Application/Organization`、Swagger 路由、应用服务显式 `CheckMvpPermissionAsync`、EF Core 集成测试、`ops/verify-mvp0-runtime.ps1` 创建部门；系统 Chrome 完成部门、岗位、职级、用户组织关系新增/编辑/删除 | 已通过自动、运行和组织页面深度 CRUD 验证 | 无 |
| KP-MVP0-BE-006 实现 KnowledgeBase 基础领域模型 | `src/KnowPulse.Domain/KnowledgeBase`、领域测试、迁移表、EF Core 集成测试 | 已通过自动验证 | 无 |
| KP-MVP0-BE-007 实现知识版本生命周期 | 发布、下架、归档领域方法、应用服务集成测试、`ops/verify-mvp0-runtime.ps1` 真实 API 发布/下架/归档；系统 Chrome 创建版本并发布为当前版本 | 已通过自动、运行和页面发布验证 | 无 |
| KP-MVP0-BE-008 实现知识权限范围模型与授权校验 | 默认全员、部门、岗位、角色、用户、安全等级、越权详情和越权下载测试；运行时创建 All、Department、Position、Role、User 权限范围；系统 Chrome 权限范围页面新增“全员”范围成功 | 已通过自动、运行和页面配置验证 | 无 |
| KP-MVP0-BE-009 接入 ABP BlobStoring 与 MinIO 文件上传下载 | BlobStoring 配置、MinIO 健康检查、附件上传/下载/删除应用服务集成测试；运行时 MinIO 对象计数 Before=4、AfterUpload=65、AfterDelete=4；页面附件前置检查脚本已固化演示附件和手工验收路径 | 已通过自动和运行验证 | 页面级附件上传/下载/删除受 Chrome 扩展本地文件访问限制，需普通浏览器手工补验 |
| KP-MVP0-BE-010 实现知识库应用服务与 REST API | Swagger 路由、未登录拦截、知识生命周期/权限范围/附件 EF Core 集成测试、登录后真实 API 闭环；系统 Chrome 标签、分类、知识文档、版本发布和权限范围页面写入通过 | 已通过自动、运行和主要页面闭环验证 | 页面级附件上传/下载/删除需手工补验 |
| KP-MVP0-BE-011 实现 MVP-0 审计日志要求 | `UseAuditing()`、GET 审计、敏感类型排除、`ops/audit-check.ps1`、运行时 9 类业务方法审计落库、敏感字段 Hits=0 | 已通过自动和运行验证 | 无 |
| KP-MVP0-FE-001 建立 Blazor Web App 壳、菜单和权限导航 | Blazor 页面、菜单 `RequirePermissions`、端点 200、`_framework/dotnet.js` 200；系统 Chrome 登录后菜单和 admin 状态可见 | 已通过代码、端点和 Chrome 登录态验证 | 无 |
| KP-MVP0-FE-002 实现组织、部门、岗位管理页面 | Blazor Organization 页面、编译验证、后端集成测试；系统 Chrome 组织 4 个页面路由烟测通过，部门、岗位、职级、用户组织关系新增/编辑/删除通过，数字和布尔字段保存通过 | 已通过页面深度 CRUD 验证 | 无 |
| KP-MVP0-FE-003 实现知识分类与标签页面 | Blazor Knowledge 分类/标签页面、编译验证、后端集成测试；系统 Chrome 标签新增和知识分类新增成功；运行时脚本覆盖分类和标签新增、编辑、删除 | 已通过页面新增、列表验证和运行时 CRUD 验证 | 无 |
| KP-MVP0-FE-004 实现知识文档与版本管理页面 | Blazor 知识文档页面、版本操作、附件 UI、编译验证、后端集成测试；系统 Chrome 知识新增、版本创建、版本发布、版本号显示修复通过；API/MinIO 附件闭环通过 | 主要知识生命周期页面验收通过 | 页面级附件上传/下载/删除需普通浏览器手工补验 |
| KP-MVP0-FE-005 实现知识权限范围配置页面 | Blazor 权限范围页面、全员/部门/岗位/角色/指定用户/安全等级选择控件、默认全员提示、后端权限范围测试；系统 Chrome 为知识文档新增“全员”范围成功；运行时脚本覆盖 Department、Position、Role、User 范围创建 | 已通过页面配置、静态控件和运行时多类型范围验证 | 无 |
| KP-MVP0-FE-006 实现前端加载、失败、无权限和重试状态 | 页面中文错误、403 提示、重试入口、上传状态；系统 Chrome 8 个登录态页面烟测无错误 | 基础视觉和运行状态通过 | 文件上传控件需本地文件访问权限后补验 |
| KP-MVP0-AI-001 定义 RAG/AI 集成接口边界与禁用清单 | `docs/rag-ai-boundary.md`、`ops/verify-mvp0-static.ps1` 禁用依赖扫描 | 已通过静态验证 | 无 |
| KP-MVP0-AI-002 验证 Qdrant 与 LiteLLM 开发环境健康检查 | `docker-compose.yml`、`ops/health-check.ps1`、Qdrant/LiteLLM HTTP 健康检查 | 已通过运行验证 | 无 |
| KP-MVP0-AI-003 建立 MVP-1 RAG 前置风险清单 | `docs/rag-ai-boundary.md`、`docs/technical-architecture.md` 风险闸口 | 已完成 | 无 |
| KP-MVP0-DEVOPS-001 编写 Docker Compose 开发环境 | PostgreSQL、MinIO、Qdrant、LiteLLM 服务定义和数据卷；后台任务存储复用 PostgreSQL 的 `AbpBackgroundJobs` 表，MVP-0 不单独部署 Hangfire Worker/Dashboard | 已通过运行和静态验证 | 无 |
| KP-MVP0-DEVOPS-002 规范环境变量、密钥和本地配置模板 | `.env.example`、本地配置说明、启动参数 | 已完成 | 无 |
| KP-MVP0-DEVOPS-003 编写本地启动、健康检查和初始化脚本 | `ops/start-local.ps1`、`ops/health-check.ps1`、`ops/verify-mvp0.ps1` | 已完成 | 无 |
| KP-MVP0-DEVOPS-004 编写开发环境持久化与备份说明 | `docs/development-environment.md` | 已完成 | 无 |
| KP-MVP0-QA-001 编写 MVP-0 验收测试计划 | `docs/mvp-0-qa-test-plan.md`、系统 Chrome UI 验收记录、`ops/verify-mvp0.ps1 -WithRuntime` | 已完成并执行主要自动/页面验收 | 页面级附件文件选择仍需手工补验 |
| KP-MVP0-QA-002 编写权限边界测试用例 | QA 计划、领域测试、EF Core 集成测试 | 自动覆盖已补强 | 页面级权限验收 |
| KP-MVP0-QA-003 编写知识生命周期端到端测试用例 | QA 计划、EF Core 集成测试、系统 Chrome 知识新增/版本创建/发布验证 | 应用服务层和主要页面链路通过 | 附件页面链路另列 |
| KP-MVP0-QA-004 编写文件上传下载与审计测试用例 | QA 计划、附件集成测试、审计检查脚本、运行时上传/下载/删除和审计敏感字段扫描 | 已通过自动和运行验证 | 页面级文件选择受工具权限限制，需普通浏览器补验 |

## 4. 当前通过项

- 依赖服务健康：PostgreSQL、MinIO、Qdrant、LiteLLM。
- 后台任务预留：`AbpBackgroundJobsEntityFrameworkCoreModule` 已接入，`KnowPulseDbContext` 调用 `ConfigureBackgroundJobs()`，迁移包含 `AbpBackgroundJobs` 表；MVP-0 不单独启动 Hangfire Dashboard/Worker。
- API Host 端点：OpenID configuration、Swagger UI、Swagger JSON。
- Blazor 端点：根页面和 `_framework/dotnet.js` 静态资源。
- 未登录 API 访问：Organization 和 KnowledgeBase 核心 API 不返回 200。
- 静态契约：文档、Blazor 授权、菜单权限、应用服务权限守卫、审计配置、RAG/AI 禁用依赖扫描。
- 自动测试：领域测试、EF Core 应用服务集成测试。
- 登录后真实 API 闭环：admin cookie 登录、部门创建、岗位和职级创建更新、用户组织关系创建更新、分类和标签新增编辑删除、知识条目创建、版本发布、All/Department/Position/Role/User 权限范围创建、附件上传、附件下载、附件删除、版本下架和归档。
- 基础演示数据：`ops/seed-demo-data.ps1` 已生成稳定 `DEMO-*` 数据，覆盖 3 个部门、3 个岗位、3 个职级、3 个知识分类、3 个标签、3 个知识文档、3 个发布版本、5 个权限范围和 1 个演示附件；第二次执行无重复创建。
- 页面附件补验前置检查：`ops/check-page-attachment-prereqs.ps1` 已固化 `artifacts\demo-seed\knowpulse-demo-attachment.txt`、`/knowledge/items` 页面入口、`#attachment-file` 文件控件、Codex Chrome Extension 检查、native host 注册表/manifest 检查、插件官方检查和手工验收步骤。
- 系统 Chrome 登录后页面验证：8 个 MVP-0 页面渲染和关键控件通过，标签新增、知识分类新增、知识文档新增、版本创建、版本发布、权限范围新增通过；组织管理部门、岗位、职级、用户组织关系新增/编辑/删除通过。后续 Chrome 扩展 native host 注册异常导致无法继续补充更多页面交互，但相关 API 运行时验证已补强。
- 组织管理表单字段验证：部门安全等级 `2 -> 3`，岗位安全等级 `4 -> 5`，职级排序/安全等级 `1/6 -> 2/7`，用户组织关系安全等级 `8 -> 9`，主组织关系标记可创建并可取消。
- 知识版本号显示修复：页面显示 `v1` 和 `v1 - UI版本865207`，静态契约防止 `v@version.VersionNumber` 字面量回归。
- MinIO 写入和删除：运行时验证记录 `Before=4`、`AfterUpload=65`、`AfterDelete=4`。
- 审计落库：运行时验证覆盖 9 类业务方法，`audit-check.ps1` 可看到用户、时间、URL、服务名和方法名；敏感字段扫描 `Hits=0`。

## 5. 未完成验收

| 缺口 | 影响任务 | 原因 | 建议验证方式 |
| --- | --- | --- | --- |
| 页面级附件上传、下载、删除未自动化完成 | KP-MVP0-BE-009、KP-MVP0-FE-004、KP-MVP0-QA-004 | API/MinIO 层已由 `ops/verify-mvp0.ps1 -WithRuntime` 验证；文件上传授权已给，且 `ops/seed-demo-data.ps1` 已上传脚本生成的演示附件；`ops/check-page-attachment-prereqs.ps1` 已固化补验入口和前置检查；页面自动化受浏览器工具限制阻断：早前 `fileChooser.setFiles` 被本地文件访问权限阻止，本轮 Chrome 控制曾能列出标签页，但接管知识文档标签页超时，随后轻量 `openTabs` 重试也超时；预检已拆分 native host 注册表/manifest 结构和 `native host official check` 两个信号 | 执行 `.\ops\check-page-attachment-prereqs.ps1 -EnsureDemoSeed` 后，使用普通浏览器手工选择 `artifacts\demo-seed\knowpulse-demo-attachment.txt` 上传、下载、删除；或在确认后打开所选 Chrome 配置的新窗口重试，若仍失败则从 Codex 插件 UI 修复/重装 Chrome 插件并启用本地文件访问权限 |

## 6. MVP-0 完成判定

当前状态是“后端、API、权限、MinIO、审计、DevOps、RAG/AI 边界、QA 自动化、组织管理页面深度 CRUD 和主要 Blazor 登录后页面链路均已有当前证据”。主业务链路已可演示；但页面级附件上传/下载/删除受浏览器工具和 Chrome native host 限制，暂不宣称 MVP-0 全量验收关闭。
