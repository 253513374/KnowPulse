using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.Education;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.Data;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace KnowPulse.KnowledgeIndexing;

public sealed class EducationAssessmentDeadlineBackgroundWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly EducationAssessmentResponseOptions _options;
    private readonly ILogger<EducationAssessmentDeadlineBackgroundWorker> _logger;

    public EducationAssessmentDeadlineBackgroundWorker(IServiceScopeFactory scopes,
        IOptions<EducationAssessmentResponseOptions> options, ILogger<EducationAssessmentDeadlineBackgroundWorker> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
        _options.EnsureValid();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_options.IsDeadlineWorkerEnabled)
                { await ProcessDueAsync(stoppingToken); await ProcessGradesAsync(stoppingToken); }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { _logger.LogWarning("Education assessment cutoff polling failed; the next poll will retry."); }
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
            var rows = await scope.ServiceProvider.GetRequiredService<EducationAssessmentSubmissionProcessor>().GetDueAttemptsAsync(ct);
            candidates = rows.Select(x => (x.Id, x.TenantId)).ToArray();
            await uow.CompleteAsync(ct);
        }
        foreach (var candidate in candidates)
        {
            using var scope = _scopes.CreateScope();
            using var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(candidate.TenantId);
            try { await scope.ServiceProvider.GetRequiredService<EducationAssessmentSubmissionProcessor>().SealDueAsync(candidate.Id, ct); }
            catch (Exception error) when (error is not OperationCanceledException)
            { _logger.LogWarning("Education assessment {AttemptId} cutoff failed; its saved responses remain recoverable.", candidate.Id); }
        }
    }

    private async Task ProcessGradesAsync(CancellationToken ct)
    {
        (Guid Id, Guid? TenantId)[] candidates;
        using (var scope = _scopes.CreateScope())
        {
            using var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: false);
            using var tenants = scope.ServiceProvider.GetRequiredService<IDataFilter>().Disable<IMultiTenant>();
            var rows = await scope.ServiceProvider.GetRequiredService<EducationAssessmentGradingProcessor>().GetPendingSubmissionsAsync(ct);
            candidates = rows.Select(x => (x.Id, x.TenantId)).ToArray();
            await uow.CompleteAsync(ct);
        }
        foreach (var candidate in candidates)
        {
            using var scope = _scopes.CreateScope();
            using var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(candidate.TenantId);
            try { await scope.ServiceProvider.GetRequiredService<EducationAssessmentGradingProcessor>().InitializeAsync(candidate.Id, ct); }
            catch (Exception error) when (error is not OperationCanceledException)
            { _logger.LogWarning("Education assessment submission {SubmissionId} grading failed; the next poll will retry.", candidate.Id); }
        }
    }
}
