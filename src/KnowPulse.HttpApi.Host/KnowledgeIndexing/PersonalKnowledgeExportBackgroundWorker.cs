using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.KnowledgeBase;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.Data;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace KnowPulse.KnowledgeIndexing;

public sealed class PersonalKnowledgeExportBackgroundWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly PersonalKnowledgeExportOptions _options;
    private readonly ILogger<PersonalKnowledgeExportBackgroundWorker> _logger;

    public PersonalKnowledgeExportBackgroundWorker(IServiceScopeFactory scopes, IOptions<PersonalKnowledgeExportOptions> options,
        ILogger<PersonalKnowledgeExportBackgroundWorker> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _options.EnsureValid();
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { if (_options.IsEnabled) await ProcessDueAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { _logger.LogWarning("Personal export polling failed; the next poll will retry."); }
            await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), stoppingToken);
        }
    }

    private async Task ProcessDueAsync(CancellationToken ct)
    {
        (Guid Id, Guid? TenantId)[] candidates;
        using (var scope = _scopes.CreateScope())
        {
            using var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: false);
            using var tenants = scope.ServiceProvider.GetRequiredService<IDataFilter>().Disable<IMultiTenant>();
            var rows = await scope.ServiceProvider.GetRequiredService<PersonalKnowledgeExportProcessor>().GetDueJobsAsync(ct);
            candidates = rows.Select(x => (x.Id, x.TenantId)).ToArray();
            await uow.CompleteAsync(ct);
        }
        foreach (var candidate in candidates)
        {
            using var scope = _scopes.CreateScope();
            using var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(candidate.TenantId);
            try { await scope.ServiceProvider.GetRequiredService<PersonalKnowledgeExportProcessor>().ProcessAsync(candidate.Id, ct); }
            catch (Exception error) when (error is not OperationCanceledException)
            { _logger.LogWarning("Personal export {ExportId} could not be processed; its state remains recoverable.", candidate.Id); }
        }
    }
}
