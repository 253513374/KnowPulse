# KnowPulse MVP-3 知识反馈和纠错闭环验收记录

版本：v0.1
日期：2026-06-28
范围：知识反馈提交、处理、关闭、关联知识版本和反馈管理页面

## 1. 交付边界

本轮完成 `KP-MVP3-BE-003` 和 `KP-MVP3-FE-002` 的最小闭环：

- 用户可基于自己可访问的知识文档提交问题、过期、错误、缺失和建议反馈。
- 反馈记录保存来源知识、来源版本、提交人、类型、说明、处理状态、处理人、处理结论、关闭原因和关联修订版本。
- 管理员可分页查询反馈、接手处理、关联同一知识下的修订版本、标记已处理和关闭反馈。
- 反馈处理不会直接修改已发布知识版本；修订仍通过知识版本草稿、发布流程完成。
- 新增知识反馈管理页面和知识文档页提交入口。
- 审计脚本增加知识反馈生命周期和敏感字段扫描。

不纳入本轮：

- 复杂质量报表。
- AI 自动推荐修订。
- 移动端专项适配。

## 2. 数据模型

| 模型 | 用途 |
| --- | --- |
| `KnowledgeFeedback` | 知识反馈主表，保存反馈来源、类型、状态、提交人、处理人、处理结论和关联修订版本 |
| `KnowledgeFeedbackType` | 反馈类型：`Problem`、`Outdated`、`Error`、`Missing`、`Suggestion` |
| `KnowledgeFeedbackStatus` | 状态：`Submitted`、`Processing`、`Resolved`、`Closed` |

迁移文件：

- `src/KnowPulse.EntityFrameworkCore/Migrations/20260628100956_Mvp3KnowledgeFeedback.cs`
- `src/KnowPulse.EntityFrameworkCore/Migrations/20260628100956_Mvp3KnowledgeFeedback.Designer.cs`

数据库确认：

- `__EFMigrationsHistory` 包含 `20260628100956_Mvp3KnowledgeFeedback`
- PostgreSQL 已创建 `AppKnowledgeFeedbacks`

## 3. API 清单

| 方法 | 路由 | 权限 | 说明 |
| --- | --- | --- | --- |
| `POST` | `/api/knowledge/feedback` | `KnowPulse.KnowledgeBase.Items` | 用户提交知识反馈，并叠加知识可见性校验 |
| `GET` | `/api/knowledge/feedback` | `KnowPulse.KnowledgeBase.Feedback` | 管理端分页查询反馈 |
| `GET` | `/api/knowledge/feedback/{id}` | `KnowPulse.KnowledgeBase.Feedback` | 管理端查询反馈详情 |
| `POST` | `/api/knowledge/feedback/{id}/process` | `KnowPulse.KnowledgeBase.Feedback` | 接手处理反馈 |
| `POST` | `/api/knowledge/feedback/{id}/link-version` | `KnowPulse.KnowledgeBase.Feedback` | 关联同一知识下的修订版本 |
| `POST` | `/api/knowledge/feedback/{id}/resolve` | `KnowPulse.KnowledgeBase.Feedback` | 标记反馈已处理 |
| `POST` | `/api/knowledge/feedback/{id}/close` | `KnowPulse.KnowledgeBase.Feedback` | 关闭反馈 |

## 4. Blazor 页面

| 页面 | 路由 | 权限 | 说明 |
| --- | --- | --- | --- |
| 知识反馈管理 | `/knowledge/feedback` | `KnowPulse.KnowledgeBase.Feedback` | 查询、查看、处理、关联版本、完成和关闭反馈 |
| 知识文档 | `/knowledge/items` | `KnowPulse.KnowledgeBase.Items` | 在当前知识和版本上下文提交反馈 |

页面状态：

- 管理页覆盖加载、空状态、错误和无权限提示。
- 关联版本输入做 Guid 前端校验；后端再次校验版本必须属于反馈来源知识。
- 知识文档页提交成功显示确认提示，不修改知识内容。

## 5. 验证命令

| 验证项 | 命令 | 当前结果 |
| --- | --- | --- |
| 解决方案构建 | `dotnet build KnowPulse.slnx --no-restore -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false -nr:false` | 通过，13 个既有 NuGet 漏洞告警，0 错误 |
| 生成迁移 | `dotnet ef migrations add Mvp3KnowledgeFeedback --no-build` | 通过 |
| 应用迁移 | `dotnet ef database update --no-build` | 已应用 `20260628100956_Mvp3KnowledgeFeedback` |
| 迁移落库确认 | 查询 `__EFMigrationsHistory` 和 `to_regclass('public."AppKnowledgeFeedbacks"')` | 已确认 |
| 领域测试 | `dotnet test test\KnowPulse.Domain.Tests\KnowPulse.Domain.Tests.csproj --filter "FullyQualifiedName~KnowledgeFeedbackTests"` | 4 通过，0 失败 |
| 应用服务测试 | `dotnet test test\KnowPulse.EntityFrameworkCore.Tests\KnowPulse.EntityFrameworkCore.Tests.csproj --filter "FullyQualifiedName~Mvp3KnowledgeFeedbackAppServiceTests"` | 3 通过，0 失败 |
| 审计检查 | `.\ops\audit-check.ps1 -FailOnSensitiveHits -Limit 10` | 通过；反馈生命周期为空态，反馈敏感字段扫描 0 命中 |
| API 页面烟测 | `GET https://localhost:44305/swagger/index.html`、`GET https://localhost:44307/knowledge/feedback` | 均返回 200 |

## 6. 验收方式

1. 启动 PostgreSQL 后确认迁移已应用。
2. 使用有 `KnowledgeBase.Items` 权限的用户进入 `/knowledge/items`，打开知识和版本，提交问题/过期/错误/缺失/建议反馈。
3. 使用有 `KnowledgeBase.Feedback` 权限的管理员进入 `/knowledge/feedback`，筛选反馈并查看详情。
4. 对反馈执行接手处理、关联同一知识下修订版本、标记已处理、关闭。
5. 回到知识版本，确认已发布版本正文和发布状态未因反馈处理直接改变。
6. 执行 `ops/audit-check.ps1`，查看知识反馈生命周期和敏感字段扫描。

## 7. 剩余风险

| 风险 | 当前处理 | 后续建议 |
| --- | --- | --- |
| 管理页关联版本使用版本 ID 输入 | 前端校验 Guid，后端校验同一知识归属 | 后续可增加同知识版本下拉选择或修订草稿创建入口 |
| 普通用户提交入口复用知识文档页权限 | 已叠加知识权限范围校验，不能对不可见知识提交反馈 | 后续拆出只读知识详情页后，可把反馈入口迁移到只读页 |
| 反馈描述可能含用户输入敏感内容 | 长度受控，审计脚本扫描不应出现知识正文和附件流字段 | 后续可增加敏感词脱敏和附件化反馈材料 |
