# 教学核心闭环实施与验证

本轮以业务文档 v1.2、项目改造计划和 Figma 的 06–08 页为依据。目标是教师资料出题、本人确认、固定学生发布、小程序真实保存和提交、教师查看本人任务结果；不扩展学校、认证、运营报表或复杂考试。

## 本地运行

运行数据库迁移和数据种子后，普通注册账号默认获得 `teacher` 角色。学生由微信登录创建并只获得 `student` 角色，不继承教师默认角色。新权限沿用 ABP 的角色授权和现有 OpenIddict。

微信配置仅放在 API Host 的服务端安全配置或环境变量：

```text
WeChat__MiniProgram__AppId
WeChat__MiniProgram__AppSecret
```

API Host 自动加载 `src/KnowPulse.HttpApi.Host/appsettings.secrets.json`；空模板为同目录的 `appsettings.secrets.example.json`。真实配置不提交。仓库根目录 `.env` 不会自动加载进 API Host，模型网关访问密钥仍须从安全配置或环境变量注入。

小程序使用 `wx.login` 获取 code，向 `/connect/token` 提交 `grant_type=wechat`、`client_id=KnowPulse_MiniProgram`、code 和 `scope=KnowPulse profile roles`，随后通过带 Bearer 的 `/api/account/my-profile` 获取显示名称，通过 `/api/abp/application-configuration?includeLocalizationResources=false` 确认稳定用户 ID（标准 ProfileDto 没有 ID）。客户端不包含 AppSecret、正确答案夹具或本地计分；微信未配置、凭证失败和 401 均不能创建本地身份。

教师入口为 `/teacher/materials`、`/teacher/shared`、`/teacher/answer`、`/teacher/generate`、`/teacher/questions`、`/teacher/students` 和 `/teacher/tasks`。小程序仅保留答题、我的两个导航。

教师资料问答需要 `KnowPulse.Rag.Answer`。新环境的数据种子会授予默认 `teacher` 角色；现有环境需在角色权限页面显式授予此权限，或在确认目标数据库、迁移与备份影响后执行数据库迁移和种子。重启应用不会自动更新已有角色。平台 Prompt 正文查看另需 `KnowPulse.QuestionBank.PromptTemplates`；普通教师出题只读取启用模板的摘要。

复现本轮已建立的隔离环境时，在后端仓库根目录启动 API Host；此命令不执行迁移：

```powershell
$settings = Get-Content .\src\KnowPulse.DbMigrator\appsettings.json -Raw | ConvertFrom-Json
$env:ConnectionStrings__Default = $settings.ConnectionStrings.Default -replace '(?i)Database=[^;]*', 'Database=KnowPulseTeachingVerification20261025'
if ($env:ConnectionStrings__Default -notmatch 'Database=KnowPulseTeachingVerification20261025(?:;|$)') { throw '隔离库检查失败' }
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:BlobStoring__Minio__BucketName = 'knowpulse-teaching-verification'
$env:RagAI__Embedding__CollectionName = 'knowpulse-teaching-verification-qwen-v4-1536'
$env:RagAI__VectorStore__KnowledgeCollectionName = 'knowpulse-teaching-verification-qwen-v4-1536'
foreach ($line in Get-Content .\.env) {
    if ($line -match '^(KNOWPULSE_LITELLM_API_KEY|KNOWPULSE_QDRANT_API_KEY)=(.*)$') {
        [Environment]::SetEnvironmentVariable($matches[1], $matches[2].Trim().Trim('"').Trim("'"), 'Process')
    }
}
dotnet run --project .\src\KnowPulse.HttpApi.Host --no-build
```

Web 在另一终端使用 `dotnet run --project .\src\KnowPulse.Blazor --no-build`。切回常规数据库或创建新环境属于单独的迁移／种子操作，不应从这段验证启动命令推断原库已经迁移。

## 数据边界

新增 `TeacherTaskCoreLoop` 迁移：教师归属可空以保留历史记录，历史不自动按 CreatorId 归属；新增师生关系和邀请，任务时间、时长及过期时间可空。默认私有与历史兼容决定见 ADR 0007。

开发验证使用独立数据库 `KnowPulseTeachingVerification20261025` 和独立附件 bucket `knowpulse-teaching-verification`，不改原 `KnowPulse` 数据库。生产执行前须备份、验证恢复、清点无归属数据，并检查旧用户指派是否重复；迁移增加用户指派唯一索引，存在重复时应先人工核对，不能直接删除历史数据。

## 当前实施状态

- 教师工作区按 Figma 教学稿接入真实服务：我的资料、共享资料、出题与题库、我的学生、答题任务及任务结果；教师正常导航不暴露企业组织和技术维护入口。
- 新资料、题目、生成任务及答题任务绑定教师；共享只允许指定教师读取当前发布版，不能读取草稿或修改原件。撤销后阻止新读取／生成，历史快照保留。
- 任务复用 Exam，发布时固化具体学生及题目评分规则，拒绝空名单；不限时使用可空字段。原题修改、关系增减不重写历史任务。
- 小程序仅保留答题／我的；真实微信身份、关联申请、待答任务、顺序保存、恢复、幂等提交和服务端结果已接通。开始及恢复响应的两处 Paper 都裁剪正确答案、解析和内部来源；结果按 Hidden／ScoreOnly／FullReview 展示。
- PostgreSQL 作答行锁覆盖保存、提交和重评，防止并发交卷产生第二份结果。公开索引完成入口改为真实处理，不允许手动标记伪造“可出题”。同一索引任务失败重试复用原分块，已持久化分块的内容／来源发生变化或已经过期时要求新任务，历史分块不删除。

## 2026-10-25 实际联调

本轮在本机浏览器和微信开发者工具中完成以下操作，使用真实 API、真实 PostgreSQL 和原生 `wx.login`。小程序网络测试夹具不作为这份联调的身份或成绩来源。

| 流程 | 实际结果 |
| --- | --- |
| 教师普通注册登录 | 获 teacher 角色，进入教师资料入口；5 组教师菜单，无维护菜单 |
| 微信登录及关联 | 服务端验证微信 code 并建立稳定 student；学生申请、教师确认后关系生效；教师发布前学生任务为 0 |
| 手工出题及确认 | 教师创建并本人确认单选、多选、判断共 3 题，发布时明确选择 1 名已关联学生 |
| 学生收到指定任务 | 刷新本人列表出现目标任务，开考响应不含正确答案、解析、内部来源及接收人名单 |
| 保存与恢复 | 首题保存后退出，重进恢复同一 attempt 及已保存答案；三类题逐题保存成功 |
| 交卷与重试 | 服务端评分 30/30，FullReview 返回获准解析；同一提交键重试返回同一 scoreId，没有新增成绩 |
| 教师查看结果 | 本人任务显示 1 人指派、1 人提交、0 人未开始，30 分；作答标识与小程序一致 |
| 权限负例 | 学生请求教师邀请管理接口实际返回 403，客户端保留有效会话并提示无权限 |

安全业务标识：任务 `3a248429-6770-32cf-988c-1c80a08b8445`，作答 `3a24842d-f304-6220-e6c5-a12f6897d5cc`，成绩 `3a24842e-c36f-f70d-cfc4-f1fa7027d7f0`。这些只用于定位隔离验证记录，不是登录凭证。

首次资料处理真实执行时，教师保存讲义正文、创建版本并发布成功，原 Embedding 上游因缺少配置返回 401，界面如实显示处理失败并禁用资料出题。没有伪造向量、手工标记 Ready 或使用无资料依据的生成结果来替代验收。后续按用户指定切换为千问，原配置阻塞已解除。

## 千问 Embedding 配置与验证

- 上游为 `text-embedding-v4`，北京地域 OpenAI 兼容接口；主、备用业务 alias 都指向同一模型，输出 1536 维。模型规格与地域接口依据[阿里云官方说明](https://help.aliyun.com/zh/model-studio/embedding-interfaces-compatible-with-openai)。
- 本地 `.env` 仅保存真实 `DASHSCOPE_API_KEY` 和 `DASHSCOPE_API_BASE`；API Host／小程序不持有供应商密钥。已重建 LiteLLM 容器并真实调用，HTTP **200**、返回 **1536 维**。网关健康检查不作为这项推理证明。
- 当前 LiteLLM v1.106.3 会丢弃未知 OpenAI 模型的普通 `dimensions` 参数。本地临时 HTTP 请求捕获先复现参数缺失，再验证 `extra_body.dimensions=1536` 确实透传；YAML 使用后一种配置，真实千问回包维度再次核对通过。
- 常规配置使用新集合 `knowledge_chunks_qwen_v4_1536`；当前隔离验证使用 `knowpulse-teaching-verification-qwen-v4-1536`，1536 维及全部权限 payload 索引已实际初始化验证。旧集合保留，既有向量不复制混用。
- 已通过受控维护页将隔离库的两个 Embedding 模型目录及 `KnowledgeEmbedding` 用途绑定更新为 `2026-10-qwen-embedding-v4-1536`，保留 gateway alias、1536 维和应用网关密钥变量。仅改 appsettings 不会覆盖持久化绑定。
- 切换后，显式旧模型任务若集合或维度与当前向量库不符，在调用模型、写记录／向量前以 `model-switch` 明确失败；须创建新处理任务，旧记录不删除。两种配置不匹配均完成 RED→GREEN，包含同 1536 维但不同集合的情况。
- 修复出题要求只进入检索、不进入模型请求的问题；生成及重试都将已保存的教师要求传入 prompt，并保留固定题型、数量与授权引用约束。AI 整组 **17/17**、全部 MVP-2 **30/30** 通过。
- Qdrant 原空 key 环境变量会启用空值鉴权，已实测无 header 为 401、空 header 为 200。当前本地改为随机密钥，服务端及 Host 使用一致值，仅重建容器、保留原数据卷。示例使用明确的开发值，部署校验仍要求替换。
- 真实处理进一步暴露 Qdrant 的生产 DI 会选中测试用双参构造，导致 HttpClient 缺 BaseAddress。两个生产接口改为显式调用配置型构造；实际模块注册配合未配置的默认 HttpClient 先复现异常，再由本地 HTTP 测试验证 Upsert／Health 的地址及鉴权 header。应用测试 **21/21** 通过，原资料任务随后真实重试成功为 Indexed／Ready。

## 完整 AI 业务闭环实测

在同一教师、同一资料及同一真实微信学生身份下完成：

1. 原资料新处理任务 `3a248457-2ad7-51da-7762-cb23e97998ca` 经真实摄取、千问 Embedding、Qdrant 写入后变为 Indexed，资料为 Ready。新集合实际为 green、1 个 point、1536 维、10 个权限索引。
2. 教师明确选择原讲义，要求“只考分数概念，不出计算题”。生成任务 `3a24845d-b822-e89d-1149-8b644958c111` 实际 HTTP 200，2/2 合法单选；每道题引用均指向所选 item `3a248421-b3c4-83b5-7b54-6d8996d615aa`、当前版本及真实 chunk，无校验错误。向量模型为千问，聊天出题仍由既有 DeepSeek alias 完成。
3. 通过正常教师 Cookie 登录的 API 采纳、实际编辑标题／题干／解析、保留引用并本人确认，2 题均 Approved 且可用于任务；浏览器恢复同一教师后回看 Ready、候选引用和确认题截图。
4. 定向发布任务 `3a24845e-b7d1-3edf-4e89-e4313880b7e5`：2 题／20 分、1 名真实学生、1 次正式提交、无时限、ScoreOnly。未使用管理员代替教师归属，也未手动改数据库状态。
5. 同一真实微信学生收到任务并逐题保存、交卷。作答 `3a24845f-3269-b3f4-04cf-62ef0f62b30d`，成绩 `3a24845f-34ec-b3d6-a63d-da3d431d7a13`，实际 **20/20**；同键重试返回同一成绩。开始及提交后的两处 Paper 不含标准答案、解析和内部来源，ScoreOnly 的 review／错题详情为空，界面只展示分数。
6. 原教师的 API 和 Web 结果页均确认 1 人指派、1 人提交、0 人未开始、20 分，且 attempt／score 与学生一致，提交时间为本地 16:34。前一份手工三题型任务的 FullReview 30/30 验证仍独立保留。
7. 最终整套构建后重启 API Host 与 Web，教师和原微信学生分别只读回查：同一 plan／attempt／score 仍为 **20/20**，资料仍 Ready，ScoreOnly 答案保护不变。未重登微信或重新交卷；证据为 `teacher-web-implementation/ai-teacher-after-restart.json` 与 `mini-student-implementation/real-ai-after-final-restart.json`。

真实 AI 证据：`teacher-web-implementation/ai-material-ready.json`、`ai-candidate-task.json`、`ai-confirmed-questions.json`、`ai-published-plan.json`、`ai-teacher-final-results.json`；对应 UI 截图为 `qwen-material-ready-ui.jpg`、`qwen-ai-candidates-citations-ui.jpg`、`qwen-ai-questions-confirmed-ui.jpg`、`qwen-ai-task-results-final.jpg`。学生证据为 `mini-student-implementation/real-ai-student-result.json`、`real-ai-score-only-result.png`。

## 自动验证与剩余门槛

- 领域 **66/66**、应用 **21/21**、最新 EF 集成测试 **103 通过、0 失败、4 个 PostgreSQL 用例按默认配置跳过**（`teaching-qwen-final-regression.trx`）。覆盖所有权、共享当前版本及撤销、指定资料约束、本人确认、多教师关系、固定名单、答案裁剪、保存恢复、幂等提交与本人结果。
- 独立 PostgreSQL 测试 **4/4 通过**：发布接收名单持久化、第二位学生指派失败时整笔回滚、两个并发请求使用同一提交键返回同一成绩，以及同一索引任务重试保持相同分块 ID 和数量。重试缺陷先由 PostgreSQL 唯一约束复现，再验证修复；普通测试默认跳过这 4 项，须显式开启。
- `dotnet ef migrations has-pending-model-changes --project . --startup-project . --no-build`（在 EntityFrameworkCore 项目目录）返回无待迁移模型差异。
- 小程序 `npm test` 使用 Node 内置测试及本地 HTTP 连接；`npm run test:host` 只读检查真实 Host 契约、OAuth 元数据、请求体、匿名／无效令牌状态和正式包依赖。
- **资料 AI 出题到学生作答、教师查看结果的核心闭环已真实通过**，包括最终构建、重启后两端只读持久化检查；完整改造计划的后续阶段及生产验收继续按原边界执行。
- 真实联调当前只有一个微信学生身份；跨教师、非接收人、后来加入的学生及不同结果策略由集成测试验证，未以多个微信真人身份重跑。真机、正式微信域名及生产发布尚未验收。
- Blazor Server 构建仍有既有依赖版本的两条 NU1903 警告，需在部署前处理；没有为本次功能顺带升级整套框架。

最终 `dotnet build KnowPulse.slnx --no-restore` 已通过（0 错误、上述 2 条既有警告）；`dotnet test KnowPulse.slnx --no-build --no-restore` 为 **66 + 21 + 103 通过、4 个可选 PostgreSQL 用例默认跳过**，各项目保存 `teaching-core-final.trx`；TestBase 仅为测试支持程序集，没有独立测试。独立 PostgreSQL **4/4** 实跑证据单独保留。小程序 `npm test` 为 **8/8**，真实 Host 契约检查通过；追加模型切换保护后的索引集成整组 **21/21**。LiteLLM 配置静态／运行验证、私有部署基线和 RAG provider 边界检查已通过；边界脚本仅精确补充 ADR 0003 已规定的 MarkItDown 转换器引用白名单。

本机最终运行入口：教师 Web `https://localhost:44307`，API `https://localhost:44305`。证据保存在本次任务的 visualization 目录：`teacher-web-implementation/teacher-student-attempt-final.jpg`、`teacher-student-attempt-bottom-final.jpg`、`teacher-permission-denied-final.jpg`，以及 `mini-student-implementation/real-positive-progress.json`、`real-positive-result-full-review.png`、`real-positive-after-restart.json`。小程序端完整记录见平级仓库的 [design-qa.md](../../KnowPulse.MiniProgram/design-qa.md)。证据不包含 token、微信 code 或 AppSecret。代码通过两个独立 Git 提交归档，本地配置和真实凭据不包含在提交中；本轮未部署生产环境。

开启隔离 PostgreSQL 测试时，设置 `KNOWPULSE_RUN_POSTGRES_TESTS=1` 和 `KNOWPULSE_POSTGRES_TEST_CONNECTION` 后运行 `dotnet test test/KnowPulse.EntityFrameworkCore.Tests --filter FullyQualifiedName~TeachingPostgresVerificationTests`。测试代码强制数据库名为上述隔离库，拒绝使用原库；连接串通过本机安全配置提供，不写入文档或命令输出。
