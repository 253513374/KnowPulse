using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.KnowledgeBase;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace KnowPulse.RagAI;

/// <summary>
/// RAG 质量评测应用服务。评测运行复用现有 IRagAnswerService，确保命中、拒答和越权指标都走真实业务边界。
/// </summary>
[Authorize(KnowPulsePermissions.Rag.Evaluations)]
public class RagEvaluationAppService : KnowPulseAppService, IRagEvaluationAppService
{
    private KnowledgeSourceAccessGuard SourceAccess => LazyServiceProvider.LazyGetRequiredService<KnowledgeSourceAccessGuard>();
    private const int DefaultTopK = 5;
    private const int MaxTopK = 20;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IRepository<RagEvaluationCase, Guid> _caseRepository;
    private readonly IRepository<RagEvaluationRun, Guid> _runRepository;
    private readonly IRepository<RagEvaluationRunCaseResult, Guid> _resultRepository;
    private readonly IRagAnswerService _answerService;
    private readonly IKnowledgeAccessContextProvider _accessContextProvider;

    /// <summary>
    /// 创建 RAG 评测应用服务。
    /// </summary>
    public RagEvaluationAppService(
        IRepository<RagEvaluationCase, Guid> caseRepository,
        IRepository<RagEvaluationRun, Guid> runRepository,
        IRepository<RagEvaluationRunCaseResult, Guid> resultRepository,
        IRagAnswerService answerService,
        IKnowledgeAccessContextProvider accessContextProvider)
    {
        _caseRepository = caseRepository;
        _runRepository = runRepository;
        _resultRepository = resultRepository;
        _answerService = answerService;
        _accessContextProvider = accessContextProvider;
    }

    /// <summary>
    /// 创建 RAG 评测用例。
    /// </summary>
    /// <param name="input">评测用例创建输入。</param>
    /// <returns>创建后的评测用例。</returns>
    public virtual async Task<RagEvaluationCaseDto> CreateCaseAsync(CreateRagEvaluationCaseDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Rag.Evaluations);
        await EnsureCitationReferencesAsync(input.ExpectedCitations.Concat(input.ForbiddenCitations));

        var entity = new RagEvaluationCase(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            input.Name,
            input.Question,
            input.ExpectedRefusal,
            Serialize(input.PermissionContext ?? new RagEvaluationPermissionContextDto()),
            Serialize(input.ExpectedCitations),
            Serialize(input.ForbiddenCitations),
            input.Notes);

        entity.RestrictToPersonalOwner(CurrentUser.Id ?? throw new AbpAuthorizationException("An identified user is required."));
        await _caseRepository.InsertAsync(entity, autoSave: true);
        return MapCaseToDto(entity);
    }

    /// <summary>
    /// 更新 RAG 评测用例。
    /// </summary>
    /// <param name="id">评测用例 ID。</param>
    /// <param name="input">评测用例更新输入。</param>
    /// <returns>更新后的评测用例。</returns>
    public virtual async Task<RagEvaluationCaseDto> UpdateCaseAsync(Guid id, UpdateRagEvaluationCaseDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Rag.Evaluations);

        var entity = await _caseRepository.GetAsync(id);
        await EnsureCaseAccessAsync(entity);
        await EnsureCitationReferencesAsync(input.ExpectedCitations.Concat(input.ForbiddenCitations));
        entity.RestrictToPersonalOwner(CurrentUser.Id ?? throw new AbpAuthorizationException("An identified user is required."));
        entity.Update(
            input.Name,
            input.Question,
            input.ExpectedRefusal,
            Serialize(input.PermissionContext ?? new RagEvaluationPermissionContextDto()),
            Serialize(input.ExpectedCitations),
            Serialize(input.ForbiddenCitations),
            input.Notes);
        entity.SetActive(input.IsActive);

        await _caseRepository.UpdateAsync(entity, autoSave: true);
        return MapCaseToDto(entity);
    }

    /// <summary>
    /// 读取单个 RAG 评测用例。
    /// </summary>
    /// <param name="id">评测用例 ID。</param>
    /// <returns>评测用例详情。</returns>
    public virtual async Task<RagEvaluationCaseDto> GetCaseAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Rag.Evaluations);

        var entity = await _caseRepository.GetAsync(id);
        await EnsureCaseAccessAsync(entity);
        return MapCaseToDto(entity);
    }

    /// <summary>
    /// 分页查询 RAG 评测用例。
    /// </summary>
    /// <param name="input">用例筛选和分页条件。</param>
    /// <returns>评测用例分页结果。</returns>
    public virtual async Task<PagedResultDto<RagEvaluationCaseDto>> GetCaseListAsync(GetRagEvaluationCaseListInput input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Rag.Evaluations);

        var query = await _caseRepository.GetQueryableAsync();
        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var filter = input.Filter.Trim();
            query = query.Where(x => x.Name.Contains(filter) || x.Question.Contains(filter));
        }

        if (input.IsActive.HasValue)
        {
            query = query.Where(x => x.IsActive == input.IsActive.Value);
        }

        query = query.Where(x => x.PersonalOwnerUserId == null || x.PersonalOwnerUserId == CurrentUser.Id);
        var cases = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.CreationTime));
        var visible = new List<RagEvaluationCase>();
        foreach (var evaluationCase in cases)
        {
            if (await CanReadCaseAsync(evaluationCase))
            {
                visible.Add(evaluationCase);
            }
        }

        return new PagedResultDto<RagEvaluationCaseDto>(visible.Count,
            visible.Skip(input.SkipCount).Take(input.MaxResultCount).Select(MapCaseToDto).ToList());
    }

    /// <summary>
    /// 执行一次 RAG 质量评测运行。
    /// </summary>
    /// <param name="input">评测运行输入。</param>
    /// <returns>评测运行结果，包含聚合指标和逐用例结果。</returns>
    public virtual async Task<RagEvaluationRunDto> RunAsync(RunRagEvaluationDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Rag.Evaluations);

        var cases = await GetRunnableCasesAsync(input);
        if (cases.Count == 0)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.RagEvaluationNoCases)
                .WithData("Message", "No active RAG evaluation cases are available.");
        }

        var run = new RagEvaluationRun(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            string.IsNullOrWhiteSpace(input.Name) ? $"RAG Evaluation {Clock.Now:yyyyMMddHHmmss}" : input.Name.Trim(),
            CurrentUser.Id,
            Clock.Now);

        run.RestrictToPersonalOwner(CurrentUser.Id ?? throw new AbpAuthorizationException("An identified user is required."));
        await _runRepository.InsertAsync(run, autoSave: true);

        var topK = input.TopK <= 0 ? DefaultTopK : Math.Min(input.TopK, MaxTopK);
        var accessContext = await _accessContextProvider.CreateAsync();
        var results = new List<RagEvaluationRunCaseResult>();

        foreach (var evaluationCase in cases)
        {
            var result = await EvaluateCaseAsync(run.Id, evaluationCase, accessContext, topK);
            results.Add(result);
            await _resultRepository.InsertAsync(result, autoSave: true);
        }

        var totalActualCitations = results.Sum(x => x.ActualCitationCount);
        var answerCaseResults = results.Where(x => !x.ExpectedRefusal).ToList();
        var refusalCaseResults = results.Where(x => x.ExpectedRefusal).ToList();
        var unauthorizedRecallCount = results.Sum(x => x.UnauthorizedCitationCount);
        var failedCaseCount = results.Count(x => !x.Passed);
        var hardFailed = unauthorizedRecallCount > 0;

        run.Complete(
            Clock.Now,
            results.Count,
            results.Count - failedCaseCount,
            failedCaseCount,
            CalculateHitRate(answerCaseResults),
            CalculateCitationAccuracyRate(answerCaseResults),
            CalculateNoAnswerRejectionRate(refusalCaseResults),
            totalActualCitations == 0 ? 0 : (double)unauthorizedRecallCount / totalActualCitations,
            unauthorizedRecallCount,
            hardFailed,
            BuildRunFailureReason(hardFailed, unauthorizedRecallCount, failedCaseCount));

        await _runRepository.UpdateAsync(run, autoSave: true);
        return MapRunToDto(run, results);
    }

    /// <summary>
    /// 读取单次 RAG 评测运行和逐用例结果。
    /// </summary>
    /// <param name="id">评测运行 ID。</param>
    /// <returns>评测运行详情和逐用例结果。</returns>
    public virtual async Task<RagEvaluationRunDto> GetRunAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Rag.Evaluations);

        var run = await _runRepository.GetAsync(id);
        var results = await GetResultsByRunIdAsync(run.Id);
        await EnsureRunAccessAsync(run, results);
        return MapRunToDto(run, results);
    }

    /// <summary>
    /// 分页查询 RAG 评测运行。
    /// </summary>
    /// <param name="input">运行记录筛选和分页条件。</param>
    /// <returns>评测运行分页结果。</returns>
    public virtual async Task<PagedResultDto<RagEvaluationRunDto>> GetRunListAsync(GetRagEvaluationRunListInput input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Rag.Evaluations);

        var query = await _runRepository.GetQueryableAsync();
        if (input.Status.HasValue)
        {
            query = query.Where(x => x.Status == input.Status.Value);
        }

        if (input.HardFailed.HasValue)
        {
            query = query.Where(x => x.HardFailed == input.HardFailed.Value);
        }

        query = query.Where(x => x.PersonalOwnerUserId == null || x.PersonalOwnerUserId == CurrentUser.Id);
        var runs = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.CreationTime));
        var visible = new List<RagEvaluationRun>();
        foreach (var run in runs)
        {
            try
            {
                await EnsureRunAccessAsync(run, await GetResultsByRunIdAsync(run.Id));
                visible.Add(run);
            }
            catch (AbpAuthorizationException)
            {
                // 整条记录在分页和统计前排除，避免运行名称与聚合摘要泄漏。
            }
        }

        return new PagedResultDto<RagEvaluationRunDto>(visible.Count,
            visible.Skip(input.SkipCount).Take(input.MaxResultCount)
                .Select(x => MapRunToDto(x, Array.Empty<RagEvaluationRunCaseResult>())).ToList());
    }

    /// <summary>
    /// 读取本次请求应运行的用例集合。
    /// </summary>
    private async Task<List<RagEvaluationCase>> GetRunnableCasesAsync(RunRagEvaluationDto input)
    {
        var query = await _caseRepository.GetQueryableAsync();
        if (input.CaseIds.Count > 0)
        {
            var caseIds = input.CaseIds.Distinct().ToList();
            query = query.Where(x => caseIds.Contains(x.Id));
        }

        if (input.ActiveCasesOnly)
        {
            query = query.Where(x => x.IsActive);
        }

        var candidates = await AsyncExecuter.ToListAsync(query.OrderBy(x => x.CreationTime));
        var allowed = new List<RagEvaluationCase>();
        foreach (var candidate in candidates)
        {
            if (await CanReadCaseAsync(candidate))
            {
                allowed.Add(candidate);
            }
            else if (input.CaseIds.Contains(candidate.Id))
            {
                throw new AbpAuthorizationException("The evaluation case is not available to the current user.");
            }
        }

        return allowed;
    }

    /// <summary>
    /// 执行单个评测用例。
    /// </summary>
    private async Task<RagEvaluationRunCaseResult> EvaluateCaseAsync(
        Guid runId,
        RagEvaluationCase evaluationCase,
        KnowledgeAccessContext accessContext,
        int topK)
    {
        var permissionContext = Deserialize<RagEvaluationPermissionContextDto>(evaluationCase.PermissionContextJson) ?? new RagEvaluationPermissionContextDto();
        var expectedCitations = Deserialize<List<RagEvaluationCitationExpectationDto>>(evaluationCase.ExpectedCitationsJson) ?? [];
        var forbiddenCitations = Deserialize<List<RagEvaluationCitationExpectationDto>>(evaluationCase.ForbiddenCitationsJson) ?? [];
        var contextError = ValidatePermissionContext(permissionContext, accessContext);

        if (contextError != null)
        {
            return CreateResult(
                runId,
                evaluationCase,
                expectedCitations,
                Array.Empty<RagCitation>(),
                actualRefusal: true,
                hit: false,
                citationAccuracyRate: 0,
                passed: false,
                failureReason: contextError,
                answerSummary: null);
        }

        var answer = await _answerService.AnswerAsync(new RagAnswerRequest
        {
            Question = evaluationCase.Question,
            TenantId = permissionContext.TenantId ?? CurrentTenant.Id,
            UserId = permissionContext.UserId ?? accessContext.UserId,
            TopK = topK
        });

        var actualCitations = answer.Citations.ToList();
        var actualRefusal = IsRefusal(answer);
        var matchedExpectedActualCount = CalculateMatchedActualCitationCount(expectedCitations, actualCitations);
        var unauthorizedCount = actualCitations.Count(citation => forbiddenCitations.Any(expectation => Matches(expectation, citation)));
        var hit = CalculateCaseHit(evaluationCase.ExpectedRefusal, expectedCitations, actualCitations, actualRefusal);
        var citationAccuracyRate = CalculateCaseCitationAccuracy(evaluationCase.ExpectedRefusal, expectedCitations, actualCitations, matchedExpectedActualCount);
        var passed = CalculateCasePassed(
            evaluationCase.ExpectedRefusal,
            expectedCitations,
            actualCitations,
            actualRefusal,
            matchedExpectedActualCount,
            unauthorizedCount);

        return CreateResult(
            runId,
            evaluationCase,
            expectedCitations,
            actualCitations,
            actualRefusal,
            hit,
            citationAccuracyRate,
            passed,
            BuildCaseFailureReason(evaluationCase.ExpectedRefusal, expectedCitations, actualRefusal, matchedExpectedActualCount, unauthorizedCount),
            TrimInline(answer.Answer, RagEvaluationRunCaseResult.MaxAnswerSummaryLength));
    }

    /// <summary>
    /// 创建逐用例结果实体。
    /// </summary>
    private RagEvaluationRunCaseResult CreateResult(
        Guid runId,
        RagEvaluationCase evaluationCase,
        IReadOnlyList<RagEvaluationCitationExpectationDto> expectedCitations,
        IReadOnlyList<RagCitation> actualCitations,
        bool actualRefusal,
        bool hit,
        double citationAccuracyRate,
        bool passed,
        string? failureReason,
        string? answerSummary)
    {
        var forbiddenCitations = Deserialize<List<RagEvaluationCitationExpectationDto>>(evaluationCase.ForbiddenCitationsJson) ?? [];
        var unauthorizedCount = actualCitations.Count(citation => forbiddenCitations.Any(expectation => Matches(expectation, citation)));
        var matchedExpectedActualCount = CalculateMatchedActualCitationCount(expectedCitations, actualCitations);

        return new RagEvaluationRunCaseResult(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            runId,
            evaluationCase.Id,
            TrimInline(evaluationCase.Question, RagEvaluationRunCaseResult.MaxQuestionSummaryLength),
            evaluationCase.ExpectedRefusal,
            actualRefusal,
            expectedCitations.Count,
            actualCitations.Count,
            matchedExpectedActualCount,
            unauthorizedCount,
            hit,
            citationAccuracyRate,
            passed,
            failureReason,
            answerSummary,
            Serialize(actualCitations.Select(x => new RagEvaluationCitationExpectationDto
            {
                KnowledgeItemId = x.KnowledgeItemId,
                KnowledgeVersionId = x.KnowledgeVersionId,
                ChunkId = x.ChunkId
            }).ToList()));
    }

    /// <summary>
    /// 校验用例声明的权限上下文是否与当前主体兼容。
    /// </summary>
    private string? ValidatePermissionContext(RagEvaluationPermissionContextDto context, KnowledgeAccessContext accessContext)
    {
        if (context.TenantId.HasValue && CurrentTenant.Id.HasValue && context.TenantId.Value != CurrentTenant.Id.Value)
        {
            return "Permission context tenant does not match current tenant.";
        }

        if (context.UserId.HasValue && accessContext.UserId.HasValue && context.UserId.Value != accessContext.UserId.Value)
        {
            return "Permission context user does not match current user.";
        }

        if (context.UserId.HasValue && !accessContext.UserId.HasValue)
        {
            return "Permission context requires an authenticated user.";
        }

        if (context.DepartmentIds.Except(accessContext.DepartmentIds).Any())
        {
            return "Permission context contains departments outside current user assignments.";
        }

        if (context.PositionIds.Except(accessContext.PositionIds).Any())
        {
            return "Permission context contains positions outside current user assignments.";
        }

        if (context.RoleIds.Except(accessContext.RoleIds).Any())
        {
            return "Permission context contains roles outside current user roles.";
        }

        if (context.UserIds.Count > 0)
        {
            if (!accessContext.UserId.HasValue || context.UserIds.Any(x => x != accessContext.UserId.Value))
            {
                return "Permission context user id scope is outside current user.";
            }
        }

        if (context.SecurityLevel.HasValue && context.SecurityLevel.Value > accessContext.SecurityLevel)
        {
            return "Permission context security level is higher than current user.";
        }

        return null;
    }

    private async Task<List<RagEvaluationRunCaseResult>> GetResultsByRunIdAsync(Guid runId)
    {
        var queryable = await _resultRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(
            queryable.Where(x => x.EvaluationRunId == runId).OrderBy(x => x.CreationTime));
    }

    private static bool IsRefusal(RagAnswerResult answer)
    {
        return answer.Citations.Count == 0 &&
               (answer.Answer.Contains(RagAnswerService.InsufficientKnowledgeAnswer, StringComparison.Ordinal) ||
                answer.Answer.Contains("知识库依据不足", StringComparison.Ordinal));
    }

    private static bool CalculateCaseHit(
        bool expectedRefusal,
        IReadOnlyList<RagEvaluationCitationExpectationDto> expectedCitations,
        IReadOnlyList<RagCitation> actualCitations,
        bool actualRefusal)
    {
        if (expectedRefusal)
        {
            return actualRefusal;
        }

        return expectedCitations.Count == 0
            ? actualCitations.Count > 0
            : expectedCitations.Any(expectation => actualCitations.Any(citation => Matches(expectation, citation)));
    }

    private static bool CalculateCasePassed(
        bool expectedRefusal,
        IReadOnlyList<RagEvaluationCitationExpectationDto> expectedCitations,
        IReadOnlyList<RagCitation> actualCitations,
        bool actualRefusal,
        int matchedExpectedActualCount,
        int unauthorizedCount)
    {
        if (unauthorizedCount > 0)
        {
            return false;
        }

        if (expectedRefusal)
        {
            return actualRefusal;
        }

        if (actualRefusal || actualCitations.Count == 0)
        {
            return false;
        }

        return expectedCitations.Count == 0 || matchedExpectedActualCount >= expectedCitations.Count;
    }

    private static int CalculateMatchedActualCitationCount(
        IReadOnlyList<RagEvaluationCitationExpectationDto> expectedCitations,
        IReadOnlyList<RagCitation> actualCitations)
    {
        if (actualCitations.Count == 0)
        {
            return 0;
        }

        if (expectedCitations.Count == 0)
        {
            return actualCitations.Count;
        }

        return actualCitations.Count(citation => expectedCitations.Any(expectation => Matches(expectation, citation)));
    }

    private static double CalculateCaseCitationAccuracy(
        bool expectedRefusal,
        IReadOnlyList<RagEvaluationCitationExpectationDto> expectedCitations,
        IReadOnlyList<RagCitation> actualCitations,
        int matchedExpectedActualCount)
    {
        if (expectedRefusal)
        {
            return actualCitations.Count == 0 ? 1 : 0;
        }

        if (actualCitations.Count == 0)
        {
            return 0;
        }

        return expectedCitations.Count == 0 ? 1 : (double)matchedExpectedActualCount / actualCitations.Count;
    }

    private static bool Matches(RagEvaluationCitationExpectationDto expectation, RagCitation citation)
    {
        var hasAnyExpectation = expectation.KnowledgeItemId.HasValue ||
                                expectation.KnowledgeVersionId.HasValue ||
                                expectation.ChunkId.HasValue;

        if (!hasAnyExpectation)
        {
            return false;
        }

        return (!expectation.KnowledgeItemId.HasValue || expectation.KnowledgeItemId.Value == citation.KnowledgeItemId) &&
               (!expectation.KnowledgeVersionId.HasValue || expectation.KnowledgeVersionId.Value == citation.KnowledgeVersionId) &&
               (!expectation.ChunkId.HasValue || expectation.ChunkId.Value == citation.ChunkId);
    }

    private static double CalculateHitRate(IReadOnlyList<RagEvaluationRunCaseResult> answerCaseResults)
    {
        return answerCaseResults.Count == 0
            ? 1
            : (double)answerCaseResults.Count(x => x.Hit) / answerCaseResults.Count;
    }

    private static double CalculateCitationAccuracyRate(IReadOnlyList<RagEvaluationRunCaseResult> answerCaseResults)
    {
        var totalActualCitations = answerCaseResults.Sum(x => x.ActualCitationCount);
        return totalActualCitations == 0
            ? (answerCaseResults.Count == 0 ? 1 : 0)
            : (double)answerCaseResults.Sum(x => x.MatchedExpectedCitationCount) / totalActualCitations;
    }

    private static double CalculateNoAnswerRejectionRate(IReadOnlyList<RagEvaluationRunCaseResult> refusalCaseResults)
    {
        return refusalCaseResults.Count == 0
            ? 1
            : (double)refusalCaseResults.Count(x => x.ActualRefusal) / refusalCaseResults.Count;
    }

    private static string? BuildCaseFailureReason(
        bool expectedRefusal,
        IReadOnlyList<RagEvaluationCitationExpectationDto> expectedCitations,
        bool actualRefusal,
        int matchedExpectedActualCount,
        int unauthorizedCount)
    {
        var reasons = new List<string>();
        if (unauthorizedCount > 0)
        {
            reasons.Add($"Unauthorized recall detected: {unauthorizedCount} citation(s).");
        }

        if (expectedRefusal && !actualRefusal)
        {
            reasons.Add("Expected refusal but answer returned citations.");
        }

        if (!expectedRefusal && actualRefusal)
        {
            reasons.Add("Expected grounded answer but got refusal.");
        }

        if (!expectedRefusal && expectedCitations.Count > 0 && matchedExpectedActualCount < expectedCitations.Count)
        {
            reasons.Add($"Expected citations matched {matchedExpectedActualCount}/{expectedCitations.Count}.");
        }

        return reasons.Count == 0 ? null : string.Join(" ", reasons);
    }

    private static string? BuildRunFailureReason(bool hardFailed, int unauthorizedRecallCount, int failedCaseCount)
    {
        if (hardFailed)
        {
            return $"Hard failure: unauthorized recall detected in {unauthorizedRecallCount} citation(s).";
        }

        return failedCaseCount == 0 ? null : $"{failedCaseCount} evaluation case(s) failed.";
    }

    private async Task EnsureCitationReferencesAsync(IEnumerable<RagEvaluationCitationExpectationDto> citations)
    {
        foreach (var citation in citations)
        {
            await SourceAccess.EnsureSourceAsync(citation.KnowledgeItemId, citation.KnowledgeVersionId, citation.ChunkId);
        }
    }

    private async Task EnsureCaseAccessAsync(RagEvaluationCase evaluationCase)
    {
        SourceAccess.EnsurePersonalOwner(evaluationCase.PersonalOwnerUserId, evaluationCase.TenantId);
        await EnsureCitationReferencesAsync(
            (Deserialize<List<RagEvaluationCitationExpectationDto>>(evaluationCase.ExpectedCitationsJson) ?? [])
                .Concat(Deserialize<List<RagEvaluationCitationExpectationDto>>(evaluationCase.ForbiddenCitationsJson) ?? []));
    }

    private async Task<bool> CanReadCaseAsync(RagEvaluationCase evaluationCase)
    {
        try
        {
            await EnsureCaseAccessAsync(evaluationCase);
            return true;
        }
        catch (AbpAuthorizationException)
        {
            return false;
        }
    }

    private async Task EnsureRunAccessAsync(RagEvaluationRun run, IReadOnlyList<RagEvaluationRunCaseResult> results)
    {
        SourceAccess.EnsurePersonalOwner(run.PersonalOwnerUserId, run.TenantId);
        foreach (var result in results)
        {
            var evaluationCase = await _caseRepository.FindAsync(result.EvaluationCaseId);
            if (evaluationCase == null || result.TenantId != run.TenantId)
            {
                throw new AbpAuthorizationException("The evaluation source is unavailable.");
            }

            await EnsureCaseAccessAsync(evaluationCase);
            await EnsureCitationReferencesAsync(Deserialize<List<RagEvaluationCitationExpectationDto>>(result.ActualCitationsJson) ?? []);
        }
    }

    private static RagEvaluationCaseDto MapCaseToDto(RagEvaluationCase entity)
    {
        return new RagEvaluationCaseDto
        {
            Id = entity.Id,
            TenantId = entity.TenantId,
            Name = entity.Name,
            Question = entity.Question,
            ExpectedRefusal = entity.ExpectedRefusal,
            IsActive = entity.IsActive,
            PermissionContext = Deserialize<RagEvaluationPermissionContextDto>(entity.PermissionContextJson) ?? new RagEvaluationPermissionContextDto(),
            ExpectedCitations = Deserialize<List<RagEvaluationCitationExpectationDto>>(entity.ExpectedCitationsJson) ?? [],
            ForbiddenCitations = Deserialize<List<RagEvaluationCitationExpectationDto>>(entity.ForbiddenCitationsJson) ?? [],
            Notes = entity.Notes,
            CreationTime = entity.CreationTime,
            CreatorId = entity.CreatorId,
            LastModificationTime = entity.LastModificationTime,
            LastModifierId = entity.LastModifierId,
            IsDeleted = entity.IsDeleted,
            DeleterId = entity.DeleterId,
            DeletionTime = entity.DeletionTime
        };
    }

    private static RagEvaluationRunDto MapRunToDto(
        RagEvaluationRun run,
        IReadOnlyList<RagEvaluationRunCaseResult> results)
    {
        return new RagEvaluationRunDto
        {
            Id = run.Id,
            TenantId = run.TenantId,
            Name = run.Name,
            OperatorUserId = run.OperatorUserId,
            Status = run.Status,
            StartedTime = run.StartedTime,
            CompletedTime = run.CompletedTime,
            TotalCaseCount = run.TotalCaseCount,
            PassedCaseCount = run.PassedCaseCount,
            FailedCaseCount = run.FailedCaseCount,
            HitRate = run.HitRate,
            CitationAccuracyRate = run.CitationAccuracyRate,
            NoAnswerRejectionRate = run.NoAnswerRejectionRate,
            UnauthorizedRecallRate = run.UnauthorizedRecallRate,
            UnauthorizedRecallCount = run.UnauthorizedRecallCount,
            HardFailed = run.HardFailed,
            FailureReason = run.FailureReason,
            Results = results.Select(MapResultToDto).ToList()
        };
    }

    private static RagEvaluationRunCaseResultDto MapResultToDto(RagEvaluationRunCaseResult result)
    {
        return new RagEvaluationRunCaseResultDto
        {
            Id = result.Id,
            EvaluationRunId = result.EvaluationRunId,
            EvaluationCaseId = result.EvaluationCaseId,
            QuestionSummary = result.QuestionSummary,
            ExpectedRefusal = result.ExpectedRefusal,
            ActualRefusal = result.ActualRefusal,
            ExpectedCitationCount = result.ExpectedCitationCount,
            ActualCitationCount = result.ActualCitationCount,
            MatchedExpectedCitationCount = result.MatchedExpectedCitationCount,
            UnauthorizedCitationCount = result.UnauthorizedCitationCount,
            Hit = result.Hit,
            CitationAccuracyRate = result.CitationAccuracyRate,
            Passed = result.Passed,
            FailureReason = result.FailureReason,
            AnswerSummary = result.AnswerSummary,
            ActualCitations = Deserialize<List<RagCitation>>(result.ActualCitationsJson) ?? []
        };
    }

    private static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static T? Deserialize<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

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
}
