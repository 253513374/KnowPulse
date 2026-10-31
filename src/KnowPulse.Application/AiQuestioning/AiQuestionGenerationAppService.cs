using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.AiModelManagement;
using KnowPulse.Education;
using KnowPulse.Permissions;
using KnowPulse.QuestionBank;
using KnowPulse.RagAI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Security.Claims;
using Volo.Abp.Uow;

namespace KnowPulse.AiQuestioning;

/// <summary>
/// AI 候选题生成应用服务。
/// 服务串联 MVP-1 RAG 检索、AI 模型调用、结构化校验、citation 落库、相似题提示和提交审核，
/// 但始终只产出候选题或待审核题，不能绕过题库审核流直接生成正式考试题。
/// </summary>
[Authorize(KnowPulsePermissions.QuestionBank.AiGeneration)]
public class AiQuestionGenerationAppService : KnowPulseAppService, IAiQuestionGenerationAppService
{
    private KnowledgeSourceAccessGuard SourceAccess => LazyServiceProvider.LazyGetRequiredService<KnowledgeSourceAccessGuard>();
    private EducationCurriculumAccessGuard CurriculumAccess => LazyServiceProvider.LazyGetRequiredService<EducationCurriculumAccessGuard>();
    private IUnitOfWorkManager Uow => LazyServiceProvider.LazyGetRequiredService<IUnitOfWorkManager>();
    private AiQuestionGenerationWorkerOptions WorkerOptions => LazyServiceProvider.LazyGetRequiredService<IOptions<AiQuestionGenerationWorkerOptions>>().Value;
    /// <summary>
    /// 模型响应 JSON 解析选项。
    /// 使用 Web 默认值并忽略属性大小写，降低不同模型 JSON 命名风格造成的解析失败。
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IRepository<AiQuestionPromptTemplate, Guid> _promptTemplateRepository;
    private readonly IRepository<AiQuestionGenerationTask, Guid> _taskRepository;
    private readonly IRepository<AiQuestionCandidate, Guid> _candidateRepository;
    private readonly IRepository<AiQuestionCandidateCitation, Guid> _candidateCitationRepository;
    private readonly IRepository<AiQuestionModelCallLog, Guid> _modelCallLogRepository;
    private readonly IKnowledgeRetriever _knowledgeRetriever;
    private readonly IRagChatClient _chatClient;
    private readonly IAiModelResolver _modelResolver;
    private readonly IQuestionWorkflowAppService _questionWorkflowAppService;
    private readonly IQuestionSimilarityService _similarityService;
    private readonly RagAiOptions _options;

    /// <summary>
    /// 创建 AI 候选题生成应用服务。
    /// 依赖通过 ABP DI 注入，仓储负责持久化生成链路，RAG 和聊天客户端负责外部模型能力。
    /// </summary>
    public AiQuestionGenerationAppService(
        IRepository<AiQuestionPromptTemplate, Guid> promptTemplateRepository,
        IRepository<AiQuestionGenerationTask, Guid> taskRepository,
        IRepository<AiQuestionCandidate, Guid> candidateRepository,
        IRepository<AiQuestionCandidateCitation, Guid> candidateCitationRepository,
        IRepository<AiQuestionModelCallLog, Guid> modelCallLogRepository,
        IKnowledgeRetriever knowledgeRetriever,
        IRagChatClient chatClient,
        IAiModelResolver modelResolver,
        IQuestionWorkflowAppService questionWorkflowAppService,
        IQuestionSimilarityService similarityService,
        IOptions<RagAiOptions> options)
    {
        _promptTemplateRepository = promptTemplateRepository;
        _taskRepository = taskRepository;
        _candidateRepository = candidateRepository;
        _candidateCitationRepository = candidateCitationRepository;
        _modelCallLogRepository = modelCallLogRepository;
        _knowledgeRetriever = knowledgeRetriever;
        _chatClient = chatClient;
        _modelResolver = modelResolver;
        _questionWorkflowAppService = questionWorkflowAppService;
        _similarityService = similarityService;
        _options = options.Value;
    }

    /// <summary>
    /// 查询当前 AI 出题模型配置。
    /// 该入口只暴露 alias、版本、限制和密钥环境变量名，不返回真实 key。
    /// </summary>
    public virtual async Task<AiQuestionModelConfigurationDto> GetModelConfigurationAsync()
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.AiGeneration);

        var generationOptions = _options.AiQuestionGeneration;
        var modelSelection = await _modelResolver.ResolveAsync(AiModelUsage.AiQuestionGeneration);
        return new AiQuestionModelConfigurationDto
        {
            PromptTemplateKey = ReadOption(generationOptions.PromptTemplateKey, AiQuestionPromptDefaults.TemplateKey),
            PromptVersion = ReadOption(generationOptions.PromptVersion, AiQuestionPromptDefaults.Version),
            ModelProvider = modelSelection.Provider,
            ModelName = modelSelection.Alias,
            ModelVersion = modelSelection.BusinessModelVersion,
            GatewayBaseUrl = _options.LiteLlm.BaseUrl,
            ApiKeyEnvironmentVariable = _options.LiteLlm.ApiKeyEnvironmentVariable,
            MaxQuestionsPerTask = Math.Max(1, generationOptions.MaxQuestionsPerTask),
            MaxContextChunks = Math.Max(1, generationOptions.MaxContextChunks),
            MaxPromptChunkLength = Math.Max(1, generationOptions.MaxPromptChunkLength),
            MaxCitationExcerptLength = Math.Max(1, generationOptions.MaxCitationExcerptLength),
            MaxOutputTokens = Math.Max(1, modelSelection.MaxOutputTokens),
            Temperature = modelSelection.Temperature
        };
    }

    /// <summary>
    /// 分页查询 AI 出题 prompt 模板版本。
    /// 该查询只读模板版本，不触发检索、模型调用或候选题生成。
    /// </summary>
    public virtual async Task<PagedResultDto<AiQuestionPromptTemplateDto>> GetPromptTemplatesAsync(
        GetAiQuestionPromptTemplateListInput input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.AiGeneration);

        var queryable = await _promptTemplateRepository.GetQueryableAsync();
        if (!string.IsNullOrWhiteSpace(input.TemplateKey))
        {
            var templateKey = input.TemplateKey.Trim();
            queryable = queryable.Where(x => x.TemplateKey == templateKey);
        }

        if (!string.IsNullOrWhiteSpace(input.Version))
        {
            var version = input.Version.Trim();
            queryable = queryable.Where(x => x.Version == version);
        }

        if (input.IsActive.HasValue)
        {
            queryable = queryable.Where(x => x.IsActive == input.IsActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(input.Keyword))
        {
            var keyword = input.Keyword.Trim();
            queryable = queryable.Where(x =>
                x.TemplateKey.Contains(keyword) ||
                x.Version.Contains(keyword) ||
                (x.Description != null && x.Description.Contains(keyword)));
        }

        var totalCount = await AsyncExecuter.CountAsync(queryable);
        var templates = await AsyncExecuter.ToListAsync(queryable
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.CreationTime)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount));

        return new PagedResultDto<AiQuestionPromptTemplateDto>(
            totalCount,
            templates.Select(MapPromptTemplateToDto).ToList());
    }

    /// <summary>
    /// 分页查询 AI 出题模型调用日志。
    /// 日志只包含受控摘要、hash、状态和模型元数据，不返回完整 prompt 或完整模型响应。
    /// </summary>
    public virtual async Task<PagedResultDto<AiQuestionModelCallLogDto>> GetCallLogsAsync(
        GetAiQuestionModelCallLogListInput input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.AiGeneration);

        var queryable = await _modelCallLogRepository.GetQueryableAsync();
        if (input.TaskId.HasValue)
        {
            queryable = queryable.Where(x => x.TaskId == input.TaskId.Value);
        }

        if (!string.IsNullOrWhiteSpace(input.PromptVersion))
        {
            var promptVersion = input.PromptVersion.Trim();
            queryable = queryable.Where(x => x.PromptVersion == promptVersion);
        }

        if (input.Status.HasValue)
        {
            queryable = queryable.Where(x => x.Status == input.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(input.ModelProvider))
        {
            var provider = input.ModelProvider.Trim();
            queryable = queryable.Where(x => x.ModelProvider == provider);
        }

        if (!string.IsNullOrWhiteSpace(input.ModelName))
        {
            var modelName = input.ModelName.Trim();
            queryable = queryable.Where(x => x.ModelName == modelName);
        }

        if (!string.IsNullOrWhiteSpace(input.ModelVersion))
        {
            var modelVersion = input.ModelVersion.Trim();
            queryable = queryable.Where(x => x.ModelVersion == modelVersion);
        }

        if (input.CreatedFrom.HasValue)
        {
            queryable = queryable.Where(x => x.CreationTime >= input.CreatedFrom.Value);
        }

        if (input.CreatedTo.HasValue)
        {
            queryable = queryable.Where(x => x.CreationTime <= input.CreatedTo.Value);
        }

        var logs = await AsyncExecuter.ToListAsync(queryable.OrderByDescending(x => x.CreationTime));
        var visibleLogs = new List<AiQuestionModelCallLog>();
        foreach (var log in logs)
        {
            var task = await _taskRepository.FindAsync(log.TaskId);
            if (task != null && log.TenantId == task.TenantId && await SourceAccess.CanReadTaskAsync(task))
            {
                visibleLogs.Add(log);
            }
        }

        return new PagedResultDto<AiQuestionModelCallLogDto>(
            visibleLogs.Count,
            visibleLogs.Skip(input.SkipCount).Take(input.MaxResultCount).Select(MapModelCallLogToDto).ToList());
    }

    /// <summary>
    /// 分页查询 AI 出题任务。
    /// 查询结果会包含候选题和调用日志摘要，便于页面展示一次生成任务的完整审计链路。
    /// </summary>
    public virtual async Task<PagedResultDto<AiQuestionGenerationTaskDto>> GetListAsync(GetAiQuestionGenerationTaskListInput input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.AiGeneration);

        var queryable = await _taskRepository.GetQueryableAsync();
        if (input.Status.HasValue)
        {
            queryable = queryable.Where(x => x.Status == input.Status.Value);
        }

        if (input.Type.HasValue)
        {
            queryable = queryable.Where(x => x.Type == input.Type.Value);
        }

        if (input.Difficulty.HasValue)
        {
            queryable = queryable.Where(x => x.Difficulty == input.Difficulty.Value);
        }

        if (input.ApplicablePositionId.HasValue)
        {
            queryable = queryable.Where(x => x.ApplicablePositionId == input.ApplicablePositionId.Value);
        }

        if (!string.IsNullOrWhiteSpace(input.Keyword))
        {
            var keyword = input.Keyword.Trim();
            queryable = queryable.Where(x => x.KnowledgeScope.Contains(keyword) || x.RetrievalQuery.Contains(keyword));
        }

        queryable = queryable.Where(x => x.PersonalOwnerUserId == null || x.PersonalOwnerUserId == CurrentUser.Id);
        var tasks = await AsyncExecuter.ToListAsync(queryable.OrderByDescending(x => x.CreationTime));
        var visibleTasks = new List<AiQuestionGenerationTask>();
        foreach (var task in tasks)
        {
            if (await SourceAccess.CanReadTaskAsync(task))
            {
                visibleTasks.Add(task);
            }
        }

        var dtos = new List<AiQuestionGenerationTaskDto>();
        foreach (var task in visibleTasks.Skip(input.SkipCount).Take(input.MaxResultCount))
        {
            dtos.Add(await MapTaskToDtoAsync(task));
        }

        return new PagedResultDto<AiQuestionGenerationTaskDto>(visibleTasks.Count, dtos);
    }

    /// <summary>
    /// 查询单个 AI 出题任务详情。
    /// 返回值包含候选题 citation 和模型调用摘要，但不返回完整 prompt 或完整知识正文。
    /// </summary>
    public virtual async Task<AiQuestionGenerationTaskDto> GetAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.AiGeneration);
        return await MapTaskToDtoAsync(await _taskRepository.GetAsync(id));
    }

    /// <summary>
    /// 查询单个 AI 候选题详情。
    /// 该入口用于前端局部刷新或未来候选题独立审核动作。
    /// </summary>
    public virtual async Task<AiQuestionCandidateDto> GetCandidateAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.AiGeneration);
        return await MapCandidateToDtoAsync(await _candidateRepository.GetAsync(id));
    }

    /// <summary>
    /// 创建持久化待执行任务。模型调用由后台处理器执行。
    /// 输入数量和检索上下文数量会按配置截断，防止单次模型调用过大；生成结果先落入候选题表。
    /// </summary>
    public virtual async Task<AiQuestionGenerationTaskDto> CreateAsync(CreateAiQuestionGenerationTaskDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.AiGeneration);
        Check.NotNull(input, nameof(input));
        Check.NotNullOrWhiteSpace(input.KnowledgeScope, nameof(input.KnowledgeScope), AiQuestionGenerationTask.MaxKnowledgeScopeLength);
        Check.NotNullOrWhiteSpace(input.Purpose, nameof(input.Purpose), 64);
        if (!Enum.IsDefined(input.Scope) || (input.Scope == KnowledgeSearchScope.PersonalLibrary && input.SchoolId.HasValue))
            throw new ArgumentException("The requested knowledge search scope is invalid.", nameof(input));
        if (input.IdempotencyKey == Guid.Empty || (input.CurriculumScope != null && !input.IdempotencyKey.HasValue) ||
            !Enum.IsDefined(input.Type) || !Enum.IsDefined(input.Difficulty))
            throw new ArgumentException("The generation input is invalid.", nameof(input));

        var fingerprint = ComputeHash(JsonSerializer.Serialize(new
        {
            input.Scope, input.SchoolId, input.Type, input.Difficulty, input.RequestedCount, input.TopK,
            KnowledgeScope = input.KnowledgeScope.Trim(), KnowledgePointText = input.KnowledgePointText?.Trim(),
            input.ApplicablePositionId, input.SecurityLevel, Tags = NormalizeTags(input.Tags), input.Score,
            Purpose = input.Purpose.Trim(), SelectionId = input.CurriculumScope?.TeacherTextbookSelectionId,
            MappingIds = input.CurriculumScope?.TextbookKnowledgePointMappingIds?.OrderBy(x => x).ToList()
        }, JsonOptions));
        var id = input.IdempotencyKey ?? GuidGenerator.Create();
        var existing = await _taskRepository.FindAsync(id);
        if (existing != null)
        {
            await SourceAccess.EnsureTaskAsync(existing);
            if (existing.PersonalOwnerUserId != CurrentUser.Id || existing.RequestFingerprint != fingerprint)
                throw new BusinessException("KnowPulse:AiGeneration:ConflictingOperation");
            return await MapTaskToDtoAsync(existing);
        }
        var snapshot = input.CurriculumScope == null ? null : await CurriculumAccess.ResolveAsync(input.CurriculumScope);
        if (snapshot != null && input.SchoolId.HasValue && snapshot.SchoolId != input.SchoolId)
            throw new BusinessException(EducationCurriculumErrorCodes.ReferenceMismatch);

        var template = await EnsureDefaultPromptTemplateAsync();
        // 使用配置上限兜底前端输入，避免任务一次性请求过多候选题或上下文片段。
        var requestedCount = Math.Clamp(input.RequestedCount, 1, Math.Max(1, _options.AiQuestionGeneration.MaxQuestionsPerTask));
        var topK = Math.Clamp(input.TopK, 1, Math.Max(1, _options.AiQuestionGeneration.MaxContextChunks));
        var retrievalQuery = BuildRetrievalQuery(input);
        var modelConfiguration = await GetModelConfigurationAsync();
        var task = new AiQuestionGenerationTask(
            id,
            CurrentTenant.Id,
            template.Id,
            template.Version,
            modelConfiguration.ModelProvider,
            modelConfiguration.ModelName,
            modelConfiguration.ModelVersion,
            input.Type,
            input.Difficulty,
            requestedCount,
            topK,
            input.KnowledgeScope.Trim(),
            retrievalQuery,
            input.ApplicablePositionId,
            input.SecurityLevel,
            NormalizeTags(input.Tags));

        task.RestrictToPersonalOwner(CurrentUser.Id ?? throw new AbpAuthorizationException("An identified user is required."));
        task.InitializeExecution(fingerprint,
            input.CurriculumScope == null ? null : JsonSerializer.Serialize(input.CurriculumScope, JsonOptions),
            snapshot == null ? null : JsonSerializer.Serialize(snapshot, JsonOptions), input.Score, input.Purpose);
        // 复用聚合的持久化扩展属性，重试必须保留原检索范围；授权仍每次查数据库。
        task.ExtraProperties["KnowledgeSearchScope"] = (int)input.Scope;
        if (input.SchoolId.HasValue) task.ExtraProperties["KnowledgeSearchSchoolId"] = input.SchoolId.Value.ToString("D");
        await _taskRepository.InsertAsync(task, autoSave: true);
        return await MapTaskToDtoAsync(task);
    }

    /// <summary>
    /// 重试已有 AI 出题任务。
    /// 重试只恢复失败运行，不删除候选或改写成功结果。
    /// </summary>
    public virtual async Task<AiQuestionGenerationTaskDto> RetryAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.AiGeneration);

        var task = await _taskRepository.GetAsync(id);
        await SourceAccess.EnsureTaskAsync(task);
        if (task.PersonalOwnerUserId != CurrentUser.Id) throw new AbpAuthorizationException();
        if (task.Status == AiQuestionGenerationTaskStatus.Failed)
        {
            task.QueueRetry(DateTime.UtcNow, WorkerOptions.MaxAttempts);
            await _taskRepository.UpdateAsync(task, autoSave: true);
        }
        return await MapTaskToDtoAsync(task);
    }

    public virtual async Task<AiQuestionGenerationTaskDto> RegenerateAsync(Guid id, RegenerateAiQuestionGenerationTaskDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.AiGeneration);
        var task = await _taskRepository.GetAsync(id);
        await SourceAccess.EnsureTaskAsync(task);
        if (task.PersonalOwnerUserId != CurrentUser.Id) throw new AbpAuthorizationException();
        if (task.QueueRegeneration(input.IdempotencyKey, DateTime.UtcNow, WorkerOptions.MaxRegenerationOperations))
            await _taskRepository.UpdateAsync(task, autoSave: true);
        return await MapTaskToDtoAsync(task);
    }

    public virtual async Task<AiQuestionCandidateDto> UpdateCandidateAsync(Guid id, UpdateAiQuestionCandidateDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.AiGeneration);
        var candidate = await _candidateRepository.GetAsync(id);
        var task = await SourceAccess.EnsureCandidateAsync(candidate);
        if (task.PersonalOwnerUserId != CurrentUser.Id) throw new AbpAuthorizationException();
        if (candidate.ContentRevision != input.ContentRevision) throw new AbpDbConcurrencyException();
        var citations = await GetCandidateCitationsAsync(id);
        var chunks = citations.Select(x => new RetrievedChunk
        {
            ChunkId = x.ChunkId, KnowledgeItemId = x.KnowledgeItemId, KnowledgeVersionId = x.KnowledgeVersionId,
            TextbookKnowledgePointMappingIds = Deserialize<List<Guid>>(x.TextbookKnowledgePointMappingIdsJson)
        }).ToList();
        var model = new ModelQuestion
        {
            Type = candidate.Type.ToString(), Difficulty = candidate.Difficulty.ToString(), Title = input.Title,
            Stem = input.Stem, Analysis = input.Analysis,
            ScoringDefinition = input.ScoringDefinition,
            KnowledgePoints = Deserialize<List<string>>(candidate.KnowledgePointsJson),
            TextbookKnowledgePointMappingIds = Deserialize<List<Guid>>(candidate.CurriculumMappingIdsJson),
            Citations = Deserialize<List<Guid>>(candidate.SubmittedCitationChunkIdsJson).Select(x => new ModelQuestionCitation { ChunkId = x }).ToList()
        };
        var errors = ValidateCandidate(task, model, input.Options, input.Answers, chunks);
        candidate.Edit(input.Title, input.Stem, input.Analysis, JsonSerializer.Serialize(input.Options, JsonOptions),
            JsonSerializer.Serialize(input.Answers, JsonOptions), errors.Count == 0 ? null : string.Join("；", errors), input.ScoringDefinition);
        await _candidateRepository.UpdateAsync(candidate, autoSave: true);
        return await MapCandidateToDtoAsync(candidate);
    }

    /// <summary>
    /// 将 AI 候选题采纳为题库草稿。
    /// 草稿保留 AI 来源和 citation，但不会自动提交审核。
    /// </summary>
    public virtual async Task<QuestionDto> AdoptCandidateAsDraftAsync(Guid candidateId)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.AiGeneration);
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.Questions);
        return await _questionWorkflowAppService.AdoptAiCandidateAsync(candidateId);
    }

    /// <summary>
    /// 兼容旧版调用方：该入口现在只采纳为草稿，不再自动提交审核。
    /// </summary>
    public virtual Task<QuestionDto> SubmitCandidateForReviewAsync(Guid candidateId, SubmitAiQuestionCandidateDto input)
    {
        return AdoptCandidateAsDraftAsync(candidateId);
    }

    /// <summary>
    /// 执行一次 AI 出题任务。
    /// 流程按顺序执行授权 RAG 检索、prompt 组装、模型调用、JSON 解析、候选题创建和调用日志落库；
    /// 任一阶段失败都会安全降级为失败任务并写入审计日志。
    /// </summary>
    [RemoteService(false)]
    [UnitOfWork(IsDisabled = true)]
    public virtual async Task ExecuteClaimedAsync(Guid id, Guid runId, CancellationToken cancellationToken = default)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.AiGeneration);
        AiQuestionGenerationTask task;
        using (_taskRepository.DisableTracking())
            task = await _taskRepository.GetAsync(id, cancellationToken: cancellationToken);
        if (task.TenantId != CurrentTenant.Id || task.PersonalOwnerUserId != CurrentUser.Id) throw new AbpAuthorizationException();
        task.EnsureRun(runId);
        var template = await _promptTemplateRepository.GetAsync(task.PromptTemplateId, cancellationToken: cancellationToken);
        await ExecuteTaskAsync(task, template, runId, cancellationToken);
    }

    private async Task ExecuteTaskAsync(
        AiQuestionGenerationTask task,
        AiQuestionPromptTemplate template,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        IReadOnlyList<RetrievedChunk> chunks;
        try
        {
            var snapshot = GetCurriculumSnapshot(task);
            if (snapshot != null) await CurriculumAccess.ValidateAsync(snapshot, cancellationToken);
            // RAG 检索必须带上租户和用户上下文，确保进入 prompt 的知识片段已经通过 MVP-1 权限边界。
            chunks = await _knowledgeRetriever.SearchAsync(new RagSearchRequest
            {
                Query = task.RetrievalQuery,
                TopK = task.TopK,
                TenantId = CurrentTenant.Id,
                UserId = CurrentUser.Id,
                Scope = GetSearchScope(task),
                SchoolId = GetSearchSchoolId(task),
                CurriculumScope = GetCurriculumScope(task),
                ForQuestionGeneration = true
            }, cancellationToken);
            foreach (var chunk in chunks)
            {
                var metadata = await SourceAccess.EnsureSourceMetadataAsync(chunk.KnowledgeItemId, chunk.KnowledgeVersionId, chunk.ChunkId,
                    requireUsable: true, schoolKnowledgeShareId: chunk.SchoolKnowledgeShareId);
                if (!chunk.SchoolKnowledgeShareId.HasValue) chunk.SourceTitle = metadata.Title ?? "未命名知识";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            await FailTaskAndLogAsync(task, template, RagCallStatus.Failed, stopwatch,
                ex is AbpAuthorizationException ? "authorization" : "retrieval", SafeFailureReason("retrieval"), null, 0, cancellationToken);
            return;
        }

        if (chunks.Count == 0)
        {
            // 没有授权上下文时禁止模型凭空生成题目，任务以 NoContext 安全失败。
            await FailTaskAndLogAsync(
                task,
                template,
                RagCallStatus.NoContext,
                stopwatch,
                "retrieval",
                "No authorized RAG context was available for AI question generation.",
                null,
                0,
                cancellationToken);
            return;
        }

        var userPrompt = BuildUserPrompt(task, template, chunks);
        var modelSelection = await _modelResolver.ResolveAsync(AiModelUsage.AiQuestionGeneration, cancellationToken);
        RagChatClientResult chatResult;
        try
        {
            // 模型只接收受控 prompt 和已授权上下文，不直接读取知识库或题库数据。
            chatResult = await _chatClient.GenerateAsync(new RagChatClientRequest
            {
                ModelName = task.ModelName,
                SystemPrompt = template.SystemPrompt,
                UserPrompt = userPrompt,
                MaxTokens = Math.Max(1, modelSelection.MaxOutputTokens),
                Temperature = modelSelection.Temperature
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            await FailTaskAndLogAsync(task, template, RagCallStatus.Failed, stopwatch, "chat", SafeFailureReason("chat"), userPrompt, chunks.Count, cancellationToken);
            return;
        }

        if (!chatResult.Succeeded)
        {
            await FailTaskAndLogAsync(
                task,
                template,
                RagCallStatus.Failed,
                stopwatch,
                "chat",
                SafeFailureReason("chat"),
                userPrompt,
                chunks.Count,
                cancellationToken,
                chatResult);
            return;
        }

        List<ModelQuestion> modelQuestions;
        try
        {
            // 模型输出必须能解析为预期 JSON 结构；坏格式不会进入候选题转换。
            modelQuestions = ParseModelQuestions(chatResult.Content);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            await FailTaskAndLogAsync(task, template, RagCallStatus.Failed, stopwatch, "parse", SafeFailureReason("parse"), userPrompt, chunks.Count, cancellationToken, chatResult);
            return;
        }

        using var completion = Uow.Begin(requiresNew: true, isTransactional: true);
        var current = await _taskRepository.GetAsync(task.Id, cancellationToken: cancellationToken);
        current.EnsureRun(runId);
        var principal = await AiQuestionGenerationIdentity.CreateAsync(LazyServiceProvider, current, cancellationToken);
        using var identity = LazyServiceProvider.LazyGetRequiredService<ICurrentPrincipalAccessor>().Change(principal);
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.AiGeneration);
        var currentSnapshot = GetCurriculumSnapshot(current);
        if (currentSnapshot != null) await CurriculumAccess.ValidateAsync(currentSnapshot, cancellationToken);
        foreach (var chunk in chunks)
        {
            await SourceAccess.EnsureSourceMetadataAsync(chunk.KnowledgeItemId, chunk.KnowledgeVersionId, chunk.ChunkId,
                requireUsable: true, forUpdate: true, schoolKnowledgeShareId: chunk.SchoolKnowledgeShareId);
            if (currentSnapshot != null)
                await LazyServiceProvider.LazyGetRequiredService<EducationMaterialScope>().ValidateSourceAsync(currentSnapshot,
                    chunk.KnowledgeItemId, chunk.KnowledgeVersionId, chunk.ChunkId, chunk.CurriculumBindingIds.ToList(),
                    chunk.TextbookKnowledgePointMappingIds.ToList(), chunk.SchoolKnowledgeShareId, cancellationToken, forUpdate: true);
        }
        var chunkById = chunks.ToDictionary(x => x.ChunkId);
        var generatedCount = 0;
        foreach (var output in modelQuestions.Take(task.RequestedCount).Select((question, index) => (Question: question, Ordinal: index + 1)))
        {
            // 即使某道题校验失败，也会以 ValidationFailed 候选题落库，方便人工排查模型输出问题。
            var candidate = await _candidateRepository.FindAsync(x => x.TaskId == task.Id && x.RunNumber == current.RunNumber && x.OutputOrdinal == output.Ordinal,
                cancellationToken: cancellationToken) ?? await CreateCandidateAsync(current, output.Question, chunkById, output.Ordinal, cancellationToken);
            if (candidate.Status == AiQuestionCandidateStatus.Generated)
            {
                generatedCount++;
            }
        }

        current.MarkGenerated(generatedCount, DateTime.UtcNow);
        await _taskRepository.UpdateAsync(current, autoSave: true, cancellationToken: cancellationToken);
        await WriteModelCallLogAsync(
            current,
            template,
            RagCallStatus.Succeeded,
            task.RetrievalQuery,
            chunks.Count,
            generatedCount,
            ToElapsedMilliseconds(stopwatch),
            null,
            null,
            userPrompt,
            chatResult,
            cancellationToken);
        await completion.CompleteAsync(cancellationToken);
    }

    /// <summary>
    /// 将单道模型输出转换为候选题实体。
    /// 方法会规范化选项、答案和知识点，匹配 citation，执行结构校验，并写入相似题提示。
    /// </summary>
    private async Task<AiQuestionCandidate> CreateCandidateAsync(
        AiQuestionGenerationTask task,
        ModelQuestion modelQuestion,
        IReadOnlyDictionary<Guid, RetrievedChunk> chunkById,
        int outputOrdinal,
        CancellationToken cancellationToken)
    {
        var options = NormalizeOptions(modelQuestion.Options);
        var answers = NormalizeAnswers(modelQuestion.Answers);
        var knowledgePoints = NormalizeKnowledgePoints(modelQuestion.KnowledgePoints);
        var matchedCitations = MatchCitations(modelQuestion.Citations, chunkById);
        // validationErrors 是安全阀：任何结构问题、citation 缺失或疑似泄露都会让候选题不可提交审核。
        var validationErrors = ValidateCandidate(task, modelQuestion, options, answers, matchedCitations);
        var status = validationErrors.Count == 0
            ? AiQuestionCandidateStatus.Generated
            : AiQuestionCandidateStatus.ValidationFailed;

        var candidate = new AiQuestionCandidate(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            task.Id,
            status,
            task.Type,
            task.Difficulty,
            SafeRequired(modelQuestion.Title, "AI 候选题标题缺失", AiQuestionCandidate.MaxTitleLength),
            SafeRequired(modelQuestion.Stem, "AI 候选题题干缺失", AiQuestionCandidate.MaxStemLength),
            task.ApplicablePositionId,
            task.SecurityLevel,
            task.Tags,
            TrimOptional(modelQuestion.Analysis, AiQuestionCandidate.MaxAnalysisLength),
            JsonSerializer.Serialize(options, JsonOptions),
            JsonSerializer.Serialize(answers, JsonOptions),
            JsonSerializer.Serialize(knowledgePoints, JsonOptions),
            validationErrors.Count == 0 ? null : string.Join("；", validationErrors));

        var snapshot = GetCurriculumSnapshot(task);
        try { candidate.SetScoringDefinition(modelQuestion.ScoringDefinition); }
        catch (BusinessException) { /* Invalid rules are recorded in the controlled validation summary. */ }
        candidate.SetGenerationContext(task.RunNumber, outputOrdinal,
            snapshot == null ? null : JsonSerializer.Serialize(snapshot, JsonOptions),
            JsonSerializer.Serialize(modelQuestion.TextbookKnowledgePointMappingIds ?? [], JsonOptions),
            JsonSerializer.Serialize((modelQuestion.Citations ?? []).Where(x => x != null).Select(x => x.ChunkId).ToList(), JsonOptions), task.Score, task.Purpose,
            snapshot != null && ValidateCurriculumMappings(task, modelQuestion, matchedCitations).Count == 0);
        await _candidateRepository.InsertAsync(candidate, autoSave: false, cancellationToken: cancellationToken);

        foreach (var citation in matchedCitations.Select((chunk, index) => new { Chunk = chunk, Rank = index + 1 }))
        {
            // citation 只保存短摘录和 hash，既能追溯来源，又避免把完整 chunk 复制到候选题表。
            var reference = new AiQuestionCandidateCitation(
                    GuidGenerator.Create(),
                    CurrentTenant.Id,
                    candidate.Id,
                    citation.Chunk.KnowledgeItemId,
                    citation.Chunk.KnowledgeVersionId,
                    citation.Chunk.ChunkId,
                    citation.Rank,
                    Math.Clamp(citation.Chunk.Score, 0, 1),
                    string.IsNullOrWhiteSpace(citation.Chunk.SourceTitle) ? "未命名知识" : citation.Chunk.SourceTitle,
                    citation.Chunk.SourcePage,
                    TrimInline(
                        citation.Chunk.Content,
                        Math.Min(
                            AiQuestionCandidateCitation.MaxSourceExcerptLength,
                            Math.Max(1, _options.AiQuestionGeneration.MaxCitationExcerptLength))),
                    ComputeHash(citation.Chunk.Content));
            reference.SetCurriculumReferences(JsonSerializer.Serialize(citation.Chunk.CurriculumBindingIds, JsonOptions),
                JsonSerializer.Serialize(citation.Chunk.TextbookKnowledgePointMappingIds, JsonOptions), citation.Chunk.SchoolKnowledgeShareId);
            await _candidateCitationRepository.InsertAsync(reference, autoSave: false, cancellationToken: cancellationToken);
        }

        // 相似题检测只产生提示，不阻止候选题生成；是否继续提交由审核人员判断。
        var similarity = await _similarityService.FindMostSimilarAsync(candidate.Title, candidate.Stem, cancellationToken: cancellationToken);
        if (similarity != null)
        {
            candidate.SetSimilarity(similarity.QuestionId, similarity.Score, similarity.Summary);
            await _candidateRepository.UpdateAsync(candidate, autoSave: false, cancellationToken: cancellationToken);
        }

        return candidate;
    }

    /// <summary>
    /// 校验单道候选题是否满足提交审核的最低条件。
    /// 校验覆盖标题/题干/解析、任务题型和难度一致性、citation 可追溯、安全词和正式题库题型结构规则。
    /// </summary>
    private List<string> ValidateCandidate(
        AiQuestionGenerationTask task,
        ModelQuestion modelQuestion,
        IReadOnlyList<AiQuestionOptionDto> options,
        IReadOnlyList<AiQuestionAnswerDto> answers,
        IReadOnlyList<RetrievedChunk> matchedCitations)
    {
        var errors = new List<string>();
        if (modelQuestion.ScoringDefinitionParseFailed)
            errors.Add("评分规则格式无效，请修订评分定义");
        if (string.IsNullOrWhiteSpace(modelQuestion.Title))
        {
            errors.Add("缺少标题");
        }

        if (string.IsNullOrWhiteSpace(modelQuestion.Stem))
        {
            errors.Add("缺少题干");
        }

        if (string.IsNullOrWhiteSpace(modelQuestion.Analysis))
        {
            errors.Add("缺少解析");
        }

        if (string.IsNullOrWhiteSpace(modelQuestion.Type))
        {
            errors.Add("缺少题型");
        }
        else if (!Enum.TryParse<QuestionType>(modelQuestion.Type, ignoreCase: true, out var outputType))
        {
            errors.Add("模型输出题型无效");
        }
        else if (outputType != task.Type)
        {
            errors.Add("模型输出题型与任务题型不一致");
        }

        if (string.IsNullOrWhiteSpace(modelQuestion.Difficulty))
        {
            errors.Add("缺少难度");
        }
        else if (!Enum.TryParse<QuestionDifficulty>(modelQuestion.Difficulty, ignoreCase: true, out var outputDifficulty))
        {
            errors.Add("模型输出难度无效");
        }
        else if (outputDifficulty != task.Difficulty)
        {
            errors.Add("模型输出难度与任务难度不一致");
        }

        if (matchedCitations.Count == 0)
        {
            errors.Add("缺少可追溯 citation");
        }
        if (modelQuestion.Citations == null || modelQuestion.Citations.Any(x => x == null || x.ChunkId == Guid.Empty ||
            matchedCitations.All(chunk => chunk.ChunkId != x.ChunkId)) ||
            modelQuestion.Citations.Select(x => x.ChunkId).Distinct().Count() != modelQuestion.Citations.Count)
            errors.Add("citation 包含空、重复或未实际检索的来源编号");
        if (modelQuestion.Options?.Any(x => x == null) == true || modelQuestion.Answers?.Any(string.IsNullOrWhiteSpace) == true)
            errors.Add("选项或答案包含空值");
        errors.AddRange(ValidateCurriculumMappings(task, modelQuestion, matchedCitations));

        if (ContainsUnsafeLeakage(modelQuestion))
        {
            errors.Add("输出疑似包含考试答案泄露或越权提示");
        }

        try
        {
            // 复用题库领域对象的定义校验，避免候选题和正式题库规则出现两套实现。
            var probe = new Question(
                GuidGenerator.Create(),
                CurrentTenant.Id,
                task.Type,
                task.Difficulty,
                QuestionCreationSource.AiGenerated,
                SafeRequired(modelQuestion.Title, "AI 候选题标题缺失", Question.MaxTitleLength),
                SafeRequired(modelQuestion.Stem, "AI 候选题题干缺失", Question.MaxStemLength),
                task.ApplicablePositionId,
                task.SecurityLevel,
                task.Tags);

            var scope = GetCurriculumSnapshot(task);
            if (scope != null && task.Type is QuestionType.FillBlank or QuestionType.MultipleChoice or QuestionType.Subjective && modelQuestion.ScoringDefinition == null)
                throw new BusinessException("KnowPulse:Education:InvalidScoringDefinition");
            modelQuestion.ScoringDefinition?.ValidateForConfirmation(task.Type, probe.Stem, task.Score,
                scope == null ? null : modelQuestion.TextbookKnowledgePointMappingIds);
            probe.SetScoringDefinitionDraft(modelQuestion.ScoringDefinition);

            probe.EnsureDefinitionIsValid(
                options.Select(x => new QuestionOption(
                    GuidGenerator.Create(),
                    CurrentTenant.Id,
                    probe.Id,
                    x.OptionKey,
                    x.Content,
                    x.DisplayOrder)).ToList(),
                answers.Select(x => new QuestionAnswer(
                    GuidGenerator.Create(),
                    CurrentTenant.Id,
                    probe.Id,
                    x.Value,
                    x.DisplayOrder)).ToList(), task.Score);
        }
        catch (Exception ex) when (ex is BusinessException or ArgumentException)
        {
            errors.Add("题目结构校验失败：" + ex.Message);
        }

        return errors;
    }

    private static List<string> ValidateCurriculumMappings(AiQuestionGenerationTask task, ModelQuestion question,
        IReadOnlyList<RetrievedChunk> citations)
    {
        var snapshot = GetCurriculumSnapshot(task);
        if (snapshot == null) return [];
        var ids = question.TextbookKnowledgePointMappingIds ?? [];
        var allowed = snapshot.Mappings.Select(x => x.MappingId).ToHashSet();
        var cited = citations.SelectMany(x => x.TextbookKnowledgePointMappingIds).ToHashSet();
        var names = snapshot.Mappings.Where(x => ids.Contains(x.MappingId)).Select(x => x.KnowledgePointName).ToHashSet(StringComparer.Ordinal);
        return ids.Count == 0 || ids.Count != ids.Distinct().Count() || ids.Any(x => x == Guid.Empty || !allowed.Contains(x) || !cited.Contains(x)) ||
            (question.KnowledgePoints ?? []).Any(x => string.IsNullOrWhiteSpace(x) || !names.Contains(x.Trim()))
            ? ["候选知识点必须来自已确认教材映射及本题实际引用的资料"] : [];
    }

    /// <summary>
    /// 将任务标记为失败并写入模型调用日志。
    /// 所有失败路径都走同一个 helper，保证页面失败状态和审计日志保持一致。
    /// </summary>
    private async Task FailTaskAndLogAsync(
        AiQuestionGenerationTask task,
        AiQuestionPromptTemplate template,
        RagCallStatus status,
        Stopwatch stopwatch,
        string failureStage,
        string failureReason,
        string? prompt,
        int retrievedChunkCount,
        CancellationToken cancellationToken,
        RagChatClientResult? chatResult = null)
    {
        using var failure = Uow.Begin(requiresNew: true, isTransactional: true);
        var current = await _taskRepository.GetAsync(task.Id, cancellationToken: cancellationToken);
        current.EnsureRun(task.RunId ?? throw new InvalidOperationException("A generation lease is required."));
        current.MarkFailed(failureStage, TrimInline(failureReason, AiQuestionGenerationTask.MaxFailureReasonLength), DateTime.UtcNow);
        if (failureStage == "chat" && current.AttemptCount < WorkerOptions.MaxAttempts)
            current.QueueRetry(DateTime.UtcNow.AddSeconds(Math.Min(1800, WorkerOptions.RetryDelaySeconds * Math.Pow(2, current.AttemptCount - 1))), WorkerOptions.MaxAttempts);
        await _taskRepository.UpdateAsync(current, autoSave: true, cancellationToken: cancellationToken);
        await WriteModelCallLogAsync(
            current,
            template,
            status,
            task.RetrievalQuery,
            retrievedChunkCount,
            candidateCount: 0,
            ToElapsedMilliseconds(stopwatch),
            failureStage,
            failureReason,
            prompt,
            chatResult,
            cancellationToken);
        await failure.CompleteAsync(cancellationToken);
    }

    private static string SafeFailureReason(string stage) => stage switch
    {
        "retrieval" => "KnowPulse:AiGeneration:RetrievalFailed",
        "chat" => "KnowPulse:AiGeneration:ModelFailed",
        "parse" => "KnowPulse:AiGeneration:InvalidResponse",
        _ => "KnowPulse:AiGeneration:WorkerFailed"
    };

    /// <summary>
    /// 写入 AI 出题模型调用日志。
    /// 日志只保存摘要、hash、模型元数据和 token 统计；完整 prompt 和完整响应不进入审计表。
    /// </summary>
    private async Task WriteModelCallLogAsync(
        AiQuestionGenerationTask task,
        AiQuestionPromptTemplate template,
        RagCallStatus status,
        string query,
        int retrievedChunkCount,
        int candidateCount,
        int executionDurationMs,
        string? failureStage,
        string? failureReason,
        string? prompt,
        RagChatClientResult? chatResult,
        CancellationToken cancellationToken)
    {
        var promptTokenCount = chatResult?.PromptTokenCount > 0
            ? chatResult.PromptTokenCount
            : RagCallLogWriter.EstimateTokenCount(prompt ?? query);
        var completionTokenCount = chatResult?.CompletionTokenCount > 0
            ? chatResult.CompletionTokenCount
            : RagCallLogWriter.EstimateTokenCount(chatResult?.Content);
        var totalTokenCount = chatResult?.TotalTokenCount > 0
            ? chatResult.TotalTokenCount
            : promptTokenCount + completionTokenCount;

        await _modelCallLogRepository.InsertAsync(
            new AiQuestionModelCallLog(
                GuidGenerator.Create(),
                CurrentTenant.Id,
                task.Id,
                template.Id,
                template.Version,
                status,
                ComputeHash(query),
                TrimInline(query, AiQuestionModelCallLog.MaxQuerySummaryLength),
                task.ModelProvider,
                task.ModelName,
                task.ModelVersion,
                task.TopK,
                retrievedChunkCount,
                candidateCount,
                promptTokenCount,
                completionTokenCount,
                totalTokenCount,
                executionDurationMs,
                TrimOptional(failureStage, AiQuestionModelCallLog.MaxFailureStageLength),
                TrimOptional(failureReason, AiQuestionModelCallLog.MaxFailureReasonLength)),
            autoSave: true,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 确保当前租户存在默认 AI 出题 prompt 模板。
    /// MVP-2 没有单独的模板管理页面，因此首次生成时按配置 key 和版本自动创建一条可追溯模板。
    /// </summary>
    private async Task<AiQuestionPromptTemplate> EnsureDefaultPromptTemplateAsync()
    {
        var key = ReadOption(_options.AiQuestionGeneration.PromptTemplateKey, AiQuestionPromptDefaults.TemplateKey);
        var version = ReadOption(_options.AiQuestionGeneration.PromptVersion, AiQuestionPromptDefaults.Version);
        var queryable = await _promptTemplateRepository.GetQueryableAsync();
        var existing = await AsyncExecuter.FirstOrDefaultAsync(queryable
            .Where(x => x.TemplateKey == key && x.Version == version));

        if (existing != null)
        {
            return existing;
        }

        return await _promptTemplateRepository.InsertAsync(
            new AiQuestionPromptTemplate(
                GuidGenerator.Create(),
                CurrentTenant.Id,
                key,
                version,
                AiQuestionPromptDefaults.SystemPrompt,
                AiQuestionPromptDefaults.UserPromptTemplate,
                AiQuestionPromptDefaults.Description,
                isActive: true),
            autoSave: true);
    }

    /// <summary>
    /// 根据用户输入构造 RAG 检索 query。
    /// query 合并知识范围、知识点、题型和难度，让检索器优先召回与本次出题目标相关的授权片段。
    /// </summary>
    private static string BuildRetrievalQuery(CreateAiQuestionGenerationTaskDto input)
    {
        var parts = new[]
        {
            input.KnowledgeScope,
            input.KnowledgePointText,
            input.Type.ToString(),
            input.Difficulty.ToString()
        };

        return string.Join(" ", parts.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));
    }

    /// <summary>
    /// 根据任务、模板和 RAG chunk 构造用户 prompt。
    /// 每个 context 都显式写入 chunkId，模型必须在输出中引用这些 ID 才能通过 citation 校验。
    /// </summary>
    private string BuildUserPrompt(
        AiQuestionGenerationTask task,
        AiQuestionPromptTemplate template,
        IReadOnlyList<RetrievedChunk> chunks)
    {
        var contexts = new StringBuilder();
        foreach (var chunk in chunks.Take(task.TopK).Select((chunk, index) => new { Chunk = chunk, Index = index + 1 }))
        {
            // prompt 中只放受控长度的片段摘录，降低上下文过长和敏感信息扩散风险。
            contexts.Append(chunk.Index);
            contexts.Append(". chunkId=");
            contexts.Append(chunk.Chunk.ChunkId);
            if (task.CurriculumSnapshotJson != null)
                contexts.Append(" textbookKnowledgePointMappingIds=" + JsonSerializer.Serialize(chunk.Chunk.TextbookKnowledgePointMappingIds, JsonOptions));
            contexts.Append(" title=");
            contexts.Append(string.IsNullOrWhiteSpace(chunk.Chunk.SourceTitle) ? "未命名知识" : chunk.Chunk.SourceTitle);
            if (chunk.Chunk.SourcePage.HasValue)
            {
                contexts.Append(" page=");
                contexts.Append(chunk.Chunk.SourcePage.Value);
            }

            contexts.Append(" score=");
            contexts.Append(Math.Round(chunk.Chunk.Score, 2));
            contexts.Append(" content=");
            contexts.AppendLine(TrimInline(chunk.Chunk.Content, _options.AiQuestionGeneration.MaxPromptChunkLength));
        }

        var prompt = template.UserPromptTemplate
            .Replace("{questionType}", task.Type.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{difficulty}", task.Difficulty.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{count}", task.RequestedCount.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{knowledgeScope}", task.KnowledgeScope, StringComparison.OrdinalIgnoreCase)
            .Replace("{securityLevel}", task.SecurityLevel.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{contexts}", contexts.ToString(), StringComparison.OrdinalIgnoreCase);
        var snapshot = GetCurriculumSnapshot(task);
        return snapshot == null ? prompt : prompt + "\n教材范围：" + JsonSerializer.Serialize(snapshot, JsonOptions) +
            "\n每题必须返回 textbookKnowledgePointMappingIds 数组，只能使用本题 citation 资料所列的已确认映射编号；knowledgePoints 如填写只能使用对应修订名称。" +
            $"\n每题分值：{task.Score}；用途：{task.Purpose}。教材与资料不足时不要补造题目。" +
            "\n课程多选、填空和Subjective必须提供 scoringDefinition：schemaVersion=1，referenceAnswer为参考解答；" +
            "多选明确 multipleChoiceScoringMode（0全部正确才得分、1无错选时按选中正确项比例得分）；" +
            "填空 blanks 每行包含独立UUID id、displayOrder、在题干唯一出现的stemMarker（如【1】）、score、allowedAnswers、" +
            "textRule（0精确、1忽略大小写、2人工）、trimWhitespace、formatInstructions、textbookKnowledgePointMappingId；" +
            "Subjective用 scoringPoints，每行含独立UUID id、displayOrder、description、score、textbookKnowledgePointMappingId。" +
            "逐空/点评分总和必须等于题分；映射仅用该题确认且有实际citation支持的编号；填空/主观的answers与options为空。" +
            "不能确定公式或表达等价时明确使用人工评分；不要猜测规则或制造占位答案。";
    }

    /// <summary>
    /// 解析模型返回的候选题 JSON。
    /// 兼容直接数组和 {"questions": [...]} 两种格式；其他格式视为坏格式并触发任务失败。
    /// </summary>
    private static List<ModelQuestion> ParseModelQuestions(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("AI question response was empty.");
        }

        var trimmed = content.Trim();
        var questions = trimmed.StartsWith("[", StringComparison.Ordinal)
            ? JsonSerializer.Deserialize<List<ModelQuestion>>(trimmed, JsonOptions)
            : JsonSerializer.Deserialize<ModelQuestionEnvelope>(trimmed, JsonOptions)?.Questions;
        if (questions is not { Count: > 0 })
        {
            throw new InvalidOperationException("AI question response did not contain a questions array.");
        }

        foreach (var question in questions)
        {
            if (question == null) throw new InvalidOperationException("AI question response contained an empty question.");
            try
            {
                if (question.ScoringDefinitionPayload.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
                    question.ScoringDefinition = QuestionScoringDefinition.Parse(question.ScoringDefinitionPayload.GetRawText());
            }
            catch (BusinessException)
            {
                question.ScoringDefinitionParseFailed = true;
            }
            finally
            {
                question.ScoringDefinitionPayload = default;
            }
        }
        return questions;
    }

    /// <summary>
    /// 规范化模型返回的选项。
    /// 缺少 optionKey 时按 A/B/C 补齐，缺少内容时给出占位，随后由题库结构校验决定是否可提交。
    /// </summary>
    private static List<AiQuestionOptionDto> NormalizeOptions(IEnumerable<ModelQuestionOption>? options)
    {
        return (options ?? Array.Empty<ModelQuestionOption>())
            .Where(x => x != null)
            .Select((x, index) => new AiQuestionOptionDto
            {
                OptionKey = TrimRequired(x.OptionKey ?? x.Key, ((char)('A' + index)).ToString(), 16),
                Content = TrimRequired(x.Content, $"选项 {index + 1}", 1024),
                DisplayOrder = x.DisplayOrder > 0 ? x.DisplayOrder : index + 1
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.OptionKey) && !string.IsNullOrWhiteSpace(x.Content))
            .ToList();
    }

    /// <summary>
    /// 规范化模型返回的答案。
    /// 空答案会被过滤，顺序字段用于保持多答案题的模型输出顺序。
    /// </summary>
    private static List<AiQuestionAnswerDto> NormalizeAnswers(IEnumerable<string>? answers)
    {
        return (answers ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select((answer, index) => new AiQuestionAnswerDto
            {
                Value = TrimRequired(answer, string.Empty, 512),
                DisplayOrder = index + 1
            })
            .ToList();
    }

    /// <summary>
    /// 规范化模型抽取的知识点名称。
    /// 这里进行长度限制和大小写去重，避免候选题页面出现重复标签。
    /// </summary>
    private static List<string> NormalizeKnowledgePoints(IEnumerable<string>? values)
    {
        return (values ?? Array.Empty<string>())
            .Select(x => TrimOptional(x, 128))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 将模型输出的 citation chunkId 匹配回本次 RAG 检索结果。
    /// 不在本次授权 chunk 集合中的 ID 会被忽略，从而阻断模型伪造或越权引用。
    /// </summary>
    private static List<RetrievedChunk> MatchCitations(
        IEnumerable<ModelQuestionCitation>? citations,
        IReadOnlyDictionary<Guid, RetrievedChunk> chunkById)
    {
        var result = new List<RetrievedChunk>();
        var seen = new HashSet<Guid>();
        foreach (var citation in citations ?? Array.Empty<ModelQuestionCitation>())
        {
            if (citation == null || citation.ChunkId == Guid.Empty ||
                !seen.Add(citation.ChunkId) ||
                !chunkById.TryGetValue(citation.ChunkId, out var chunk))
            {
                continue;
            }

            result.Add(chunk);
        }

        return result;
    }

    /// <summary>
    /// 将任务实体映射为页面 DTO。
    /// 映射时加载候选题和调用日志，使任务详情页可以一次展示生成结果、引用和失败原因。
    /// </summary>
    private async Task<AiQuestionGenerationTaskDto> MapTaskToDtoAsync(AiQuestionGenerationTask task)
    {
        await SourceAccess.EnsureTaskAsync(task);
        var candidates = await GetTaskCandidatesAsync(task.Id);
        var callLogs = await GetTaskCallLogsAsync(task.Id);

        return new AiQuestionGenerationTaskDto
        {
            Id = task.Id,
            CreationTime = task.CreationTime,
            CreatorId = task.CreatorId,
            LastModificationTime = task.LastModificationTime,
            LastModifierId = task.LastModifierId,
            IsDeleted = task.IsDeleted,
            DeleterId = task.DeleterId,
            DeletionTime = task.DeletionTime,
            Status = task.Status,
            CurriculumScope = GetCurriculumScope(task),
            CurriculumSnapshot = GetCurriculumSnapshot(task), Score = task.Score, Purpose = task.Purpose,
            RunNumber = task.RunNumber, AttemptCount = task.AttemptCount,
            LastHeartbeatAtUtc = task.LastHeartbeatAtUtc, NextAttemptAtUtc = task.NextAttemptAtUtc,
            Type = task.Type,
            Difficulty = task.Difficulty,
            RequestedCount = task.RequestedCount,
            GeneratedCount = task.GeneratedCount,
            TopK = task.TopK,
            KnowledgeScope = task.KnowledgeScope,
            Scope = GetSearchScope(task),
            SchoolId = GetSearchSchoolId(task),
            RetrievalQuery = task.RetrievalQuery,
            PromptVersion = task.PromptVersion,
            ModelProvider = task.ModelProvider,
            ModelName = task.ModelName,
            ModelVersion = task.ModelVersion,
            ApplicablePositionId = task.ApplicablePositionId,
            SecurityLevel = task.SecurityLevel,
            Tags = task.Tags,
            StartedTime = task.StartedTime,
            CompletedTime = task.CompletedTime,
            FailureStage = task.FailureStage,
            FailureReason = task.FailureReason,
            Candidates = candidates,
            CallLogs = callLogs
        };
    }

    private static EducationCurriculumScopeDto? GetCurriculumScope(AiQuestionGenerationTask task) =>
        task.CurriculumScopeJson == null ? null : JsonSerializer.Deserialize<EducationCurriculumScopeDto>(task.CurriculumScopeJson, JsonOptions);

    private static EducationCurriculumSnapshot? GetCurriculumSnapshot(AiQuestionGenerationTask task) =>
        task.CurriculumSnapshotJson == null ? null : JsonSerializer.Deserialize<EducationCurriculumSnapshot>(task.CurriculumSnapshotJson, JsonOptions);

    private static KnowledgeSearchScope GetSearchScope(AiQuestionGenerationTask task)
    {
        if (!task.ExtraProperties.TryGetValue("KnowledgeSearchScope", out var value)) return KnowledgeSearchScope.AllAccessible;
        if (Enum.TryParse<KnowledgeSearchScope>(value?.ToString(), out var scope) && Enum.IsDefined(scope)) return scope;
        throw new InvalidOperationException("The saved knowledge search scope is invalid.");
    }

    private static Guid? GetSearchSchoolId(AiQuestionGenerationTask task)
    {
        if (!task.ExtraProperties.TryGetValue("KnowledgeSearchSchoolId", out var value)) return null;
        if (Guid.TryParse(value?.ToString(), out var schoolId) && schoolId != Guid.Empty) return schoolId;
        throw new InvalidOperationException("The saved knowledge search school is invalid.");
    }

    /// <summary>
    /// 获取任务下所有候选题并按创建时间排序。
    /// </summary>
    private async Task<List<AiQuestionCandidateDto>> GetTaskCandidatesAsync(Guid taskId)
    {
        var queryable = await _candidateRepository.GetQueryableAsync();
        var candidates = await AsyncExecuter.ToListAsync(queryable
            .Where(x => x.TaskId == taskId)
            .OrderBy(x => x.CreationTime));

        var result = new List<AiQuestionCandidateDto>();
        foreach (var candidate in candidates)
        {
            result.Add(await MapCandidateToDtoAsync(candidate));
        }

        return result;
    }

    /// <summary>
    /// 将候选题实体映射为页面 DTO。
    /// JSON 字段在这里反序列化为选项、答案和知识点列表，citation 也一并加载。
    /// </summary>
    private async Task<AiQuestionCandidateDto> MapCandidateToDtoAsync(AiQuestionCandidate candidate)
    {
        await SourceAccess.EnsureCandidateAsync(candidate);
        var citations = await GetCandidateCitationsAsync(candidate.Id);
        var canShowSimilarity = candidate.HighestSimilarQuestionId.HasValue &&
            await SourceAccess.CanReadQuestionAsync(candidate.HighestSimilarQuestionId.Value);
        var canShowLinkedQuestion = candidate.LinkedQuestionId.HasValue &&
            await SourceAccess.CanReadQuestionAsync(candidate.LinkedQuestionId.Value);
        return new AiQuestionCandidateDto
        {
            Id = candidate.Id,
            CreationTime = candidate.CreationTime,
            CreatorId = candidate.CreatorId,
            LastModificationTime = candidate.LastModificationTime,
            LastModifierId = candidate.LastModifierId,
            IsDeleted = candidate.IsDeleted,
            DeleterId = candidate.DeleterId,
            DeletionTime = candidate.DeletionTime,
            TaskId = candidate.TaskId,
            ScoringDefinition = candidate.ScoringDefinitionJson == null ? null : QuestionScoringDefinition.Parse(candidate.ScoringDefinitionJson),
            RunNumber = candidate.RunNumber, OutputOrdinal = candidate.OutputOrdinal,
            ContentRevision = candidate.ContentRevision, IsEdited = candidate.IsEdited,
            Score = candidate.Score, Purpose = candidate.Purpose,
            CurriculumSnapshot = candidate.CurriculumSnapshotJson == null ? null : JsonSerializer.Deserialize<EducationCurriculumSnapshot>(candidate.CurriculumSnapshotJson, JsonOptions),
            TextbookKnowledgePointMappingIds = Deserialize<List<Guid>>(candidate.CurriculumMappingIdsJson),
            SubmittedCitationChunkIds = Deserialize<List<Guid>>(candidate.SubmittedCitationChunkIdsJson),
            HasValidatedCurriculumMapping = candidate.HasValidatedCurriculumMapping,
            Status = candidate.Status,
            Type = candidate.Type,
            Difficulty = candidate.Difficulty,
            Title = candidate.Title,
            Stem = candidate.Stem,
            ApplicablePositionId = candidate.ApplicablePositionId,
            SecurityLevel = candidate.SecurityLevel,
            Tags = candidate.Tags,
            Analysis = candidate.Analysis,
            ValidationErrors = candidate.ValidationErrors,
            LinkedQuestionId = canShowLinkedQuestion ? candidate.LinkedQuestionId : null,
            HighestSimilarQuestionId = canShowSimilarity ? candidate.HighestSimilarQuestionId : null,
            HighestSimilarityScore = canShowSimilarity ? candidate.HighestSimilarityScore : null,
            SimilaritySummary = canShowSimilarity ? candidate.SimilaritySummary : null,
            Options = Deserialize<List<AiQuestionOptionDto>>(candidate.OptionsJson),
            Answers = Deserialize<List<AiQuestionAnswerDto>>(candidate.AnswersJson),
            KnowledgePoints = Deserialize<List<string>>(candidate.KnowledgePointsJson),
            Citations = citations.Select(x => new AiQuestionCandidateCitationDto
            {
                Id = x.Id,
                CandidateId = x.CandidateId,
                KnowledgeItemId = x.KnowledgeItemId,
                KnowledgeVersionId = x.KnowledgeVersionId,
                ChunkId = x.ChunkId,
                SchoolKnowledgeShareId = x.SchoolKnowledgeShareId,
                CurriculumBindingIds = Deserialize<List<Guid>>(x.CurriculumBindingIdsJson),
                TextbookKnowledgePointMappingIds = Deserialize<List<Guid>>(x.TextbookKnowledgePointMappingIdsJson),
                Rank = x.Rank,
                Score = x.Score,
                SourceTitle = x.SourceTitle,
                SourcePage = x.SourcePage,
                SourceExcerpt = x.SourceExcerpt,
                SourceHash = x.SourceHash
            }).ToList()
        };
    }

    /// <summary>
    /// 获取候选题 citation，并按 rank 保持模型输出引用顺序。
    /// </summary>
    private async Task<List<AiQuestionCandidateCitation>> GetCandidateCitationsAsync(Guid candidateId)
    {
        var queryable = await _candidateCitationRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(queryable
            .Where(x => x.CandidateId == candidateId)
            .OrderBy(x => x.Rank));
    }

    /// <summary>
    /// 获取任务模型调用日志摘要。
    /// 日志按创建时间倒序返回，重试任务时最新一次调用排在前面。
    /// </summary>
    private async Task<List<AiQuestionModelCallLogDto>> GetTaskCallLogsAsync(Guid taskId)
    {
        var queryable = await _modelCallLogRepository.GetQueryableAsync();
        var logs = await AsyncExecuter.ToListAsync(queryable
            .Where(x => x.TaskId == taskId)
            .OrderByDescending(x => x.CreationTime));

        return logs.Select(MapModelCallLogToDto).ToList();
    }

    /// <summary>
    /// 将 prompt 模板实体映射为 DTO。
    /// </summary>
    private static AiQuestionPromptTemplateDto MapPromptTemplateToDto(AiQuestionPromptTemplate template)
    {
        return new AiQuestionPromptTemplateDto
        {
            Id = template.Id,
            CreationTime = template.CreationTime,
            CreatorId = template.CreatorId,
            LastModificationTime = template.LastModificationTime,
            LastModifierId = template.LastModifierId,
            IsDeleted = template.IsDeleted,
            DeleterId = template.DeleterId,
            DeletionTime = template.DeletionTime,
            TemplateKey = template.TemplateKey,
            Version = template.Version,
            Description = template.Description,
            SystemPrompt = template.SystemPrompt,
            UserPromptTemplate = template.UserPromptTemplate,
            IsActive = template.IsActive
        };
    }

    /// <summary>
    /// 将模型调用日志实体映射为 DTO。
    /// </summary>
    private static AiQuestionModelCallLogDto MapModelCallLogToDto(AiQuestionModelCallLog log)
    {
        return new AiQuestionModelCallLogDto
        {
            Id = log.Id,
            TaskId = log.TaskId,
            PromptTemplateId = log.PromptTemplateId,
            PromptVersion = log.PromptVersion,
            Status = log.Status,
            QueryHash = log.QueryHash,
            QuerySummary = log.QuerySummary,
            ModelProvider = log.ModelProvider,
            ModelName = log.ModelName,
            ModelVersion = log.ModelVersion,
            TopK = log.TopK,
            RetrievedChunkCount = log.RetrievedChunkCount,
            CandidateCount = log.CandidateCount,
            PromptTokenCount = log.PromptTokenCount,
            CompletionTokenCount = log.CompletionTokenCount,
            TotalTokenCount = log.TotalTokenCount,
            ExecutionDurationMs = log.ExecutionDurationMs,
            FailureStage = log.FailureStage,
            FailureReason = log.FailureReason,
            CreationTime = log.CreationTime
        };
    }

    /// <summary>
    /// 反序列化候选题内部 JSON 字段。
    /// 数据为空或反序列化结果为空时返回默认实例，避免页面 DTO 映射抛出空引用。
    /// </summary>
    private static T Deserialize<T>(string value)
        where T : new()
    {
        return JsonSerializer.Deserialize<T>(value, JsonOptions) ?? new T();
    }

    /// <summary>
    /// 检测模型输出是否包含明显的考试答案泄露或越权表述。
    /// 该检测是 MVP-2 的保守安全阀，命中时让候选题进入 ValidationFailed。
    /// </summary>
    private static bool ContainsUnsafeLeakage(ModelQuestion question)
    {
        var text = string.Join(" ", new[]
        {
            question.Title,
            question.Stem,
            question.Analysis
        });

        return text.Contains("正式考试答案", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("泄露答案", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("越权", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 为必填字段提供安全兜底。
    /// 兜底值让校验失败候选题仍可落库展示，而不是因为缺字段中断整个任务。
    /// </summary>
    private static string SafeRequired(string? value, string fallback, int maxLength)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : TrimInline(value, maxLength);
    }

    /// <summary>
    /// 裁剪必填字符串。
    /// 为空时返回调用方指定的占位值，非空时统一做单行化和长度限制。
    /// </summary>
    private static string TrimRequired(string? value, string fallback, int maxLength)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : TrimInline(value, maxLength);
    }

    /// <summary>
    /// 裁剪可选字符串。
    /// 空白值统一转为空，避免数据库里出现只包含空格的可选字段。
    /// </summary>
    private static string? TrimOptional(string? value, int maxLength)
    {
        return string.IsNullOrWhiteSpace(value) ? null : TrimInline(value, maxLength);
    }

    /// <summary>
    /// 读取配置字符串，空白值回退到默认值。
    /// </summary>
    private static string ReadOption(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    /// <summary>
    /// 将多行文本压缩为受控长度的单行摘要。
    /// 用于失败原因、query 摘要和 citation 摘录，避免页面和日志被长文本撑开。
    /// </summary>
    private static string TrimInline(string value, int maxLength)
    {
        var normalized = value
            .Replace("\r\n", " ")
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength].TrimEnd();
    }

    /// <summary>
    /// 规范化标签文本。
    /// 标签按逗号拆分、去空白和去重，再以统一分隔符保存。
    /// </summary>
    private static string? NormalizeTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
        {
            return null;
        }

        return string.Join(", ", tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 计算 SHA-256 hash。
    /// 用于 query 和 citation 原文追溯，在不保存完整文本的情况下提供一致性比对能力。
    /// </summary>
    private static string ComputeHash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    /// <summary>
    /// 将 Stopwatch 耗时转换为 int 毫秒。
    /// 超过 int 上限时截断，避免极端情况下写入日志溢出。
    /// </summary>
    private static int ToElapsedMilliseconds(Stopwatch stopwatch)
    {
        return stopwatch.ElapsedMilliseconds > int.MaxValue
            ? int.MaxValue
            : (int)stopwatch.ElapsedMilliseconds;
    }

    /// <summary>
    /// 模型响应外层对象。
    /// 允许模型以 {"questions": [...]} 的形式返回多道候选题。
    /// </summary>
    private sealed class ModelQuestionEnvelope
    {
        /// <summary>
        /// 模型生成的候选题列表。
        /// </summary>
        public List<ModelQuestion> Questions { get; set; } = new();
    }

    /// <summary>
    /// 模型返回的单道候选题结构。
    /// 字段名和默认 prompt 中声明的 JSON schema 对齐，解析后还会经过服务端校验。
    /// </summary>
    private sealed class ModelQuestion
    {
        [JsonPropertyName("scoringDefinition")]
        public JsonElement ScoringDefinitionPayload { get; set; }
        [JsonIgnore]
        public QuestionScoringDefinition? ScoringDefinition { get; set; }
        [JsonIgnore]
        public bool ScoringDefinitionParseFailed { get; set; }
        public List<Guid> TextbookKnowledgePointMappingIds { get; set; } = [];
        /// <summary>
        /// 模型声明的题型。仅用于一致性校验，最终候选题题型以任务要求为准。
        /// </summary>
        public string? Type { get; set; }

        /// <summary>
        /// 模型声明的难度。MVP-2 保存任务难度，暂不使用该字段覆盖任务要求。
        /// </summary>
        public string? Difficulty { get; set; }

        /// <summary>
        /// 候选题标题。
        /// </summary>
        public string? Title { get; set; }

        /// <summary>
        /// 候选题题干。
        /// </summary>
        public string? Stem { get; set; }

        /// <summary>
        /// 候选题选项。非选择题可以为空。
        /// </summary>
        public List<ModelQuestionOption> Options { get; set; } = new();

        /// <summary>
        /// 候选题答案列表。
        /// </summary>
        public List<string> Answers { get; set; } = new();

        /// <summary>
        /// 候选题解析。
        /// </summary>
        public string? Analysis { get; set; }

        /// <summary>
        /// 模型抽取的知识点名称。
        /// </summary>
        public List<string> KnowledgePoints { get; set; } = new();

        /// <summary>
        /// 候选题引用的 RAG chunk。
        /// 每个 chunkId 必须来自本次授权检索结果。
        /// </summary>
        public List<ModelQuestionCitation> Citations { get; set; } = new();
    }

    /// <summary>
    /// 模型返回的选项结构。
    /// 同时支持 optionKey 和 key，降低模型字段名轻微漂移造成的解析失败。
    /// </summary>
    private sealed class ModelQuestionOption
    {
        /// <summary>
        /// 推荐字段名，表示选项键，例如 A。
        /// </summary>
        public string? OptionKey { get; set; }

        /// <summary>
        /// 兼容字段名，部分模型可能返回 key。
        /// </summary>
        public string? Key { get; set; }

        /// <summary>
        /// 选项正文。
        /// </summary>
        public string? Content { get; set; }

        /// <summary>
        /// 选项显示顺序。小于等于 0 时由服务端按输出顺序补齐。
        /// </summary>
        public int DisplayOrder { get; set; }
    }

    /// <summary>
    /// 模型返回的 citation 结构。
    /// 服务端只接受本次 RAG 检索结果中存在的 chunkId。
    /// </summary>
    private sealed class ModelQuestionCitation
    {
        /// <summary>
        /// 引用的 RAG chunk ID。
        /// </summary>
        public Guid ChunkId { get; set; }
    }
}
