# 全仓库审计报告

**What this repo does:** KnowPulse 管理企业和教师的知识资料、检索问答、题库、考试与学习。教育模块进一步支持学校关系、师生微信工作区、冻结测评、学情证据和教师确认的教学方案。按共享 PostgreSQL、多个师生同时使用的学校场景审查；生产容量未给定，不虚构承载人数。

本报告只列能落到输入或执行路径的问题，共 **8 项**。1、2 有本轮纯领域代码探针；3、4、5、6 是源码路径结论，其中 4 另外核对了当前 ABP 版本的事务规则；7 是可复现的脚本失败；8 是测试边界缺口。没有把已记录的规模限制、尚未确认的产品政策和推测性安全问题当成缺陷。

## Must fix · 优先修复

### 1. 旧考试会用迟到的答案覆盖较新的答案

- **What this is:** 企业旧考试的 `ExamAnswer.Save` 接收答案内容和客户端版本。入口为 [ExamAppService.SaveAnswersAsync](../../src/KnowPulse.Application/Exam/ExamAppService.cs#L322)，规则在 [ExamAnswer.cs](../../src/KnowPulse.Domain/Exam/ExamAnswer.cs#L45)。
- **Problem:** 已保存版本 2 的答案 B，另一设备迟到地提交版本 1 的答案 A；代码接受 A 并将其升为版本 3。本轮实际调用领域对象复现，旧版本没有被当成冲突。
- **Fix:** 按现有客户端协议拒绝低于或等于当前版本的非重复请求；同一操作标识仅在内容一致时返回旧回执。检查 Web 调用方的版本含义后同步修正，并增加一个双设备乱序保存用例。
- **If we skip it:** 网络乱序或换设备后会静默丢失较新的作答。教育专用响应修订使用另一套版本检查，不可因该路径较完善而忽略仍可访问的企业旧入口。

### 2. 旧考试超时后仍恢复同一份不可编辑的答卷

- **What this is:** [StartAttemptAsync](../../src/KnowPulse.Application/Exam/ExamAppService.cs#L264) 优先返回任一 `InProgress` 答卷；[ExamAttempt](../../src/KnowPulse.Domain/Exam/ExamAttempt.cs#L64) 控制作答时限。
- **Problem:** 计划还开放且允许两次尝试时，第一次答卷已经超时，恢复入口仍返回它。保存只抛异常而不改变状态；提交先设 `Expired` 再抛异常，正常事务回滚后不会持久化这个变化；当前后台截止任务只处理教育答卷。
- **Fix:** 在同一事务内先结束已经超时的旧答卷，再判断剩余次数并返回或创建有效尝试。不要依赖抛出异常前的状态赋值完成持久化；保留已经保存的答案。
- **If we skip it:** 用户进入“能恢复、不能保存或提交、也不能开始下一次”的死路。本轮探针确认超时保存被拒后对象仍为 `InProgress`，数据库回滚部分为源码与事务规则推导，未做真实 PostgreSQL 复现。

### 3. 重建知识索引后，过期向量可能挤掉有效检索结果

- **What this is:** [摄取管道](../../src/KnowPulse.Application/RagAI/KnowledgeIngestionPipeline.cs#L117) 重建时停用旧分块并生成新分块 ID；[Qdrant 适配](../../src/KnowPulse.Application/RagAI/QdrantKnowledgeVectorStore.cs#L105) 以分块 ID upsert。
- **Problem:** 停用只写主库，旧向量仍保留原来的已发布 payload。全树查找 `DeleteByKnowledgeVersionAsync` 只有定义和测试，没有生产调用；[检索器](../../src/KnowPulse.Application/RagAI/KnowledgeRetriever.cs#L176) 只取最多 100、通常为 TopK 三倍的候选，复核后不补召回。若前 15 个都是同一文档历次重建留下的高分旧块，TopK=5 可以返回空，即使有效新块排在第 16 位。
- **Fix:** 在重建完成后按确切旧分块 ID 清理或停用旧向量，保留可重试的清理事实；或把当前有效索引代次加入候选过滤。不能直接无条件调用“删整个版本”，那会误删并发产生的新向量。
- **If we skip it:** 索引显示成功，但重建次数越多越可能查不到内容，向量存储也持续膨胀。数据库复核仍阻止旧正文泄漏，这项首先是检索正确性和清理问题。

### 4. 知识索引把外部转换和模型调用包在一笔长事务里

- **What this is:** [KnowledgeIndexJobProcessor.ProcessAsync](../../src/KnowPulse.Application/KnowledgeIndexing/KnowledgeIndexJobProcessor.cs#L47) 带普通 `[UnitOfWork]`，内部依次调用摄取、Embedding 和 Qdrant。进度写入使用 `autoSave: true`。
- **Problem:** 在当前默认事务配置下，`ProcessAsync` 的事务到整个方法结束才提交。假如模型调用耗时 60 秒，其他连接不能看到中途已 flush 的心跳，且已更新行的锁持续存在；`autoSave` 不是事务提交。[ABP 10.6.0 拦截器](https://raw.githubusercontent.com/abpframework/abp/10.6.0/framework/src/Volo.Abp.Uow/Volo/Abp/Uow/UnitOfWorkInterceptor.cs) 和 [默认选项](https://raw.githubusercontent.com/abpframework/abp/10.6.0/framework/src/Volo.Abp.Uow/Volo/Abp/Uow/AbpUnitOfWorkDefaultOptions.cs) 支持这一判断。
- **Fix:** 参考已有 AI 出题处理器，用短事务认领、独立心跳、事务外外部调用、带执行编号复核的短事务完成。必须同时保留过期执行者的防回写检查，不能仅关闭事务或给心跳另开事务却继续持有外层锁。
- **If we skip it:** 进度和卡住检测失真，慢模型拖住数据库事务，恢复操作可能等待锁。当前 SQLite 测试主动关闭事务，因此“管道通过测试”不能证明该场景正常。

## Should fix · 随后修复

### 5. 日志分页会先读取和授权检查全部历史记录

- **What this is:** [RagCallLogAppService.GetListAsync](../../src/KnowPulse.Application/RagAI/RagCallLogAppService.cs#L94) 提供带来源校验的分页日志。
- **Problem:** 代码先载入过滤后的全部日志和全部引用，再逐引用调用来源授权，最后才 `Skip/Take`。一个用户积累 10,000 条、每条 5 个引用时，打开第一页也会走至多 50,000 次来源校验，而不是只处理这一页；这是操作次数推导，不是本轮压测数据。
- **Fix:** 将可表达的归属及来源授权合入 SQL，再对当前页批量读取引用。若先采用分批扫描，必须明确总数语义，不能把扫描上限当成真实总数，也不能把授权校验移到分页之后。
- **If we skip it:** 审计页延迟和内存随历史增长，同一数据库还会承担教学请求。该问题没有使用项目已接受的检索规模 shortcut 作为依据。

### 6. 每个模型请求都新建并释放 HTTP 连接池

- **What this is:** [Embedding 客户端](../../src/KnowPulse.Application/RagAI/LiteLlmKnowledgeEmbeddingClient.cs#L45) 与 [Chat 客户端](../../src/KnowPulse.Application/RagAI/LiteLlmRagChatClient.cs#L165) 在每次调用中 `new HttpClient`。
- **Problem:** 一个文档分成 200 块时，Embedding 循环会创建 200 个独立客户端，无法复用前一请求的连接池；同时问答和出题继续重复这一行为。存在确定的连接复用损失，但本轮没有测出连接耗尽阈值。
- **Fix:** 使用现有 .NET DI 的 `IHttpClientFactory` 或由 DI 管理的客户端；认证头仍按请求设置。保留超时与受控错误信息，不引入新的网络框架。
- **If we skip it:** 额外握手和短连接增加延迟、网关压力及端口占用，真实吞吐上限更难预测。

### 7. Provider 边界检查与已批准的 ADR 冲突

- **What this is:** [verify-rag-provider-boundaries.ps1](../../ops/verify-rag-provider-boundaries.ps1#L18) 全面禁止 DataIngestion 引用；[ADR 0003](../adr/0003-markitdown-backed-document-ingestion.md) 已批准隔离在适配器内的 MarkItDown。
- **Problem:** 本轮运行脚本实际失败，命中合法的转换器和 Application 包引用。检查无法区分“批准的适配实现”和“类型泄漏到契约或领域”。
- **Fix:** 只对确切批准的文件与包设窄例外，继续检查 Contracts、Domain 和 UI 不得引用这些 provider 类型，并保留违规样本验证。
- **If we skip it:** 开发者会反复遇到假失败，或逐渐跳过整个边界检查，失去它原本应有的保护。

### 8. 默认集成测试无法验证事务回滚与真实写竞争

- **What this is:** [EF 测试模块](../../test/KnowPulse.EntityFrameworkCore.Tests/EntityFrameworkCore/KnowPulseEntityFrameworkCoreTestModule.cs#L66) 使用内存 SQLite，并调用 `AddAlwaysDisableUnitOfWorkTransaction`。
- **Problem:** 发布、学校授权屏障、封存和评分替换都依赖事务；默认测试无法在“写入第一张表后抛异常”时证明前面的写入回滚。本轮没有找到纳入常规测试入口的 PostgreSQL 事务回归套件；历史手工验收日志不能持续守住这一点。
- **Fix:** 保留现有快速测试，增加一个很小的 PostgreSQL 专项：发布中途失败回滚、同一学生并发保存或封存、改分和结果指针原子替换。独立测试库运行，不把全量业务测试搬到真实库。
- **If we skip it:** 测试通过仍可能漏掉生产数据库上的原子性和锁问题。该项不是要求增加大量测试，而是补当前测试机制无法表达的边界。

## 已检查而没有认定为缺陷的设计

- 个人知识库的所有者约束不会被历史企业知识“空范围可见”规则覆盖；学校共享使用指定版本、用途和当前关系复核。
- 教育正式成绩来自不可变提交与已确认评分，不直接暴露老师当前评分草稿；隐藏、仅分数、完整讲评使用不同投影。
- 已经失效的共享向量仍存在，不等于能够越权读取；主库二次校验是独立防线。
- 学校级写屏障有并发成本，但承担关系撤销与业务写入的一致性；未取得负载证据前不建议直接移除。
- 按进程限流、个人正文 `Contains`、受限共享集合展开等已有明确 shortcut；未因假设的大规模负载将其升格为确定缺陷。
- 领域接口隔离外部供应商有实际用途，不因只有一个生产实现就建议删除；两套考试仍有独立调用方，不能直接删旧系统。

**Verdict:** 分层与教育业务边界可继续使用；先修 1–4，再处理查询、连接复用和持续验证。没有认定可以安全删除的生产依赖，不编造“可省多少行”的数字。

**Not checked:** 没有运行生产环境、真实微信、真实模型、压力或安全渗透测试；没有逐行读完全部源码、49 次历史迁移、所有 UI 和第三方实现；没有执行真实数据库迁移或旧库升级。具体覆盖见[来源与覆盖范围](sources-and-coverage.md)。
