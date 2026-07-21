using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.KnowledgeBase;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace KnowPulse.KnowledgeIndexing;

/// <summary>
/// 知识索引任务应用服务。MVP-1 阶段先把“创建任务、手动重建、状态流转、失败重试”
/// 作为可审计的业务入口落库，实际 chunk/embedding pipeline 后续按任务消费。
/// </summary>
[Authorize(KnowPulsePermissions.KnowledgeIndexing.IndexJobs)]
public class KnowledgeIndexJobAppService : KnowPulseAppService, IKnowledgeIndexJobAppService
{
    private static readonly TimeSpan StaleJobTimeout = TimeSpan.FromMinutes(15);

    private readonly IRepository<KnowledgeIndexJob, Guid> _jobRepository;
    private readonly IRepository<KnowledgeItem, Guid> _itemRepository;
    private readonly IRepository<KnowledgeVersion, Guid> _versionRepository;
    private readonly IKnowledgeIndexJobProcessor _jobProcessor;

    /// <summary>
    /// 创建知识索引任务应用服务。
    /// </summary>
    public KnowledgeIndexJobAppService(
        IRepository<KnowledgeIndexJob, Guid> jobRepository,
        IRepository<KnowledgeItem, Guid> itemRepository,
        IRepository<KnowledgeVersion, Guid> versionRepository,
        IKnowledgeIndexJobProcessor jobProcessor)
    {
        _jobRepository = jobRepository;
        _itemRepository = itemRepository;
        _versionRepository = versionRepository;
        _jobProcessor = jobProcessor;
    }

    /// <summary>
    /// 分页查询知识索引任务，并支持按知识条目、版本和状态过滤。
    /// </summary>
    public virtual async Task<PagedResultDto<KnowledgeIndexJobDto>> GetListAsync(GetKnowledgeIndexJobListInput input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeIndexing.IndexJobs);

        var query = await _jobRepository.GetQueryableAsync();
        if (input.KnowledgeItemId.HasValue)
        {
            query = query.Where(x => x.KnowledgeItemId == input.KnowledgeItemId.Value);
        }

        if (input.KnowledgeVersionId.HasValue)
        {
            query = query.Where(x => x.KnowledgeVersionId == input.KnowledgeVersionId.Value);
        }

        if (input.Status.HasValue)
        {
            query = query.Where(x => x.Status == input.Status.Value);
        }

        var totalCount = await AsyncExecuter.CountAsync(query);
        var jobs = await AsyncExecuter.ToListAsync(
            query
                .OrderByDescending(x => x.CreationTime)
                .ThenByDescending(x => x.Id)
                .Skip(input.SkipCount)
                .Take(input.MaxResultCount));

        return new PagedResultDto<KnowledgeIndexJobDto>(totalCount, jobs.Select(MapToDto).ToList());
    }

    /// <summary>
    /// 获取单条知识索引任务详情。
    /// </summary>
    public virtual async Task<KnowledgeIndexJobDto> GetAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeIndexing.IndexJobs);
        return MapToDto(await _jobRepository.GetAsync(id));
    }

    /// <summary>
    /// 获取索引任务队列的可操作摘要。
    /// </summary>
    public virtual async Task<KnowledgeIndexJobOperationalSummaryDto> GetOperationalSummaryAsync()
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeIndexing.IndexJobs);

        var now = DateTime.UtcNow;
        var staleBoundary = now.Subtract(StaleJobTimeout);
        var query = await _jobRepository.GetQueryableAsync();
        var lastIndexedTime = await AsyncExecuter.FirstOrDefaultAsync(
            query
                .Where(x => x.Status == KnowledgeIndexJobStatus.Indexed && x.CompletedTime.HasValue)
                .OrderByDescending(x => x.CompletedTime)
                .Select(x => x.CompletedTime));

        return new KnowledgeIndexJobOperationalSummaryDto
        {
            PendingCount = await AsyncExecuter.LongCountAsync(query.Where(x => x.Status == KnowledgeIndexJobStatus.Pending)),
            ProcessingCount = await AsyncExecuter.LongCountAsync(query.Where(x => x.Status == KnowledgeIndexJobStatus.Processing)),
            FailedCount = await AsyncExecuter.LongCountAsync(query.Where(x => x.Status == KnowledgeIndexJobStatus.Failed)),
            RetryDueCount = await AsyncExecuter.LongCountAsync(query.Where(x =>
                x.Status == KnowledgeIndexJobStatus.Failed &&
                x.RetryCount <= x.MaxRetryCount &&
                (!x.NextRetryTime.HasValue || x.NextRetryTime <= now))),
            ExhaustedCount = await AsyncExecuter.LongCountAsync(query.Where(x =>
                x.Status == KnowledgeIndexJobStatus.Failed && x.RetryCount > x.MaxRetryCount)),
            StaleCount = await AsyncExecuter.LongCountAsync(query.Where(x =>
                x.Status == KnowledgeIndexJobStatus.Processing &&
                x.LastHeartbeatTime.HasValue &&
                x.LastHeartbeatTime <= staleBoundary)),
            LastIndexedTime = lastIndexedTime
        };
    }

    /// <summary>
    /// 创建知识索引任务，并校验知识版本确实属于指定知识条目。
    /// </summary>
    public virtual async Task<KnowledgeIndexJobDto> CreateAsync(CreateKnowledgeIndexJobDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeIndexing.IndexJobs);
        await EnsureVersionBelongsToItemAsync(input.KnowledgeItemId, input.KnowledgeVersionId);

        return MapToDto(await InsertJobAsync(
            input.KnowledgeItemId,
            input.KnowledgeVersionId,
            input.TriggerType,
            input.EmbeddingModelVersionId,
            input.MaxRetryCount));
    }

    /// <summary>
    /// 为指定知识版本创建一条手动重建任务。
    /// </summary>
    public virtual async Task<KnowledgeIndexJobDto> ReindexVersionAsync(Guid knowledgeVersionId)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeIndexing.IndexJobs);
        var version = await _versionRepository.GetAsync(knowledgeVersionId);
        await _itemRepository.GetAsync(version.KnowledgeItemId);

        var activeJob = await FindActiveJobAsync(version.Id);
        if (activeJob != null)
        {
            return MapToDto(activeJob);
        }

        // 手动重建总是创建新任务，不复写旧任务。旧任务是历史事实，页面需要能展示每次失败/成功记录。
        return MapToDto(await InsertJobAsync(
            version.KnowledgeItemId,
            version.Id,
            KnowledgeIndexJobTriggerType.ManualRebuild,
            embeddingModelVersionId: null,
            maxRetryCount: 3));
    }

    /// <summary>
    /// 从失败任务派生新的待处理任务，保留原失败任务用于审计。
    /// </summary>
    public virtual async Task<KnowledgeIndexJobDto> RetryAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeIndexing.IndexJobs);
        var failedJob = await _jobRepository.GetAsync(id);
        if (failedJob.Status != KnowledgeIndexJobStatus.Failed)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.KnowledgeIndexJobRetryRequiresFailedStatus);
        }

        var activeJob = await FindActiveJobAsync(failedJob.KnowledgeVersionId);
        if (activeJob != null)
        {
            return MapToDto(activeJob);
        }

        // 重试也创建新任务，保留原失败任务的 FailureStage/FailureReason，便于排障和统计失败率。
        return MapToDto(await InsertJobAsync(
            failedJob.KnowledgeItemId,
            failedJob.KnowledgeVersionId,
            failedJob.TriggerType,
            failedJob.EmbeddingModelVersionId,
            failedJob.MaxRetryCount,
            retryOfJobId: failedJob.Id));
    }

    /// <summary>
    /// 立即运行真实索引 pipeline，而不是只把任务标记为处理中。
    /// </summary>
    public virtual async Task<KnowledgeIndexJobDto> ExecuteAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeIndexing.IndexJobs);
        await _jobProcessor.ProcessAsync(id, ignoreRetrySchedule: true);
        return MapToDto(await _jobRepository.GetAsync(id));
    }

    /// <summary>
    /// 终止卡住的处理中任务并创建一条具有来源关系的恢复任务。
    /// </summary>
    public virtual async Task<KnowledgeIndexJobDto> RecoverAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeIndexing.IndexJobs);
        var stuckJob = await _jobRepository.GetAsync(id);
        if (stuckJob.Status != KnowledgeIndexJobStatus.Processing)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.KnowledgeIndexJobRecoveryRequiresProcessingStatus);
        }

        stuckJob.MarkFailed(
            "recovery",
            "The processing job was stopped by an administrator and replaced with a recovery job.",
            DateTime.UtcNow);
        await _jobRepository.UpdateAsync(stuckJob, autoSave: true);

        return MapToDto(await InsertJobAsync(
            stuckJob.KnowledgeItemId,
            stuckJob.KnowledgeVersionId,
            KnowledgeIndexJobTriggerType.ManualRebuild,
            stuckJob.EmbeddingModelVersionId,
            stuckJob.MaxRetryCount,
            retryOfJobId: stuckJob.Id));
    }

    /// <summary>
    /// 将索引任务标记为处理中。
    /// </summary>
    public virtual async Task<KnowledgeIndexJobDto> StartAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeIndexing.IndexJobs);
        var job = await _jobRepository.GetAsync(id);
        job.Start(DateTime.UtcNow);

        return MapToDto(await _jobRepository.UpdateAsync(job, autoSave: true));
    }

    /// <summary>
    /// 将索引任务标记为已完成索引。
    /// </summary>
    public virtual async Task<KnowledgeIndexJobDto> MarkIndexedAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeIndexing.IndexJobs);
        var job = await _jobRepository.GetAsync(id);
        job.MarkIndexed(DateTime.UtcNow);

        return MapToDto(await _jobRepository.UpdateAsync(job, autoSave: true));
    }

    /// <summary>
    /// 将索引任务标记为失败，并记录失败阶段、原因和可选重试时间。
    /// </summary>
    public virtual async Task<KnowledgeIndexJobDto> MarkFailedAsync(Guid id, MarkKnowledgeIndexJobFailedDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeIndexing.IndexJobs);
        var job = await _jobRepository.GetAsync(id);
        job.MarkFailed(input.FailureStage, input.FailureReason, DateTime.UtcNow, input.NextRetryTime);

        return MapToDto(await _jobRepository.UpdateAsync(job, autoSave: true));
    }

    /// <summary>
    /// 校验知识版本归属于指定知识条目，避免错误组合生成无效索引任务。
    /// </summary>
    private async Task EnsureVersionBelongsToItemAsync(Guid knowledgeItemId, Guid knowledgeVersionId)
    {
        // 显式验证版本归属，避免调用方构造 item/version 交叉组合导致索引任务指向错误业务事实。
        await _itemRepository.GetAsync(knowledgeItemId);
        var version = await _versionRepository.GetAsync(knowledgeVersionId);
        if (version.KnowledgeItemId != knowledgeItemId)
        {
            throw new EntityNotFoundException(typeof(KnowledgeVersion), knowledgeVersionId);
        }
    }

    /// <summary>
    /// 插入新的索引任务，并记录当前用户和租户上下文。
    /// </summary>
    private Task<KnowledgeIndexJob> InsertJobAsync(
        Guid knowledgeItemId,
        Guid knowledgeVersionId,
        KnowledgeIndexJobTriggerType triggerType,
        Guid? embeddingModelVersionId,
        int maxRetryCount,
        Guid? retryOfJobId = null)
    {
        var job = new KnowledgeIndexJob(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            knowledgeItemId,
            knowledgeVersionId,
            triggerType,
            embeddingModelVersionId,
            CurrentUser.Id,
            maxRetryCount,
            retryOfJobId);

        return _jobRepository.InsertAsync(job, autoSave: true);
    }

    /// <summary>
    /// 将领域聚合映射为应用层 DTO。
    /// </summary>
    private static KnowledgeIndexJobDto MapToDto(KnowledgeIndexJob job)
    {
        return new KnowledgeIndexJobDto
        {
            Id = job.Id,
            CreationTime = job.CreationTime,
            CreatorId = job.CreatorId,
            LastModificationTime = job.LastModificationTime,
            LastModifierId = job.LastModifierId,
            IsDeleted = job.IsDeleted,
            DeleterId = job.DeleterId,
            DeletionTime = job.DeletionTime,
            KnowledgeItemId = job.KnowledgeItemId,
            KnowledgeVersionId = job.KnowledgeVersionId,
            EmbeddingModelVersionId = job.EmbeddingModelVersionId,
            RequestedByUserId = job.RequestedByUserId,
            RetryOfJobId = job.RetryOfJobId,
            TriggerType = job.TriggerType,
            Status = job.Status,
            RetryCount = job.RetryCount,
            MaxRetryCount = job.MaxRetryCount,
            FailureStage = job.FailureStage,
            FailureReason = job.FailureReason,
            StartedTime = job.StartedTime,
            CompletedTime = job.CompletedTime,
            NextRetryTime = job.NextRetryTime,
            ProcessingStage = job.ProcessingStage,
            ProgressPercent = job.ProgressPercent,
            LastHeartbeatTime = job.LastHeartbeatTime,
            CanRetry = job.CanRetry,
            IsStale = job.IsStale(DateTime.UtcNow, StaleJobTimeout)
        };
    }

    private async Task<KnowledgeIndexJob?> FindActiveJobAsync(Guid knowledgeVersionId)
    {
        var query = await _jobRepository.GetQueryableAsync();
        return await AsyncExecuter.FirstOrDefaultAsync(
            query
                .Where(x => x.KnowledgeVersionId == knowledgeVersionId)
                .Where(x => x.Status == KnowledgeIndexJobStatus.Pending ||
                            x.Status == KnowledgeIndexJobStatus.Processing)
                .OrderByDescending(x => x.CreationTime));
    }
}
