# KnowPulse 本地开发环境

版本：v0.1  
日期：2026-06-24  
对应任务：`KP-MVP0-DEVOPS-001`、`KP-MVP0-DEVOPS-002`、`KP-MVP0-DEVOPS-003`、`KP-MVP0-BE-001`

## 1. 基线

- .NET SDK：`10.0.301`，由 `global.json` 固定。
- ABP CLI：`10.3.0`。
- 后端：ABP Framework + ASP.NET Core + EF Core。
- 前端：Blazor Web App，不引入 Vue 或 React 作为 MVP-0 主前端。
- 业务事实库：PostgreSQL。
- 文件存储：MinIO。
- 向量库：Qdrant，MVP-0 仅做健康检查。
- 模型网关：LiteLLM，MVP-0 仅做健康检查。
- LiteLLM 本地镜像默认使用固定 release tag `ghcr.io/berriai/litellm:v1.106.3`。如需生产级固定，可在 `.env` 或部署 secret 中替换为内部镜像摘要。

## 2. 依赖服务

复制环境变量模板：

```powershell
Copy-Item .env.example .env
```

启动本地依赖：

```powershell
docker compose up -d
```

一键启动全部依赖、健康检查并执行数据库迁移：

```powershell
.\ops\start-local.ps1
```

仅启动 MVP-0 核心业务依赖 PostgreSQL 和 MinIO：

```powershell
.\ops\start-local.ps1 -CoreOnly
```

常用跳过项：

```powershell
.\ops\start-local.ps1 -SkipDocker
.\ops\start-local.ps1 -SkipHealthCheck
.\ops\start-local.ps1 -SkipMigrator
```

如需覆盖迁移连接字符串：

```powershell
.\ops\start-local.ps1 -ConnectionString "Host=localhost;Port=5432;Database=KnowPulse;User ID=root;Password=myPassword;"
```

`start-local.ps1` 默认会为本地 OpenIddict 客户端写入以下地址：

- Blazor：`https://localhost:44307`
- Swagger：`https://localhost:44305`

如需覆盖本地前端或 Swagger 地址：

```powershell
.\ops\start-local.ps1 -BlazorRootUrl "https://localhost:44307" -SwaggerRootUrl "https://localhost:44305"
```

默认端口：

| 服务 | 地址 | 用途 |
| --- | --- | --- |
| PostgreSQL | `localhost:5432` | ABP 业务库和 Hangfire 预留存储 |
| MinIO API | `http://localhost:9000` | 文件对象存储 |
| MinIO Console | `http://localhost:9001` | 本地对象存储管理 |
| Qdrant | `http://localhost:6333` | MVP-1 向量检索预留 |
| LiteLLM | `http://localhost:4000` | MVP-1 模型网关预留 |

ABP BlobStoring 默认使用 MinIO：

| 配置键 | 默认值 | 说明 |
| --- | --- | --- |
| `BlobStoring:Minio:BucketName` | `knowpulse-knowledge` | 知识附件 bucket |
| `BlobStoring:Minio:EndPoint` | `localhost:9000` | MinIO API endpoint，不带协议 |
| `BlobStoring:Minio:AccessKey` | `knowpulse` | 本地访问账号 |
| `BlobStoring:Minio:SecretKey` | `knowpulse-local-password` | 本地访问密钥 |
| `BlobStoring:Minio:WithSSL` | `false` | 本地默认 HTTP |
| `BlobStoring:Minio:CreateBucketIfNotExists` | `true` | 开发环境自动创建 bucket |

本地如需覆盖，可使用 .NET 环境变量，例如：

```powershell
$env:BlobStoring__Minio__EndPoint="localhost:9000"
$env:BlobStoring__Minio__AccessKey="knowpulse"
$env:BlobStoring__Minio__SecretKey="knowpulse-local-password"
```

健康检查：

```powershell
.\ops\health-check.ps1
.\ops\health-check.ps1 -CoreOnly
```

MVP-4 私有化部署配置基线检查：

```powershell
.\ops\verify-private-deployment-baseline.ps1
```

该脚本默认只做仓库静态检查，不启动容器或应用。它会检查 Docker 镜像 tag、端口、数据卷、环境变量覆盖入口、LiteLLM/Qdrant 密钥变量名，以及 `.env`、`appsettings.secrets.json`、`.pfx` 等本地 secret 文件是否被 Git 跟踪。演示或生产环境在注入真实 secret 后可执行：

```powershell
.\ops\verify-private-deployment-baseline.ps1 -Environment Demo
.\ops\verify-private-deployment-baseline.ps1 -Environment Production
```

审计日志检查：

```powershell
.\ops\audit-check.ps1
```

MVP-1 RAG 回归和安全扫描：

```powershell
.\ops\verify-mvp1.ps1
```

该脚本串联 MVP-1 过滤测试、RAG provider 边界扫描、Qdrant collection 验证、LiteLLM alias 静态验证和 RAG 审计敏感字段扫描。若需要初始化缺失的本地 Qdrant collection：

```powershell
.\ops\verify-mvp1.ps1 -EnsureQdrant
```

如果只想执行纯代码和配置回归，可跳过依赖本地运行服务的步骤：

```powershell
.\ops\verify-mvp1.ps1 -SkipQdrant -SkipAudit
```

单独执行 MVP-1 测试过滤器：

```powershell
dotnet test .\KnowPulse.slnx --no-restore --filter "FullyQualifiedName~Mvp1" -v:m -m:1 -nr:false -p:BuildInParallel=false
```

MVP-3 学习路径最小闭环定向验证：

```powershell
dotnet test .\test\KnowPulse.Domain.Tests\KnowPulse.Domain.Tests.csproj --filter "FullyQualifiedName~LearningPathTests"
dotnet test .\test\KnowPulse.EntityFrameworkCore.Tests\KnowPulse.EntityFrameworkCore.Tests.csproj --filter "FullyQualifiedName~Mvp3LearningPathAppServiceTests"
dotnet build .\src\KnowPulse.Blazor.Client\KnowPulse.Blazor.Client.csproj --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false -nr:false
```

MVP-0 静态契约检查：

```powershell
.\ops\verify-mvp0-static.ps1
```

该脚本检查架构文档、Blazor 页面授权、菜单权限绑定、后端应用服务权限守卫、本地 OIDC 配置，以及 MVP-0 业务代码是否直接引入禁用的 RAG/AI provider。它不能替代登录后的浏览器验收，但可以作为提交前的快速守门。

MVP-0 综合验证：

```powershell
.\ops\verify-mvp0.ps1
```

该脚本串联 `dotnet test --no-build --no-restore`、依赖健康检查、静态契约检查、本地 API/Blazor 端点检查和审计检查。若需要在验证前先执行完整构建，先停止正在从 `bin\Debug` 输出目录运行的 API Host / Blazor，再执行：

```powershell
.\ops\verify-mvp0.ps1 -WithBuild
```

登录后真实业务闭环验证：

```powershell
.\ops\verify-mvp0-runtime.ps1
```

该脚本使用本地 `admin` 账号通过 Account 登录页获取 cookie 和 XSRF token，然后调用真实 API 完成部门、知识分类、知识条目、版本发布、权限范围、附件上传、附件下载、附件删除、版本下架和归档，并检查 MinIO 对象写入与 ABP 审计动作。它会写入本地开发数据库测试数据，默认使用 ABP 模板本地密码 `1q2w3E*`，可通过 `-AdminPassword` 覆盖：

```powershell
.\ops\verify-mvp0-runtime.ps1 -AdminPassword "<local-admin-password>"
```

如需在综合验证中一起执行真实业务闭环：

```powershell
.\ops\verify-mvp0.ps1 -WithRuntime
```

生成可重复使用的本地演示数据：

```powershell
.\ops\seed-demo-data.ps1
```

该脚本使用本地 `admin` 账号通过 Account 登录页获取 cookie 和 XSRF token，然后通过真实 API 幂等创建或更新 `DEMO-*` 基础数据：部门、岗位、职级、用户组织关系、知识分类、标签、知识条目、发布版本、权限范围，并上传脚本生成的 `artifacts\demo-seed\knowpulse-demo-attachment.txt` 演示附件。第二次执行会复用已有数据，不会重复创建同名基础数据。

也可以在综合验证中一起生成或复核演示数据：

```powershell
.\ops\verify-mvp0.ps1 -WithDemoSeed
```

页面级附件上传、下载、删除验收前置检查：

```powershell
.\ops\check-page-attachment-prereqs.ps1
```

该脚本会生成安全的本地演示附件 `artifacts\demo-seed\knowpulse-demo-attachment.txt`，检查 Blazor 知识文档页面、API 登录页、Codex Chrome Extension 安装状态、native host 本机注册表/manifest 结构和插件官方检查结果，并输出手工页面验收路径。若希望在检查前一并补齐 `DEMO-*` 演示数据，可执行：

```powershell
.\ops\check-page-attachment-prereqs.ps1 -EnsureDemoSeed
```

默认模式下，端点不可达、Chrome native host 或本地文件访问权限会以 Warn 输出，方便在普通浏览器中继续手工补验。若要把页面和浏览器自动化前置条件作为硬失败，可使用：

```powershell
.\ops\check-page-attachment-prereqs.ps1 -RequireEndpoints -RequireBrowserAutomation
```

如需通过 Codex Chrome 自动化完成页面文件选择，还需要 Chrome 插件 native host 官方检查通过，并在 Chrome 扩展详情中启用本地文件访问权限；否则使用普通浏览器手工选择上述演示附件完成上传、下载和删除补验。

默认通过 `docker compose exec postgres psql` 查询 `AbpAuditLogs`、`AbpAuditLogActions` 和 `AbpEntityPropertyChanges`。如果使用本机 `psql`，可改用：

```powershell
.\ops\audit-check.ps1 -LocalPsql
```

知识反馈闭环的审计验证可使用：

```powershell
.\ops\audit-check.ps1 -FailOnSensitiveHits -Limit 10
```

脚本会额外输出最近知识反馈生命周期、反馈相关审计动作，以及反馈审计参数中是否出现 `ContentText`、附件流或超长处理字段。

关键参数：

| 参数 | 默认值 | 说明 |
| --- | --- | --- |
| `Limit` | `20` | 最近审计动作和方法统计返回条数 |
| `ServiceNameLike` | `%KnowPulse%` | 按应用服务名过滤 |
| `MethodNameLike` | `%` | 按方法名过滤 |
| `LocalPsql` | `false` | 使用本机 `psql` 而不是 Docker 容器内 `psql` |

## 3. 数据库初始化

启动 PostgreSQL 后执行：

```powershell
dotnet run --project .\src\KnowPulse.DbMigrator\KnowPulse.DbMigrator.csproj
```

EF Core 迁移需要先在 `src\KnowPulse.EntityFrameworkCore` 生成迁移，再重新构建 EF 项目后应用数据库。使用 `--no-build` 更新数据库时，必须确认迁移已经编译进程序集：

```powershell
Push-Location .\src\KnowPulse.EntityFrameworkCore
dotnet ef migrations add Mvp3KnowledgeFeedback --no-build
Pop-Location
dotnet build .\src\KnowPulse.EntityFrameworkCore\KnowPulse.EntityFrameworkCore.csproj --no-restore -m:1 -p:UseSharedCompilation=false
Push-Location .\src\KnowPulse.EntityFrameworkCore
dotnet ef database update --no-build
Pop-Location
```

当前默认连接字符串：

```text
Host=localhost;Port=5432;Database=KnowPulse;User ID=root;Password=myPassword;
```

本地如需覆盖连接字符串，使用 .NET 环境变量：

```powershell
$env:ConnectionStrings__Default="Host=localhost;Port=5432;Database=KnowPulse;User ID=root;Password=myPassword;"
```

## 4. 启动应用

首次启动 API Host 前，先安装 ABP MVC 静态资源。缺少 `wwwroot/libs` 时，Swagger 或 Account 页面可能返回 500。

```powershell
Push-Location .\src\KnowPulse.HttpApi.Host
abp install-libs
Pop-Location
```

先启动 API Host：

```powershell
dotnet run --project .\src\KnowPulse.HttpApi.Host\KnowPulse.HttpApi.Host.csproj
```

再启动 Blazor：

```powershell
dotnet run --project .\src\KnowPulse.Blazor\KnowPulse.Blazor.csproj
```

默认地址：

- API / Swagger：`https://localhost:44305`
- Blazor：`https://localhost:44307`

如果本机 `dotnet run` 因 `obj` 缓存文件锁或访问拒绝失败，可先构建到独立输出目录，再从 Host 项目目录直接运行 DLL：

```powershell
dotnet build .\src\KnowPulse.HttpApi.Host\KnowPulse.HttpApi.Host.csproj --no-restore -p:OutDir=.\artifacts\verify-host-build\
Push-Location .\src\KnowPulse.HttpApi.Host
dotnet .\artifacts\verify-host-build\KnowPulse.HttpApi.Host.dll --urls https://localhost:44305
Pop-Location
```

## 5. 密钥规则

- 只提交 `.env.example`。
- 不提交 `.env`、`appsettings.secrets.json`、真实模型密钥、真实对象存储密钥。
- 仅做基础健康检查时可不填模型密钥；教学资料处理使用千问 `text-embedding-v4`，须在本地 `.env` 填写 `DASHSCOPE_API_KEY`。`DASHSCOPE_API_BASE` 默认北京地域的兼容接口；不同地域须使用对应密钥和地址。修改后执行 `docker compose up -d --no-deps litellm`，仅重启容器不能更新环境变量。
- LiteLLM 的 MVP-1 应用访问 key 使用 `KNOWPULSE_LITELLM_API_KEY`，真实供应商 key 只放在本地 `.env` 或安全配置源。
- Qdrant 在 MVP-0/MVP-1 都不保存业务事实数据；MVP-1 索引可由 PostgreSQL 和 MinIO 重建。

## 6. 数据持久化

Docker volume：

| Volume | 内容 | 是否业务事实 |
| --- | --- | --- |
| `knowpulse-postgres-data` | PostgreSQL 数据，挂载到容器 `/var/lib/postgresql` | 是 |
| `knowpulse-minio-data` | 上传文件对象 | 是 |
| `knowpulse-qdrant-data` | 向量索引 | 否，可重建 |

## 7. 本地备份与恢复

PostgreSQL 备份：

```powershell
docker exec knowpulse-postgres pg_dump -U root -d KnowPulse -Fc -f /tmp/knowpulse.dump
docker cp knowpulse-postgres:/tmp/knowpulse.dump .\deploy\backups\knowpulse.dump
```

PostgreSQL 恢复：

```powershell
docker cp .\deploy\backups\knowpulse.dump knowpulse-postgres:/tmp/knowpulse.dump
docker exec knowpulse-postgres pg_restore -U root -d KnowPulse --clean --if-exists /tmp/knowpulse.dump
```

MinIO 本地对象保存在 `knowpulse-minio-data` volume。开发环境如需保留上传文件，优先使用 MinIO Console 下载 bucket，或在停止容器前使用 `mc mirror` 将 `knowpulse-knowledge` bucket 镜像到本地备份目录。

Qdrant 在 MVP-0 只作为 MVP-1 环境占位；MVP-1 也只保存可重建向量索引，不保存业务事实。真实索引可由 PostgreSQL 和 MinIO 重建；如需临时保留，可使用 Qdrant snapshot 机制导出。

## 8. 清理

清理本地依赖和数据：

```powershell
docker compose down -v
```

执行前确认没有需要保留的本地测试数据。
