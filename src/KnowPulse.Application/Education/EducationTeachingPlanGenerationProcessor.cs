using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.Uow;

namespace KnowPulse.Education;

public class EducationTeachingPlanGenerationProcessor(IAbpLazyServiceProvider services, IServiceScopeFactory scopes, IOptions<EducationTeachingPlanGenerationOptions> options) : ITransientDependency
{
    [UnitOfWork(IsDisabled = true)]
    public virtual async Task ProcessAsync(Guid id, CancellationToken ct = default)
    {
        var limits = options.Value; limits.EnsureValid(); var uow = services.LazyGetRequiredService<IUnitOfWorkManager>(); var repo = services.LazyGetRequiredService<IRepository<EducationTeachingPlanGenerationJob, Guid>>();
        var runId = Guid.NewGuid(); EducationTeachingPlanGenerationJob job;
        using (var claim = uow.Begin(requiresNew: true, isTransactional: true)) {
            using var tracking = repo.EnableTracking(); job = await repo.GetAsync(id, cancellationToken: ct);
            var before = job.Status; var owned = job.Claim(runId, DateTime.UtcNow, limits.LeaseSeconds);
            if (owned || before == EducationPlanGenerationStatus.Processing && job.Status == EducationPlanGenerationStatus.Failed) await repo.UpdateAsync(job, autoSave: true, cancellationToken: ct);
            await claim.CompleteAsync(ct); if (!owned) return;
        }
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(ct); using var stopHeartbeat = new CancellationTokenSource();
        var heartbeat = HeartbeatAsync(job, runId, execution, stopHeartbeat.Token);
        try {
            ClaimsPrincipal principal;
            using (var read = uow.Begin(requiresNew: true, isTransactional: false)) { principal = await EducationTeachingPlanGenerationIdentity.CreateAsync(services, job, ct); await read.CompleteAsync(ct); }
            using var identity = services.LazyGetRequiredService<ICurrentPrincipalAccessor>().Change(principal);
            await services.LazyGetRequiredService<EducationTeachingPlanAppService>().ExecuteGenerationAsync(id, runId, execution.Token);
        } catch (Exception error) {
            using var failure = uow.Begin(requiresNew: true, isTransactional: true); using var tracking = repo.EnableTracking(); var current = await repo.GetAsync(id);
            if (current.Status == EducationPlanGenerationStatus.Processing && current.RunId == runId) {
                current.Fail(runId, error is AbpAuthorizationException ? EducationPlanGenerationFailure.Authorization : EducationPlanGenerationFailure.Persistence, DateTime.UtcNow);
                await repo.UpdateAsync(current, autoSave: true);
            } await failure.CompleteAsync();
        } finally { stopHeartbeat.Cancel(); try { await heartbeat; } catch (OperationCanceledException) { } }
    }
    private async Task HeartbeatAsync(EducationTeachingPlanGenerationJob original, Guid runId, CancellationTokenSource execution, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(2, Math.Min(30, options.Value.LeaseSeconds / 3))));
        while (await timer.WaitForNextTickAsync(ct)) {
            try {
                using var scope = scopes.CreateScope(); var provider = scope.ServiceProvider;
                using var tenant = provider.GetRequiredService<ICurrentTenant>().Change(original.TenantId);
                using var uow = provider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true);
                var repo = provider.GetRequiredService<IRepository<EducationTeachingPlanGenerationJob, Guid>>(); using var tracking = repo.EnableTracking(); var job = await repo.GetAsync(original.Id, cancellationToken: ct);
                if (job.TenantId != original.TenantId || job.OwnerUserId != original.OwnerUserId || !job.Heartbeat(runId, DateTime.UtcNow, options.Value.LeaseSeconds)) { execution.Cancel(); return; }
                await repo.UpdateAsync(job, autoSave: true, cancellationToken: ct); await uow.CompleteAsync(ct);
            } catch (AbpDbConcurrencyException) { }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception) { execution.Cancel(); return; }
        }
    }
}

internal static class EducationTeachingPlanGenerationIdentity
{
    internal static async Task<ClaimsPrincipal> CreateAsync(IAbpLazyServiceProvider services, EducationTeachingPlanGenerationJob job, CancellationToken ct)
    {
        using var tracking = services.LazyGetRequiredService<IRepository<IdentityUser, Guid>>().DisableTracking();
        var account = await services.LazyGetRequiredService<IRepository<IdentityUser, Guid>>().FindAsync(job.OwnerUserId, cancellationToken: ct);
        if (account == null || !account.IsActive || account.TenantId != job.TenantId) throw new AbpAuthorizationException();
        var claims = new List<Claim> { new(AbpClaimTypes.UserId, account.Id.ToString()), new(AbpClaimTypes.UserName, account.UserName) };
        if (account.TenantId.HasValue) claims.Add(new(AbpClaimTypes.TenantId, account.TenantId.Value.ToString()));
        claims.AddRange((await services.LazyGetRequiredService<IdentityUserManager>().GetRolesAsync(account)).Select(x => new Claim(AbpClaimTypes.Role, x)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TeachingPlanGenerationWorker", AbpClaimTypes.UserName, AbpClaimTypes.Role));
        foreach (var permission in new[] { EducationPermissions.TeachingPlanGenerate, EducationPermissions.TeachingPlanManage, EducationPermissions.AnalysisManage })
            if (!await services.LazyGetRequiredService<IPermissionChecker>().IsGrantedAsync(principal, permission)) throw new AbpAuthorizationException();
        return principal;
    }
}
