using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.Uow;

namespace KnowPulse.KnowledgeBase;

[RemoteService(false)]
public class PersonalKnowledgeExportProcessor : ITransientDependency
{
    private readonly IRepository<PersonalKnowledgeExport, Guid> _jobs;
    private readonly IBlobContainer<KnowledgeAttachmentBlobContainer> _blobs;
    private readonly IUnitOfWorkManager _uow;
    private readonly IAbpLazyServiceProvider _services;
    private readonly PersonalKnowledgeExportOptions _limits;
    private readonly ILogger<PersonalKnowledgeExportProcessor> _logger;

    public PersonalKnowledgeExportProcessor(IRepository<PersonalKnowledgeExport, Guid> jobs,
        IBlobContainer<KnowledgeAttachmentBlobContainer> blobs, IUnitOfWorkManager uow, IAbpLazyServiceProvider services,
        IOptions<PersonalKnowledgeExportOptions> options, ILogger<PersonalKnowledgeExportProcessor> logger)
    {
        _jobs = jobs;
        _blobs = blobs;
        _uow = uow;
        _services = services;
        _limits = options.Value;
        _limits.EnsureValid();
        _logger = logger;
    }

    public virtual async Task<List<PersonalKnowledgeExport>> GetDueJobsAsync(CancellationToken ct = default)
    {
        var stale = DateTime.UtcNow.AddSeconds(-_limits.LeaseSeconds);
        var query = await _jobs.GetQueryableAsync();
        return await _services.LazyGetRequiredService<IAsyncQueryableExecuter>().ToListAsync(query.Where(x =>
            x.Status == PersonalKnowledgeExportStatus.Pending ||
            (x.Status == PersonalKnowledgeExportStatus.Processing && x.LastHeartbeatAtUtc <= stale) ||
            x.Status == PersonalKnowledgeExportStatus.Completed || x.HasRetiredArtifacts ||
            ((x.Status == PersonalKnowledgeExportStatus.Failed || x.Status == PersonalKnowledgeExportStatus.Expired ||
                x.Status == PersonalKnowledgeExportStatus.Cancelled) && (x.BlobName != null || x.TemporaryFilePending)))
            .OrderBy(x => x.Status == PersonalKnowledgeExportStatus.Pending ? 0 :
                x.Status == PersonalKnowledgeExportStatus.Processing && x.LastHeartbeatAtUtc <= stale ? 1 : 2)
            .ThenBy(x => x.LastCleanupAtUtc ?? x.LastHeartbeatAtUtc ?? x.CreationTime).ThenBy(x => x.Id).Take(_limits.BatchSize), ct);
    }

    [UnitOfWork(IsDisabled = true)]
    public virtual async Task ProcessAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await CleanupRetiredAsync(id, cancellationToken);
        Guid runId;
        PersonalKnowledgeExport claimed;
        using (var claimUow = _uow.Begin(requiresNew: true, isTransactional: true))
        {
            var job = await _jobs.GetAsync(id, cancellationToken: cancellationToken);
            if (job.Status == PersonalKnowledgeExportStatus.Completed)
            {
                if (job.ExpiresAtUtc <= DateTime.UtcNow) job.Expire();
                else
                {
                    try
                    {
                        await PersonalKnowledgeExportSource.ValidateAsync(_services, job, _limits, false, cancellationToken);
                        job.Heartbeat(DateTime.UtcNow);
                    }
                    catch (BusinessException error) { job.Fail("authorization", error.Code!, DateTime.UtcNow); }
                }
            }
            if (job.Status == PersonalKnowledgeExportStatus.Processing &&
                job.LastHeartbeatAtUtc <= DateTime.UtcNow.AddSeconds(-_limits.LeaseSeconds))
                job.Fail("worker", PersonalKnowledgeExportErrorCodes.StorageFailed, DateTime.UtcNow);
            if (job.Status != PersonalKnowledgeExportStatus.Pending)
            {
                await _jobs.UpdateAsync(job, autoSave: true, cancellationToken: cancellationToken);
                await claimUow.CompleteAsync(cancellationToken);
                if (job.Status is PersonalKnowledgeExportStatus.Failed or PersonalKnowledgeExportStatus.Expired or PersonalKnowledgeExportStatus.Cancelled or PersonalKnowledgeExportStatus.Completed)
                    await CleanupAsync(id, job.RunId, cancellationToken);
                return;
            }
            // A lease timeout cannot prove that an old SaveAsync has stopped. Keep its immutable cleanup target.
            if (job.RunId.HasValue && !job.RunStopped && (job.BlobName != null || job.TemporaryFilePending))
                job.RetireCurrentArtifact();
            if (job.RunId.HasValue && job.RunStopped && job.TemporaryFilePending)
            {
                if (!TryDeleteTemporaryFile(GetTempPath(id, job.RunId.Value))) throw new BusinessException(PersonalKnowledgeExportErrorCodes.StorageFailed);
                job.ClearTemporaryFile();
            }
            if (job.BlobName != null)
            {
                await _blobs.DeleteAsync(job.BlobName, cancellationToken);
                job.ClearBlob();
            }
            runId = Guid.NewGuid();
            job.Start(runId, DateTime.UtcNow, $"personal-exports/{job.OwnerUserId:N}/{job.Id:N}/{runId:N}.zip");
            await _jobs.UpdateAsync(job, autoSave: true, cancellationToken: cancellationToken);
            await claimUow.CompleteAsync(cancellationToken);
            claimed = job;
        }

        var stage = "snapshot";
        var tempPath = GetTempPath(id, runId);
        try
        {
            PersonalExportSnapshot snapshot;
            using (var snapshotUow = _uow.Begin(requiresNew: true, isTransactional: false))
            {
                var job = await GetRunAsync(id, runId, cancellationToken);
                snapshot = await PersonalKnowledgeExportSource.CaptureAsync(_services, job, _limits, cancellationToken);
                job.SetSnapshot(JsonSerializer.Serialize(snapshot.References), DateTime.UtcNow);
                await _jobs.UpdateAsync(job, autoSave: true, cancellationToken: cancellationToken);
                await snapshotUow.CompleteAsync(cancellationToken);
            }

            stage = "zip";
            Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
            using (var file = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, useAsync: true))
            {
                using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
                {
                    long sourceBytes = 0;
                    var lastHeartbeat = DateTime.UtcNow;
                    async Task CheckAsync()
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (file.Length > _limits.MaxFileBytes) throw new BusinessException(PersonalKnowledgeExportErrorCodes.LimitExceeded);
                        if (DateTime.UtcNow - lastHeartbeat < TimeSpan.FromSeconds(Math.Max(1, _limits.LeaseSeconds / 3))) return;
                        using var heartbeatUow = _uow.Begin(requiresNew: true, isTransactional: false);
                        var running = await GetRunAsync(id, runId, cancellationToken);
                        await PersonalKnowledgeExportSource.EnsureOwnerAsync(_services, running, cancellationToken);
                        running.Heartbeat(DateTime.UtcNow);
                        await _jobs.UpdateAsync(running, autoSave: true, cancellationToken: cancellationToken);
                        await heartbeatUow.CompleteAsync(cancellationToken);
                        lastHeartbeat = DateTime.UtcNow;
                    }

                    async Task WriteTextAsync(string path, string content)
                    {
                        var bytes = Encoding.UTF8.GetBytes(content);
                        sourceBytes = checked(sourceBytes + bytes.LongLength);
                        if (sourceBytes > _limits.MaxSourceBytes) throw new BusinessException(PersonalKnowledgeExportErrorCodes.LimitExceeded);
                        using var entry = zip.CreateEntry(path, CompressionLevel.Optimal).Open();
                        await entry.WriteAsync(bytes, cancellationToken);
                        await CheckAsync();
                    }

                    var manifest = new
                    {
                        Format = "KnowPulse-PersonalKnowledge-1", ExportId = id, SnapshotAtUtc = DateTime.UtcNow,
                        Items = snapshot.Items.Select(x => new { x.Id, x.Title, x.Summary, x.IsActive,
                            MaterialType = x.MaterialType.ToString(), x.MaterialYear, x.SubjectName, CustomTags = x.GetPersonalTags() }),
                        Versions = snapshot.Versions.Select(x => new { x.Id, x.KnowledgeItemId, x.VersionNumber, x.Title, x.Status,
                            BodyPath = $"items/{x.KnowledgeItemId:N}/versions/{x.Id:N}/content.txt",
                            NotebookSources = snapshot.Items.Single(item => item.Id == x.KnowledgeItemId).GetPrivateNotebookSources()
                                .Where(source => source.NoteVersionId == x.Id && !source.RemovedAtUtc.HasValue)
                                .Select(source => new { Kind = source.Source.Kind.ToString(), source.Source.RecipientId, source.Source.SourceVersionId,
                                    source.Source.ComponentId, source.RecordedAtUtc, RequiresCurrentAuthorization = true }) }),
                        Attachments = snapshot.Attachments.Select(x => new { x.Id, x.KnowledgeVersionId, x.FileName, x.ContentType, x.Size,
                            Path = GetAttachmentPath(x) })
                    };
                    await WriteTextAsync("manifest.json", JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
                    foreach (var version in snapshot.Versions)
                        await WriteTextAsync($"items/{version.KnowledgeItemId:N}/versions/{version.Id:N}/content.txt", version.ContentText ?? string.Empty);
                    var buffer = new byte[65536];
                    foreach (var attachment in snapshot.Attachments)
                    {
                        using var original = await _blobs.GetAsync(attachment.BlobName, cancellationToken);
                        using var entry = zip.CreateEntry(GetAttachmentPath(attachment), CompressionLevel.Optimal).Open();
                        long actualBytes = 0;
                        int count;
                        while ((count = await original.ReadAsync(buffer, cancellationToken)) > 0)
                        {
                            actualBytes = checked(actualBytes + count);
                            sourceBytes = checked(sourceBytes + count);
                            if (sourceBytes > _limits.MaxSourceBytes) throw new BusinessException(PersonalKnowledgeExportErrorCodes.LimitExceeded);
                            if (actualBytes > attachment.Size) throw new BusinessException(PersonalKnowledgeExportErrorCodes.SourceChanged);
                            await entry.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                            await CheckAsync();
                        }
                        if (actualBytes != attachment.Size) throw new BusinessException(PersonalKnowledgeExportErrorCodes.SourceChanged);
                    }
                }
                if (file.Length > _limits.MaxFileBytes) throw new BusinessException(PersonalKnowledgeExportErrorCodes.LimitExceeded);
                file.Position = 0;
                stage = "storage";
                await _blobs.SaveAsync(claimed.BlobName!, file, overrideExisting: true, cancellationToken);

                stage = "finalize";
                using var completeUow = _uow.Begin(requiresNew: true, isTransactional: true);
                var current = await GetRunAsync(id, runId, cancellationToken);
                await PersonalKnowledgeExportSource.ValidateAsync(_services, current, _limits, true, cancellationToken);
                current.Complete(file.Length, DateTime.UtcNow, DateTime.UtcNow.AddHours(_limits.FileLifetimeHours));
                await _jobs.UpdateAsync(current, autoSave: true, cancellationToken: cancellationToken);
                await completeUow.CompleteAsync(cancellationToken);
            }
        }
        catch (Exception error)
        {
            var code = error is BusinessException business ? business.Code! : PersonalKnowledgeExportErrorCodes.StorageFailed;
            // Controlled metadata only: provider exceptions can contain storage paths or original content.
            _logger.LogWarning("Personal export {ExportId} failed at {Stage}, code {FailureCode}.", id, stage, code);
            using var failUow = _uow.Begin(requiresNew: true, isTransactional: false);
            var current = await _jobs.GetAsync(id);
            if (current.RunId == runId && current.Status != PersonalKnowledgeExportStatus.Completed)
            {
                current.Fail(stage, code, DateTime.UtcNow);
                await _jobs.UpdateAsync(current, autoSave: true);
            }
            await failUow.CompleteAsync();
            if (error is OperationCanceledException && cancellationToken.IsCancellationRequested) throw;
        }
        finally
        {
            await MarkRunStoppedAsync(id, runId);
            await CleanupAsync(id, runId, CancellationToken.None);
        }
    }

    private async Task<PersonalKnowledgeExport> GetRunAsync(Guid id, Guid runId, CancellationToken ct)
    {
        var job = await _jobs.GetAsync(id, cancellationToken: ct);
        if (job.RunId != runId || job.Status != PersonalKnowledgeExportStatus.Processing)
            throw new BusinessException(PersonalKnowledgeExportErrorCodes.Cancelled);
        return job;
    }

    private async Task CleanupAsync(Guid id, Guid? runId, CancellationToken ct)
    {
        try
        {
            using var cleanupUow = _uow.Begin(requiresNew: true, isTransactional: false);
            var job = await _jobs.GetAsync(id, cancellationToken: ct);
            if (!runId.HasValue) return;
            var artifact = job.GetArtifact(runId.Value);
            if (artifact == null || (job.RunId == runId && job.Status == PersonalKnowledgeExportStatus.Processing)) return;
            if (artifact.TemporaryFilePending && TryDeleteTemporaryFile(GetTempPath(id, runId.Value)) && artifact.RunStopped)
                artifact = artifact with { TemporaryFilePending = false };
            if ((job.RunId != runId || job.Status != PersonalKnowledgeExportStatus.Completed) && artifact.BlobName != null)
            {
                await _blobs.DeleteAsync(artifact.BlobName, ct);
                // Deletion of an absent object is not a fence against a late write by the old executor.
                if (artifact.RunStopped) artifact = artifact with { BlobName = null };
            }
            job.ApplyCleanup(artifact);
            job.RecordCleanup(DateTime.UtcNow);
            await _jobs.UpdateAsync(job, autoSave: true, cancellationToken: ct);
            await cleanupUow.CompleteAsync(ct);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _logger.LogWarning("Personal export {ExportId} cleanup will be retried.", id);
        }
    }

    private async Task MarkRunStoppedAsync(Guid id, Guid runId)
    {
        try
        {
            using var stopUow = _uow.Begin(requiresNew: true, isTransactional: false);
            var job = await _jobs.GetAsync(id);
            job.MarkRunStopped(runId);
            await _jobs.UpdateAsync(job, autoSave: true);
            await stopUow.CompleteAsync();
        }
        catch (Exception)
        {
            // ponytail: unknown retired runs retain cleanup targets; host fencing is required before retiring unconfirmed writers.
            _logger.LogWarning("Personal export {ExportId} run {RunId} stop acknowledgement was not persisted.", id, runId);
        }
    }

    private async Task CleanupRetiredAsync(Guid id, CancellationToken ct)
    {
        Guid[] retiredIds;
        using (var readUow = _uow.Begin(requiresNew: true, isTransactional: false))
        {
            var job = await _jobs.GetAsync(id, cancellationToken: ct);
            retiredIds = job.GetRetiredArtifacts().Select(x => x.RunId).ToArray();
            await readUow.CompleteAsync(ct);
        }
        foreach (var retiredId in retiredIds) await CleanupAsync(id, retiredId, ct);
    }

    private static string GetTempPath(Guid id, Guid runId) => Path.Combine(Path.GetTempPath(), "knowpulse-personal-exports", $"{id:N}-{runId:N}.zip");
    private static string GetAttachmentPath(KnowledgeAttachment attachment)
    {
        var extension = Path.GetExtension(attachment.FileName);
        if (extension.Length is < 2 or > 17 || extension.Skip(1).Any(character => !char.IsAsciiLetterOrDigit(character))) extension = ".bin";
        return $"attachments/{attachment.Id:N}{extension.ToLowerInvariant()}";
    }
    private bool TryDeleteTemporaryFile(string path)
    {
        try { File.Delete(path); return true; }
        catch (IOException) { _logger.LogWarning("Personal export temporary file cleanup will be retried."); }
        catch (UnauthorizedAccessException) { _logger.LogWarning("Personal export temporary file cleanup will be retried."); }
        return false;
    }
}
