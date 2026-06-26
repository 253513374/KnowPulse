# KnowPulse 模块边界

版本：v0.1  
日期：2026-06-24  
对应任务：`KP-MVP0-BE-001`

KnowPulse MVP-0 使用 ABP 分层单体。模块边界先通过命名空间、权限名前缀、表命名前缀、菜单分组和应用服务边界约束，不在 MVP-0 拆分微服务。

| 模块 | MVP-0 状态 | 主要责任 | 约束 |
| --- | --- | --- | --- |
| Organization | 实现 | 部门、岗位、职级、用户组织关系 | 为知识权限范围提供组织维度 |
| KnowledgeBase | 实现 | 分类、标签、知识条目、版本、附件、权限范围 | 不直接依赖 Qdrant 或模型 SDK |
| KnowledgeIndexing | 占位 | 文档解析、切片、索引任务 | MVP-0 不实现真实索引 |
| RagService | 占位 | 权限感知检索、引用、二次校验 | MVP-0 不实现问答 |
| AiOrchestration | 占位 | Agent 编排和风险闸口 | MVP-0 不接业务主链路 |
| QuestionBank | 暂缓 | 题库与题目生成 | MVP-0 不实现 |
| Exam | 暂缓 | 组卷、考试、评分 | MVP-0 不实现 |
| Learning | 暂缓 | 学习记录和学习任务 | MVP-0 不实现 |

命名约定：

- 权限前缀：`KnowPulse.Organization.*`、`KnowPulse.KnowledgeBase.*`。
- Blazor 菜单前缀：`KnowPulse.Organization.*`、`KnowPulse.KnowledgeBase.*`。
- 数据库表前缀保持 ABP 当前 `App` 前缀，后续业务表使用清晰实体名。
- 预览或不稳定 AI provider 不进入 MVP-0 业务代码路径。
