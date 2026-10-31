using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace KnowPulse.KnowledgeBase;

[Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
public class KnowledgePersonalExportAppService : KnowPulseAppService, IKnowledgePersonalExportAppService
{
    private readonly IRepository<PersonalKnowledgeExport, Guid> _jobs;
    private readonly IBlobContainer<KnowledgeAttachmentBlobContainer> _blobs;
    private readonly IUnitOfWorkManager _uow;
    private readonly PersonalKnowledgeExportOptions _limits;

    public KnowledgePersonalExportAppService(IRepository<PersonalKnowledgeExport, Guid> jobs,
        IBlobContainer<KnowledgeAttachmentBlobContainer> blobs, IUnitOfWorkManager uow, IOptions<PersonalKnowledgeExportOptions> options)
    {
        _jobs = jobs;
        _blobs = blobs;
        _uow = uow;
        _limits = options.Value;
        _limits.EnsureValid();
    }

    public virtual async Task<PersonalKnowledgeExportDto> CreateAsync(CreatePersonalKnowledgeExportDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.PersonalLibrary);
        if (!CurrentUser.Id.HasValue || CurrentUser.Id == Guid.Empty) throw new AbpAuthorizationException();
        if (input.IdempotencyKey == Guid.Empty) throw new BusinessException(PersonalKnowledgeExportErrorCodes.NotReady);
        var candidate = new PersonalKnowledgeExport(input.IdempotencyKey, CurrentTenant.Id, CurrentUser.Id.Value);
        await PersonalKnowledgeExportSource.EnsureOwnerAsync(LazyServiceProvider, candidate);
        try
        {
            using var transaction = _uow.Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true);
            var existing = await _jobs.FindAsync(candidate.Id);
            if (existing != null) EnsureMyJob(existing);
            var job = existing ?? await _jobs.InsertAsync(candidate, autoSave: true);
            await transaction.CompleteAsync();
            return Map(job);
        }
        catch (Exception error) when (error is not AbpAuthorizationException && error is not BusinessException && error is not OperationCanceledException)
        {
            using var recovery = _uow.Begin(requiresNew: true, isTransactional: false);
            var winner = await _jobs.FindAsync(candidate.Id);
            if (winner == null) throw;
            EnsureMyJob(winner);
            await recovery.CompleteAsync();
            return Map(winner);
        }
    }

    public virtual async Task<PersonalKnowledgeExportDto> GetAsync(Guid id) => Map(await GetMyJobAsync(id));

    public virtual async Task<PagedResultDto<PersonalKnowledgeExportDto>> GetListAsync(PagedResultRequestDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.PersonalLibrary);
        if (!CurrentUser.Id.HasValue || CurrentUser.Id == Guid.Empty) throw new AbpAuthorizationException();
        await PersonalKnowledgeExportSource.EnsureOwnerAsync(LazyServiceProvider,
            new PersonalKnowledgeExport(Guid.NewGuid(), CurrentTenant.Id, CurrentUser.Id.Value));
        var query = (await _jobs.GetQueryableAsync()).Where(x => !x.IsDeleted && x.TenantId == CurrentTenant.Id && x.OwnerUserId == CurrentUser.Id);
        var total = await AsyncExecuter.CountAsync(query);
        var rows = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.CreationTime).ThenBy(x => x.Id)
            .Skip(input.SkipCount).Take(Math.Min(input.MaxResultCount, 100)));
        return new PagedResultDto<PersonalKnowledgeExportDto>(total, rows.Select(Map).ToList());
    }

    public virtual async Task<PersonalKnowledgeExportDto> RetryAsync(Guid id)
    {
        var job = await GetMyJobAsync(id);
        job.Retry();
        return Map(await _jobs.UpdateAsync(job, autoSave: true));
    }

    public virtual async Task<PersonalKnowledgeExportDto> CancelAsync(Guid id)
    {
        var job = await GetMyJobAsync(id);
        job.Cancel();
        return Map(await _jobs.UpdateAsync(job, autoSave: true));
    }

    public virtual async Task<IRemoteStreamContent> DownloadAsync(Guid id)
    {
        var job = await GetMyJobAsync(id);
        if (job.Status != PersonalKnowledgeExportStatus.Completed || !job.ExpiresAtUtc.HasValue ||
            job.ExpiresAtUtc <= DateTime.UtcNow || job.BlobName == null || !job.FileSize.HasValue)
            throw new BusinessException(PersonalKnowledgeExportErrorCodes.NotReady);
        await PersonalKnowledgeExportSource.ValidateAsync(LazyServiceProvider, job, _limits, checkStamps: false);
        var stream = await _blobs.GetAsync(job.BlobName);
        return new RemoteStreamContent(stream, $"personal-knowledge-{job.Id:N}.zip", "application/zip", job.FileSize, disposeStream: true);
    }

    private async Task<PersonalKnowledgeExport> GetMyJobAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.PersonalLibrary);
        var job = await _jobs.FindAsync(id);
        if (job == null) throw new AbpAuthorizationException("This personal export is unavailable.");
        EnsureMyJob(job);
        await PersonalKnowledgeExportSource.EnsureOwnerAsync(LazyServiceProvider, job);
        return job;
    }

    private void EnsureMyJob(PersonalKnowledgeExport job)
    {
        if (!CurrentUser.IsAuthenticated || job.IsDeleted || job.OwnerUserId != CurrentUser.Id || job.LibraryId != CurrentUser.Id || job.TenantId != CurrentTenant.Id)
            throw new AbpAuthorizationException("This personal export is unavailable to this account.");
    }

    private static PersonalKnowledgeExportDto Map(PersonalKnowledgeExport job)
    {
        var expired = job.Status == PersonalKnowledgeExportStatus.Completed && job.ExpiresAtUtc <= DateTime.UtcNow;
        return new PersonalKnowledgeExportDto
        {
            Id = job.Id, Status = expired ? PersonalKnowledgeExportStatus.Expired : job.Status, CreationTime = job.CreationTime,
            SnapshotAtUtc = job.SnapshotAtUtc, StartedAtUtc = job.StartedAtUtc, LastHeartbeatAtUtc = job.LastHeartbeatAtUtc,
            CompletedAtUtc = job.CompletedAtUtc, ExpiresAtUtc = job.ExpiresAtUtc, RetryCount = job.RetryCount,
            FileSize = job.FileSize, FailureStage = job.FailureStage, FailureCode = job.FailureCode,
            CanRetry = job.Status == PersonalKnowledgeExportStatus.Failed,
            CanDownload = job.Status == PersonalKnowledgeExportStatus.Completed && !expired && job.BlobName != null
        };
    }
}
