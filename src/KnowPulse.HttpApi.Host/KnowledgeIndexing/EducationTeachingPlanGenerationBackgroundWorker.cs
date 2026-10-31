using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.Education;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace KnowPulse.KnowledgeIndexing;

public sealed class EducationTeachingPlanGenerationBackgroundWorker(IServiceScopeFactory scopes, ILogger<EducationTeachingPlanGenerationBackgroundWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested) {
            try {
                (Guid Id, Guid? Tenant)[] candidates;
                using (var scope = scopes.CreateScope()) {
                    var provider = scope.ServiceProvider; using var uow = provider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: false);
                    using var all = provider.GetRequiredService<IDataFilter>().Disable<IMultiTenant>(); var now = DateTime.UtcNow;
                    var rows = await provider.GetRequiredService<IAsyncQueryableExecuter>().ToListAsync((await provider.GetRequiredService<IRepository<EducationTeachingPlanGenerationJob, Guid>>().GetQueryableAsync())
                        .Where(x => x.Status == EducationPlanGenerationStatus.Pending || x.Status == EducationPlanGenerationStatus.Processing && x.LeaseExpiresAtUtc <= now)
                        .OrderBy(x => x.RequestedAtUtc).ThenBy(x => x.Id).Select(x => new { x.Id, x.TenantId }).Take(5), stoppingToken);
                    candidates = rows.Select(x => (x.Id, x.TenantId)).ToArray(); await uow.CompleteAsync(stoppingToken);
                }
                foreach (var item in candidates) {
                    using var scope = scopes.CreateScope(); using var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(item.Tenant);
                    try { await scope.ServiceProvider.GetRequiredService<EducationTeachingPlanGenerationProcessor>().ProcessAsync(item.Id, stoppingToken); }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                    catch (Exception) { logger.LogWarning("Teaching plan generation {JobId} stopped; its recorded state remains recoverable.", item.Id); }
                }
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("Teaching plan generation polling failed; the next poll will retry."); }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
