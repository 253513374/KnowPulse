using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace KnowPulse.KnowledgeIndexing;

/// <summary>
/// 轮询待处理与到期重试任务，并交给共享处理器执行。
/// </summary>
public sealed class KnowledgeIndexJobBackgroundWorker : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly KnowledgeIndexJobWorkerOptions _options;
    private readonly ILogger<KnowledgeIndexJobBackgroundWorker> _logger;

    public KnowledgeIndexJobBackgroundWorker(
        IServiceScopeFactory serviceScopeFactory,
        IOptions<KnowledgeIndexJobWorkerOptions> options,
        ILogger<KnowledgeIndexJobBackgroundWorker> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_options.IsEnabled)
                {
                    await ProcessDueJobsAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Knowledge index background worker polling failed.");
            }

            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task ProcessDueJobsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        IndexJobCandidate[] candidates;
        using (var queryScope = _serviceScopeFactory.CreateScope())
        {
            var repository = queryScope.ServiceProvider.GetRequiredService<IRepository<KnowledgeIndexJob, Guid>>();
            var dataFilter = queryScope.ServiceProvider.GetRequiredService<IDataFilter>();
            var unitOfWorkManager = queryScope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            using var unitOfWork = unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
            using (dataFilter.Disable<IMultiTenant>())
            {
                var query = await repository.GetQueryableAsync();
                candidates = await query
                    .Where(x => x.Status == KnowledgeIndexJobStatus.Pending ||
                                (x.Status == KnowledgeIndexJobStatus.Failed &&
                                 x.RetryCount <= x.MaxRetryCount &&
                                 (!x.NextRetryTime.HasValue || x.NextRetryTime <= now)))
                    .OrderBy(x => x.CreationTime)
                    .Take(Math.Max(1, _options.BatchSize))
                    .Select(x => new IndexJobCandidate(x.Id, x.TenantId))
                    .ToArrayAsync(cancellationToken);
            }

            await unitOfWork.CompleteAsync(cancellationToken);
        }

        foreach (var candidate in candidates)
        {
            using var jobScope = _serviceScopeFactory.CreateScope();
            var currentTenant = jobScope.ServiceProvider.GetRequiredService<ICurrentTenant>();
            using (currentTenant.Change(candidate.TenantId))
            {
                try
                {
                    var processor = jobScope.ServiceProvider.GetRequiredService<IKnowledgeIndexJobProcessor>();
                    await processor.ProcessAsync(candidate.Id, cancellationToken: cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Knowledge index job {KnowledgeIndexJobId} could not be processed.", candidate.Id);
                }
            }
        }
    }

    private sealed record IndexJobCandidate(Guid Id, Guid? TenantId);
}
