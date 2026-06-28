using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Exam;
using KnowPulse.KnowledgeBase;
using KnowPulse.Organization;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace KnowPulse.Learning;

/// <summary>
/// 学习路径应用服务，提供 MVP-3 最小闭环的路径配置和个人学习内容查询。
/// </summary>
[Authorize(KnowPulsePermissions.Learning.View)]
public class LearningPathAppService : KnowPulseAppService, ILearningPathAppService
{
    private readonly IRepository<LearningPath, Guid> _pathRepository;
    private readonly IRepository<LearningPathAssignment, Guid> _assignmentRepository;
    private readonly IRepository<LearningPathRequiredKnowledge, Guid> _requiredKnowledgeRepository;
    private readonly IRepository<LearningPathRecommendedKnowledge, Guid> _recommendedKnowledgeRepository;
    private readonly IRepository<LearningPathRequiredExam, Guid> _requiredExamRepository;
    private readonly IRepository<LearningKnowledgeReadRecord, Guid> _readRecordRepository;
    private readonly IRepository<KnowledgeItem, Guid> _knowledgeItemRepository;
    private readonly IRepository<KnowledgeVersion, Guid> _knowledgeVersionRepository;
    private readonly IRepository<KnowledgePermissionScope, Guid> _knowledgePermissionScopeRepository;
    private readonly IRepository<ExamPlan, Guid> _examPlanRepository;
    private readonly IRepository<ExamAssignment, Guid> _examAssignmentRepository;
    private readonly IRepository<ExamAttempt, Guid> _examAttemptRepository;
    private readonly IRepository<ExamScore, Guid> _examScoreRepository;
    private readonly IRepository<UserOrganizationAssignment, Guid> _userOrganizationAssignmentRepository;
    private readonly IKnowledgeAccessContextProvider _knowledgeAccessContextProvider;

    public LearningPathAppService(
        IRepository<LearningPath, Guid> pathRepository,
        IRepository<LearningPathAssignment, Guid> assignmentRepository,
        IRepository<LearningPathRequiredKnowledge, Guid> requiredKnowledgeRepository,
        IRepository<LearningPathRecommendedKnowledge, Guid> recommendedKnowledgeRepository,
        IRepository<LearningPathRequiredExam, Guid> requiredExamRepository,
        IRepository<LearningKnowledgeReadRecord, Guid> readRecordRepository,
        IRepository<KnowledgeItem, Guid> knowledgeItemRepository,
        IRepository<KnowledgeVersion, Guid> knowledgeVersionRepository,
        IRepository<KnowledgePermissionScope, Guid> knowledgePermissionScopeRepository,
        IRepository<ExamPlan, Guid> examPlanRepository,
        IRepository<ExamAssignment, Guid> examAssignmentRepository,
        IRepository<ExamAttempt, Guid> examAttemptRepository,
        IRepository<ExamScore, Guid> examScoreRepository,
        IRepository<UserOrganizationAssignment, Guid> userOrganizationAssignmentRepository,
        IKnowledgeAccessContextProvider knowledgeAccessContextProvider)
    {
        _pathRepository = pathRepository;
        _assignmentRepository = assignmentRepository;
        _requiredKnowledgeRepository = requiredKnowledgeRepository;
        _recommendedKnowledgeRepository = recommendedKnowledgeRepository;
        _requiredExamRepository = requiredExamRepository;
        _readRecordRepository = readRecordRepository;
        _knowledgeItemRepository = knowledgeItemRepository;
        _knowledgeVersionRepository = knowledgeVersionRepository;
        _knowledgePermissionScopeRepository = knowledgePermissionScopeRepository;
        _examPlanRepository = examPlanRepository;
        _examAssignmentRepository = examAssignmentRepository;
        _examAttemptRepository = examAttemptRepository;
        _examScoreRepository = examScoreRepository;
        _userOrganizationAssignmentRepository = userOrganizationAssignmentRepository;
        _knowledgeAccessContextProvider = knowledgeAccessContextProvider;
    }

    /// <summary>
    /// 管理端分页查询学习路径，返回完整路径配置，不按个人知识权限过滤内容。
    /// </summary>
    /// <param name="input">查询条件和分页参数。</param>
    /// <returns>学习路径分页结果。</returns>
    public virtual async Task<PagedResultDto<LearningPathDto>> GetListAsync(GetLearningPathListInput input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Learning.Manage);

        var queryable = await _pathRepository.GetQueryableAsync();
        queryable = ApplyFilters(queryable, input);

        var totalCount = await AsyncExecuter.CountAsync(queryable);
        var paths = await AsyncExecuter.ToListAsync(queryable
            .OrderByDescending(x => x.CreationTime)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount));

        return new PagedResultDto<LearningPathDto>(totalCount, await MapPathsAsync(paths, filterKnowledgeVisibility: false));
    }

    /// <summary>
    /// 管理端查询单条学习路径详情。
    /// </summary>
    /// <param name="id">学习路径 ID。</param>
    /// <returns>学习路径详情。</returns>
    public virtual async Task<LearningPathDto> GetAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Learning.Manage);
        return await MapPathAsync(await _pathRepository.GetAsync(id), filterKnowledgeVisibility: false);
    }

    /// <summary>
    /// 创建学习路径，并整体保存分配、知识和考试配置。
    /// </summary>
    /// <param name="input">学习路径创建输入。</param>
    /// <returns>创建后的学习路径详情。</returns>
    public virtual async Task<LearningPathDto> CreateAsync(CreateLearningPathDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Learning.Manage);
        EnsureInputHasAssignmentAndContent(input);

        var path = await _pathRepository.InsertAsync(
            new LearningPath(GuidGenerator.Create(), CurrentTenant.Id, input.Name.Trim(), TrimToNull(input.Description)),
            autoSave: true);

        await ReplaceConfigurationAsync(path.Id, input);

        return await MapPathAsync(path, filterKnowledgeVisibility: false);
    }

    /// <summary>
    /// 更新学习路径基础信息和完整配置，配置变更会递增路径版本。
    /// </summary>
    /// <param name="id">学习路径 ID。</param>
    /// <param name="input">学习路径更新输入。</param>
    /// <returns>更新后的学习路径详情。</returns>
    public virtual async Task<LearningPathDto> UpdateAsync(Guid id, UpdateLearningPathDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Learning.Manage);
        EnsureInputHasAssignmentAndContent(input);

        var path = await _pathRepository.GetAsync(id);
        path.UpdateDetails(input.Name.Trim(), TrimToNull(input.Description));
        path = await _pathRepository.UpdateAsync(path, autoSave: true);

        await ReplaceConfigurationAsync(path.Id, input);

        return await MapPathAsync(path, filterKnowledgeVisibility: false);
    }

    /// <summary>
    /// 停用学习路径。停用后普通用户个人学习中心不再返回该路径。
    /// </summary>
    /// <param name="id">学习路径 ID。</param>
    /// <returns>停用后的学习路径详情。</returns>
    public virtual async Task<LearningPathDto> DeactivateAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Learning.Manage);

        var path = await _pathRepository.GetAsync(id);
        path.Deactivate();
        path = await _pathRepository.UpdateAsync(path, autoSave: true);

        return await MapPathAsync(path, filterKnowledgeVisibility: false);
    }

    /// <summary>
    /// 查询当前用户被分配到的启用学习路径，并按知识权限过滤知识内容。
    /// </summary>
    /// <param name="input">查询条件和分页参数。</param>
    /// <returns>当前用户可见的学习路径分页结果。</returns>
    public virtual async Task<PagedResultDto<LearningPathDto>> GetMyListAsync(GetLearningPathListInput input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Learning.View);

        var userId = GetCurrentUserId();
        var queryable = await _pathRepository.GetQueryableAsync();
        var allActivePaths = await AsyncExecuter.ToListAsync(
            ApplyFilters(queryable, input)
                .Where(x => x.IsActive)
                .OrderByDescending(x => x.CreationTime));

        var assignedPaths = new List<LearningPath>();
        foreach (var path in allActivePaths)
        {
            if (await IsAssignedAsync(path.Id, userId))
            {
                assignedPaths.Add(path);
            }
        }

        var paged = assignedPaths.Skip(input.SkipCount).Take(input.MaxResultCount).ToList();
        return new PagedResultDto<LearningPathDto>(assignedPaths.Count, await MapPathsAsync(paged, filterKnowledgeVisibility: true));
    }

    /// <summary>
    /// 查询当前用户被分配到的单条启用学习路径详情。
    /// </summary>
    /// <param name="id">学习路径 ID。</param>
    /// <returns>当前用户可见的学习路径详情。</returns>
    /// <exception cref="AbpAuthorizationException">当前用户未分配该路径或路径已停用时抛出。</exception>
    public virtual async Task<LearningPathDto> GetMyAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Learning.View);

        var path = await _pathRepository.GetAsync(id);
        if (!path.IsActive || !await IsAssignedAsync(path.Id, GetCurrentUserId()))
        {
            throw new AbpAuthorizationException("The current user is not assigned to this learning path.");
        }

        return await MapPathAsync(path, filterKnowledgeVisibility: true);
    }

    /// <summary>
    /// 记录当前用户阅读或完成学习路径下的知识版本，并返回更新后的路径状态。
    /// </summary>
    /// <param name="input">阅读记录输入。</param>
    /// <returns>更新阅读状态后的学习路径详情。</returns>
    /// <exception cref="AbpAuthorizationException">当前用户未分配该路径或无权访问该知识版本时抛出。</exception>
    public virtual async Task<LearningPathDto> MarkKnowledgeReadAsync(MarkLearningKnowledgeReadDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Learning.View);

        var userId = GetCurrentUserId();
        var path = await _pathRepository.GetAsync(input.LearningPathId);
        if (!path.IsActive || !await IsAssignedAsync(path.Id, userId))
        {
            throw new AbpAuthorizationException("The current user is not assigned to this learning path.");
        }

        if (!await IsVisibleLearningKnowledgeAsync(
                path.Id,
                input.KnowledgeItemId,
                input.KnowledgeVersionId))
        {
            throw new AbpAuthorizationException("The current user cannot access this learning content.");
        }

        var readTime = DateTime.UtcNow;
        var record = await FindReadRecordAsync(path.Id, userId, input.KnowledgeItemId, input.KnowledgeVersionId);
        if (record == null)
        {
            await _readRecordRepository.InsertAsync(
                new LearningKnowledgeReadRecord(
                    GuidGenerator.Create(),
                    CurrentTenant.Id,
                    path.Id,
                    input.KnowledgeItemId,
                    input.KnowledgeVersionId,
                    userId,
                    readTime,
                    input.MarkCompleted),
                autoSave: true);
        }
        else
        {
            record.RecordRead(readTime, input.MarkCompleted);
            await _readRecordRepository.UpdateAsync(record, autoSave: true);
        }

        return await MapPathAsync(path, filterKnowledgeVisibility: true);
    }

    private async Task ReplaceConfigurationAsync(Guid pathId, UpdateLearningPathDto input)
    {
        await DeleteExistingConfigurationAsync(pathId);

        foreach (var assignment in input.Assignments)
        {
            await _assignmentRepository.InsertAsync(
                new LearningPathAssignment(
                    GuidGenerator.Create(),
                    CurrentTenant.Id,
                    pathId,
                    assignment.ScopeType,
                    assignment.TargetId),
                autoSave: true);
        }

        var requiredKnowledge = await ResolveKnowledgeAsync(input.RequiredKnowledge);
        for (var i = 0; i < requiredKnowledge.Count; i++)
        {
            var item = requiredKnowledge[i];
            await _requiredKnowledgeRepository.InsertAsync(
                new LearningPathRequiredKnowledge(
                    GuidGenerator.Create(),
                    CurrentTenant.Id,
                    pathId,
                    item.KnowledgeItemId,
                    item.KnowledgeVersionId,
                    NormalizeDisplayOrder(item.DisplayOrder, i),
                    TrimToNull(item.Note)),
                autoSave: true);
        }

        var recommendedKnowledge = await ResolveKnowledgeAsync(input.RecommendedKnowledge);
        for (var i = 0; i < recommendedKnowledge.Count; i++)
        {
            var item = recommendedKnowledge[i];
            await _recommendedKnowledgeRepository.InsertAsync(
                new LearningPathRecommendedKnowledge(
                    GuidGenerator.Create(),
                    CurrentTenant.Id,
                    pathId,
                    item.KnowledgeItemId,
                    item.KnowledgeVersionId,
                    NormalizeDisplayOrder(item.DisplayOrder, i),
                    TrimToNull(item.Reason)),
                autoSave: true);
        }

        for (var i = 0; i < input.RequiredExams.Count; i++)
        {
            var exam = input.RequiredExams[i];
            var plan = await _examPlanRepository.GetAsync(exam.ExamPlanId);
            if (plan.Status != ExamPlanStatus.Published)
            {
                throw new BusinessException(KnowPulseDomainErrorCodes.ExamPlanNotPublished);
            }

            await _requiredExamRepository.InsertAsync(
                new LearningPathRequiredExam(
                    GuidGenerator.Create(),
                    CurrentTenant.Id,
                    pathId,
                    plan.Id,
                    NormalizeDisplayOrder(exam.DisplayOrder, i),
                    exam.PassingRequired,
                    TrimToNull(exam.Note)),
                autoSave: true);
        }
    }

    private async Task DeleteExistingConfigurationAsync(Guid pathId)
    {
        var assignments = await GetAssignmentsAsync(pathId);
        foreach (var assignment in assignments)
        {
            await _assignmentRepository.DeleteAsync(assignment, autoSave: true);
        }

        var requiredKnowledge = await GetRequiredKnowledgeAsync(pathId);
        foreach (var item in requiredKnowledge)
        {
            await _requiredKnowledgeRepository.DeleteAsync(item, autoSave: true);
        }

        var recommendedKnowledge = await GetRecommendedKnowledgeAsync(pathId);
        foreach (var item in recommendedKnowledge)
        {
            await _recommendedKnowledgeRepository.DeleteAsync(item, autoSave: true);
        }

        var requiredExams = await GetRequiredExamsAsync(pathId);
        foreach (var item in requiredExams)
        {
            await _requiredExamRepository.DeleteAsync(item, autoSave: true);
        }
    }

    private async Task<List<ResolvedKnowledgeInput>> ResolveKnowledgeAsync(IReadOnlyList<LearningPathKnowledgeInputDto> inputs)
    {
        var result = new List<ResolvedKnowledgeInput>();
        foreach (var input in inputs)
        {
            var item = await _knowledgeItemRepository.GetAsync(input.KnowledgeItemId);
            if (!item.IsActive)
            {
                throw new EntityNotFoundException(typeof(KnowledgeItem), item.Id);
            }

            var versionId = input.KnowledgeVersionId ?? item.CurrentPublishedVersionId;
            if (!versionId.HasValue)
            {
                throw new BusinessException(KnowPulseDomainErrorCodes.KnowledgeVersionNotPublished);
            }

            var version = await _knowledgeVersionRepository.GetAsync(versionId.Value);
            if (version.KnowledgeItemId != item.Id)
            {
                throw new EntityNotFoundException(typeof(KnowledgeVersion), version.Id);
            }

            if (version.Status != KnowledgeVersionStatus.Published)
            {
                throw new BusinessException(KnowPulseDomainErrorCodes.KnowledgeVersionNotPublished);
            }

            result.Add(new ResolvedKnowledgeInput(
                item.Id,
                version.Id,
                input.DisplayOrder,
                input.Note,
                input.Reason));
        }

        return result;
    }

    private async Task<bool> IsAssignedAsync(Guid pathId, Guid userId)
    {
        var assignments = await GetAssignmentsAsync(pathId);
        if (assignments.Count == 0)
        {
            return false;
        }

        if (assignments.Any(x => x.IsActive && x.ScopeType == LearningPathAssignmentScopeType.All))
        {
            return true;
        }

        var assignmentQueryable = await _userOrganizationAssignmentRepository.GetQueryableAsync();
        var userAssignments = await AsyncExecuter.ToListAsync(assignmentQueryable.Where(x => x.UserId == userId && x.IsActive));
        var departmentIds = userAssignments.Select(x => x.DepartmentId).ToHashSet();
        var positionIds = userAssignments.Where(x => x.PositionId.HasValue).Select(x => x.PositionId!.Value).ToHashSet();
        var jobLevelIds = userAssignments.Where(x => x.JobLevelId.HasValue).Select(x => x.JobLevelId!.Value).ToHashSet();

        return assignments.Any(x =>
            x.IsActive &&
            x.TargetId.HasValue &&
            ((x.ScopeType == LearningPathAssignmentScopeType.Department && departmentIds.Contains(x.TargetId.Value)) ||
             (x.ScopeType == LearningPathAssignmentScopeType.Position && positionIds.Contains(x.TargetId.Value)) ||
             (x.ScopeType == LearningPathAssignmentScopeType.JobLevel && jobLevelIds.Contains(x.TargetId.Value))));
    }

    private async Task<List<LearningPathAssignment>> GetAssignmentsAsync(Guid pathId)
    {
        var queryable = await _assignmentRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(queryable.Where(x => x.LearningPathId == pathId).OrderBy(x => x.ScopeType));
    }

    private async Task<List<LearningPathRequiredKnowledge>> GetRequiredKnowledgeAsync(Guid pathId)
    {
        var queryable = await _requiredKnowledgeRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(queryable.Where(x => x.LearningPathId == pathId).OrderBy(x => x.DisplayOrder));
    }

    private async Task<List<LearningPathRecommendedKnowledge>> GetRecommendedKnowledgeAsync(Guid pathId)
    {
        var queryable = await _recommendedKnowledgeRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(queryable.Where(x => x.LearningPathId == pathId).OrderBy(x => x.DisplayOrder));
    }

    private async Task<List<LearningPathRequiredExam>> GetRequiredExamsAsync(Guid pathId)
    {
        var queryable = await _requiredExamRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(queryable.Where(x => x.LearningPathId == pathId).OrderBy(x => x.DisplayOrder));
    }

    private async Task<LearningKnowledgeReadRecord?> FindReadRecordAsync(
        Guid pathId,
        Guid userId,
        Guid knowledgeItemId,
        Guid knowledgeVersionId)
    {
        var queryable = await _readRecordRepository.GetQueryableAsync();
        return await AsyncExecuter.FirstOrDefaultAsync(queryable.Where(x =>
            x.LearningPathId == pathId &&
            x.UserId == userId &&
            x.KnowledgeItemId == knowledgeItemId &&
            x.KnowledgeVersionId == knowledgeVersionId));
    }

    private IQueryable<LearningPath> ApplyFilters(IQueryable<LearningPath> queryable, GetLearningPathListInput input)
    {
        if (input.IsActive.HasValue)
        {
            queryable = queryable.Where(x => x.IsActive == input.IsActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(input.Keyword))
        {
            var keyword = input.Keyword.Trim();
            queryable = queryable.Where(x => x.Name.Contains(keyword) || (x.Description != null && x.Description.Contains(keyword)));
        }

        return queryable;
    }

    private async Task<List<LearningPathDto>> MapPathsAsync(IReadOnlyCollection<LearningPath> paths, bool filterKnowledgeVisibility)
    {
        var result = new List<LearningPathDto>();
        foreach (var path in paths)
        {
            result.Add(await MapPathAsync(path, filterKnowledgeVisibility));
        }

        return result;
    }

    private async Task<LearningPathDto> MapPathAsync(LearningPath path, bool filterKnowledgeVisibility)
    {
        var requiredKnowledge = await GetRequiredKnowledgeAsync(path.Id);
        var recommendedKnowledge = await GetRecommendedKnowledgeAsync(path.Id);
        var requiredExams = await GetRequiredExamsAsync(path.Id);
        var currentUserId = filterKnowledgeVisibility ? GetCurrentUserId() : (Guid?)null;
        var readRecordsByKnowledge = currentUserId.HasValue
            ? await GetReadRecordLookupAsync(path.Id, currentUserId.Value)
            : new Dictionary<LearningKnowledgeKey, LearningKnowledgeReadRecord>();

        var accessContext = filterKnowledgeVisibility ? await _knowledgeAccessContextProvider.CreateAsync() : null;
        var knowledgeItemIds = requiredKnowledge.Select(x => x.KnowledgeItemId)
            .Concat(recommendedKnowledge.Select(x => x.KnowledgeItemId))
            .Distinct()
            .ToList();
        var scopesByItemId = filterKnowledgeVisibility
            ? await GetKnowledgeScopeLookupAsync(knowledgeItemIds)
            : new Dictionary<Guid, List<KnowledgePermissionScope>>();

        return new LearningPathDto
        {
            Id = path.Id,
            CreationTime = path.CreationTime,
            CreatorId = path.CreatorId,
            LastModificationTime = path.LastModificationTime,
            LastModifierId = path.LastModifierId,
            IsDeleted = path.IsDeleted,
            DeleterId = path.DeleterId,
            DeletionTime = path.DeletionTime,
            Name = path.Name,
            Description = path.Description,
            IsActive = path.IsActive,
            ConfigVersion = path.ConfigVersion,
            Assignments = (await GetAssignmentsAsync(path.Id)).Select(MapAssignment).ToList(),
            RequiredKnowledge = await MapRequiredKnowledgeAsync(requiredKnowledge, accessContext, scopesByItemId, readRecordsByKnowledge),
            RecommendedKnowledge = await MapRecommendedKnowledgeAsync(recommendedKnowledge, accessContext, scopesByItemId, readRecordsByKnowledge),
            RequiredExams = await MapRequiredExamsAsync(requiredExams, currentUserId)
        };
    }

    private async Task<List<LearningPathKnowledgeDto>> MapRequiredKnowledgeAsync(
        IReadOnlyCollection<LearningPathRequiredKnowledge> items,
        KnowledgeAccessContext? accessContext,
        IReadOnlyDictionary<Guid, List<KnowledgePermissionScope>> scopesByItemId,
        IReadOnlyDictionary<LearningKnowledgeKey, LearningKnowledgeReadRecord> readRecordsByKnowledge)
    {
        var result = new List<LearningPathKnowledgeDto>();
        foreach (var item in items)
        {
            var dto = await TryMapKnowledgeAsync(
                item.Id,
                item.LearningPathId,
                item.KnowledgeItemId,
                item.KnowledgeVersionId,
                item.DisplayOrder,
                item.Note,
                null,
                accessContext,
                scopesByItemId,
                readRecordsByKnowledge);
            if (dto != null)
            {
                result.Add(dto);
            }
        }

        return result;
    }

    private async Task<List<LearningPathKnowledgeDto>> MapRecommendedKnowledgeAsync(
        IReadOnlyCollection<LearningPathRecommendedKnowledge> items,
        KnowledgeAccessContext? accessContext,
        IReadOnlyDictionary<Guid, List<KnowledgePermissionScope>> scopesByItemId,
        IReadOnlyDictionary<LearningKnowledgeKey, LearningKnowledgeReadRecord> readRecordsByKnowledge)
    {
        var result = new List<LearningPathKnowledgeDto>();
        foreach (var item in items)
        {
            var dto = await TryMapKnowledgeAsync(
                item.Id,
                item.LearningPathId,
                item.KnowledgeItemId,
                item.KnowledgeVersionId,
                item.DisplayOrder,
                null,
                item.Reason,
                accessContext,
                scopesByItemId,
                readRecordsByKnowledge);
            if (dto != null)
            {
                result.Add(dto);
            }
        }

        return result;
    }

    private async Task<LearningPathKnowledgeDto?> TryMapKnowledgeAsync(
        Guid id,
        Guid pathId,
        Guid knowledgeItemId,
        Guid knowledgeVersionId,
        int displayOrder,
        string? note,
        string? reason,
        KnowledgeAccessContext? accessContext,
        IReadOnlyDictionary<Guid, List<KnowledgePermissionScope>> scopesByItemId,
        IReadOnlyDictionary<LearningKnowledgeKey, LearningKnowledgeReadRecord> readRecordsByKnowledge)
    {
        var item = await _knowledgeItemRepository.FindAsync(knowledgeItemId);
        var version = await _knowledgeVersionRepository.FindAsync(knowledgeVersionId);
        if (item == null || version == null)
        {
            return null;
        }

        if (accessContext != null)
        {
            if (!item.IsActive)
            {
                return null;
            }

            var scopes = scopesByItemId.TryGetValue(item.Id, out var itemScopes)
                ? itemScopes
                : new List<KnowledgePermissionScope>();
            if (!KnowledgeAccessEvaluator.CanAccess(scopes, accessContext))
            {
                return null;
            }
        }

        var key = new LearningKnowledgeKey(item.Id, version.Id);
        var record = readRecordsByKnowledge.TryGetValue(key, out var readRecord) ? readRecord : null;
        return new LearningPathKnowledgeDto
        {
            Id = id,
            LearningPathId = pathId,
            KnowledgeItemId = item.Id,
            KnowledgeVersionId = version.Id,
            KnowledgeTitle = item.Title,
            VersionTitle = version.Title,
            VersionNumber = version.VersionNumber,
            DisplayOrder = displayOrder,
            Note = note,
            Reason = reason,
            CompletionStatus = GetKnowledgeCompletionStatus(record),
            LastReadTime = record?.LastReadTime,
            CompletedTime = record?.CompletedTime,
            ReadCount = record?.ReadCount ?? 0
        };
    }

    private async Task<List<LearningPathRequiredExamDto>> MapRequiredExamsAsync(
        IReadOnlyCollection<LearningPathRequiredExam> exams,
        Guid? currentUserId)
    {
        var result = new List<LearningPathRequiredExamDto>();
        var planIds = exams.Select(x => x.ExamPlanId).Distinct().ToList();
        var scoresByPlanId = currentUserId.HasValue
            ? await GetExamScoreLookupAsync(planIds, currentUserId.Value)
            : new Dictionary<Guid, List<ExamScore>>();
        var attemptsByPlanId = currentUserId.HasValue
            ? await GetExamAttemptLookupAsync(planIds, currentUserId.Value)
            : new Dictionary<Guid, List<ExamAttempt>>();

        foreach (var item in exams)
        {
            var plan = await _examPlanRepository.FindAsync(item.ExamPlanId);
            if (plan == null)
            {
                continue;
            }

            var isAssignedToCurrentUser = !currentUserId.HasValue || await IsExamAssignedAsync(plan.Id, currentUserId.Value);
            var scores = scoresByPlanId.TryGetValue(plan.Id, out var planScores) ? planScores : new List<ExamScore>();
            var attempts = attemptsByPlanId.TryGetValue(plan.Id, out var planAttempts) ? planAttempts : new List<ExamAttempt>();
            var bestScore = scores.OrderByDescending(x => x.ObtainedScore).FirstOrDefault();
            var passedScore = scores.Where(x => x.IsPassed).OrderByDescending(x => x.ScoredTime).FirstOrDefault();
            var lastAttemptTime = attempts.Select(GetAttemptLastActivityTime)
                .Concat(scores.Select(x => x.ScoredTime))
                .DefaultIfEmpty()
                .Max();

            result.Add(new LearningPathRequiredExamDto
            {
                Id = item.Id,
                LearningPathId = item.LearningPathId,
                ExamPlanId = plan.Id,
                ExamName = plan.Name,
                ExamStatus = plan.Status,
                StartTime = plan.StartTime,
                EndTime = plan.EndTime,
                DisplayOrder = item.DisplayOrder,
                PassingRequired = item.PassingRequired,
                Note = item.Note,
                CompletionStatus = GetExamCompletionStatus(item.PassingRequired, isAssignedToCurrentUser, scores, attempts),
                IsAssignedToCurrentUser = isAssignedToCurrentUser,
                LastAttemptTime = lastAttemptTime == default ? null : lastAttemptTime,
                PassedTime = passedScore?.ScoredTime,
                BestScore = bestScore?.ObtainedScore,
                PassingScore = plan.PassingScore
            });
        }

        return result;
    }

    private async Task<Dictionary<Guid, List<KnowledgePermissionScope>>> GetKnowledgeScopeLookupAsync(IEnumerable<Guid> itemIds)
    {
        var ids = itemIds.Distinct().ToList();
        var lookup = ids.ToDictionary(id => id, _ => new List<KnowledgePermissionScope>());
        if (ids.Count == 0)
        {
            return lookup;
        }

        var queryable = await _knowledgePermissionScopeRepository.GetQueryableAsync();
        var scopes = await AsyncExecuter.ToListAsync(queryable.Where(x => ids.Contains(x.KnowledgeItemId) && x.IsEnabled));
        foreach (var scope in scopes)
        {
            lookup[scope.KnowledgeItemId].Add(scope);
        }

        return lookup;
    }

    private async Task<Dictionary<LearningKnowledgeKey, LearningKnowledgeReadRecord>> GetReadRecordLookupAsync(Guid pathId, Guid userId)
    {
        var queryable = await _readRecordRepository.GetQueryableAsync();
        var records = await AsyncExecuter.ToListAsync(queryable.Where(x => x.LearningPathId == pathId && x.UserId == userId));
        return records
            .GroupBy(x => new LearningKnowledgeKey(x.KnowledgeItemId, x.KnowledgeVersionId))
            .ToDictionary(x => x.Key, x => x.OrderByDescending(record => record.LastReadTime).First());
    }

    private async Task<Dictionary<Guid, List<ExamScore>>> GetExamScoreLookupAsync(IReadOnlyCollection<Guid> planIds, Guid userId)
    {
        if (planIds.Count == 0)
        {
            return new Dictionary<Guid, List<ExamScore>>();
        }

        var queryable = await _examScoreRepository.GetQueryableAsync();
        var scores = await AsyncExecuter.ToListAsync(queryable.Where(x => planIds.Contains(x.PlanId) && x.UserId == userId));
        return scores.GroupBy(x => x.PlanId).ToDictionary(x => x.Key, x => x.ToList());
    }

    private async Task<Dictionary<Guid, List<ExamAttempt>>> GetExamAttemptLookupAsync(IReadOnlyCollection<Guid> planIds, Guid userId)
    {
        if (planIds.Count == 0)
        {
            return new Dictionary<Guid, List<ExamAttempt>>();
        }

        var queryable = await _examAttemptRepository.GetQueryableAsync();
        var attempts = await AsyncExecuter.ToListAsync(queryable.Where(x => planIds.Contains(x.PlanId) && x.UserId == userId));
        return attempts.GroupBy(x => x.PlanId).ToDictionary(x => x.Key, x => x.ToList());
    }

    private async Task<bool> IsExamAssignedAsync(Guid planId, Guid userId)
    {
        var assignments = await GetExamAssignmentsAsync(planId);
        if (assignments.Count == 0 || assignments.Any(x => x.IsActive && x.ScopeType == ExamAssignmentScopeType.All))
        {
            return true;
        }

        if (assignments.Any(x => x.IsActive && x.ScopeType == ExamAssignmentScopeType.User && x.TargetId == userId))
        {
            return true;
        }

        var assignmentQueryable = await _userOrganizationAssignmentRepository.GetQueryableAsync();
        var userAssignments = await AsyncExecuter.ToListAsync(assignmentQueryable.Where(x => x.UserId == userId && x.IsActive));
        var departmentIds = userAssignments.Select(x => x.DepartmentId).ToHashSet();
        var positionIds = userAssignments.Where(x => x.PositionId.HasValue).Select(x => x.PositionId!.Value).ToHashSet();

        return assignments.Any(x =>
            x.IsActive &&
            ((x.ScopeType == ExamAssignmentScopeType.Department && x.TargetId.HasValue && departmentIds.Contains(x.TargetId.Value)) ||
             (x.ScopeType == ExamAssignmentScopeType.Position && x.TargetId.HasValue && positionIds.Contains(x.TargetId.Value))));
    }

    private async Task<List<ExamAssignment>> GetExamAssignmentsAsync(Guid planId)
    {
        var queryable = await _examAssignmentRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(queryable.Where(x => x.PlanId == planId));
    }

    private async Task<bool> IsVisibleLearningKnowledgeAsync(
        Guid pathId,
        Guid knowledgeItemId,
        Guid knowledgeVersionId)
    {
        var requiredKnowledge = await GetRequiredKnowledgeAsync(pathId);
        var recommendedKnowledge = await GetRecommendedKnowledgeAsync(pathId);
        if (!requiredKnowledge.Any(x => x.KnowledgeItemId == knowledgeItemId && x.KnowledgeVersionId == knowledgeVersionId) &&
            !recommendedKnowledge.Any(x => x.KnowledgeItemId == knowledgeItemId && x.KnowledgeVersionId == knowledgeVersionId))
        {
            return false;
        }

        var item = await _knowledgeItemRepository.FindAsync(knowledgeItemId);
        var version = await _knowledgeVersionRepository.FindAsync(knowledgeVersionId);
        if (item == null ||
            version == null ||
            !item.IsActive ||
            version.KnowledgeItemId != item.Id ||
            version.Status != KnowledgeVersionStatus.Published)
        {
            return false;
        }

        var accessContext = await _knowledgeAccessContextProvider.CreateAsync();
        var scopesByItemId = await GetKnowledgeScopeLookupAsync(new[] { knowledgeItemId });
        var scopes = scopesByItemId.TryGetValue(knowledgeItemId, out var itemScopes)
            ? itemScopes
            : new List<KnowledgePermissionScope>();
        return KnowledgeAccessEvaluator.CanAccess(scopes, accessContext);
    }

    private static LearningContentCompletionStatus GetKnowledgeCompletionStatus(LearningKnowledgeReadRecord? record)
    {
        if (record == null)
        {
            return LearningContentCompletionStatus.NotStarted;
        }

        return record.CompletedTime.HasValue
            ? LearningContentCompletionStatus.Completed
            : LearningContentCompletionStatus.InProgress;
    }

    private static LearningContentCompletionStatus GetExamCompletionStatus(
        bool passingRequired,
        bool isAssignedToCurrentUser,
        IReadOnlyCollection<ExamScore> scores,
        IReadOnlyCollection<ExamAttempt> attempts)
    {
        if (!isAssignedToCurrentUser)
        {
            return LearningContentCompletionStatus.Unavailable;
        }

        if (passingRequired)
        {
            if (scores.Any(x => x.IsPassed))
            {
                return LearningContentCompletionStatus.Completed;
            }

            return scores.Count > 0 || attempts.Count > 0
                ? LearningContentCompletionStatus.InProgress
                : LearningContentCompletionStatus.NotStarted;
        }

        if (scores.Count > 0)
        {
            return LearningContentCompletionStatus.Completed;
        }

        return attempts.Count > 0
            ? LearningContentCompletionStatus.InProgress
            : LearningContentCompletionStatus.NotStarted;
    }

    private static DateTime GetAttemptLastActivityTime(ExamAttempt attempt)
    {
        return attempt.ScoredTime ??
               attempt.SubmittedTime ??
               attempt.LastSavedTime ??
               attempt.StartedTime;
    }

    private static LearningPathAssignmentDto MapAssignment(LearningPathAssignment assignment)
    {
        return new LearningPathAssignmentDto
        {
            Id = assignment.Id,
            LearningPathId = assignment.LearningPathId,
            ScopeType = assignment.ScopeType,
            TargetId = assignment.TargetId,
            IsActive = assignment.IsActive
        };
    }

    private static void EnsureInputHasAssignmentAndContent(UpdateLearningPathDto input)
    {
        if (input.Assignments.Count == 0)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.LearningPathAssignmentRequired);
        }

        if (input.RequiredKnowledge.Count == 0 &&
            input.RecommendedKnowledge.Count == 0 &&
            input.RequiredExams.Count == 0)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.LearningPathContentRequired);
        }
    }

    private Guid GetCurrentUserId()
    {
        return CurrentUser.Id ?? throw new AbpAuthorizationException("Authentication is required for this operation.");
    }

    private static int NormalizeDisplayOrder(int displayOrder, int index)
    {
        return displayOrder > 0 ? displayOrder : index + 1;
    }

    private static string? TrimToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record ResolvedKnowledgeInput(
        Guid KnowledgeItemId,
        Guid KnowledgeVersionId,
        int DisplayOrder,
        string? Note,
        string? Reason);

    private readonly record struct LearningKnowledgeKey(Guid KnowledgeItemId, Guid KnowledgeVersionId);
}
