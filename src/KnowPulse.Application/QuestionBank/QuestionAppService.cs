using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.AiQuestioning;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace KnowPulse.QuestionBank;

/// <summary>
/// 题库应用服务，覆盖题目 CRUD、分页筛选、提交审核、审核通过/驳回/退回、停用和归档。
/// 服务层负责显式权限检查、领域状态流转、题目定义校验、来源/知识点/选项/答案替换和 DTO 组装。
/// </summary>
[Authorize(KnowPulsePermissions.QuestionBank.Questions)]
[ExposeServices(typeof(IQuestionAppService), typeof(IQuestionWorkflowAppService), IncludeSelf = true)]
public class QuestionAppService : KnowPulseAppService, IQuestionAppService, IQuestionWorkflowAppService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IRepository<Question, Guid> _questionRepository;
    private readonly IRepository<QuestionOption, Guid> _optionRepository;
    private readonly IRepository<QuestionAnswer, Guid> _answerRepository;
    private readonly IRepository<QuestionAnalysis, Guid> _analysisRepository;
    private readonly IRepository<QuestionKnowledgePoint, Guid> _knowledgePointRepository;
    private readonly IRepository<QuestionSource, Guid> _sourceRepository;
    private readonly IRepository<QuestionReview, Guid> _reviewRepository;
    private readonly IRepository<QuestionSimilarityCheck, Guid> _similarityRepository;
    private readonly IRepository<AiQuestionCandidate, Guid> _candidateRepository;
    private readonly IRepository<AiQuestionCandidateCitation, Guid> _candidateCitationRepository;

    /// <summary>
    /// 创建题库应用服务。
    /// </summary>
    /// <param name="questionRepository">题目聚合仓储。</param>
    /// <param name="optionRepository">题目选项仓储。</param>
    /// <param name="answerRepository">题目标准答案仓储。</param>
    /// <param name="analysisRepository">题目解析仓储。</param>
    /// <param name="knowledgePointRepository">题目知识点仓储。</param>
    /// <param name="sourceRepository">题目来源仓储。</param>
    /// <param name="reviewRepository">题目审核记录仓储。</param>
    public QuestionAppService(
        IRepository<Question, Guid> questionRepository,
        IRepository<QuestionOption, Guid> optionRepository,
        IRepository<QuestionAnswer, Guid> answerRepository,
        IRepository<QuestionAnalysis, Guid> analysisRepository,
        IRepository<QuestionKnowledgePoint, Guid> knowledgePointRepository,
        IRepository<QuestionSource, Guid> sourceRepository,
        IRepository<QuestionReview, Guid> reviewRepository,
        IRepository<QuestionSimilarityCheck, Guid> similarityRepository,
        IRepository<AiQuestionCandidate, Guid> candidateRepository,
        IRepository<AiQuestionCandidateCitation, Guid> candidateCitationRepository)
    {
        _questionRepository = questionRepository;
        _optionRepository = optionRepository;
        _answerRepository = answerRepository;
        _analysisRepository = analysisRepository;
        _knowledgePointRepository = knowledgePointRepository;
        _sourceRepository = sourceRepository;
        _reviewRepository = reviewRepository;
        _similarityRepository = similarityRepository;
        _candidateRepository = candidateRepository;
        _candidateCitationRepository = candidateCitationRepository;
    }

    /// <summary>
    /// 分页查询题库题目。
    /// 支持题型、状态、难度、创建来源、适用岗位、知识点和关键字筛选，并返回题目的完整展示 DTO。
    /// </summary>
    /// <param name="input">分页、排序和筛选条件。</param>
    /// <returns>题目分页结果。</returns>
    public virtual async Task<PagedResultDto<QuestionDto>> GetListAsync(GetQuestionListInput input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.Questions);

        var queryable = await _questionRepository.GetQueryableAsync();
        queryable = queryable.Where(x => x.OwnerTeacherId == CurrentUser.Id ||
            (x.OwnerTeacherId == null && IsLegacyAdministrator));
        queryable = ApplyFilters(queryable, input);

        if (!string.IsNullOrWhiteSpace(input.KnowledgePoint))
        {
            var pointQueryable = await _knowledgePointRepository.GetQueryableAsync();
            var pointQuestionIds = await AsyncExecuter.ToListAsync(
                pointQueryable
                    .Where(x => x.Name.Contains(input.KnowledgePoint.Trim()))
                    .Select(x => x.QuestionId));
            queryable = queryable.Where(x => pointQuestionIds.Contains(x.Id));
        }

        var totalCount = await AsyncExecuter.CountAsync(queryable);
        var questions = await AsyncExecuter.ToListAsync(
            queryable
                .OrderByDescending(x => x.CreationTime)
                .Skip(input.SkipCount)
                .Take(input.MaxResultCount));

        var dtos = await MapToDtosAsync(questions);
        return new PagedResultDto<QuestionDto>(totalCount, dtos);
    }

    /// <summary>
    /// 分页查询正式题库题目。
    /// 该方法会复制调用方筛选条件，并强制追加审核通过和可用于考试的条件。
    /// </summary>
    /// <param name="input">分页、排序和筛选条件。</param>
    /// <returns>正式题库分页结果。</returns>
    public virtual Task<PagedResultDto<QuestionDto>> GetFormalListAsync(GetQuestionListInput input)
    {
        var formalInput = NormalizeFormalListInput(input);
        return GetListAsync(formalInput);
    }

    /// <summary>
    /// 获取题目详情。
    /// 详情包含题目基础信息、选项、答案、解析、知识点、来源和审核记录。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <returns>题目详情 DTO。</returns>
    public virtual async Task<QuestionDto> GetAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.Questions);
        return await MapToDtoAsync(await GetOwnedQuestionAsync(id));
    }

    /// <summary>
    /// 获取题目来源引用列表。
    /// 调用前会先确认题目存在，避免返回孤立来源数据。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <returns>题目来源引用列表。</returns>
    public virtual async Task<ListResultDto<QuestionSourceDto>> GetSourcesAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.Questions);
        await GetOwnedQuestionAsync(id);

        var sources = await GetQuestionSourcesAsync(id);
        return new ListResultDto<QuestionSourceDto>(sources.Select(MapSourceToDto).ToList());
    }

    /// <summary>
    /// 获取题目审核记录列表。
    /// 调用前会先确认题目存在，记录按创建时间正序返回。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <returns>题目审核和状态操作记录列表。</returns>
    public virtual async Task<ListResultDto<QuestionReviewDto>> GetReviewsAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.Questions);
        await GetOwnedQuestionAsync(id);

        var reviews = await GetReviewEntitiesAsync(id);
        return new ListResultDto<QuestionReviewDto>(reviews.Select(MapReviewToDto).ToList());
    }

    /// <summary>
    /// 创建题目草稿。
    /// 创建后会替换写入选项、标准答案、解析、知识点和来源，并执行领域定义校验。
    /// </summary>
    /// <param name="input">题目创建输入。</param>
    /// <returns>创建后的题目详情。</returns>
    public virtual async Task<QuestionDto> CreateAsync(CreateQuestionDto input)
    {
        return await CreateManualDraftAsync(input);
    }

    /// <inheritdoc />
    public virtual async Task<QuestionDto> CreateManualDraftAsync(SaveQuestionDraftDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.Questions);
        return await CreateDraftAsync(input, QuestionCreationSource.Manual);
    }

    /// <inheritdoc />
    public virtual async Task<QuestionDto> AdoptAiCandidateAsync(Guid candidateId)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.AiGeneration);
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.Questions);

        var candidate = await _candidateRepository.GetAsync(candidateId);
        var taskRepository = LazyServiceProvider.LazyGetRequiredService<IRepository<AiQuestionGenerationTask, Guid>>();
        EnsureTeachingOwner((await taskRepository.GetAsync(candidate.TaskId)).OwnerTeacherId);
        if (candidate.LinkedQuestionId.HasValue)
        {
            throw new Volo.Abp.BusinessException(KnowPulseDomainErrorCodes.AiQuestionCandidateAlreadySubmitted);
        }

        if (candidate.Status != AiQuestionCandidateStatus.Generated ||
            !string.IsNullOrWhiteSpace(candidate.ValidationErrors))
        {
            throw new Volo.Abp.BusinessException(KnowPulseDomainErrorCodes.AiQuestionCandidateNotReady);
        }

        var citations = await GetCandidateCitationsAsync(candidate.Id);
        if (citations.Count == 0)
        {
            throw new Volo.Abp.BusinessException(KnowPulseDomainErrorCodes.AiQuestionCandidateCitationRequired);
        }

        var input = new SaveQuestionDraftDto
        {
            Type = candidate.Type,
            Difficulty = candidate.Difficulty,
            Title = candidate.Title,
            Stem = candidate.Stem,
            ApplicablePositionId = candidate.ApplicablePositionId,
            SecurityLevel = candidate.SecurityLevel,
            Tags = candidate.Tags,
            Analysis = candidate.Analysis,
            Options = Deserialize<List<AiQuestionOptionDto>>(candidate.OptionsJson)
                .Select(x => new CreateUpdateQuestionOptionDto
                {
                    OptionKey = x.OptionKey,
                    Content = x.Content,
                    DisplayOrder = x.DisplayOrder
                }).ToList(),
            Answers = Deserialize<List<AiQuestionAnswerDto>>(candidate.AnswersJson)
                .Select(x => new CreateUpdateQuestionAnswerDto
                {
                    Value = x.Value,
                    DisplayOrder = x.DisplayOrder
                }).ToList(),
            KnowledgePointNames = Deserialize<List<string>>(candidate.KnowledgePointsJson),
            Sources = citations.Select(x => new CreateUpdateQuestionSourceDto
            {
                KnowledgeItemId = x.KnowledgeItemId,
                KnowledgeVersionId = x.KnowledgeVersionId,
                ChunkId = x.ChunkId,
                SourceTitle = x.SourceTitle,
                SourceExcerpt = x.SourceExcerpt,
                SourceHash = x.SourceHash
            }).ToList()
        };

        var question = await CreateDraftAsync(input, QuestionCreationSource.AiGenerated);
        if (candidate.HighestSimilarQuestionId.HasValue && candidate.HighestSimilarityScore.HasValue)
        {
            await _similarityRepository.InsertAsync(
                new QuestionSimilarityCheck(
                    GuidGenerator.Create(),
                    CurrentTenant.Id,
                    question.Id,
                    candidate.HighestSimilarQuestionId,
                    candidate.HighestSimilarityScore.Value,
                    candidate.SimilaritySummary),
                autoSave: true);
        }

        candidate.MarkAdoptedAsDraft(question.Id);
        await _candidateRepository.UpdateAsync(candidate, autoSave: true);

        return question;
    }

    /// <inheritdoc />
    public virtual async Task<QuestionDto> SaveDraftAsync(Guid id, SaveQuestionDraftDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.Questions);

        var question = await GetOwnedQuestionAsync(id);
        question.UpdateDraft(
            input.Type,
            input.Difficulty,
            input.Title,
            input.Stem,
            input.ApplicablePositionId,
            input.SecurityLevel,
            NormalizeTags(input.Tags));

        await ReplaceDetailsAsync(id, input);
        await _questionRepository.UpdateAsync(question, autoSave: true);

        return await MapToDtoAsync(question);
    }

    /// <inheritdoc />
    public virtual async Task<QuestionDto> SubmitAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.Questions);
        var question = await GetOwnedQuestionAsync(id);
        var options = await GetOptionsAsync(id);
        var answers = await GetAnswersAsync(id);
        var fromStatus = question.Status;

        question.SubmitForReview(options, answers, DateTime.UtcNow);
        await _questionRepository.UpdateAsync(question, autoSave: true);
        await AddReviewAsync(question, QuestionReviewAction.Submitted, fromStatus, question.Status, "Submitted for review.");

        return await MapToDtoAsync(question);
    }

    /// <inheritdoc />
    public virtual async Task<QuestionDto> DecideReviewAsync(Guid id, DecideQuestionReviewDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.Reviews);

        if (input.Decision is QuestionReviewDecision.ReturnForRevision or QuestionReviewDecision.Reject &&
            string.IsNullOrWhiteSpace(input.Comment))
        {
            throw new Volo.Abp.BusinessException(KnowPulseDomainErrorCodes.QuestionReviewCommentRequired);
        }

        var question = await GetOwnedQuestionAsync(id);
        var fromStatus = question.Status;
        var now = DateTime.UtcNow;
        QuestionReviewAction action;

        switch (input.Decision)
        {
            case QuestionReviewDecision.Approve:
                question.Approve(CurrentUser.Id, now);
                action = QuestionReviewAction.Approved;
                break;
            case QuestionReviewDecision.ReturnForRevision:
                question.ReturnForRevision(CurrentUser.Id, now);
                action = QuestionReviewAction.ReturnedForRevision;
                break;
            case QuestionReviewDecision.Reject:
                question.Reject(CurrentUser.Id, now);
                action = QuestionReviewAction.Rejected;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(input.Decision));
        }

        await _questionRepository.UpdateAsync(question, autoSave: true);
        await AddReviewAsync(question, action, fromStatus, question.Status, input.Comment);
        return await MapToDtoAsync(question);
    }

    /// <inheritdoc />
    public virtual async Task<QuestionDto> ChangeAvailabilityAsync(Guid id, ChangeQuestionAvailabilityDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.Questions);
        var question = await GetOwnedQuestionAsync(id);
        var fromStatus = question.Status;
        QuestionReviewAction reviewAction;
        string comment;

        switch (input.Action)
        {
            case QuestionAvailabilityAction.Disable:
                question.Disable(DateTime.UtcNow);
                reviewAction = QuestionReviewAction.Disabled;
                comment = "Disabled.";
                break;
            case QuestionAvailabilityAction.Archive:
                question.Archive(DateTime.UtcNow);
                reviewAction = QuestionReviewAction.Archived;
                comment = "Archived.";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(input.Action));
        }

        await _questionRepository.UpdateAsync(question, autoSave: true);
        await AddReviewAsync(question, reviewAction, fromStatus, question.Status, comment);
        return await MapToDtoAsync(question);
    }

    private async Task<QuestionDto> CreateDraftAsync(
        SaveQuestionDraftDto input,
        QuestionCreationSource creationSource)
    {
        var question = new Question(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            input.Type,
            input.Difficulty,
            creationSource,
            input.Title,
            input.Stem,
            input.ApplicablePositionId,
            input.SecurityLevel,
            NormalizeTags(input.Tags));

        question.AssignTeacher(CurrentUser.Id!.Value);
        await _questionRepository.InsertAsync(question, autoSave: true);
        await ReplaceDetailsAsync(question.Id, input);

        return await MapToDtoAsync(question);
    }

    /// <summary>
    /// 更新题目内容。
    /// 服务端会整体替换选项、答案、解析、知识点和来源；已归档题目会被领域规则拒绝编辑。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <param name="input">题目更新输入。</param>
    /// <returns>更新后的题目详情。</returns>
    public virtual async Task<QuestionDto> UpdateAsync(Guid id, UpdateQuestionDto input)
    {
        return await SaveDraftAsync(id, input);
    }

    /// <summary>
    /// 删除题目。
    /// 删除行为由 ABP 仓储处理，关联明细通过 EF 关系和软删除/审计机制保留一致性。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <returns>异步删除任务。</returns>
    public virtual async Task DeleteAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.Questions);
        await GetOwnedQuestionAsync(id);
        await _questionRepository.DeleteAsync(id);
    }

    public virtual async Task<QuestionDto> ConfirmAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.Questions);
        var question = await GetOwnedQuestionAsync(id);
        if (question.Status == QuestionStatus.Approved)
        {
            return await MapToDtoAsync(question);
        }

        var before = question.Status;
        if (question.Status != QuestionStatus.PendingReview)
        {
            question.SubmitForReview(await GetOptionsAsync(id), await GetAnswersAsync(id), Clock.Now);
        }

        question.Approve(CurrentUser.Id, Clock.Now);
        await _questionRepository.UpdateAsync(question, autoSave: true);
        await AddReviewAsync(question, QuestionReviewAction.Approved, before, question.Status, "教师本人确认");
        return await MapToDtoAsync(question);
    }

    private async Task<Question> GetOwnedQuestionAsync(Guid id)
    {
        var question = await _questionRepository.GetAsync(id);
        EnsureTeachingOwner(question.OwnerTeacherId);
        return question;
    }

    /// <summary>
    /// 提交题目审核。
    /// 提交前会读取当前选项和标准答案并执行题型约束校验，成功后写入提交审核记录。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <returns>提交审核后的题目详情。</returns>
    public virtual async Task<QuestionDto> SubmitReviewAsync(Guid id)
    {
        return await SubmitAsync(id);
    }

    /// <summary>
    /// 审核通过题目。
    /// 仅具备题目审核权限的用户可调用，成功后题目可进入正式题库和新试卷组卷。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <param name="input">审核意见输入。</param>
    /// <returns>审核通过后的题目详情。</returns>
    [Authorize(KnowPulsePermissions.QuestionBank.Reviews)]
    public virtual async Task<QuestionDto> ApproveAsync(Guid id, ReviewQuestionDto input)
    {
        return await DecideReviewAsync(id, new DecideQuestionReviewDto
        {
            Decision = QuestionReviewDecision.Approve,
            Comment = input.Comment
        });
    }

    /// <summary>
    /// 驳回题目。
    /// 仅具备题目审核权限的用户可调用，驳回后题目可编辑并重新提交审核。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <param name="input">审核意见输入。</param>
    /// <returns>驳回后的题目详情。</returns>
    [Authorize(KnowPulsePermissions.QuestionBank.Reviews)]
    public virtual async Task<QuestionDto> RejectAsync(Guid id, ReviewQuestionDto input)
    {
        return await DecideReviewAsync(id, new DecideQuestionReviewDto
        {
            Decision = QuestionReviewDecision.Reject,
            Comment = input.Comment
        });
    }

    /// <summary>
    /// 退回题目修改。
    /// 仅具备题目审核权限的用户可调用，退回后题目需要修改并重新提交审核。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <param name="input">审核意见输入。</param>
    /// <returns>退回修改后的题目详情。</returns>
    [Authorize(KnowPulsePermissions.QuestionBank.Reviews)]
    public virtual async Task<QuestionDto> ReturnForRevisionAsync(Guid id, ReviewQuestionDto input)
    {
        return await DecideReviewAsync(id, new DecideQuestionReviewDto
        {
            Decision = QuestionReviewDecision.ReturnForRevision,
            Comment = input.Comment
        });
    }

    /// <summary>
    /// 停用题目。
    /// 停用会保留题目历史和审核记录，但题目不再进入后续新试卷筛选。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <returns>停用后的题目详情。</returns>
    public virtual async Task<QuestionDto> DisableAsync(Guid id)
    {
        return await ChangeAvailabilityAsync(id, new ChangeQuestionAvailabilityDto
        {
            Action = QuestionAvailabilityAction.Disable
        });
    }

    /// <summary>
    /// 归档题目。
    /// 归档后题目不再允许编辑或停用，并会写入归档操作记录。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <returns>归档后的题目详情。</returns>
    public virtual async Task<QuestionDto> ArchiveAsync(Guid id)
    {
        return await ChangeAvailabilityAsync(id, new ChangeQuestionAvailabilityDto
        {
            Action = QuestionAvailabilityAction.Archive
        });
    }

    private IQueryable<Question> ApplyFilters(IQueryable<Question> queryable, GetQuestionListInput input)
    {
        if (input.Worklist.HasValue)
        {
            queryable = input.Worklist.Value switch
            {
                QuestionWorklistKind.Drafts => queryable.Where(x =>
                    x.Status == QuestionStatus.Draft ||
                    x.Status == QuestionStatus.ReturnedForRevision ||
                    x.Status == QuestionStatus.Rejected),
                QuestionWorklistKind.ReviewQueue => queryable.Where(x => x.Status == QuestionStatus.PendingReview),
                QuestionWorklistKind.Formal => queryable.Where(x => x.Status == QuestionStatus.Approved),
                _ => throw new ArgumentOutOfRangeException(nameof(input.Worklist))
            };
        }

        if (input.Type.HasValue)
        {
            queryable = queryable.Where(x => x.Type == input.Type.Value);
        }

        if (input.Status.HasValue)
        {
            queryable = queryable.Where(x => x.Status == input.Status.Value);
        }

        if (input.Difficulty.HasValue)
        {
            queryable = queryable.Where(x => x.Difficulty == input.Difficulty.Value);
        }

        if (input.CreationSource.HasValue)
        {
            queryable = queryable.Where(x => x.CreationSource == input.CreationSource.Value);
        }

        if (input.ApplicablePositionId.HasValue)
        {
            queryable = queryable.Where(x => x.ApplicablePositionId == input.ApplicablePositionId.Value);
        }

        if (input.OnlyUsableForExam)
        {
            queryable = queryable.Where(x => x.Status == QuestionStatus.Approved);
        }

        if (!string.IsNullOrWhiteSpace(input.Keyword))
        {
            var keyword = input.Keyword.Trim();
            queryable = queryable.Where(x =>
                x.Title.Contains(keyword) ||
                x.Stem.Contains(keyword) ||
                (x.Tags != null && x.Tags.Contains(keyword)));
        }

        return queryable;
    }

    private async Task ReplaceDetailsAsync(Guid questionId, SaveQuestionDraftDto input)
    {
        await DeleteExistingDetailsAsync(questionId);

        var options = input.Options
            .Where(x => !string.IsNullOrWhiteSpace(x.OptionKey) && !string.IsNullOrWhiteSpace(x.Content))
            .Select((x, index) => new QuestionOption(
                GuidGenerator.Create(),
                CurrentTenant.Id,
                questionId,
                x.OptionKey.Trim(),
                x.Content.Trim(),
                x.DisplayOrder == 0 ? index + 1 : x.DisplayOrder))
            .ToList();

        var answers = input.Answers
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .Select((x, index) => new QuestionAnswer(
                GuidGenerator.Create(),
                CurrentTenant.Id,
                questionId,
                x.Value.Trim(),
                x.DisplayOrder == 0 ? index + 1 : x.DisplayOrder))
            .ToList();

        var question = await _questionRepository.GetAsync(questionId);
        question.EnsureDefinitionIsValid(options, answers);

        foreach (var option in options)
        {
            await _optionRepository.InsertAsync(option, autoSave: true);
        }

        foreach (var answer in answers)
        {
            await _answerRepository.InsertAsync(answer, autoSave: true);
        }

        if (!string.IsNullOrWhiteSpace(input.Analysis))
        {
            await _analysisRepository.InsertAsync(
                new QuestionAnalysis(GuidGenerator.Create(), CurrentTenant.Id, questionId, input.Analysis.Trim()),
                autoSave: true);
        }

        foreach (var name in NormalizeKnowledgePoints(input.KnowledgePointNames))
        {
            await _knowledgePointRepository.InsertAsync(
                new QuestionKnowledgePoint(GuidGenerator.Create(), CurrentTenant.Id, questionId, name),
                autoSave: true);
        }

        foreach (var source in input.Sources.Where(HasSourceValue))
        {
            await _sourceRepository.InsertAsync(
                new QuestionSource(
                    GuidGenerator.Create(),
                    CurrentTenant.Id,
                    questionId,
                    source.KnowledgeItemId,
                    source.KnowledgeVersionId,
                    source.ChunkId,
                    TrimToNull(source.SourceTitle),
                    TrimToNull(source.SourceExcerpt),
                    TrimToNull(source.SourceHash),
                    TrimToNull(source.ManualSourceDescription)),
                autoSave: true);
        }
    }

    private async Task DeleteExistingDetailsAsync(Guid questionId)
    {
        foreach (var option in await GetOptionsAsync(questionId))
        {
            await _optionRepository.DeleteAsync(option, autoSave: true);
        }

        foreach (var answer in await GetAnswersAsync(questionId))
        {
            await _answerRepository.DeleteAsync(answer, autoSave: true);
        }

        var analyses = await GetAnalysesAsync(questionId);
        foreach (var analysis in analyses)
        {
            await _analysisRepository.DeleteAsync(analysis, autoSave: true);
        }

        foreach (var point in await GetKnowledgePointsAsync(questionId))
        {
            await _knowledgePointRepository.DeleteAsync(point, autoSave: true);
        }

        foreach (var source in await GetQuestionSourcesAsync(questionId))
        {
            await _sourceRepository.DeleteAsync(source, autoSave: true);
        }
    }

    private async Task AddReviewAsync(
        Question question,
        QuestionReviewAction action,
        QuestionStatus fromStatus,
        QuestionStatus toStatus,
        string? comment)
    {
        await _reviewRepository.InsertAsync(
            new QuestionReview(
                GuidGenerator.Create(),
                CurrentTenant.Id,
                question.Id,
                CurrentUser.Id,
                action,
                fromStatus,
                toStatus,
                TrimToNull(comment),
                DateTime.UtcNow),
            autoSave: true);
    }

    private async Task<List<QuestionOption>> GetOptionsAsync(Guid questionId)
    {
        var queryable = await _optionRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(queryable.Where(x => x.QuestionId == questionId).OrderBy(x => x.DisplayOrder));
    }

    private async Task<List<QuestionAnswer>> GetAnswersAsync(Guid questionId)
    {
        var queryable = await _answerRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(queryable.Where(x => x.QuestionId == questionId).OrderBy(x => x.DisplayOrder));
    }

    private async Task<List<QuestionAnalysis>> GetAnalysesAsync(Guid questionId)
    {
        var queryable = await _analysisRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(queryable.Where(x => x.QuestionId == questionId));
    }

    private async Task<List<QuestionKnowledgePoint>> GetKnowledgePointsAsync(Guid questionId)
    {
        var queryable = await _knowledgePointRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(queryable.Where(x => x.QuestionId == questionId).OrderBy(x => x.Name));
    }

    private async Task<List<QuestionSource>> GetQuestionSourcesAsync(Guid questionId)
    {
        var queryable = await _sourceRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(queryable.Where(x => x.QuestionId == questionId).OrderBy(x => x.CreationTime));
    }

    private async Task<List<QuestionReview>> GetReviewEntitiesAsync(Guid questionId)
    {
        var queryable = await _reviewRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(queryable.Where(x => x.QuestionId == questionId).OrderBy(x => x.CreationTime));
    }

    private async Task<List<AiQuestionCandidateCitation>> GetCandidateCitationsAsync(Guid candidateId)
    {
        var queryable = await _candidateCitationRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(
            queryable.Where(x => x.CandidateId == candidateId).OrderBy(x => x.Rank));
    }

    private async Task<List<QuestionDto>> MapToDtosAsync(IReadOnlyCollection<Question> questions)
    {
        var result = new List<QuestionDto>();
        foreach (var question in questions)
        {
            result.Add(await MapToDtoAsync(question));
        }

        return result;
    }

    private async Task<QuestionDto> MapToDtoAsync(Question question)
    {
        var analyses = await GetAnalysesAsync(question.Id);
        var options = await GetOptionsAsync(question.Id);
        var answers = await GetAnswersAsync(question.Id);
        var knowledgePoints = await GetKnowledgePointsAsync(question.Id);
        var sources = await GetQuestionSourcesAsync(question.Id);
        var reviews = await GetReviewEntitiesAsync(question.Id);

        return new QuestionDto
        {
            Id = question.Id,
            OwnerTeacherId = question.OwnerTeacherId,
            CreationTime = question.CreationTime,
            CreatorId = question.CreatorId,
            LastModificationTime = question.LastModificationTime,
            LastModifierId = question.LastModifierId,
            IsDeleted = question.IsDeleted,
            DeleterId = question.DeleterId,
            DeletionTime = question.DeletionTime,
            Type = question.Type,
            Status = question.Status,
            Difficulty = question.Difficulty,
            CreationSource = question.CreationSource,
            Title = question.Title,
            Stem = question.Stem,
            ApplicablePositionId = question.ApplicablePositionId,
            SecurityLevel = question.SecurityLevel,
            Tags = question.Tags,
            SubmittedTime = question.SubmittedTime,
            ReviewedTime = question.ReviewedTime,
            ReviewedByUserId = question.ReviewedByUserId,
            IsUsableForExam = question.IsUsableForExam,
            Analysis = analyses.FirstOrDefault()?.Content,
            Options = options.Select(x => new QuestionOptionDto
            {
                Id = x.Id,
                OptionKey = x.OptionKey,
                Content = x.Content,
                DisplayOrder = x.DisplayOrder
            }).ToList(),
            Answers = answers.Select(x => new QuestionAnswerDto
            {
                Id = x.Id,
                Value = x.Value,
                DisplayOrder = x.DisplayOrder
            }).ToList(),
            KnowledgePoints = knowledgePoints.Select(x => new QuestionKnowledgePointDto
            {
                Id = x.Id,
                Name = x.Name
            }).ToList(),
            Sources = sources.Select(MapSourceToDto).ToList(),
            Reviews = reviews.Select(MapReviewToDto).ToList()
        };
    }

    private static GetQuestionListInput NormalizeFormalListInput(GetQuestionListInput input)
    {
        return new GetQuestionListInput
        {
            Worklist = QuestionWorklistKind.Formal,
            Type = input.Type,
            Status = QuestionStatus.Approved,
            Difficulty = input.Difficulty,
            CreationSource = input.CreationSource,
            ApplicablePositionId = input.ApplicablePositionId,
            KnowledgePoint = input.KnowledgePoint,
            Keyword = input.Keyword,
            OnlyUsableForExam = true,
            SkipCount = input.SkipCount,
            MaxResultCount = input.MaxResultCount,
            Sorting = input.Sorting
        };
    }

    private static QuestionReviewDto MapReviewToDto(QuestionReview review)
    {
        return new QuestionReviewDto
        {
            Id = review.Id,
            CreationTime = review.CreationTime,
            CreatorId = review.CreatorId,
            ReviewerUserId = review.ReviewerUserId,
            Action = review.Action,
            FromStatus = review.FromStatus,
            ToStatus = review.ToStatus,
            Comment = review.Comment,
            ReviewTime = review.ReviewTime
        };
    }

    private static QuestionSourceDto MapSourceToDto(QuestionSource source)
    {
        return new QuestionSourceDto
        {
            Id = source.Id,
            KnowledgeItemId = source.KnowledgeItemId,
            KnowledgeVersionId = source.KnowledgeVersionId,
            ChunkId = source.ChunkId,
            SourceTitle = source.SourceTitle,
            SourceExcerpt = source.SourceExcerpt,
            SourceHash = source.SourceHash,
            ManualSourceDescription = source.ManualSourceDescription
        };
    }

    private static List<string> NormalizeKnowledgePoints(IEnumerable<string> values)
    {
        return values
            .Select(TrimToNull)
            .Where(x => x != null)
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? NormalizeTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
        {
            return null;
        }

        return string.Join(", ", tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static bool HasSourceValue(CreateUpdateQuestionSourceDto source)
    {
        return source.KnowledgeItemId.HasValue ||
               source.KnowledgeVersionId.HasValue ||
               source.ChunkId.HasValue ||
               !string.IsNullOrWhiteSpace(source.SourceTitle) ||
               !string.IsNullOrWhiteSpace(source.SourceExcerpt) ||
               !string.IsNullOrWhiteSpace(source.SourceHash) ||
               !string.IsNullOrWhiteSpace(source.ManualSourceDescription);
    }

    private static string? TrimToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static T Deserialize<T>(string value)
        where T : new()
    {
        return JsonSerializer.Deserialize<T>(value, JsonOptions) ?? new T();
    }
}
