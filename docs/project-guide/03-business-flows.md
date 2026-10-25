# 核心业务流程

[返回导读](README.md)

## 知识发布与异步索引

```mermaid
sequenceDiagram
    participant admin as 管理员
    participant ui as 上传引导
    participant app as KnowledgeItemAppService
    participant blob as MinIO
    participant db as PostgreSQL
    participant worker as 索引 worker
    participant status as 任务查询 API
    admin->>ui: 选择正文或附件，填写信息和访问范围
    ui->>app: 创建知识条目与草稿版本
    app->>db: 保存草稿
    opt 有附件
        ui->>app: 上传附件
        app->>blob: 保存文件
        app->>db: 保存附件元数据
    end
    ui->>app: PublishVersionWithAccessPolicyAsync
    app->>app: 校验发布权限和完整访问策略
    app->>db: 同一工作单元替换策略、切换版本、失效旧索引、创建任务
    db-->>ui: 发布结果与 Pending 任务
    worker->>db: 轮询 Pending 和到期 Failed 任务
    worker->>worker: 解析、分块、Embedding、写入向量库
    worker->>db: 记录执行阶段、失败或 Indexed
    ui->>status: 查询任务状态
    status->>db: 读取任务进度
    db-->>status: 任务状态
    status-->>ui: 进度与结果
```

界面通过 API 查询任务状态。附件保存早于原子发布，Blob 不参与关系型数据库事务；发布失败后保留可修复草稿。旧 `PublishVersionAsync` 为兼容调用会在空策略时补全员规则，新引导走显式策略入口。

**发布成功**表示版本、访问策略和待执行任务已经保存。**可检索**还需要摄取、Embedding、向量写入全部成功，并通过检索时的状态和权限复核。

源码：[上传引导](../../src/KnowPulse.Blazor.Client/Pages/Knowledge/Upload.razor)、[发布服务](../../src/KnowPulse.Application/KnowledgeBase/KnowledgeItemAppService.cs)、[原子发布 ADR](../adr/0005-atomic-knowledge-publish-with-explicit-access-policy.md)。

## 索引执行与恢复

```mermaid
flowchart TB
    queued["Pending 任务"] --> reader["reader：读取正文和附件"]
    reader --> processor["processor：规范化 Markdown"]
    processor --> chunker["chunker：分块与重叠"]
    chunker --> persistence["persistence：写入 PostgreSQL 分块"]
    persistence --> embedding["embedding：LiteLLM 向量与记录"]
    embedding --> vectorStore["vector-store：验证集合并 upsert"]
    vectorStore --> indexed["Indexed"]
    reader --> failed["Failed：阶段与安全错误摘要"]
    embedding --> failed
    vectorStore --> failed
    failed --> retry["预算内按退避时间重试"]
    retry --> reader
```

这是阶段主线，其他阶段异常也会标记失败。当前分块目标为 1,200 字符，重叠 120 字符；这是字符策略，不是语义分块。worker 默认每 5 秒轮询、每批最多 5 项、逐项处理，配置可覆盖。

```mermaid
stateDiagram-v2
    [*] --> Pending
    Pending --> Processing: 开始处理
    Processing --> Indexed: 完整管道成功
    Processing --> Failed: 记录失败
    Failed --> Processing: 到期且仍有重试预算
    Failed --> Pending: 手工重试派生新任务
    Processing --> Pending: 管理员恢复派生新任务
    Indexed --> Outdated: 版本或权限变化
    Pending --> Outdated: 发布事实失效
    Processing --> Outdated: 发布事实失效
    Failed --> Outdated: 发布事实失效
    Indexed --> Deleted: 删除索引结果的领域状态
```

标有“派生新任务”的边表示 `RetryOfJobId` 来源关系，不是把原记录直接改回 Pending。自动失败重试复用原任务，延迟按指数增长，上限 30 分钟；默认最大重试次数为 3。恢复入口先把原任务记为失败再创建新任务；源码没有据此证明已经中断原执行进程，不能把它当作强制取消能力。

正文和 `.txt/.md/.markdown` 可直接处理；PDF、Office 等依赖启用且可用的 MarkItDown。当前 reader 遇到不支持的附件会让任务失败，**不会自动跳过图片、视频或压缩包**。混合附件需特别检查；“仅附件”不等于后台一定能完成索引。

源码：[worker](../../src/KnowPulse.HttpApi.Host/KnowledgeIndexing/KnowledgeIndexJobBackgroundWorker.cs)、[处理器](../../src/KnowPulse.Application/KnowledgeIndexing/KnowledgeIndexJobProcessor.cs)、[摄取](../../src/KnowPulse.Application/RagAI/KnowledgeIngestionPipeline.cs)、[Embedding](../../src/KnowPulse.Application/RagAI/KnowledgeEmbeddingPipeline.cs)、[任务领域模型](../../src/KnowPulse.Domain/KnowledgeIndexing/KnowledgeIndexJob.cs)。

## 权限感知问答

```mermaid
flowchart TB
    request["用户问题"] --> principal["验证身份与当前主体"]
    principal --> context["服务端组织、角色、安全等级"]
    context --> embed["问题 Embedding"]
    embed --> candidates["Qdrant 权限快照粗过滤"]
    candidates --> check["PostgreSQL 二次校验"]
    check --> decision{"存在可访问的当前发布分块？"}
    decision -->|"否"| refusal["依据不足，拒答并记录"]
    decision -->|"是"| prompt["最多 5 个授权片段组成上下文"]
    prompt --> chat["LiteLLM Chat"]
    chat --> status{"生成成功？"}
    status -->|"是"| answer["回答、引用、置信度、人工复核标记"]
    status -->|"否"| fallback["基于授权片段的确定性降级回答"]
    answer --> audit["调用元数据与引用审计"]
    fallback --> audit
    refusal --> audit
```

二次校验同时检查租户、条目有效性、当前发布版本、分块快照和实时知识权限。向量库中的旧权限快照不能覆盖主库事实。检索/Embedding 故障时返回保守结果；Chat 故障且已有授权片段时可降级展示依据。

引用保留知识、版本、分块标识；审计保留问题 hash、受限摘要和引用 metadata，不保存完整 prompt 或完整文档。置信度来自检索分数计算，不是模型正确率保证。

源码：[检索](../../src/KnowPulse.Application/RagAI/KnowledgeRetriever.cs)、[回答](../../src/KnowPulse.Application/RagAI/RagAnswerService.cs)、[审计写入](../../src/KnowPulse.Application/RagAI/RagCallLogWriter.cs)。

## AI 出题与人工审核

```mermaid
flowchart TB
    input["知识范围、题型、难度和数量"] --> retrieval["授权 RAG 检索"]
    retrieval --> generate["提示模板与 Chat 生成"]
    generate --> validate["解析和校验候选 JSON"]
    validate --> candidate["候选题与引用记录"]
    candidate --> adopt["人工采纳为草稿"]
    manual["人工新建"] --> draft["Draft"]
    adopt --> draft
    draft --> pending["PendingReview"]
    pending --> approved["Approved：正式题库"]
    pending --> returned["ReturnedForRevision 或 Rejected"]
    returned --> draft
    approved --> exam["新试卷组卷"]
    approved --> disabled["Disabled 或 Archived"]
```

AI 出题任务当前在创建请求内同步执行；任务实体和“索引后台 worker”是两种实现，不能混为一谈。没有授权上下文时任务失败，不让模型凭空生成。兼容接口 `SubmitCandidateForReviewAsync` 当前只采纳为草稿，不自动送审。题目停用/修订影响新组卷，已生成试卷使用快照。

源码：[生成任务](../../src/KnowPulse.Application/AiQuestioning/AiQuestionGenerationAppService.cs)、[题库工作流](../../src/KnowPulse.Application/QuestionBank/QuestionAppService.cs)、[题目领域规则](../../src/KnowPulse.Domain/QuestionBank/Question.cs)。

## 考试与评分

```mermaid
sequenceDiagram
    participant admin as 管理员
    participant app as ExamAppService
    participant db as PostgreSQL
    participant user as 被分配的考生
    admin->>app: 固定题目或按规则组卷，创建计划与分配
    app->>db: 保存试卷、题目和答案快照
    user->>app: 查询我的计划并开始考试
    app->>app: 检查计划窗口、分配和次数
    app->>db: 创建或复用作答记录
    user->>app: 分批保存答案及客户端版本
    app->>app: 检查作答归属、到期和试卷题目
    app->>db: 保存答案与版本
    user->>app: 提交作答
    app->>app: 依快照执行客观题评分
    app->>db: 保存成绩、错题和 Scored 状态
    app-->>user: 返回作答与结果 DTO
```

评分支持精确匹配及配置的多选部分得分；它不依赖当前题库答案被后来修改。最后一条返回映射当前存在答案披露和结果可见性问题，见[审计第 1 项](06-audit.md#1-考生响应直接包含标准答案和解析)。过期作答也不等于已实现后台自动交卷，见第 2 项。

源码：[考试应用服务](../../src/KnowPulse.Application/Exam/ExamAppService.cs)、[作答领域模型](../../src/KnowPulse.Domain/Exam/ExamAttempt.cs)、[答题页面](../../src/KnowPulse.Blazor.Client/Pages/Exam/Take.razor)。

## 学习路径

```mermaid
flowchart TB
    configure["配置必学、推荐知识与必考计划"] --> assign["按全员、部门、岗位、职级分配"]
    assign --> mine["当前用户查询我的路径"]
    mine --> filter["校验路径分配和知识可见范围"]
    filter --> content["展示可访问知识及考试状态"]
    content --> read["记录指定知识版本阅读或完成"]
    read --> progress["按路径、用户、知识和版本归并进度"]
    content --> exam["参加已分配考试"]
    exam --> progress
```

学习路径分配不授予知识访问权，也不替代考试分配。阅读记录绑定具体版本；路径关联版本与当前发布版本变化的行为应查看 `TryMapKnowledgeAsync`，不要假设版本更新自动继承完成状态。

源码：[学习服务](../../src/KnowPulse.Application/Learning/LearningPathAppService.cs)、[个人 API](../../src/KnowPulse.HttpApi/Controllers/Learning/LearningPathsController.cs)、[个人页面](../../src/KnowPulse.Blazor.Client/Pages/Learning/MyLearning.razor)。

## 知识反馈闭环

```mermaid
flowchart TB
    submit["用户提交可见知识的反馈"] --> submitted["Submitted"]
    submitted --> processing["Processing：管理员接手"]
    processing --> revision["可创建或关联同一知识的修订版本"]
    revision --> publish["修订版本发布并完成索引"]
    publish --> resolved["Resolved：处理结论"]
    resolved --> closed["Closed"]
    processing --> closed
```

图展示常见处理主线，具体关闭条件以领域方法为准。标记 Resolved 前必须关联同一知识下的当前发布版本，且该版本最新索引任务为 Indexed。创建修订会复制来源正文，但不会自动复制附件，也不替代发布和索引。小程序的反馈表单目前没有真实持久化。

源码：[反馈服务](../../src/KnowPulse.Application/KnowledgeBase/KnowledgeFeedbackAppService.cs)、[反馈领域模型](../../src/KnowPulse.Domain/KnowledgeBase/KnowledgeFeedback.cs)。
