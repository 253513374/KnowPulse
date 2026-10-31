using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Volo.Abp;
using KnowPulse.AiQuestioning;
using KnowPulse.Education;
using KnowPulse.KnowledgeBase;
using KnowPulse.Permissions;
using KnowPulse.RagAI;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace KnowPulse.QuestionBank;

/// <summary>
/// 题库应用服务，覆盖题目 CRUD、分页筛选、提交审核、审核通过/驳回/退回、停用和归档。
/// 服务层负责显式权限检查、领域状态流转、题目定义校验、来源/知识点/选项/答案替换和 DTO 组装。
/// </summary>
[Authorize(KnowPulsePermissions.QuestionBank.Questions)]
[ExposeServices(typeof(IQuestionAppService), typeof(IQuestionWorkflowAppService), IncludeSelf = true)]
public partial class QuestionAppService : KnowPulseAppService, IQuestionAppService, IQuestionWorkflowAppService
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

    private KnowledgeSourceAccessGuard SourceAccess => LazyServiceProvider.LazyGetRequiredService<KnowledgeSourceAccessGuard>();
    private EducationCurriculumAccessGuard CurriculumAccess => LazyServiceProvider.LazyGetRequiredService<EducationCurriculumAccessGuard>();
    private QuestionCurriculumAccessGuard QuestionCurriculum => LazyServiceProvider.LazyGetRequiredService<QuestionCurriculumAccessGuard>();
    private IUnitOfWorkManager Uow => LazyServiceProvider.LazyGetRequiredService<IUnitOfWorkManager>();

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
        var curriculum = input.CurriculumScope == null ? null : await CurriculumAccess.ResolveAsync(input.CurriculumScope);

        var queryable = await _questionRepository.GetQueryableAsync();
        queryable = queryable.Where(x => x.OwnerTeacherId == CurrentUser.Id ||
            (x.OwnerTeacherId == null && (IsLegacyAdministrator || x.PersonalOwnerUserId == CurrentUser.Id ||
                x.CurriculumSnapshotJson != null)));
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

        queryable = queryable.Where(x => x.PersonalOwnerUserId == null || x.PersonalOwnerUserId == CurrentUser.Id);
        // ponytail: legacy source checks scan candidates before paging; move source authorization into SQL when the catalog makes this costly.
        var questions = await AsyncExecuter.ToListAsync(queryable.OrderByDescending(x => x.CreationTime));
        var visible = new List<Question>();
        foreach (var question in questions)
        {
            if (curriculum != null)
            {
                if (!QuestionCurriculumAccessGuard.MatchesScope(question, curriculum)) continue;
                try { await QuestionCurriculum.EnsureAsync(question); }
                catch (Exception error) when (error is Volo.Abp.Authorization.AbpAuthorizationException || error is Volo.Abp.BusinessException) { continue; }
            }
            if (await SourceAccess.CanReadQuestionAsync(question))
            {
                visible.Add(question);
            }
        }

        var dtos = await MapToDtosAsync(visible.Skip(input.SkipCount).Take(input.MaxResultCount).ToList());
        return new PagedResultDto<QuestionDto>(visible.Count, dtos);
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
        return await MapToDtoAsync(await GetAccessibleQuestionAsync(id));
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
        await GetAccessibleQuestionAsync(id);

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
        await GetAccessibleQuestionAsync(id);

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
        var operationId = input.CurriculumScope == null ? null : input.OperationId;
        if (input.CurriculumScope != null && (!operationId.HasValue || operationId == Guid.Empty))
            throw new Volo.Abp.BusinessException(EducationErrorCodes.InvalidIdentifier);
        var requestHash = operationId.HasValue ? GetManualCreateRequestHash(input) : null;
        try
        {
            using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
            if (operationId.HasValue)
            {
                var previous = await _questionRepository.FindAsync(operationId.Value);
                if (previous != null)
                {
                    EnsureSameManualCreate(previous, requestHash!);
                    return await CompleteQuestionAsync(uow, previous);
                }
            }
            var result = await CreateDraftAsync(input, QuestionCreationSource.Manual, operationId: operationId, createRequestHash: requestHash);
            return await CompleteQuestionAsync(uow, await _questionRepository.GetAsync(result.Id));
        }
        catch (Exception error) when (operationId.HasValue && error is not Volo.Abp.Authorization.AbpAuthorizationException &&
            error is not Volo.Abp.BusinessException && error is not OperationCanceledException)
        {
            using var recovery = Uow.Begin(requiresNew: true, isTransactional: false);
            var winner = await _questionRepository.FindAsync(operationId.Value);
            if (winner == null) throw;
            EnsureSameManualCreate(winner, requestHash!);
            return await CompleteQuestionAsync(recovery, winner);
        }
    }

    private void EnsureSameManualCreate(Question question, string requestHash)
    {
        if (!CurrentUser.IsAuthenticated || question.TenantId != CurrentTenant.Id || question.PersonalOwnerUserId != CurrentUser.Id)
            throw new Volo.Abp.Authorization.AbpAuthorizationException();
        if (question.GetProperty<string?>("EducationManualCreateRequestHash") != requestHash)
            throw new Volo.Abp.BusinessException("KnowPulse:Education:QuestionCreateOperationConflict");
    }

    private static string GetManualCreateRequestHash(SaveQuestionDraftDto input)
    {
        var normalized = new
        {
            input.Type, input.Difficulty, input.Title, input.Stem, input.ApplicablePositionId, input.SecurityLevel,
            Tags = NormalizeTags(input.Tags), Analysis = TrimToNull(input.Analysis),
            Options = input.Options.Where(x => !string.IsNullOrWhiteSpace(x.OptionKey) && !string.IsNullOrWhiteSpace(x.Content))
                .Select((x, i) => new { Key = x.OptionKey.Trim(), Content = x.Content.Trim(), Order = x.DisplayOrder == 0 ? i + 1 : x.DisplayOrder }).ToArray(),
            Answers = input.Answers.Where(x => !string.IsNullOrWhiteSpace(x.Value))
                .Select((x, i) => new { Value = x.Value.Trim(), Order = x.DisplayOrder == 0 ? i + 1 : x.DisplayOrder }).ToArray(),
            KnowledgePointNames = NormalizeKnowledgePoints(input.KnowledgePointNames).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            Sources = input.Sources.Where(HasSourceValue).Select(x => new
            {
                x.KnowledgeItemId, x.KnowledgeVersionId, x.ChunkId, x.SchoolKnowledgeShareId,
                BindingIds = x.CurriculumBindingIds.OrderBy(id => id).ToArray(), MappingIds = x.TextbookKnowledgePointMappingIds.OrderBy(id => id).ToArray(),
                SourceTitle = x.KnowledgeItemId.HasValue || x.KnowledgeVersionId.HasValue || x.ChunkId.HasValue ? null : TrimToNull(x.SourceTitle),
                SourceExcerpt = TrimToNull(x.SourceExcerpt), SourceHash = TrimToNull(x.SourceHash), ManualSourceDescription = TrimToNull(x.ManualSourceDescription)
            }).ToArray(),
            input.CurriculumScope!.TeacherTextbookSelectionId,
            MappingIds = input.CurriculumScope.TextbookKnowledgePointMappingIds.OrderBy(x => x).ToArray(),
            Score = input.CurriculumScore.ToString("G29", CultureInfo.InvariantCulture),
            ScoringDefinition = input.ScoringDefinition?.ToCanonicalJson(),
            PointWeights = input.CurriculumPointWeights.OrderBy(x => x.TextbookKnowledgePointMappingId).Select(x => new
            {
                x.TextbookKnowledgePointMappingId, Weight = x.Weight.ToString("G29", CultureInfo.InvariantCulture), Score = x.Score.ToString("G29", CultureInfo.InvariantCulture)
            }).ToArray()
        };
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(normalized, JsonOptions))));
    }

    /// <inheritdoc />
    public virtual async Task<QuestionDto> AdoptAiCandidateAsync(Guid candidateId)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.AiGeneration);
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.Questions);
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);

        var candidate = await _candidateRepository.GetAsync(candidateId);
        var task = await SourceAccess.EnsureCandidateAsync(candidate);
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

        var curriculum = candidate.CurriculumSnapshotJson == null ? null : Deserialize<EducationCurriculumSnapshot>(candidate.CurriculumSnapshotJson);
        if (curriculum != null)
        {
            if (!candidate.HasValidatedCurriculumMapping) throw new Volo.Abp.BusinessException(KnowPulseDomainErrorCodes.AiQuestionCandidateNotReady);
            await CurriculumAccess.ValidateForUseAsync(curriculum);
            foreach (var citation in citations.OrderBy(x => x.KnowledgeVersionId).ThenBy(x => x.ChunkId))
                await LazyServiceProvider.LazyGetRequiredService<EducationMaterialScope>().ValidateSourceAsync(curriculum,
                    citation.KnowledgeItemId, citation.KnowledgeVersionId, citation.ChunkId,
                    Deserialize<List<Guid>>(citation.CurriculumBindingIdsJson), Deserialize<List<Guid>>(citation.TextbookKnowledgePointMappingIdsJson), citation.SchoolKnowledgeShareId, forUpdate: true);
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
            ScoringDefinition = candidate.ScoringDefinitionJson == null ? null : QuestionScoringDefinition.Parse(candidate.ScoringDefinitionJson),
            CurriculumScope = curriculum == null ? null : ScopeOf(curriculum),
            CurriculumScore = candidate.Score,
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
                SourceHash = x.SourceHash,
                SchoolKnowledgeShareId = x.SchoolKnowledgeShareId,
                CurriculumBindingIds = Deserialize<List<Guid>>(x.CurriculumBindingIdsJson),
                TextbookKnowledgePointMappingIds = Deserialize<List<Guid>>(x.TextbookKnowledgePointMappingIdsJson)
            }).ToList()
        };

        var question = await CreateDraftAsync(input, QuestionCreationSource.AiGenerated, task.PersonalOwnerUserId, curriculum,
            Deserialize<List<Guid>>(candidate.CurriculumMappingIdsJson));
        if (candidate.HighestSimilarQuestionId.HasValue && candidate.HighestSimilarityScore.HasValue &&
            await SourceAccess.CanReadQuestionAsync(candidate.HighestSimilarQuestionId.Value))
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

        return await CompleteQuestionAsync(uow, await _questionRepository.GetAsync(question.Id));
    }

    /// <inheritdoc />
    public virtual async Task<QuestionDto> SaveDraftAsync(Guid id, SaveQuestionDraftDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.Questions);
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);

        var question = await GetAccessibleQuestionAsync(id);
        if (question.CurriculumSnapshotJson != null && string.IsNullOrWhiteSpace(input.ConcurrencyStamp))
            throw new AbpDbConcurrencyException();
        if (!string.IsNullOrWhiteSpace(input.ConcurrencyStamp) && question.ConcurrencyStamp != input.ConcurrencyStamp)
            throw new AbpDbConcurrencyException();
        var contentChanged = question.CurriculumSnapshotJson != null && await DefinitionChangedAsync(question, input);
        if (input.CurriculumScope != null)
        {
            if (question.PersonalOwnerUserId != CurrentUser.Id) throw new Volo.Abp.Authorization.AbpAuthorizationException();
            var snapshot = await CurriculumAccess.ResolveAsync(input.CurriculumScope);
            var original = question.CurriculumSnapshotJson == null ? null : Deserialize<EducationCurriculumSnapshot>(question.CurriculumSnapshotJson);
            var references = Deserialize<List<EducationCurriculumSourceReference>>(question.CurriculumSourceReferencesJson);
            var scopeChanged = original == null || original.TeacherUserId != snapshot.TeacherUserId || original.SchoolId != snapshot.SchoolId ||
                original.TeachingAssignmentId != snapshot.TeachingAssignmentId || original.TeacherTextbookSelectionId != snapshot.TeacherTextbookSelectionId ||
                original.TextbookVersionId != snapshot.TextbookVersionId || original.EducationSubjectId != snapshot.EducationSubjectId || original.Grade != snapshot.Grade ||
                !original.Mappings.Select(x => x.MappingId).ToHashSet().SetEquals(snapshot.Mappings.Select(x => x.MappingId)) || question.CurriculumScore != input.CurriculumScore;
            var newSource = input.Sources.Any(source => (source.KnowledgeItemId.HasValue || source.KnowledgeVersionId.HasValue || source.ChunkId.HasValue) &&
                !references.Any(old => old.KnowledgeItemId == source.KnowledgeItemId && old.KnowledgeVersionId == source.KnowledgeVersionId && old.ChunkId == source.ChunkId));
            if (scopeChanged || newSource)
            {
                await SetCurriculumDraftAsync(question, input, snapshot, Deserialize<List<Guid>>(question.CurriculumSuggestedMappingIdsJson));
                contentChanged = true;
            }
        }
        else if (question.CurriculumSnapshotJson != null && input.Sources.Any(source => (source.KnowledgeItemId.HasValue || source.KnowledgeVersionId.HasValue || source.ChunkId.HasValue) &&
            !Deserialize<List<EducationCurriculumSourceReference>>(question.CurriculumSourceReferencesJson).Any(old => old.KnowledgeItemId == source.KnowledgeItemId &&
                old.KnowledgeVersionId == source.KnowledgeVersionId && old.ChunkId == source.ChunkId)))
        {
            await SetCurriculumDraftAsync(question, input, Deserialize<EducationCurriculumSnapshot>(question.CurriculumSnapshotJson),
                Deserialize<List<Guid>>(question.CurriculumSuggestedMappingIdsJson));
            contentChanged = true;
        }
        if (question.CurriculumSnapshotJson != null && input.CurriculumPointWeights.Count > 0)
        {
            var originalWeights = Deserialize<List<QuestionCurriculumPointWeight>>(question.CurriculumPointWeightsJson);
            var weightsChanged = !originalWeights.Select(x => (x.TextbookKnowledgePointMappingId, x.Weight, x.Score)).OrderBy(x => x.TextbookKnowledgePointMappingId)
                .SequenceEqual(input.CurriculumPointWeights.Select(x => (x.TextbookKnowledgePointMappingId, x.Weight, x.Score)).OrderBy(x => x.TextbookKnowledgePointMappingId));
            if (weightsChanged)
            {
                Question.ValidatePointWeights(Deserialize<EducationCurriculumSnapshot>(question.CurriculumSnapshotJson), input.CurriculumPointWeights, question.CurriculumScore!.Value);
                question.SetCurriculumPointWeightsDraft(JsonSerializer.Serialize(input.CurriculumPointWeights, JsonOptions));
                contentChanged = true;
            }
        }
        var sourceOwner = await GetInputSourceOwnerAsync(input);
        if (sourceOwner.HasValue)
        {
            question.RestrictToPersonalOwner(sourceOwner.Value);
        }

        question.UpdateDraft(
            input.Type,
            input.Difficulty,
            input.Title,
            input.Stem,
            input.ApplicablePositionId,
            input.SecurityLevel,
            NormalizeTags(input.Tags));

        question.SetScoringDefinitionDraft(input.ScoringDefinition);

        await ReplaceDetailsAsync(id, input);
        if (contentChanged) question.InvalidateCurriculumConfirmation();
        await _questionRepository.UpdateAsync(question, autoSave: true);

        return await CompleteQuestionAsync(uow, question);
    }

    public virtual async Task<QuestionDto> ConfirmCurriculumAsync(Guid id, ConfirmQuestionCurriculumDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.Questions);
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var question = await GetAccessibleQuestionAsync(id);
        if (question.PersonalOwnerUserId != CurrentUser.Id || question.CurriculumSnapshotJson == null)
            throw new Volo.Abp.Authorization.AbpAuthorizationException();
        if (question.ConcurrencyStamp != input.ConcurrencyStamp) throw new Volo.Abp.Data.AbpDbConcurrencyException();
        var snapshot = await CurriculumAccess.ResolveAsync(input.Scope);
        var original = Deserialize<EducationCurriculumSnapshot>(question.CurriculumSnapshotJson);
        if (original.TextbookVersionId != snapshot.TextbookVersionId || original.TeacherTextbookSelectionId != snapshot.TeacherTextbookSelectionId ||
            !original.Mappings.Select(x => x.MappingId).ToHashSet().SetEquals(snapshot.Mappings.Select(x => x.MappingId)))
            throw new Volo.Abp.BusinessException(EducationCurriculumErrorCodes.ReferenceMismatch);
        question.EnsureDefinitionIsValid(await GetOptionsAsync(id), await GetAnswersAsync(id), input.Score, input.PointWeights);
        Question.ValidatePointWeights(original, input.PointWeights, input.Score);
        await QuestionCurriculum.EnsureAsync(question, requireConfirmed: false, forUpdate: true);
        question.ConfirmCurriculum(JsonSerializer.Serialize(input.PointWeights, JsonOptions), input.Score, CurrentUser.Id!.Value, DateTime.UtcNow);
        await _questionRepository.UpdateManyAsync([question], autoSave: true);
        return await CompleteQuestionAsync(uow, question);
    }

    /// <inheritdoc />
    public virtual async Task<QuestionDto> SubmitAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.Questions);
        using var uow = Uow.Begin(requiresNew: true, isTransactional: true);
        var question = await GetAccessibleQuestionAsync(id);
        await QuestionCurriculum.EnsureAsync(question, forUpdate: true);
        await SourceAccess.EnsureQuestionAsync(question, requireUsableSources: true, forUpdate: true);
        var options = await GetOptionsAsync(id);
        var answers = await GetAnswersAsync(id);
        var fromStatus = question.Status;

        question.SubmitForReview(options, answers, DateTime.UtcNow);
        await _questionRepository.UpdateAsync(question, autoSave: true);
        await AddReviewAsync(question, QuestionReviewAction.Submitted, fromStatus, question.Status, "Submitted for review.");

        return await CompleteQuestionAsync(uow, question);
    }

    /// <inheritdoc />
    public virtual async Task<QuestionDto> ChangeAvailabilityAsync(Guid id, ChangeQuestionAvailabilityDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.Questions);
        var question = await GetAccessibleQuestionAsync(id);
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
        QuestionCreationSource creationSource,
        Guid? personalOwnerUserId = null, EducationCurriculumSnapshot? curriculum = null, List<Guid>? suggestedMappings = null,
        Guid? operationId = null, string? createRequestHash = null)
    {
        var sourceOwner = await GetInputSourceOwnerAsync(input);
        if (input.CurriculumScope != null)
        {
            curriculum ??= await CurriculumAccess.ResolveAsync(input.CurriculumScope);
            personalOwnerUserId = CurrentUser.Id ?? throw new Volo.Abp.Authorization.AbpAuthorizationException();
        }
        personalOwnerUserId ??= sourceOwner;
        var question = new Question(
            operationId ?? GuidGenerator.Create(),
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
        if (personalOwnerUserId.HasValue)
        {
            SourceAccess.EnsurePersonalOwner(personalOwnerUserId, CurrentTenant.Id);
            question.RestrictToPersonalOwner(personalOwnerUserId.Value);
        }
        if (curriculum != null) await SetCurriculumDraftAsync(question, input, curriculum, suggestedMappings ?? []);
        question.SetScoringDefinitionDraft(input.ScoringDefinition);
        if (createRequestHash != null) question.SetProperty("EducationManualCreateRequestHash", createRequestHash);

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
        await GetAccessibleQuestionAsync(id);
        await _questionRepository.DeleteAsync(id);
    }

    public virtual async Task<QuestionDto> ConfirmAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.QuestionBank.Questions);
        var question = await GetAccessibleQuestionAsync(id);
        if (question.CurriculumSnapshotJson != null || question.ScoringDefinitionJson != null)
        {
            throw new UserFriendlyException("这道题包含教材范围或结构化评分规则，请在高级题库的题目复核页面完成审核，生成正式评分版本。");
        }
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
            OperationId = input.OperationId,
            ConcurrencyStamp = input.ConcurrencyStamp,
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
            OperationId = input.OperationId,
            ConcurrencyStamp = input.ConcurrencyStamp,
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
            OperationId = input.OperationId,
            ConcurrencyStamp = input.ConcurrencyStamp,
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
        var question = await _questionRepository.GetAsync(questionId);
        var existingSources = await GetQuestionSourcesAsync(questionId);
        if (!question.PersonalOwnerUserId.HasValue)
        {
            // 旧题尚未记录私人归属时也必须检查原引用；来源缺失不能被当作组织来源而删除。
            foreach (var source in existingSources)
            {
                var item = await SourceAccess.EnsureSourceAsync(source.KnowledgeItemId, source.KnowledgeVersionId, source.ChunkId);
                if (item?.OwnerUserId.HasValue == true)
                {
                    question.RestrictToPersonalOwner(CurrentUser.Id ??
                        throw new Volo.Abp.Authorization.AbpAuthorizationException("An identified owner is required."));
                }
            }
        }

        var retainedSources = existingSources.Where(x => question.PersonalOwnerUserId.HasValue &&
            (x.KnowledgeItemId.HasValue || x.KnowledgeVersionId.HasValue || x.ChunkId.HasValue)).ToList();
        var retainedIds = retainedSources.Select(x => x.Id).ToHashSet();
        var sourceReferences = retainedSources.Select(x => (x.KnowledgeItemId, x.KnowledgeVersionId, x.ChunkId)).ToHashSet();
        await DeleteExistingDetailsAsync(questionId, retainedIds);

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

        // Course rules may be incomplete while a teacher saves a draft; confirmation and
        // submission enforce the complete definition. Legacy objective drafts keep their rules.
        if (question.CurriculumSnapshotJson == null && question.Type != QuestionType.Subjective)
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
            // 个人题的授权链随题保留；编辑/清空显示来源不能移除授权依据，重复保存也不新增同一引用。
            if ((source.KnowledgeItemId.HasValue || source.KnowledgeVersionId.HasValue || source.ChunkId.HasValue) &&
                !sourceReferences.Add((source.KnowledgeItemId, source.KnowledgeVersionId, source.ChunkId)))
            {
                continue;
            }

            var entity = new QuestionSource(
                    GuidGenerator.Create(),
                    CurrentTenant.Id,
                    questionId,
                    source.KnowledgeItemId,
                    source.KnowledgeVersionId,
                    source.ChunkId,
                    TrimToNull(source.SourceTitle),
                    TrimToNull(source.SourceExcerpt),
                    TrimToNull(source.SourceHash),
                    TrimToNull(source.ManualSourceDescription));
            if (question.CurriculumSnapshotJson != null || source.SchoolKnowledgeShareId.HasValue)
                entity.SetCurriculumReferences(JsonSerializer.Serialize(source.CurriculumBindingIds, JsonOptions),
                    JsonSerializer.Serialize(source.TextbookKnowledgePointMappingIds, JsonOptions), source.SchoolKnowledgeShareId);
            await _sourceRepository.InsertAsync(entity, autoSave: true);
        }
    }

    private async Task DeleteExistingDetailsAsync(Guid questionId, IReadOnlySet<Guid> retainedSourceIds)
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
            if (!retainedSourceIds.Contains(source.Id))
            {
                await _sourceRepository.DeleteAsync(source, autoSave: true);
            }
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

    private async Task<QuestionDto> CompleteQuestionAsync(IUnitOfWork uow, Question question)
    {
        await uow.SaveChangesAsync();
        var result = await MapToDtoAsync(question);
        await uow.CompleteAsync();
        return result;
    }

    private async Task<QuestionDto> MapToDtoAsync(Question question)
    {
        await SourceAccess.EnsureQuestionAsync(question);
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
            ScoringDefinition = question.ScoringDefinitionJson == null ? null : QuestionScoringDefinition.Parse(question.ScoringDefinitionJson),
            ApprovedDefinitionRevisionId = question.ApprovedDefinitionRevisionId,
            ConcurrencyStamp = question.ConcurrencyStamp,
            Curriculum = question.CurriculumSnapshotJson == null ? null : Deserialize<EducationCurriculumSnapshot>(question.CurriculumSnapshotJson),
            CurriculumSuggestedMappingIds = Deserialize<List<Guid>>(question.CurriculumSuggestedMappingIdsJson),
            CurriculumPointWeights = Deserialize<List<QuestionCurriculumPointWeight>>(question.CurriculumPointWeightsJson),
            CurriculumScoringPoints = QuestionCurriculumAccessGuard.GetScoringPoints(question.CurriculumSnapshotJson, question.CurriculumPointWeightsJson),
            CurriculumScore = question.CurriculumScore,
            CurriculumConfirmed = question.CurriculumConfirmed,
            CurriculumConfirmedByUserId = question.CurriculumConfirmedByUserId,
            CurriculumConfirmedAtUtc = question.CurriculumConfirmedAtUtc,
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
            CurriculumScope = input.CurriculumScope,
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

    private async Task<Question> GetAccessibleQuestionAsync(Guid id)
    {
        var question = await _questionRepository.GetAsync(id);
        await SourceAccess.EnsureQuestionAsync(question);
        return question;
    }

    private async Task<Guid?> GetInputSourceOwnerAsync(SaveQuestionDraftDto input)
    {
        Guid? owner = null;
        foreach (var source in input.Sources.Where(HasSourceValue).OrderBy(x => x.KnowledgeVersionId).ThenBy(x => x.ChunkId))
        {
            var metadata = await SourceAccess.EnsureSourceMetadataAsync(
                source.KnowledgeItemId, source.KnowledgeVersionId, source.ChunkId, requireUsable: true,
                forUpdate: true, schoolKnowledgeShareId: source.SchoolKnowledgeShareId);
            var item = metadata.Item;
            if (item?.OwnerUserId != null)
            {
                // 借用学校共享资料创建的题目归创建者；资料作者的私人修订不进入来源标题。
                owner = CurrentUser.Id;
                if (metadata.IsShared) source.SchoolKnowledgeShareId = metadata.SchoolKnowledgeShareId;
                if (metadata.IsShared && (input.CurriculumScope == null || !source.SchoolKnowledgeShareId.HasValue))
                {
                    source.SourceTitle = metadata.Title;
                }
            }
        }

        return owner;
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
            SchoolKnowledgeShareId = source.SchoolKnowledgeShareId,
            CurriculumBindingIds = Deserialize<List<Guid>>(source.CurriculumBindingIdsJson),
            TextbookKnowledgePointMappingIds = Deserialize<List<Guid>>(source.TextbookKnowledgePointMappingIdsJson),
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

    private async Task SetCurriculumDraftAsync(Question question, SaveQuestionDraftDto input, EducationCurriculumSnapshot snapshot, List<Guid> suggestedMappings)
    {
        await CurriculumAccess.ValidateForUseAsync(snapshot);
        var references = Deserialize<List<EducationCurriculumSourceReference>>(question.CurriculumSourceReferencesJson);
        foreach (var original in references.OrderBy(x => x.KnowledgeVersionId).ThenBy(x => x.ChunkId))
            await LazyServiceProvider.LazyGetRequiredService<EducationMaterialScope>().ValidateSourceAsync(snapshot,
                original.KnowledgeItemId, original.KnowledgeVersionId, original.ChunkId, original.CurriculumBindingIds,
                original.TextbookKnowledgePointMappingIds, original.SchoolKnowledgeShareId, forUpdate: true);
        foreach (var source in input.Sources.Where(x => x.KnowledgeItemId.HasValue || x.KnowledgeVersionId.HasValue || x.ChunkId.HasValue))
        {
            if (references.Any(old => old.KnowledgeItemId == source.KnowledgeItemId && old.KnowledgeVersionId == source.KnowledgeVersionId && old.ChunkId == source.ChunkId)) continue;
            if (!source.KnowledgeItemId.HasValue || !source.KnowledgeVersionId.HasValue || !source.ChunkId.HasValue)
                throw new Volo.Abp.BusinessException("KnowPulse:Education:CurriculumSourceRequired");
            await LazyServiceProvider.LazyGetRequiredService<EducationMaterialScope>().ValidateSourceAsync(snapshot,
                source.KnowledgeItemId.Value, source.KnowledgeVersionId.Value, source.ChunkId.Value,
                source.CurriculumBindingIds, source.TextbookKnowledgePointMappingIds, source.SchoolKnowledgeShareId, forUpdate: true);
            if (source.SchoolKnowledgeShareId.HasValue)
            {
                var share = await LazyServiceProvider.LazyGetRequiredService<IRepository<SchoolKnowledgeShare, Guid>>().GetAsync(source.SchoolKnowledgeShareId.Value);
                source.SourceTitle = share.Title;
            }
            references.Add(new EducationCurriculumSourceReference
            {
                KnowledgeItemId = source.KnowledgeItemId.Value, KnowledgeVersionId = source.KnowledgeVersionId.Value, ChunkId = source.ChunkId.Value,
                SchoolKnowledgeShareId = source.SchoolKnowledgeShareId, CurriculumBindingIds = source.CurriculumBindingIds.ToList(),
                TextbookKnowledgePointMappingIds = source.TextbookKnowledgePointMappingIds.ToList()
            });
        }
        if (suggestedMappings.Any(id => !snapshot.Mappings.Any(x => x.MappingId == id)))
            throw new Volo.Abp.BusinessException(EducationCurriculumErrorCodes.ReferenceMismatch);
        question.SetCurriculumDraft(JsonSerializer.Serialize(snapshot, JsonOptions), JsonSerializer.Serialize(suggestedMappings, JsonOptions),
            JsonSerializer.Serialize(references, JsonOptions), input.CurriculumScore);
        if (input.CurriculumPointWeights.Count > 0)
        {
            Question.ValidatePointWeights(snapshot, input.CurriculumPointWeights, input.CurriculumScore);
            question.SetCurriculumPointWeightsDraft(JsonSerializer.Serialize(input.CurriculumPointWeights, JsonOptions));
        }
    }

    private async Task<bool> DefinitionChangedAsync(Question question, SaveQuestionDraftDto input)
    {
        var current = await MapToDtoAsync(question);
        var options = input.Options.Where(x => !string.IsNullOrWhiteSpace(x.OptionKey) && !string.IsNullOrWhiteSpace(x.Content))
            .Select((x, i) => new { Key = x.OptionKey.Trim(), Content = x.Content.Trim(), Order = x.DisplayOrder == 0 ? i + 1 : x.DisplayOrder }).OrderBy(x => x.Order).ToList();
        var answers = input.Answers.Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .Select((x, i) => new { Value = x.Value.Trim(), Order = x.DisplayOrder == 0 ? i + 1 : x.DisplayOrder }).OrderBy(x => x.Order).ToList();
        return current.Type != input.Type || current.Difficulty != input.Difficulty || current.Title != input.Title || current.Stem != input.Stem ||
            current.ApplicablePositionId != input.ApplicablePositionId || current.SecurityLevel != input.SecurityLevel || current.Tags != NormalizeTags(input.Tags) ||
            current.Analysis != TrimToNull(input.Analysis) || question.ScoringDefinitionJson != input.ScoringDefinition?.ToCanonicalJson() ||
            JsonSerializer.Serialize(current.Options.Select(x => new { Key = x.OptionKey, x.Content, Order = x.DisplayOrder }), JsonOptions) != JsonSerializer.Serialize(options, JsonOptions) ||
            JsonSerializer.Serialize(current.Answers.Select(x => new { x.Value, Order = x.DisplayOrder }), JsonOptions) != JsonSerializer.Serialize(answers, JsonOptions) ||
            !current.KnowledgePoints.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(NormalizeKnowledgePoints(input.KnowledgePointNames));
    }

    private static EducationCurriculumScopeDto ScopeOf(EducationCurriculumSnapshot snapshot) => new()
    { TeacherTextbookSelectionId = snapshot.TeacherTextbookSelectionId, TextbookKnowledgePointMappingIds = snapshot.Mappings.Select(x => x.MappingId).ToList() };

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
