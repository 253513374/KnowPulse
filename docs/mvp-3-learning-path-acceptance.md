# KnowPulse MVP-3 学习路径最小闭环验收记录

版本：v0.2
日期：2026-06-28
范围：学习路径、阅读记录、个人学习中心、必学知识、推荐知识和必考考试的最小闭环

## 1. 交付边界

本轮完成 `KP-MVP3-BE-001`、`KP-MVP3-BE-002` 和 `KP-MVP3-FE-001` 的最小闭环实现：

- 管理员可按全员、部门、岗位、职级配置学习路径。
- 学习路径可配置必学知识、推荐知识和必考考试。
- 必学/推荐知识绑定到已发布知识版本，避免知识后续修改影响已配置路径。
- 必考考试引用已发布考试计划。
- 学习路径支持创建、编辑、查询和停用。
- 普通用户只能查询当前用户被分配到的启用学习路径。
- 个人学习内容中的知识项继续叠加现有 `KnowledgePermissionScope` 可见性判断。
- 新增阅读记录，保存当前用户在学习路径下的知识版本阅读状态、最近阅读时间、完成时间和阅读次数。
- 新增 Blazor “我的学习”页面和菜单，展示必学知识、推荐知识、必考考试、完成状态和最近阅读时间。
- 学习中心支持跳转到知识文档指定版本预览，以及跳转到在线考试入口。
- 页面覆盖加载、空状态、错误和无权限提示。

不纳入本轮：

- 复杂报表和运营 BI。
- AI 推荐。
- 移动端页面。

## 2. 数据模型

| 模型 | 用途 |
| --- | --- |
| `LearningPath` | 学习路径主表，保存名称、说明、启用状态和配置版本 |
| `LearningPathAssignment` | 路径分配规则，支持 `All`、`Department`、`Position`、`JobLevel` |
| `LearningPathRequiredKnowledge` | 必学知识，保存知识条目和已发布知识版本 |
| `LearningPathRecommendedKnowledge` | 推荐知识，保存知识条目和已发布知识版本 |
| `LearningPathRequiredExam` | 必考考试，引用已发布考试计划 |
| `LearningKnowledgeReadRecord` | 用户在学习路径下的知识阅读记录，保存最近阅读时间、完成时间和阅读次数 |

迁移文件：

- `src/KnowPulse.EntityFrameworkCore/Migrations/20260628083615_Mvp3LearningPath.cs`
- `src/KnowPulse.EntityFrameworkCore/Migrations/20260628083615_Mvp3LearningPath.Designer.cs`
- `src/KnowPulse.EntityFrameworkCore/Migrations/20260628085925_Mvp3LearningCenter.cs`
- `src/KnowPulse.EntityFrameworkCore/Migrations/20260628085925_Mvp3LearningCenter.Designer.cs`

## 3. API 清单

| 方法 | 路由 | 权限 | 说明 |
| --- | --- | --- | --- |
| `GET` | `/api/learning/paths` | `KnowPulse.Learning.Manage` | 管理端分页查询学习路径 |
| `GET` | `/api/learning/paths/{id}` | `KnowPulse.Learning.Manage` | 管理端查询学习路径详情 |
| `POST` | `/api/learning/paths` | `KnowPulse.Learning.Manage` | 创建学习路径 |
| `PUT` | `/api/learning/paths/{id}` | `KnowPulse.Learning.Manage` | 编辑学习路径，整体替换分配和内容配置 |
| `POST` | `/api/learning/paths/{id}/deactivate` | `KnowPulse.Learning.Manage` | 停用学习路径 |
| `GET` | `/api/learning/paths/my` | `KnowPulse.Learning.View` | 查询当前用户被分配到的启用路径 |
| `GET` | `/api/learning/paths/my/{id}` | `KnowPulse.Learning.View` | 查询当前用户被分配到的单条路径 |
| `POST` | `/api/learning/paths/my/knowledge-read` | `KnowPulse.Learning.View` | 记录当前用户阅读或完成学习路径下的知识版本 |

## 4. Blazor 页面

| 页面 | 路由 | 菜单 | 权限 | 说明 |
| --- | --- | --- | --- | --- |
| 我的学习 | `/learning/my` | `学习路径 / 我的学习` | `KnowPulse.Learning.View` | 展示当前用户被分配到的学习路径、必学知识、推荐知识、必考考试、完成状态和最近阅读时间 |
| 知识文档深链 | `/knowledge/items?itemId={id}&versionId={id}` | 复用知识库菜单 | `KnowPulse.KnowledgeBase.Items` | 支持学习中心跳转后打开指定知识版本预览 |
| 在线考试入口 | `/exam/take` | 复用考试菜单 | `KnowPulse.Exam.Participate` | 支持学习中心跳转到考试入口，实际开始考试仍由考试模块分配规则校验 |

## 5. 验证命令

| 验证项 | 命令 | 当前结果 |
| --- | --- | --- |
| EF Core 项目构建 | `dotnet build src\KnowPulse.EntityFrameworkCore\KnowPulse.EntityFrameworkCore.csproj --no-restore -m:1 -p:UseSharedCompilation=false` | 通过 |
| Application 项目构建 | `dotnet build src\KnowPulse.Application\KnowPulse.Application.csproj --no-restore -m:1 -p:UseSharedCompilation=false` | 通过 |
| 解决方案构建 | `dotnet build KnowPulse.slnx --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false -nr:false` | 通过，13 个既有 NuGet 漏洞告警，0 错误 |
| 生成学习中心迁移 | `dotnet ef migrations add Mvp3LearningCenter --no-build` | 通过 |
| 更新数据库 | `dotnet ef database update --no-build` | 已应用 `20260628085925_Mvp3LearningCenter` |
| 迁移列表确认 | `dotnet ef migrations list --no-build` | 包含 `20260628085925_Mvp3LearningCenter` |
| 学习路径领域测试 | `dotnet test test\KnowPulse.Domain.Tests\KnowPulse.Domain.Tests.csproj --filter "FullyQualifiedName~LearningPathTests"` | 4 通过，0 失败 |
| 学习路径应用服务测试 | `dotnet test test\KnowPulse.EntityFrameworkCore.Tests\KnowPulse.EntityFrameworkCore.Tests.csproj --filter "FullyQualifiedName~Mvp3LearningPathAppServiceTests"` | 4 通过，0 失败 |
| Blazor Client 构建 | `dotnet build src\KnowPulse.Blazor.Client\KnowPulse.Blazor.Client.csproj --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false -nr:false` | 通过，0 警告，0 错误 |
| 前端路由可访问性 | `GET https://localhost:44307/learning/my` | 200，返回 Blazor HTML 壳 |
| API 路由可访问性 | `GET https://localhost:44305/swagger/index.html` | 200 |

说明：测试和解决方案构建仍输出既有 NuGet 漏洞告警，主要为 `Scriban 7.0.0` 和 `SQLitePCLRaw.lib.e_sqlite3 2.1.11`，不是本轮新增失败。迁移生成后第一次 `database update --no-build` 使用旧程序集误判已最新，重新构建 EF Core 项目后已实际应用 `20260628085925_Mvp3LearningCenter`。

## 6. 前端验收记录

| 状态 | 验收点 | 当前记录 |
| --- | --- | --- |
| 加载 | `/learning/my` 首屏加载时显示加载中 | Razor 构建通过；路由 GET 返回 200 |
| 空状态 | 无分配学习路径时显示“暂无分配给你的学习路径” | Razor 构建覆盖，登录态数据场景浏览器人工验收待补 |
| 错误 | API 异常时显示错误条和重试按钮 | Razor 构建覆盖，浏览器人工验收待补 |
| 无权限 | `Learning.View` 缺失或登录失效时显示无权限/未登录提示 | Razor 构建覆盖；未登录路由返回 Blazor 壳，登录态权限场景待补 |
| 跳转 | “去阅读”写入阅读记录并跳转知识版本预览，“去考试入口”跳转 `/exam/take` | 应用服务测试覆盖阅读记录，知识深链 Razor 构建通过 |

## 7. 剩余风险

| 风险 | 当前处理 | 后续建议 |
| --- | --- | --- |
| 学习路径变更只通过 `ConfigVersion` 标识版本 | 阅读记录按路径、知识条目和知识版本保存，不保存路径配置快照 | 后续如需要历史追溯，补充路径配置快照或历史查询 |
| 必考考试和考试模块原有分配可能不一致 | 学习路径会展示被路径分配的考试，但考试开始仍由 `ExamAppService` 自身分配规则兜底 | 管理页面后续应提示考试计划分配范围，避免配置冲突 |
| 知识详情跳转仍依赖知识库页面权限 | 学习中心只显示知识权限范围允许的内容；跳转目标页仍由 `KnowledgeBase.Items` 授权 | 后续可拆出只读知识详情页，降低对管理页权限的依赖 |
| 登录态浏览器人工验收尚未补齐 | 本轮已完成 Razor 构建、服务测试和未登录路由 200 抽验 | 用普通浏览器登录后补 `/learning/my` 数据态、空态和权限态验收 |
