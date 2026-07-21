using System;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.RagAI;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
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

    public KnowledgeIndexJobProcessor(
        IRepository<KnowledgeIndexJob, Guid> jobRepository,
        IKnowledgeIngestionPipeline ingestionPipeline,
        IKnowledgeEmbeddingPipeline embeddingPipeline,
        ILogger<KnowledgeIndexJobProcessor> logger)
    {
        _jobRepository = jobRepository;
        _ingestionPipeline = ingestionPipeline;
        _embeddingPipeline = embeddingPipeline;
        _logger = logger;
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
