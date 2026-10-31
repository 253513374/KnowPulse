using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.Education;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Volo.Abp.Data;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace KnowPulse.KnowledgeIndexing;

public sealed class EducationLearningReportBackgroundWorker(IServiceScopeFactory scopes, ILogger<EducationLearningReportBackgroundWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await PollAsync(false, stoppingToken); await PollAsync(true, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("Education learning report polling failed; the next poll will retry."); }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task PollAsync(bool checkSources, CancellationToken ct)
    {
        EducationLearningReportWorkItem[] candidates;
        using (var scope = scopes.CreateScope())
        {
            using var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: false);
            using var tenants = scope.ServiceProvider.GetRequiredService<IDataFilter>().Disable<IMultiTenant>();
            var processor = scope.ServiceProvider.GetRequiredService<EducationLearningReportProcessor>();
            candidates = (await (checkSources ? processor.GetSourceChecksAsync(ct) : processor.GetPendingAsync(ct))).ToArray();
            await uow.CompleteAsync(ct);
        }
        foreach (var candidate in candidates)
        {
            using var scope = scopes.CreateScope();
            using var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(candidate.TenantId);
            try
            {
                var processor = scope.ServiceProvider.GetRequiredService<EducationLearningReportProcessor>();
                if (checkSources) await processor.RefreshChangedAsync(candidate.Id, ct); else await processor.ProcessAsync(candidate.Id, ct);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            { logger.LogWarning("Education learning report {WorkItemId} processing failed; saved versions remain recoverable.", candidate.Id); }
        }
    }
}
