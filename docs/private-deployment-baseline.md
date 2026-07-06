# KnowPulse MVP-4 私有化部署配置基线

版本：v0.1
日期：2026-07-01
对应任务：`KP-MVP4-DEVOPS-001 建立私有化部署配置基线`

## 1. 范围和边界

本基线把当前本地开发 Docker Compose 和 .NET appsettings 整理为可演示、可生产准备的私有化部署配置边界。

纳入范围：

- PostgreSQL、MinIO、Qdrant、LiteLLM、ABP HttpApi Host、Blazor Web App 的镜像、端口、数据卷、环境变量和密钥注入规则。
- 开发、演示、生产三套配置边界。
- 可重复执行的静态配置验证命令。

不纳入范围：

- Kubernetes。
- 微服务拆分。
- 备份恢复演练。
- 压测。
- OpenTelemetry、告警、回滚和后置增强功能的正式落地。

## 2. 部署拓扑

MVP-4 当前基线仍是模块化单体应用加独立基础设施：

| 组件 | 当前形态 | 生产准备要求 |
| --- | --- | --- |
| ABP HttpApi Host | .NET 进程或发布产物 | 通过 `ASPNETCORE_URLS`、`App__*`、`AuthServer__*`、`ConnectionStrings__Default` 等环境变量覆盖 |
| Blazor Web App | .NET 进程或发布产物 | 通过独立 `ASPNETCORE_URLS` 监听前端端口；发布时替换 `wwwroot/appsettings.json` 中的 API 地址 |
| PostgreSQL | Docker Compose service `postgres` | 固定主版本镜像；数据卷持久化；真实口令只来自 secret/env |
| MinIO | Docker Compose service `minio` | 固定 RELEASE tag；bucket 与访问密钥通过配置源注入 |
| Qdrant | Docker Compose service `qdrant` | 固定版本 tag；生产启用 API key；索引可由 PostgreSQL 和 MinIO 重建 |
| LiteLLM | Docker Compose service `litellm` | 固定 release tag 或 digest；上游模型 key 只通过 env/secret 注入 |

当前 `docker-compose.yml` 只管理依赖服务，不把 API Host 和 Blazor 打进容器。演示/生产可继续用发布后的 .NET 进程或在外部部署仓库封装镜像，但必须沿用本文的环境变量和密钥边界。

## 3. 环境边界

| 环境 | 用途 | 允许默认值 | 必须覆盖 |
| --- | --- | --- | --- |
| 开发 | 本机开发、自动化静态检查、MVP 回归 | `.env.example` 中的本地端口和本地占位口令 | 真实模型 key 可为空；`.env` 不提交 |
| 演示 | 客户演示、内网试点、非正式生产数据 | 固定镜像 tag、演示域名、演示数据脚本 | PostgreSQL、MinIO、LiteLLM、模型供应商 key、`StringEncryption__DefaultPassPhrase` 必须用演示 secret |
| 生产 | 正式私有化交付 | 无仓库内密钥默认值 | 所有口令、连接串、模型 key、加密口令、外部 URL、TLS 证书和 Qdrant API key 必须由环境变量或 secret 注入 |

开发默认值只为本地可运行服务准备。演示和生产不得复用 `myPassword`、`knowpulse-local-password`、`sk-local-dev-only`、`local-dev-only-change-before-demo` 等占位值。

## 4. 镜像基线

| 服务 | 默认镜像 | 规则 |
| --- | --- | --- |
| PostgreSQL | `postgres:18-alpine` | 固定主版本；主版本升级必须单独演练迁移 |
| MinIO | `minio/minio:RELEASE.2025-09-07T16-13-09Z` | 使用 RELEASE tag，不使用 `latest` |
| Qdrant | `qdrant/qdrant:v1.15.1` | 固定版本 tag；embedding 维度变化时新建 collection |
| LiteLLM | `ghcr.io/berriai/litellm:v1.89.4` | 固定 release tag；生产可替换为内部镜像 digest |

禁止在演示和生产使用 `latest`、`main`、`main-stable`、`rc`、`dev`、`nightly` 等浮动或预发布 channel tag。

## 5. 端口和访问边界

| 组件 | 默认外部端口 | 容器/进程端口 | 开发暴露 | 演示/生产建议 |
| --- | --- | --- | --- | --- |
| PostgreSQL | `5432` | `5432` | 本机可连 | 仅内网或宿主机本地访问，不暴露公网 |
| MinIO API | `9000` | `9000` | 本机可连 | 仅 API Host 可访问；外部下载必须走 ABP 权限校验 |
| MinIO Console | `9001` | `9001` | 本机可访问 | 仅管理员内网访问或关闭外部暴露 |
| Qdrant HTTP | `6333` | `6333` | 本机可连 | 仅 API Host 可访问，生产启用 API key |
| Qdrant gRPC | `6334` | `6334` | 本机可连 | 如未使用可不对外暴露 |
| LiteLLM | `4000` | `4000` | 本机可连 | 仅 API Host 可访问，使用 master/app key |
| API Host | `44305` | `44305` | HTTPS 本机开发证书 | 使用正式域名和 TLS；反向代理可终止 TLS |
| Blazor | `44307` | `44307` | HTTPS 本机开发证书 | 使用正式域名和 TLS；静态配置指向 API 正式域名 |

## 6. 数据卷

| Volume | 挂载点 | 内容 | 业务事实 |
| --- | --- | --- | --- |
| `knowpulse-postgres-data` | `/var/lib/postgresql` | PostgreSQL 数据目录 | 是 |
| `knowpulse-minio-data` | `/data` | 知识附件、导入/导出对象 | 是 |
| `knowpulse-qdrant-data` | `/qdrant/storage` | 向量索引和 payload | 否，可由 PostgreSQL + MinIO 重建 |

生产可以把 named volume 替换为宿主机路径或企业存储卷，但路径不得进入仓库。Qdrant 不是业务事实库，恢复策略以 PostgreSQL 和 MinIO 为准。

## 7. 环境变量和 secret 注入

### 7.1 PostgreSQL

| 配置 | 开发默认 | 演示/生产 |
| --- | --- | --- |
| `POSTGRES_IMAGE` | `postgres:18-alpine` | 固定 tag 或内部镜像 digest |
| `POSTGRES_USER` | `root` | 专用数据库账号 |
| `POSTGRES_PASSWORD` | `myPassword` | secret/env 注入 |
| `POSTGRES_DB` | `KnowPulse` | 按环境命名 |
| `KNOWPULSE_POSTGRES_PORT` | `5432` | 按内网端口规划 |
| `ConnectionStrings__Default` | 本机连接串 | secret/env 注入完整连接串 |

### 7.2 MinIO

| 配置 | 开发默认 | 演示/生产 |
| --- | --- | --- |
| `MINIO_IMAGE` | 固定 RELEASE tag | 固定 RELEASE tag 或内部镜像 digest |
| `MINIO_ROOT_USER` | `knowpulse` | 专用管理员账号 |
| `MINIO_ROOT_PASSWORD` | `knowpulse-local-password` | secret/env 注入 |
| `BlobStoring__Minio__BucketName` | `knowpulse-knowledge` | 按环境命名 |
| `BlobStoring__Minio__EndPoint` | `localhost:9000` | 内网 endpoint，不带协议 |
| `BlobStoring__Minio__AccessKey` | `knowpulse` | secret/env 注入或最小权限账号 |
| `BlobStoring__Minio__SecretKey` | `knowpulse-local-password` | secret/env 注入 |
| `BlobStoring__Minio__WithSSL` | `false` | 生产按实际 TLS 设置 |

### 7.3 Qdrant

| 配置 | 开发默认 | 演示/生产 |
| --- | --- | --- |
| `QDRANT_IMAGE` | `qdrant/qdrant:v1.15.1` | 固定 tag 或内部镜像 digest |
| `KNOWPULSE_QDRANT_HTTP_PORT` | `6333` | 内网端口 |
| `KNOWPULSE_QDRANT_GRPC_PORT` | `6334` | 内网端口或不暴露 |
| `QDRANT__SERVICE__API_KEY` | 空 | 生产必须 secret/env 注入 |
| `KNOWPULSE_QDRANT_API_KEY` | 空 | 与 Qdrant 服务端 key 同源注入 |
| `RagAI__VectorStore__Endpoint` | `http://localhost:6333` | 内网 endpoint |

### 7.4 LiteLLM

| 配置 | 开发默认 | 演示/生产 |
| --- | --- | --- |
| `LITELLM_IMAGE` | `ghcr.io/berriai/litellm:v1.89.4` | 固定 release tag、digest 或内部镜像 |
| `KNOWPULSE_LITELLM_PORT` | `4000` | 内网端口 |
| `LITELLM_MASTER_KEY` | `sk-local-dev-only` | secret/env 注入 |
| `KNOWPULSE_LITELLM_API_KEY` | `sk-local-dev-only` | API Host 使用的 app key，secret/env 注入 |
| `OPENAI_API_KEY` | 空 | embedding alias 使用，secret/env 注入 |
| `DEEPSEEK_API_KEY` | 空 | chat alias 使用，secret/env 注入 |
| `RagAI__LiteLlm__BaseUrl` | `http://localhost:4000` | 内网 endpoint |

`deploy/litellm/config.example.yaml` 只能保存模型 alias 和 `os.environ/...` 引用，不保存真实供应商 key。

### 7.5 API Host

| 配置 | 开发默认 | 演示/生产 |
| --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Development` | `Production` 或部署约定值 |
| `ASPNETCORE_URLS` | 由 launchSettings 使用 `https://localhost:44305` | 进程监听地址 |
| `App__SelfUrl` | `https://localhost:44305` | API 正式 URL |
| `App__CorsOrigins` | `https://localhost:44307` | Blazor 正式 URL |
| `App__RedirectAllowedUrls` | `https://localhost:44307` | Blazor 正式 URL |
| `AuthServer__Authority` | `https://localhost:44305` | API/Auth 正式 URL |
| `StringEncryption__DefaultPassPhrase` | 本地占位值 | secret/env 注入，生产禁止使用仓库默认值 |

### 7.6 Blazor

| 配置 | 开发默认 | 演示/生产 |
| --- | --- | --- |
| `ASPNETCORE_URLS` | 由 launchSettings 使用 `https://localhost:44307` | Blazor 进程监听地址 |
| `wwwroot/appsettings.json:AuthServer:Authority` | `https://localhost:44305` | 发布时替换为 API/Auth 正式 URL |
| `wwwroot/appsettings.json:RemoteServices:Default:BaseUrl` | `https://localhost:44305` | 发布时替换为 API 正式 URL |

Blazor Client 的 `wwwroot/appsettings.json` 是前端可见静态配置，只能放公开 endpoint 和 client id，不能放 secret。

## 8. 密钥规则

- 仓库只提交 `.env.example`、`deploy/litellm/config.example.yaml` 和本地开发 appsettings 默认值。
- 禁止提交 `.env`、`.env.*`、`appsettings.secrets.json`、`.pfx`、真实连接串、真实模型 key、真实对象存储 key。
- appsettings 中的 `RagAI:LiteLlm:ApiKeyEnvironmentVariable` 只保存变量名 `KNOWPULSE_LITELLM_API_KEY`。
- appsettings 中的 `RagAI:VectorStore:ApiKeyEnvironmentVariable` 只保存变量名 `KNOWPULSE_QDRANT_API_KEY`。
- 演示/生产密钥必须来自部署平台 secret、系统环境变量、受控 `.env` 文件或企业密钥系统，且该文件不得进入 Git。

## 9. 最小验证命令

开发基线静态检查：

```powershell
.\ops\verify-private-deployment-baseline.ps1
```

演示环境检查。执行前必须已经把演示密钥注入当前进程环境：

```powershell
.\ops\verify-private-deployment-baseline.ps1 -Environment Demo
```

生产环境检查。执行前必须已经把生产密钥注入当前进程环境：

```powershell
.\ops\verify-private-deployment-baseline.ps1 -Environment Production
```

Compose 模板解析：

```powershell
docker compose --env-file .env.example -f docker-compose.yml config --quiet
```

依赖服务健康检查：

```powershell
.\ops\health-check.ps1
```

LiteLLM/RAG 配置一致性检查：

```powershell
.\ops\verify-litellm-rag-config.ps1
```

Qdrant collection 检查：

```powershell
.\ops\verify-qdrant-knowledge-collection.ps1
```

## 10. 私有化准备步骤

1. 固定镜像 tag 或替换为内部镜像 digest。
2. 在部署平台创建 secret，不把真实值写入仓库。
3. 按第 7 节注入 PostgreSQL、MinIO、Qdrant、LiteLLM、API Host 和 Blazor 配置。
4. 执行 `docker compose --env-file <受控env文件> config --quiet`。
5. 启动依赖服务并执行 `.\ops\health-check.ps1`。
6. 使用生产连接串运行 `KnowPulse.DbMigrator`。
7. 启动 API Host 和 Blazor，并确认 Blazor 静态配置指向 API 正式 URL。
8. 执行 `.\ops\verify-private-deployment-baseline.ps1 -Environment Demo` 或 `-Environment Production`。
9. 执行现有 MVP 回归脚本，例如 `.\ops\verify-mvp1.ps1`、`.\ops\verify-mvp2.ps1`、`.\ops\verify-mvp3.ps1`，按环境选择是否运行需要真实服务的检查。

## 11. 剩余风险

- 备份、恢复和 Qdrant 重建演练属于 `KP-MVP4-DEVOPS-002`，本基线只定义数据事实边界。
- OpenTelemetry、告警和模型调用指标属于 `KP-MVP4-OBS-*`，本基线只保留配置入口。
- 性能压测和高频查询索引优化属于 `KP-MVP4-PERF-*`。
- 发布、回滚和迁移检查清单属于 `KP-MVP4-REL-*`。
- API Host 和 Blazor 尚未纳入当前 `docker-compose.yml`，生产镜像封装需要单独在部署仓库或后续任务中完成。
