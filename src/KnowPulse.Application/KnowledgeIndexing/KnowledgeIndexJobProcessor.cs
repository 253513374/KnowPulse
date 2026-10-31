using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.KnowledgeBase;
using KnowPulse.RagAI;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Linq;
using Volo.Abp.Uow;

namespace KnowPulse.KnowledgeIndexing;

/// <summary>
/// 串联 reader、chunk、embedding 和向量写入的真实任务执行器。
/// </summary>
public class KnowledgeIndexJobProcessor : IKnowledgeIndexJobProcessor, ITransientDependency
{
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromMinutes(30);

    private readonly IRepository<KnowledgeIndexJob, Guid> _jobRepository;
    private readonly IKnowledgeIngestionPipeline _ingestionPipeline;
    private readonly IKnowledgeEmbeddingPipeline _embeddingPipeline;
    private readonly ILogger<KnowledgeIndexJobProcessor> _logger;
    private readonly IAbpLazyServiceProvider _lazyServiceProvider;

    public KnowledgeIndexJobProcessor(
        IRepository<KnowledgeIndexJob, Guid> jobRepository,
        IKnowledgeIngestionPipeline ingestionPipeline,
        IKnowledgeEmbeddingPipeline embeddingPipeline,
        ILogger<KnowledgeIndexJobProcessor> logger,
        IAbpLazyServiceProvider lazyServiceProvider)
    {
        _jobRepository = jobRepository;
        _ingestionPipeline = ingestionPipeline;
        _embeddingPipeline = embeddingPipeline;
        _logger = logger;
        _lazyServiceProvider = lazyServiceProvider;
    }

    /// <summary>
    /// 执行指定任务。已完成或已失效任务直接返回，保证重复投递不会重复建索引。
    /// </summary>
    [UnitOfWork]
    public virtual async Task ProcessAsync(
        Guid knowledgeIndexJobId,
        bool ignoreRetrySchedule = false,
        CancellationToken cancellationToken = default)
    {
        var job = await _jobRepository.GetAsync(knowledgeIndexJobId, cancellationToken: cancellationToken);
        if (job.Status is KnowledgeIndexJobStatus.Indexed or KnowledgeIndexJobStatus.Outdated or KnowledgeIndexJobStatus.Deleted)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (job.Status == KnowledgeIndexJobStatus.Processing)
        {
            return;
        }

        if (job.Status == KnowledgeIndexJobStatus.Failed)
        {
            if (!job.CanRetry)
            {
                return;
            }

            if (!ignoreRetrySchedule && !job.IsRetryDue(now))
            {
                return;
            }
        }

        if (!await KnowledgeIndexVersionQuery.CanIndexAsync(_lazyServiceProvider, job.KnowledgeVersionId,
                job.KnowledgeItemId, job.TenantId, cancellationToken))
        {
            job.MarkOutdated("Knowledge version is no longer available for indexing.");
            await _jobRepository.UpdateAsync(job, autoSave: true, cancellationToken: cancellationToken);
            return;
        }

        job.Start(now);
        job.ReportProgress("reader", 5, now);
        await _jobRepository.UpdateAsync(job, autoSave: true, cancellationToken: cancellationToken);

        try
        {
            var ingestion = await _ingestionPipeline.IngestAsync(
                job.KnowledgeVersionId,
                job.Id,
                completeIndexJob: false,
                cancellationToken);
            if (!ingestion.Succeeded)
            {
                await ScheduleRetryAsync(job.Id, cancellationToken);
                return;
            }

            var embedding = await _embeddingPipeline.GenerateForVersionAsync(
                job.KnowledgeVersionId,
                job.EmbeddingModelVersionId,
                job.Id,
                cancellationToken);
            if (!embedding.Succeeded)
            {
                await ScheduleRetryAsync(job.Id, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Knowledge index job {KnowledgeIndexJobId} failed unexpectedly.", job.Id);
            var current = await _jobRepository.GetAsync(job.Id, cancellationToken: cancellationToken);
            if (current.Status is not (KnowledgeIndexJobStatus.Indexed or KnowledgeIndexJobStatus.Outdated or KnowledgeIndexJobStatus.Deleted))
            {
                current.MarkFailed("worker", $"Unhandled index worker error: {ex.GetType().Name}", DateTime.UtcNow);
                ScheduleRetry(current, DateTime.UtcNow);
                await _jobRepository.UpdateAsync(current, autoSave: true, cancellationToken: cancellationToken);
            }
        }
    }

    private async Task ScheduleRetryAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _jobRepository.GetAsync(jobId, cancellationToken: cancellationToken);
        if (job.Status != KnowledgeIndexJobStatus.Failed || !job.CanRetry)
        {
            return;
        }

        ScheduleRetry(job, DateTime.UtcNow);
        await _jobRepository.UpdateAsync(job, autoSave: true, cancellationToken: cancellationToken);
    }

    private static void ScheduleRetry(KnowledgeIndexJob job, DateTime now)
    {
        var exponent = Math.Max(0, job.RetryCount - 1);
        var delayMinutes = Math.Min(Math.Pow(2, exponent), MaximumRetryDelay.TotalMinutes);
        job.ScheduleRetry(now.AddMinutes(delayMinutes));
    }
}

/// <summary>
/// 索引生命期只依赖主库版本和明确共享事实，不依赖交互式读者身份或向量 payload。
/// </summary>
internal static class KnowledgeIndexVersionQuery
{
    internal static async Task<IQueryable<KnowledgeVersion>> GetSharedVersionsAsync(IAbpLazyServiceProvider services)
    {
        var versions = await services.LazyGetRequiredService<IRepository<KnowledgeVersion, Guid>>().GetQueryableAsync();
        var items = await services.LazyGetRequiredService<IRepository<KnowledgeItem, Guid>>().GetQueryableAsync();
        var libraries = await services.LazyGetRequiredService<IRepository<KnowledgeLibrary, Guid>>().GetQueryableAsync();
        var shares = await services.LazyGetRequiredService<IRepository<SchoolKnowledgeShare, Guid>>().GetQueryableAsync();
        var schools = await services.LazyGetRequiredService<IRepository<School, Guid>>().GetQueryableAsync();
        var memberships = await services.LazyGetRequiredService<IRepository<SchoolMembership, Guid>>().GetQueryableAsync();
        var users = await services.LazyGetRequiredService<IRepository<IdentityUser, Guid>>().GetQueryableAsync();
        var now = DateTime.UtcNow;
        return versions.Where(version => !version.IsDeleted && version.Status == KnowledgeVersionStatus.Published &&
            items.Any(item => !item.IsDeleted && item.IsActive && item.Id == version.KnowledgeItemId &&
                item.TenantId == version.TenantId && item.LibraryId.HasValue && item.OwnerUserId.HasValue &&
                libraries.Any(library => !library.IsDeleted && library.Id == item.LibraryId &&
                    library.OwnerUserId == item.OwnerUserId && library.TenantId == item.TenantId) &&
                users.Any(user => !user.IsDeleted && user.IsActive && user.Id == item.OwnerUserId && user.TenantId == item.TenantId) &&
                shares.Any(share => !share.IsDeleted && !share.IsRevoked &&
                    share.KnowledgeItemId == item.Id && share.KnowledgeVersionId == version.Id &&
                    share.TenantId == item.TenantId && share.LibraryId == item.LibraryId &&
                    share.OwnerUserId == item.OwnerUserId &&
                    schools.Any(school => !school.IsDeleted && school.Id == share.SchoolId && school.TenantId == item.TenantId) &&
                    memberships.Any(member => !member.IsDeleted && member.TenantId == item.TenantId &&
                        member.SchoolId == share.SchoolId && member.UserId == item.OwnerUserId &&
                        member.Role == SchoolMemberRole.Teacher && member.EffectiveFromUtc <= now &&
                        (!member.EffectiveToUtc.HasValue || now < member.EffectiveToUtc.Value)))));
    }

    internal static async Task<IQueryable<KnowledgeVersion>> GetIndexableVersionsAsync(IAbpLazyServiceProvider services)
    {
        var versions = await services.LazyGetRequiredService<IRepository<KnowledgeVersion, Guid>>().GetQueryableAsync();
        var items = await services.LazyGetRequiredService<IRepository<KnowledgeItem, Guid>>().GetQueryableAsync();
        var libraries = await services.LazyGetRequiredService<IRepository<KnowledgeLibrary, Guid>>().GetQueryableAsync();
        var sharedVersions = await GetSharedVersionsAsync(services);
        return versions.Where(version => !version.IsDeleted &&
                items.Any(item => !item.IsDeleted && item.IsActive && item.Id == version.KnowledgeItemId && item.TenantId == version.TenantId &&
                    ((!item.LibraryId.HasValue && !item.OwnerUserId.HasValue && version.Status == KnowledgeVersionStatus.Draft) ||
                     (version.Status == KnowledgeVersionStatus.Published && version.IsCurrentPublished &&
                      item.CurrentPublishedVersionId == version.Id &&
                      ((!item.LibraryId.HasValue && !item.OwnerUserId.HasValue) ||
                       libraries.Any(library => !library.IsDeleted && library.Id == item.LibraryId &&
                           library.OwnerUserId == item.OwnerUserId && library.TenantId == item.TenantId))) ||
                     sharedVersions.Any(shared => shared.Id == version.Id))));
    }

    internal static async Task<bool> CanIndexAsync(IAbpLazyServiceProvider services, Guid versionId,
        Guid itemId, Guid? tenantId, CancellationToken cancellationToken = default)
    {
        var versions = await GetIndexableVersionsAsync(services);
        return await services.LazyGetRequiredService<IAsyncQueryableExecuter>().AnyAsync(versions.Where(version =>
            version.Id == versionId && version.KnowledgeItemId == itemId && version.TenantId == tenantId), cancellationToken);
    }
}
