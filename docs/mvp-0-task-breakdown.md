# KnowPulse MVP-0 开发任务拆分与分发

版本：v1.0  
日期：2026-06-24  
角色：架构任务拆分与开发分发官  
输入文档：

- `docs/system-architecture.md`
- `docs/technical-architecture.md`

## 1. 拆分范围和假设

### 1.1 拆分范围

本次只拆分 MVP-0：工程基础与知识库闭环。

MVP-0 目标：

- 建立 ABP + Blazor 工程基础。
- 建立 PostgreSQL、MinIO、Qdrant、LiteLLM、Hangfire 本地开发环境。
- 完成用户、部门、岗位、角色、基础权限。
- 完成知识分类、标签、文档上传、知识版本、发布、下架、归档。
- 完成知识权限范围：全员、部门、岗位、角色、指定用户、安全等级。
- 完成基础审计日志。

### 1.2 本次不包含

- 不实现权限化 RAG 问答。
- 不实现 DataIngestion pipeline。
- 不实现 VectorData/Qdrant 真实写入。
- 不实现 AI 出题、题库、考试、评分。
- 不引入 RabbitMQ。
- 不拆微服务。
- 不建设完整 BI 报表。

### 1.3 执行假设

- 默认主前端为 Blazor Web App。
- 后端为 ABP Framework + ASP.NET Core。
- PostgreSQL 是业务事实库。
- MinIO 负责文件存储。
- Qdrant 和 LiteLLM 在 MVP-0 只要求开发环境可启动和健康检查，不进入核心业务链路。
- 任务按 Backend、Frontend、RAG/AI、DevOps、QA 五类角色分发。
- 单个任务控制在 0.5 到 3 人日。

## 2. 任务总览

| ID | 标题 | 模块 | 负责人角色 | 优先级 | 预估 | 依赖 |
| --- | --- | --- | --- | --- | --- | --- |
| KP-MVP0-BE-001 | 初始化 ABP 解决方案与模块边界 | Engineering | Backend | P0 | 1.5d | 无 |
| KP-MVP0-BE-002 | 配置 PostgreSQL 与基础迁移链路 | Infrastructure | Backend | P0 | 1d | KP-MVP0-BE-001, KP-MVP0-DEVOPS-001 |
| KP-MVP0-BE-003 | 配置 Identity、租户与权限基础 | Identity | Backend | P0 | 1.5d | KP-MVP0-BE-001 |
| KP-MVP0-BE-004 | 实现 Organization 领域模型 | Organization | Backend | P0 | 2d | KP-MVP0-BE-002 |
| KP-MVP0-BE-005 | 实现 Organization 应用服务与权限点 | Organization | Backend | P0 | 2d | KP-MVP0-BE-003, KP-MVP0-BE-004 |
| KP-MVP0-BE-006 | 实现 KnowledgeBase 基础领域模型 | KnowledgeBase | Backend | P0 | 2d | KP-MVP0-BE-002 |
| KP-MVP0-BE-007 | 实现知识版本生命周期 | KnowledgeBase | Backend | P0 | 2d | KP-MVP0-BE-006 |
| KP-MVP0-BE-008 | 实现知识权限范围模型与授权校验 | KnowledgeBase | Backend | P0 | 2d | KP-MVP0-BE-004, KP-MVP0-BE-006 |
| KP-MVP0-BE-009 | 接入 ABP BlobStoring 与 MinIO 文件上传下载 | KnowledgeBase | Backend | P0 | 2d | KP-MVP0-DEVOPS-001, KP-MVP0-BE-006 |
| KP-MVP0-BE-010 | 实现知识库应用服务与 REST API | KnowledgeBase | Backend | P0 | 2d | KP-MVP0-BE-006, KP-MVP0-BE-007, KP-MVP0-BE-008, KP-MVP0-BE-009 |
| KP-MVP0-BE-011 | 实现 MVP-0 审计日志要求 | Audit | Backend | P1 | 1d | KP-MVP0-BE-005, KP-MVP0-BE-010 |
| KP-MVP0-FE-001 | 建立 Blazor Web App 壳、菜单和权限导航 | Frontend | Frontend | P0 | 1.5d | KP-MVP0-BE-003 |
| KP-MVP0-FE-002 | 实现组织、部门、岗位管理页面 | Organization | Frontend | P0 | 2d | KP-MVP0-BE-005, KP-MVP0-FE-001 |
| KP-MVP0-FE-003 | 实现知识分类与标签页面 | KnowledgeBase | Frontend | P0 | 1.5d | KP-MVP0-BE-010, KP-MVP0-FE-001 |
| KP-MVP0-FE-004 | 实现知识文档与版本管理页面 | KnowledgeBase | Frontend | P0 | 2.5d | KP-MVP0-BE-010, KP-MVP0-FE-003 |
| KP-MVP0-FE-005 | 实现知识权限范围配置页面 | KnowledgeBase | Frontend | P0 | 2d | KP-MVP0-BE-008, KP-MVP0-FE-004 |
| KP-MVP0-FE-006 | 实现前端加载、失败、无权限和重试状态 | Frontend | Frontend | P1 | 1d | KP-MVP0-FE-002, KP-MVP0-FE-004, KP-MVP0-FE-005 |
| KP-MVP0-AI-001 | 定义 RAG/AI 集成接口边界与禁用清单 | RAG/AI | RAG/AI | P1 | 1d | KP-MVP0-BE-001 |
| KP-MVP0-AI-002 | 验证 Qdrant 与 LiteLLM 开发环境健康检查 | RAG/AI | RAG/AI | P1 | 1d | KP-MVP0-DEVOPS-001 |
| KP-MVP0-AI-003 | 建立 MVP-1 RAG 前置风险清单 | RAG/AI | RAG/AI | P2 | 0.5d | KP-MVP0-AI-001, KP-MVP0-AI-002 |
| KP-MVP0-DEVOPS-001 | 编写 Docker Compose 开发环境 | Infrastructure | DevOps | P0 | 1.5d | 无 |
| KP-MVP0-DEVOPS-002 | 规范环境变量、密钥和本地配置模板 | Infrastructure | DevOps | P0 | 1d | KP-MVP0-DEVOPS-001 |
| KP-MVP0-DEVOPS-003 | 编写本地启动、健康检查和初始化脚本 | Infrastructure | DevOps | P1 | 1d | KP-MVP0-DEVOPS-001, KP-MVP0-BE-001 |
| KP-MVP0-DEVOPS-004 | 编写开发环境持久化与备份说明 | Infrastructure | DevOps | P2 | 0.5d | KP-MVP0-DEVOPS-001 |
| KP-MVP0-QA-001 | 编写 MVP-0 验收测试计划 | QA | QA | P0 | 1d | 任务总览确认 |
| KP-MVP0-QA-002 | 编写权限边界测试用例 | QA | QA | P0 | 1.5d | KP-MVP0-BE-008 |
| KP-MVP0-QA-003 | 编写知识生命周期端到端测试用例 | QA | QA | P0 | 1.5d | KP-MVP0-BE-010, KP-MVP0-FE-004 |
| KP-MVP0-QA-004 | 编写文件上传下载与审计测试用例 | QA | QA | P1 | 1d | KP-MVP0-BE-009, KP-MVP0-BE-011 |

## 3. 按角色分发

### 3.1 Backend

| 任务 ID | 任务标题 | 优先级 | 预估 | 依赖 |
| --- | --- | --- | --- | --- |
| KP-MVP0-BE-001 | 初始化 ABP 解决方案与模块边界 | P0 | 1.5d | 无 |
| KP-MVP0-BE-002 | 配置 PostgreSQL 与基础迁移链路 | P0 | 1d | KP-MVP0-BE-001, KP-MVP0-DEVOPS-001 |
| KP-MVP0-BE-003 | 配置 Identity、租户与权限基础 | P0 | 1.5d | KP-MVP0-BE-001 |
| KP-MVP0-BE-004 | 实现 Organization 领域模型 | P0 | 2d | KP-MVP0-BE-002 |
| KP-MVP0-BE-005 | 实现 Organization 应用服务与权限点 | P0 | 2d | KP-MVP0-BE-003, KP-MVP0-BE-004 |
| KP-MVP0-BE-006 | 实现 KnowledgeBase 基础领域模型 | P0 | 2d | KP-MVP0-BE-002 |
| KP-MVP0-BE-007 | 实现知识版本生命周期 | P0 | 2d | KP-MVP0-BE-006 |
| KP-MVP0-BE-008 | 实现知识权限范围模型与授权校验 | P0 | 2d | KP-MVP0-BE-004, KP-MVP0-BE-006 |
| KP-MVP0-BE-009 | 接入 ABP BlobStoring 与 MinIO 文件上传下载 | P0 | 2d | KP-MVP0-DEVOPS-001, KP-MVP0-BE-006 |
| KP-MVP0-BE-010 | 实现知识库应用服务与 REST API | P0 | 2d | KP-MVP0-BE-006, KP-MVP0-BE-007, KP-MVP0-BE-008, KP-MVP0-BE-009 |
| KP-MVP0-BE-011 | 实现 MVP-0 审计日志要求 | P1 | 1d | KP-MVP0-BE-005, KP-MVP0-BE-010 |

### 3.2 Frontend

| 任务 ID | 任务标题 | 优先级 | 预估 | 依赖 |
| --- | --- | --- | --- | --- |
| KP-MVP0-FE-001 | 建立 Blazor Web App 壳、菜单和权限导航 | P0 | 1.5d | KP-MVP0-BE-003 |
| KP-MVP0-FE-002 | 实现组织、部门、岗位管理页面 | P0 | 2d | KP-MVP0-BE-005, KP-MVP0-FE-001 |
| KP-MVP0-FE-003 | 实现知识分类与标签页面 | P0 | 1.5d | KP-MVP0-BE-010, KP-MVP0-FE-001 |
| KP-MVP0-FE-004 | 实现知识文档与版本管理页面 | P0 | 2.5d | KP-MVP0-BE-010, KP-MVP0-FE-003 |
| KP-MVP0-FE-005 | 实现知识权限范围配置页面 | P0 | 2d | KP-MVP0-BE-008, KP-MVP0-FE-004 |
| KP-MVP0-FE-006 | 实现前端加载、失败、无权限和重试状态 | P1 | 1d | KP-MVP0-FE-002, KP-MVP0-FE-004, KP-MVP0-FE-005 |

### 3.3 RAG/AI

| 任务 ID | 任务标题 | 优先级 | 预估 | 依赖 |
| --- | --- | --- | --- | --- |
| KP-MVP0-AI-001 | 定义 RAG/AI 集成接口边界与禁用清单 | P1 | 1d | KP-MVP0-BE-001 |
| KP-MVP0-AI-002 | 验证 Qdrant 与 LiteLLM 开发环境健康检查 | P1 | 1d | KP-MVP0-DEVOPS-001 |
| KP-MVP0-AI-003 | 建立 MVP-1 RAG 前置风险清单 | P2 | 0.5d | KP-MVP0-AI-001, KP-MVP0-AI-002 |

### 3.4 DevOps

| 任务 ID | 任务标题 | 优先级 | 预估 | 依赖 |
| --- | --- | --- | --- | --- |
| KP-MVP0-DEVOPS-001 | 编写 Docker Compose 开发环境 | P0 | 1.5d | 无 |
| KP-MVP0-DEVOPS-002 | 规范环境变量、密钥和本地配置模板 | P0 | 1d | KP-MVP0-DEVOPS-001 |
| KP-MVP0-DEVOPS-003 | 编写本地启动、健康检查和初始化脚本 | P1 | 1d | KP-MVP0-DEVOPS-001, KP-MVP0-BE-001 |
| KP-MVP0-DEVOPS-004 | 编写开发环境持久化与备份说明 | P2 | 0.5d | KP-MVP0-DEVOPS-001 |

### 3.5 QA

| 任务 ID | 任务标题 | 优先级 | 预估 | 依赖 |
| --- | --- | --- | --- | --- |
| KP-MVP0-QA-001 | 编写 MVP-0 验收测试计划 | P0 | 1d | 任务总览确认 |
| KP-MVP0-QA-002 | 编写权限边界测试用例 | P0 | 1.5d | KP-MVP0-BE-008 |
| KP-MVP0-QA-003 | 编写知识生命周期端到端测试用例 | P0 | 1.5d | KP-MVP0-BE-010, KP-MVP0-FE-004 |
| KP-MVP0-QA-004 | 编写文件上传下载与审计测试用例 | P1 | 1d | KP-MVP0-BE-009, KP-MVP0-BE-011 |

## 4. 详细任务

### KP-MVP0-BE-001 初始化 ABP 解决方案与模块边界

- 阶段：MVP-0
- 模块：Engineering
- 类型：Backend
- 负责人角色：Backend
- 优先级：P0
- 预估：1.5 人日
- 依赖：无
- 来源文档：`docs/system-architecture.md` 第 3、5、16 节；`docs/technical-architecture.md` 第 3、6、15 节

任务说明：

初始化 ABP + ASP.NET Core + Blazor Web App 解决方案，并建立 Organization、KnowledgeBase、KnowledgeIndexing、RagService、QuestionBank、Exam、Learning、AiOrchestration 的模块边界。MVP-0 只实现 Organization、KnowledgeBase、基础权限和审计，其余模块保留空模块或接口占位。

验收标准：

- 解决方案可以本地编译。
- 项目结构体现 ABP 模块边界。
- 默认主前端为 Blazor Web App。
- 暂缓模块不包含业务实现，只保留必要命名空间或占位说明。

完成定义：

- `global.json` 已固定 .NET SDK 主版本。
- 依赖版本不使用 floating version。
- 编译通过。
- README 或开发说明包含本地启动入口。

### KP-MVP0-BE-002 配置 PostgreSQL 与基础迁移链路

- 阶段：MVP-0
- 模块：Infrastructure
- 类型：Backend
- 负责人角色：Backend
- 优先级：P0
- 预估：1 人日
- 依赖：KP-MVP0-BE-001, KP-MVP0-DEVOPS-001
- 来源文档：`docs/system-architecture.md` 第 6 节；`docs/technical-architecture.md` 第 3.2、10 节

任务说明：

配置 ABP EF Core 使用 PostgreSQL，建立初始迁移链路，确保后续业务实体可以通过迁移落库。

验收标准：

- 应用连接 Docker Compose 中的 PostgreSQL。
- 初始迁移可以执行成功。
- 数据库连接字符串从配置读取，不硬编码。
- PostgreSQL 被明确作为业务事实库。

完成定义：

- 迁移命令或 DbMigrator 可运行。
- 本地数据库初始化文档已更新。
- 失败连接有明确错误提示。

### KP-MVP0-BE-003 配置 Identity、租户与权限基础

- 阶段：MVP-0
- 模块：Identity
- 类型：Backend
- 负责人角色：Backend
- 优先级：P0
- 预估：1.5 人日
- 依赖：KP-MVP0-BE-001
- 来源文档：`docs/system-architecture.md` 第 4.1、11 节；`docs/technical-architecture.md` 第 5.1、9 节

任务说明：

配置 ABP Identity、租户、角色和权限基础，为 Organization 与 KnowledgeBase 权限点提供基础设施。

验收标准：

- 支持管理员登录。
- 支持角色和权限点定义。
- 支持租户上下文。
- 前端菜单可按权限控制显示。

完成定义：

- 基础管理员账号可登录。
- PermissionDefinitionProvider 已配置 MVP-0 权限点。
- 权限相关接口有应用服务测试或集成测试。

### KP-MVP0-BE-004 实现 Organization 领域模型

- 阶段：MVP-0
- 模块：Organization
- 类型：Backend
- 负责人角色：Backend
- 优先级：P0
- 预估：2 人日
- 依赖：KP-MVP0-BE-002
- 来源文档：`docs/system-architecture.md` 第 4.2 节；`docs/technical-architecture.md` 第 6.1 节

任务说明：

实现部门、岗位、职级和用户组织关系基础模型，为知识权限范围提供部门、岗位和安全等级依据。

验收标准：

- 支持 Department、Position、JobLevel 基础实体。
- 支持用户与部门、岗位的关系。
- 支持安全等级字段或可扩展属性。
- 实体启用租户隔离。

完成定义：

- 数据库迁移已生成。
- 领域规则有单元测试。
- 基础数据可通过种子或 API 创建。

### KP-MVP0-BE-005 实现 Organization 应用服务与权限点

- 阶段：MVP-0
- 模块：Organization
- 类型：Backend
- 负责人角色：Backend
- 优先级：P0
- 预估：2 人日
- 依赖：KP-MVP0-BE-003, KP-MVP0-BE-004
- 来源文档：`docs/system-architecture.md` 第 4.1、4.2、7.1 节；`docs/technical-architecture.md` 第 9.2 节

任务说明：

提供部门、岗位、职级和用户组织关系的应用服务、DTO、权限点和基础 API。

验收标准：

- 管理员可以创建、编辑、查询部门和岗位。
- 无权限用户不能调用管理 API。
- API 返回分页结果。
- 删除或禁用组织数据时不破坏已有知识权限配置。

完成定义：

- 应用服务测试覆盖权限校验。
- OpenAPI 可见。
- 操作写入基础审计。

### KP-MVP0-BE-006 实现 KnowledgeBase 基础领域模型

- 阶段：MVP-0
- 模块：KnowledgeBase
- 类型：Backend
- 负责人角色：Backend
- 优先级：P0
- 预估：2 人日
- 依赖：KP-MVP0-BE-002
- 来源文档：`docs/system-architecture.md` 第 4.3、6.2 节；`docs/technical-architecture.md` 第 6.2 节

任务说明：

实现知识分类、标签、知识条目、知识版本、附件、反馈和阅读记录的基础领域模型。MVP-0 可先实现分类、标签、知识条目、知识版本、附件和权限范围。

验收标准：

- 支持 KnowledgeItem、KnowledgeVersion、KnowledgeAttachment、KnowledgeTag。
- KnowledgeVersion 支持 Draft、Published、Archived、Disabled 等基础状态。
- 知识实体启用租户隔离。
- 领域模型不直接依赖 Qdrant。

完成定义：

- 数据库迁移已生成。
- 领域模型单元测试覆盖状态初始值和基本不变量。
- 表命名和模块命名一致。

### KP-MVP0-BE-007 实现知识版本生命周期

- 阶段：MVP-0
- 模块：KnowledgeBase
- 类型：Backend
- 负责人角色：Backend
- 优先级：P0
- 预估：2 人日
- 依赖：KP-MVP0-BE-006
- 来源文档：`docs/system-architecture.md` 第 4.3、8.1 节；`docs/technical-architecture.md` 第 10.1、15.1 节

任务说明：

实现知识版本创建、发布、下架、归档和禁用的状态流转。MVP-0 只需要保证业务状态正确，不创建真实索引任务。

验收标准：

- Draft 可以发布为 Published。
- Published 可以下架或归档。
- 已归档版本不能再次发布。
- 同一知识条目只允许一个当前发布版本。

完成定义：

- 状态流转有单元测试。
- 并发发布有保护。
- 操作记录审计。

### KP-MVP0-BE-008 实现知识权限范围模型与授权校验

- 阶段：MVP-0
- 模块：KnowledgeBase
- 类型：Backend
- 负责人角色：Backend
- 优先级：P0
- 预估：2 人日
- 依赖：KP-MVP0-BE-004, KP-MVP0-BE-006
- 来源文档：`docs/system-architecture.md` 第 4.3、6.4、11.3 节；`docs/technical-architecture.md` 第 7.3、7.4、9.2 节

任务说明：

实现 KnowledgePermissionScope，支持全员、部门、岗位、角色、指定用户和安全等级限制。MVP-0 要用于知识列表和详情授权，MVP-1 继续用于 RAG 二次校验。

验收标准：

- 支持 All、Department、Position、Role、User 可见范围。
- 支持 securityLevel 限制。
- 用户只能查询和打开有权限的知识。
- 权限校验不依赖前端隐藏按钮。

完成定义：

- 权限边界测试覆盖部门、岗位、角色、用户和安全等级。
- API 层返回明确的无权限错误。
- 权限范围变更记录审计。

### KP-MVP0-BE-009 接入 ABP BlobStoring 与 MinIO 文件上传下载

- 阶段：MVP-0
- 模块：KnowledgeBase
- 类型：Backend
- 负责人角色：Backend
- 优先级：P0
- 预估：2 人日
- 依赖：KP-MVP0-DEVOPS-001, KP-MVP0-BE-006
- 来源文档：`docs/system-architecture.md` 第 4.3、6.1、11.2 节；`docs/technical-architecture.md` 第 3、13.3 节

任务说明：

使用 ABP BlobStoring 接入 MinIO，实现知识附件上传、下载和删除。下载必须经过 ABP 权限校验。

验收标准：

- 文件上传保存到 MinIO。
- 数据库保存附件元数据，不保存大文件内容。
- 无权限用户不能下载文件。
- 对象路径包含租户或业务隔离前缀。

完成定义：

- 上传下载接口测试通过。
- MinIO bucket 和路径规则已记录。
- 文件下载权限测试通过。

### KP-MVP0-BE-010 实现知识库应用服务与 REST API

- 阶段：MVP-0
- 模块：KnowledgeBase
- 类型：Backend
- 负责人角色：Backend
- 优先级：P0
- 预估：2 人日
- 依赖：KP-MVP0-BE-006, KP-MVP0-BE-007, KP-MVP0-BE-008, KP-MVP0-BE-009
- 来源文档：`docs/system-architecture.md` 第 7.2 节；`docs/technical-architecture.md` 第 17.1 节

任务说明：

实现知识分类、标签、知识条目、版本、发布、归档、文件上传和权限配置的应用服务与 REST API。

验收标准：

- 提供知识列表、详情、创建、版本创建、发布、归档、文件上传 API。
- 列表接口分页。
- 写操作使用 DTO，不直接暴露领域实体。
- API 有权限控制和审计。

完成定义：

- OpenAPI 可查看接口。
- 应用服务测试覆盖主要路径。
- 前端可直接调用完成知识闭环。

### KP-MVP0-BE-011 实现 MVP-0 审计日志要求

- 阶段：MVP-0
- 模块：Audit
- 类型：Backend
- 负责人角色：Backend
- 优先级：P1
- 预估：1 人日
- 依赖：KP-MVP0-BE-005, KP-MVP0-BE-010
- 来源文档：`docs/system-architecture.md` 第 11、14 节；`docs/technical-architecture.md` 第 9.3 节

任务说明：

补齐 MVP-0 关键操作审计，包括组织变更、知识上传、修改、发布、下架、归档、权限变更和文件下载。

验收标准：

- 关键操作记录操作者、租户、时间、业务 ID、操作类型。
- 不保存敏感文档原文到审计日志。
- 审计日志可用于排查权限和知识变更问题。

完成定义：

- 审计字段统一。
- 至少有基础查询方式或数据库验证方式。
- QA 可以基于审计日志验证操作路径。

### KP-MVP0-FE-001 建立 Blazor Web App 壳、菜单和权限导航

- 阶段：MVP-0
- 模块：Frontend
- 类型：Frontend
- 负责人角色：Frontend
- 优先级：P0
- 预估：1.5 人日
- 依赖：KP-MVP0-BE-003
- 来源文档：`docs/system-architecture.md` 第 5、10.3 节；`docs/technical-architecture.md` 第 3.1 节

任务说明：

建立 Blazor Web App 基础布局、导航菜单、登录状态和权限控制。菜单至少覆盖组织管理、知识分类、标签、知识库。

验收标准：

- 用户登录后看到与权限匹配的菜单。
- 无权限菜单不显示。
- 页面布局符合后台管理场景。
- 不引入 Vue 或 React 主前端。

完成定义：

- 本地可访问 Blazor 前端。
- 菜单权限和后端权限点对应。
- 基础页面加载无控制台错误。

### KP-MVP0-FE-002 实现组织、部门、岗位管理页面

- 阶段：MVP-0
- 模块：Organization
- 类型：Frontend
- 负责人角色：Frontend
- 优先级：P0
- 预估：2 人日
- 依赖：KP-MVP0-BE-005, KP-MVP0-FE-001
- 来源文档：`docs/system-architecture.md` 第 4.2 节；`docs/technical-architecture.md` 第 6.1 节

任务说明：

实现部门、岗位、职级和用户组织关系的基础管理页面。

验收标准：

- 支持列表、创建、编辑、启用/禁用。
- 列表分页。
- 表单校验清晰。
- 无权限用户看到无权限提示。

完成定义：

- 页面与后端 API 联调通过。
- 加载、保存、失败状态完整。
- QA 可按页面执行组织管理用例。

### KP-MVP0-FE-003 实现知识分类与标签页面

- 阶段：MVP-0
- 模块：KnowledgeBase
- 类型：Frontend
- 负责人角色：Frontend
- 优先级：P0
- 预估：1.5 人日
- 依赖：KP-MVP0-BE-010, KP-MVP0-FE-001
- 来源文档：`docs/system-architecture.md` 第 4.3 节；`docs/technical-architecture.md` 第 6.2 节

任务说明：

实现知识分类和标签管理页面，为知识文档创建提供基础数据。

验收标准：

- 支持分类和标签的增删改查。
- 支持分页或树形展示。
- 已被知识引用的数据删除时给出明确提示。

完成定义：

- 页面联调通过。
- 表单校验和错误提示完整。
- 权限控制生效。

### KP-MVP0-FE-004 实现知识文档与版本管理页面

- 阶段：MVP-0
- 模块：KnowledgeBase
- 类型：Frontend
- 负责人角色：Frontend
- 优先级：P0
- 预估：2.5 人日
- 依赖：KP-MVP0-BE-010, KP-MVP0-FE-003
- 来源文档：`docs/system-architecture.md` 第 4.3、8.1 节；`docs/technical-architecture.md` 第 15.1 节

任务说明：

实现知识条目、版本、附件上传、发布、归档和下架页面。

验收标准：

- 管理员可以创建知识条目。
- 可以上传附件并创建版本。
- 可以发布、下架、归档版本。
- 页面展示当前版本状态和索引状态占位。

完成定义：

- 文件上传进度和错误提示完整。
- 发布和归档操作有确认。
- 知识详情展示附件和版本记录。

### KP-MVP0-FE-005 实现知识权限范围配置页面

- 阶段：MVP-0
- 模块：KnowledgeBase
- 类型：Frontend
- 负责人角色：Frontend
- 优先级：P0
- 预估：2 人日
- 依赖：KP-MVP0-BE-008, KP-MVP0-FE-004
- 来源文档：`docs/system-architecture.md` 第 4.3、11.3 节；`docs/technical-architecture.md` 第 9.2 节

任务说明：

实现知识权限范围配置 UI，支持全员、部门、岗位、角色、指定用户和安全等级。

验收标准：

- 管理员可以选择权限范围类型。
- 可以选择部门、岗位、角色、用户。
- 可以设置安全等级。
- 保存后不同用户看到的知识列表不同。

完成定义：

- 选择控件支持搜索或分页。
- 无权限范围配置给出明确默认规则。
- 权限配置变更后页面状态刷新正确。

### KP-MVP0-FE-006 实现前端加载、失败、无权限和重试状态

- 阶段：MVP-0
- 模块：Frontend
- 类型：Frontend
- 负责人角色：Frontend
- 优先级：P1
- 预估：1 人日
- 依赖：KP-MVP0-FE-002, KP-MVP0-FE-004, KP-MVP0-FE-005
- 来源文档：`.agents/architecture-task-dispatcher.md` 质量检查清单；`docs/system-architecture.md` 第 10.3 节

任务说明：

统一 MVP-0 Blazor 页面中的加载、失败、无权限、空数据和重试状态。

验收标准：

- 列表加载时有明确状态。
- API 失败时显示可理解错误。
- 403 无权限显示无权限状态。
- 文件上传失败支持重试。

完成定义：

- 关键页面均接入统一状态组件或一致处理方式。
- QA 可覆盖失败路径。

### KP-MVP0-AI-001 定义 RAG/AI 集成接口边界与禁用清单

- 阶段：MVP-0
- 模块：RAG/AI
- 类型：RAG/AI
- 负责人角色：RAG/AI
- 优先级：P1
- 预估：1 人日
- 依赖：KP-MVP0-BE-001
- 来源文档：`docs/system-architecture.md` 第 4.5、5、15 节；`docs/technical-architecture.md` 第 5.2、5.3、18.2 节

任务说明：

定义 MVP-1 将要使用的 `IKnowledgeIngestionPipeline`、`IKnowledgeVectorStore`、`IKnowledgeRetriever` 接口边界和禁用清单。MVP-0 不实现真实 RAG，只建立防止 preview provider 渗透业务代码的约束。

验收标准：

- 明确 RAG/AI 相关接口命名和所在模块。
- 明确 MVP-0 禁止业务代码直接依赖 DataIngestion、Qdrant provider 和模型 SDK。
- 明确 MVP-1 需要实现的最小接口清单。

完成定义：

- 形成 Markdown 设计说明或代码接口占位。
- Backend 和 RAG/AI 对接口边界达成一致。

### KP-MVP0-AI-002 验证 Qdrant 与 LiteLLM 开发环境健康检查

- 阶段：MVP-0
- 模块：RAG/AI
- 类型：RAG/AI
- 负责人角色：RAG/AI
- 优先级：P1
- 预估：1 人日
- 依赖：KP-MVP0-DEVOPS-001
- 来源文档：`docs/system-architecture.md` 第 3.2、5、9.1 节；`docs/technical-architecture.md` 第 12.1 节

任务说明：

验证 Qdrant 和 LiteLLM 在开发环境中可启动、可访问，并为后续 MVP-1 接入提供健康检查地址和连接参数。

验收标准：

- Qdrant 健康检查通过。
- LiteLLM 健康检查通过。
- 连接参数通过配置读取。
- 不在代码或文档中提交真实密钥。

完成定义：

- 健康检查命令写入开发文档。
- DevOps 可以复现验证步骤。

### KP-MVP0-AI-003 建立 MVP-1 RAG 前置风险清单

- 阶段：MVP-0
- 模块：RAG/AI
- 类型：RAG/AI
- 负责人角色：RAG/AI
- 优先级：P2
- 预估：0.5 人日
- 依赖：KP-MVP0-AI-001, KP-MVP0-AI-002
- 来源文档：`docs/system-architecture.md` 第 15 节；`docs/technical-architecture.md` 第 18.2 节

任务说明：

整理 MVP-1 开始前必须确认的 RAG 风险，包括 DataIngestion preview、Qdrant provider preview、payload filter 能力、embedding 模型选择和权限二次校验测试。

验收标准：

- 风险清单覆盖 preview 依赖。
- 清单包含降级方案：直接 Qdrant SDK 或自研写入器。
- 清单包含权限越权测试要求。

完成定义：

- 风险清单提交到 docs 或任务系统。
- MVP-1 排期前完成评审。

### KP-MVP0-DEVOPS-001 编写 Docker Compose 开发环境

- 阶段：MVP-0
- 模块：Infrastructure
- 类型：DevOps
- 负责人角色：DevOps
- 优先级：P0
- 预估：1.5 人日
- 依赖：无
- 来源文档：`docs/system-architecture.md` 第 9.1、16 节；`docs/technical-architecture.md` 第 3.2、12.1、15.1 节

任务说明：

编写本地 Docker Compose，包含 PostgreSQL、MinIO、Qdrant、LiteLLM 和必要的数据卷。Hangfire 由应用使用 PostgreSQL 存储，MVP-0 不单独部署 worker 也可以先预留配置。

验收标准：

- 一条命令启动 PostgreSQL、MinIO、Qdrant、LiteLLM。
- 所有服务使用固定镜像 tag，不使用 latest。
- 数据卷持久化。
- 端口和账号密码通过环境变量或本地模板配置。

完成定义：

- 新人可以按文档启动依赖服务。
- 本地服务重启后数据不丢失。

### KP-MVP0-DEVOPS-002 规范环境变量、密钥和本地配置模板

- 阶段：MVP-0
- 模块：Infrastructure
- 类型：DevOps
- 负责人角色：DevOps
- 优先级：P0
- 预估：1 人日
- 依赖：KP-MVP0-DEVOPS-001
- 来源文档：`docs/system-architecture.md` 第 11.4 节；`docs/technical-architecture.md` 第 3.2、13.3 节

任务说明：

规范本地开发环境变量、连接字符串、MinIO 凭据、LiteLLM key 占位和 `.env.example`。

验收标准：

- 仓库只提交 `.env.example`，不提交真实密钥。
- 应用配置能从环境变量读取。
- 敏感配置有明确说明。

完成定义：

- `.gitignore` 覆盖本地密钥文件。
- 配置文档说明每个变量用途。

### KP-MVP0-DEVOPS-003 编写本地启动、健康检查和初始化脚本

- 阶段：MVP-0
- 模块：Infrastructure
- 类型：DevOps
- 负责人角色：DevOps
- 优先级：P1
- 预估：1 人日
- 依赖：KP-MVP0-DEVOPS-001, KP-MVP0-BE-001
- 来源文档：`docs/system-architecture.md` 第 9.1、14.1 节；`docs/technical-architecture.md` 第 15.1 节

任务说明：

提供本地启动、依赖健康检查和基础初始化脚本，支持开发人员快速验证环境是否可用。

验收标准：

- 可以检查 PostgreSQL、MinIO、Qdrant、LiteLLM 是否可访问。
- 可以输出清晰的失败原因。
- 脚本不依赖个人机器绝对路径。

完成定义：

- README 或开发文档包含使用说明。
- QA 可以使用脚本准备测试环境。

### KP-MVP0-DEVOPS-004 编写开发环境持久化与备份说明

- 阶段：MVP-0
- 模块：Infrastructure
- 类型：DevOps
- 负责人角色：DevOps
- 优先级：P2
- 预估：0.5 人日
- 依赖：KP-MVP0-DEVOPS-001
- 来源文档：`docs/system-architecture.md` 第 12.3 节；`docs/technical-architecture.md` 第 13.3 节

任务说明：

补充开发环境数据卷、PostgreSQL 备份、MinIO 数据目录和 Qdrant snapshot 的说明。MVP-0 不要求生产级备份自动化。

验收标准：

- 开发人员知道哪些目录保存数据。
- 提供清理和重建环境的步骤。
- 说明 Qdrant 可由业务数据重建，不是业务事实库。

完成定义：

- 文档已提交。
- 不包含生产密钥或真实客户数据。

### KP-MVP0-QA-001 编写 MVP-0 验收测试计划

- 阶段：MVP-0
- 模块：QA
- 类型：QA
- 负责人角色：QA
- 优先级：P0
- 预估：1 人日
- 依赖：任务总览确认
- 来源文档：`docs/system-architecture.md` 第 2.2、16 节；`docs/technical-architecture.md` 第 15.1 节

任务说明：

根据 MVP-0 目标编写验收测试计划，覆盖开发环境、组织权限、知识库、文件、权限范围和审计。

验收标准：

- 测试计划覆盖 MVP-0 所有验收项。
- 每个验收项映射到至少一个开发任务。
- 测试数据包含不同部门、岗位、角色和用户。

完成定义：

- 测试计划提交到 docs 或测试管理系统。
- 团队评审通过。

### KP-MVP0-QA-002 编写权限边界测试用例

- 阶段：MVP-0
- 模块：QA
- 类型：QA
- 负责人角色：QA
- 优先级：P0
- 预估：1.5 人日
- 依赖：KP-MVP0-BE-008
- 来源文档：`docs/system-architecture.md` 第 11.1、11.2、11.3 节；`docs/technical-architecture.md` 第 9.2 节

任务说明：

编写知识访问权限边界测试，覆盖全员、部门、岗位、角色、指定用户和安全等级。

验收标准：

- 覆盖有权限和无权限路径。
- 覆盖 API 直接调用，不只测前端隐藏按钮。
- 覆盖跨部门、跨岗位、跨角色。

完成定义：

- 测试用例可由 QA 手工执行或自动化实现。
- 每个失败结果有预期错误码或提示。

### KP-MVP0-QA-003 编写知识生命周期端到端测试用例

- 阶段：MVP-0
- 模块：QA
- 类型：QA
- 负责人角色：QA
- 优先级：P0
- 预估：1.5 人日
- 依赖：KP-MVP0-BE-010, KP-MVP0-FE-004
- 来源文档：`docs/system-architecture.md` 第 8.1、16 节；`docs/technical-architecture.md` 第 15.1 节

任务说明：

编写从创建知识、上传文件、创建版本、发布、下架、归档到权限验证的端到端测试用例。

验收标准：

- 覆盖知识版本正常流转。
- 覆盖非法状态流转。
- 覆盖发布后普通用户可见。
- 覆盖归档后不可继续作为当前版本。

完成定义：

- 用例包含测试数据准备和清理。
- 失败时能定位到前端、API 或数据问题。

### KP-MVP0-QA-004 编写文件上传下载与审计测试用例

- 阶段：MVP-0
- 模块：QA
- 类型：QA
- 负责人角色：QA
- 优先级：P1
- 预估：1 人日
- 依赖：KP-MVP0-BE-009, KP-MVP0-BE-011
- 来源文档：`docs/system-architecture.md` 第 11.4、14.2 节；`docs/technical-architecture.md` 第 9.3、13.3 节

任务说明：

编写文件上传、下载、无权限下载、文件元数据和审计记录的测试用例。

验收标准：

- 有权限用户可上传和下载。
- 无权限用户不能下载。
- 文件原文不进入审计日志。
- 上传、下载、发布、权限变更有审计记录。

完成定义：

- 用例明确文件大小、类型和异常场景。
- 审计验证方式可执行。

## 5. 验收矩阵

| 验收项 | 覆盖任务 | 验收方式 | 风险 |
| --- | --- | --- | --- |
| 本地一条命令可启动 PostgreSQL、MinIO、Qdrant、LiteLLM | KP-MVP0-DEVOPS-001, KP-MVP0-DEVOPS-003 | 脚本执行 + 手工验证 | 开发环境不一致 |
| ABP + Blazor 工程可运行 | KP-MVP0-BE-001, KP-MVP0-FE-001 | 本地运行 + 编译验证 | 工程基础阻塞 |
| 管理员可以登录并看到权限菜单 | KP-MVP0-BE-003, KP-MVP0-FE-001 | 手工验证 + 权限测试 | 认证/权限基础不稳 |
| 可以维护部门、岗位、角色相关基础数据 | KP-MVP0-BE-004, KP-MVP0-BE-005, KP-MVP0-FE-002 | API 测试 + 页面验证 | 权限范围无法落地 |
| 管理员可以上传文档并发布知识版本 | KP-MVP0-BE-006, KP-MVP0-BE-007, KP-MVP0-BE-009, KP-MVP0-BE-010, KP-MVP0-FE-004 | 端到端测试 | 知识库闭环不完整 |
| 不同部门、岗位、角色用户看到不同知识列表 | KP-MVP0-BE-008, KP-MVP0-FE-005, KP-MVP0-QA-002 | 权限边界测试 | 越权 |
| 文件下载必须经过 ABP 权限校验 | KP-MVP0-BE-008, KP-MVP0-BE-009, KP-MVP0-QA-004 | API 直接调用测试 | 文件泄露 |
| 关键操作有审计日志 | KP-MVP0-BE-011, KP-MVP0-QA-004 | 数据库验证 + 手工验证 | 无法追溯 |
| Qdrant 和 LiteLLM 开发服务可健康检查 | KP-MVP0-DEVOPS-001, KP-MVP0-AI-002 | 健康检查命令 | MVP-1 环境阻塞 |
| preview provider 不进入 MVP-0 业务代码 | KP-MVP0-AI-001 | 代码评审 | 架构边界污染 |

## 6. 主要风险

| 风险 | 影响 | 控制措施 | 覆盖任务 |
| --- | --- | --- | --- |
| ABP 工程初始化选择错误 | 后续模块和前端返工 | 先固定 .NET、ABP、Blazor、PostgreSQL 版本基线 | KP-MVP0-BE-001, KP-MVP0-BE-002 |
| 权限范围建模过粗 | MVP-1 RAG 二次校验难以复用 | MVP-0 就支持部门、岗位、角色、用户、安全等级 | KP-MVP0-BE-008 |
| 文件下载绕过权限 | 敏感知识泄露 | 所有下载走 ABP 应用服务权限校验 | KP-MVP0-BE-009, KP-MVP0-QA-004 |
| 前端只隐藏按钮、不做 API 权限 | 越权调用 | 后端应用服务权限点和 QA API 直接调用测试 | KP-MVP0-BE-003, KP-MVP0-QA-002 |
| MinIO/Qdrant/LiteLLM 环境不稳定 | 后续 RAG 阶段阻塞 | Docker Compose 固定 tag 和健康检查 | KP-MVP0-DEVOPS-001, KP-MVP0-AI-002 |
| preview RAG provider 提前进入业务代码 | 后续升级成本高 | MVP-0 只定义接口边界，不实现真实 provider | KP-MVP0-AI-001 |
| 审计缺失 | 无法追踪知识和权限变更 | MVP-0 明确关键操作审计 | KP-MVP0-BE-011 |

## 7. 建议执行顺序

1. DevOps 先完成 `KP-MVP0-DEVOPS-001`。
2. Backend 并行完成 `KP-MVP0-BE-001` 和 `KP-MVP0-BE-003`。
3. Backend 完成 PostgreSQL、Organization、KnowledgeBase 基础模型。
4. Frontend 在权限和基础 API 可用后开始 Blazor 页面。
5. RAG/AI 在开发环境可用后完成健康检查和 MVP-1 风险清单。
6. QA 从第一周开始写验收计划和权限测试，不等开发全部完成。

## 8. 暂缓清单

以下内容不要放入 MVP-0 开发任务：

- RAG 问答真实实现。
- DataIngestion pipeline 真实处理文档。
- VectorData/Qdrant 写入 chunk。
- AI 出题。
- 题库与考试。
- 主观题 AI 评分。
- RabbitMQ。
- 微服务拆分。
- 复杂防作弊。
