using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.Uow;

namespace KnowPulse.AiQuestioning;

public class AiQuestionGenerationProcessor : ITransientDependency
{
    private readonly IRepository<AiQuestionGenerationTask, Guid> _tasks;
    private readonly IUnitOfWorkManager _uow;
    private readonly IAbpLazyServiceProvider _services;
    private readonly IServiceScopeFactory _scopes;
    private readonly AiQuestionGenerationWorkerOptions _options;
    private readonly ILogger<AiQuestionGenerationProcessor> _logger;

    public AiQuestionGenerationProcessor(IRepository<AiQuestionGenerationTask, Guid> tasks, IUnitOfWorkManager uow,
        IAbpLazyServiceProvider services, IServiceScopeFactory scopes, IOptions<AiQuestionGenerationWorkerOptions> options,
        ILogger<AiQuestionGenerationProcessor> logger)
    {
        _tasks = tasks; _uow = uow; _services = services; _scopes = scopes; _options = options.Value; _logger = logger;
        _options.EnsureValid();
    }

    public virtual async Task<List<AiQuestionGenerationTask>> GetDueTasksAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var stale = now.AddSeconds(-_options.LeaseSeconds);
        using var tracking = _tasks.DisableTracking();
        var query = await _tasks.GetQueryableAsync();
        return await _services.LazyGetRequiredService<IAsyncQueryableExecuter>().ToListAsync(query.Where(x =>
            (x.Status == AiQuestionGenerationTaskStatus.Pending && (!x.NextAttemptAtUtc.HasValue || x.NextAttemptAtUtc <= now)) ||
            (x.Status == AiQuestionGenerationTaskStatus.Processing && (!x.LastHeartbeatAtUtc.HasValue || x.LastHeartbeatAtUtc <= stale)))
            .OrderBy(x => x.CreationTime).ThenBy(x => x.Id).Take(_options.BatchSize), ct);
    }

    [UnitOfWork(IsDisabled = true)]
    public virtual async Task ProcessAsync(Guid id, CancellationToken ct = default)
    {
        var runId = Guid.NewGuid();
        AiQuestionGenerationTask task;
        using (var claim = _uow.Begin(requiresNew: true, isTransactional: true))
        {
            task = await _tasks.GetAsync(id, cancellationToken: ct);
            if (!task.TryClaim(runId, DateTime.UtcNow, _options.LeaseSeconds, _options.MaxAttempts))
            {
                if (task.Status == AiQuestionGenerationTaskStatus.Failed) await _tasks.UpdateAsync(task, autoSave: true, cancellationToken: ct);
                await claim.CompleteAsync(ct);
                return;
            }
            await _tasks.UpdateAsync(task, autoSave: true, cancellationToken: ct);
            await claim.CompleteAsync(ct);
        }
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var heartbeatStop = new CancellationTokenSource();
        var heartbeat = HeartbeatAsync(task, runId, execution, heartbeatStop.Token);
        try
        {
            using var read = _uow.Begin(requiresNew: true, isTransactional: false);
            var principal = await AiQuestionGenerationIdentity.CreateAsync(_services, task, execution.Token);
            using var identity = _services.LazyGetRequiredService<ICurrentPrincipalAccessor>().Change(principal);
            await _services.LazyGetRequiredService<AiQuestionGenerationAppService>().ExecuteClaimedAsync(id, runId, execution.Token);
            await read.CompleteAsync(execution.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || execution.IsCancellationRequested) { }
        catch (Exception error)
        {
            _logger.LogWarning("AI generation {TaskId} stopped at {ErrorType}; its state is recoverable.", id, error.GetType().Name);
            using var failure = _uow.Begin(requiresNew: true, isTransactional: true);
            var current = await _tasks.GetAsync(id);
            if (current.Status == AiQuestionGenerationTaskStatus.Processing && current.RunId == runId)
            {
                current.MarkFailed(error is AbpAuthorizationException ? "authorization" : "worker",
                    error is AbpAuthorizationException ? "KnowPulse:AiGeneration:AuthorizationChanged" : "KnowPulse:AiGeneration:WorkerFailed", DateTime.UtcNow);
                if (error is not AbpAuthorizationException && current.AttemptCount < _options.MaxAttempts)
                    current.QueueRetry(DateTime.UtcNow.AddSeconds(RetryDelay(current.AttemptCount)), _options.MaxAttempts);
                await _tasks.UpdateAsync(current, autoSave: true);
            }
            await failure.CompleteAsync();
        }
        finally
        {
            heartbeatStop.Cancel();
            try { await heartbeat; }
            catch (OperationCanceledException) { }
        }
    }

    private int RetryDelay(int attempt) => (int)Math.Min(1800, _options.RetryDelaySeconds * Math.Pow(2, Math.Max(0, attempt - 1)));

    private async Task HeartbeatAsync(AiQuestionGenerationTask task, Guid runId, CancellationTokenSource execution, CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(2, Math.Min(30, _options.LeaseSeconds / 3))));
        while (await timer.WaitForNextTickAsync(stop))
        {
            try
            {
                using var scope = _scopes.CreateScope();
                using var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(task.TenantId);
                using var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true);
                var repository = scope.ServiceProvider.GetRequiredService<IRepository<AiQuestionGenerationTask, Guid>>();
                var current = await repository.GetAsync(task.Id, cancellationToken: stop);
                if (current.Status != AiQuestionGenerationTaskStatus.Processing || current.RunId != runId)
                {
                    execution.Cancel();
                    return;
                }
                current.Heartbeat(runId, DateTime.UtcNow);
                await repository.UpdateAsync(current, autoSave: true, cancellationToken: stop);
                await uow.CompleteAsync(stop);
            }
            catch (AbpDbConcurrencyException) { }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                _logger.LogWarning("AI generation heartbeat {TaskId} stopped at {ErrorType}.", task.Id, error.GetType().Name);
                execution.Cancel();
                return;
            }
        }
    }
}

internal static class AiQuestionGenerationIdentity
{
    internal static async Task<ClaimsPrincipal> CreateAsync(IAbpLazyServiceProvider services, AiQuestionGenerationTask task, CancellationToken ct)
    {
        var ownerId = task.PersonalOwnerUserId ?? throw new AbpAuthorizationException();
        using var tracking = services.LazyGetRequiredService<IRepository<IdentityUser, Guid>>().DisableTracking();
        var account = await services.LazyGetRequiredService<IRepository<IdentityUser, Guid>>().FindAsync(ownerId, cancellationToken: ct);
        if (account == null || !account.IsActive || account.TenantId != task.TenantId) throw new AbpAuthorizationException();
        var claims = new List<Claim> { new(AbpClaimTypes.UserId, account.Id.ToString()), new(AbpClaimTypes.UserName, account.UserName) };
        if (account.TenantId.HasValue) claims.Add(new Claim(AbpClaimTypes.TenantId, account.TenantId.Value.ToString()));
        var roles = await services.LazyGetRequiredService<IdentityUserManager>().GetRolesAsync(account);
        claims.AddRange(roles.Select(x => new Claim(AbpClaimTypes.Role, x)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "AiGenerationWorker", AbpClaimTypes.UserName, AbpClaimTypes.Role));
        if (!await services.LazyGetRequiredService<IPermissionChecker>().IsGrantedAsync(principal, KnowPulsePermissions.QuestionBank.AiGeneration))
            throw new AbpAuthorizationException();
        return principal;
    }
}
