using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.KnowledgeBase;

public class PersonalKnowledgeExport : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid LibraryId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public PersonalKnowledgeExportStatus Status { get; private set; }
    public DateTime? SnapshotAtUtc { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? LastHeartbeatAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime? ExpiresAtUtc { get; private set; }
    public DateTime? LastCleanupAtUtc { get; private set; }
    public int RetryCount { get; private set; }
    public Guid? RunId { get; private set; }
    public bool RunStopped { get; private set; }
    public string? SourceManifestJson { get; private set; }
    public string? BlobName { get; private set; }
    public bool TemporaryFilePending { get; private set; }
    public string? RetiredArtifactsJson { get; private set; }
    public bool HasRetiredArtifacts { get; private set; }
    public long? FileSize { get; private set; }
    public string? FailureStage { get; private set; }
    public string? FailureCode { get; private set; }

    protected PersonalKnowledgeExport() { }

    public PersonalKnowledgeExport(Guid id, Guid? tenantId, Guid ownerUserId) : base(id)
    {
        if (id == Guid.Empty || ownerUserId == Guid.Empty) throw new ArgumentException("An export requires an operation and owner.");
        TenantId = tenantId;
        LibraryId = ownerUserId;
        OwnerUserId = ownerUserId;
    }

    public void Start(Guid runId, DateTime now, string blobName)
    {
        if (Status != PersonalKnowledgeExportStatus.Pending || runId == Guid.Empty) throw new BusinessException(PersonalKnowledgeExportErrorCodes.NotReady);
        Status = PersonalKnowledgeExportStatus.Processing;
        RunId = runId;
        RunStopped = false;
        BlobName = Check.NotNullOrWhiteSpace(blobName, nameof(blobName), KnowledgeAttachment.MaxBlobNameLength);
        TemporaryFilePending = true;
        StartedAtUtc = LastHeartbeatAtUtc = now;
        FailureStage = FailureCode = null;
    }

    public void SetSnapshot(string manifestJson, DateTime now)
    {
        SourceManifestJson = manifestJson;
        SnapshotAtUtc = LastHeartbeatAtUtc = now;
    }

    public void Heartbeat(DateTime now) => LastHeartbeatAtUtc = now;

    public void Complete(long fileSize, DateTime now, DateTime expiresAt)
    {
        if (Status != PersonalKnowledgeExportStatus.Processing || fileSize <= 0 || expiresAt <= now)
            throw new BusinessException(PersonalKnowledgeExportErrorCodes.NotReady);
        Status = PersonalKnowledgeExportStatus.Completed;
        FileSize = fileSize;
        CompletedAtUtc = LastHeartbeatAtUtc = now;
        ExpiresAtUtc = expiresAt;
    }

    public void Fail(string stage, string code, DateTime now)
    {
        if (Status == PersonalKnowledgeExportStatus.Cancelled) return;
        Status = PersonalKnowledgeExportStatus.Failed;
        FailureStage = Check.NotNullOrWhiteSpace(stage, nameof(stage), 32);
        FailureCode = Check.NotNullOrWhiteSpace(code, nameof(code), 128);
        LastHeartbeatAtUtc = now;
    }

    public void Retry()
    {
        if (Status != PersonalKnowledgeExportStatus.Failed) throw new BusinessException(PersonalKnowledgeExportErrorCodes.CannotRetry);
        Status = PersonalKnowledgeExportStatus.Pending;
        RetryCount++;
        SnapshotAtUtc = StartedAtUtc = LastHeartbeatAtUtc = CompletedAtUtc = ExpiresAtUtc = null;
        SourceManifestJson = null;
        FileSize = null;
        FailureStage = FailureCode = null;
        // BlobName remains until recoverable cleanup succeeds, before the next run is claimed.
    }

    public void Cancel()
    {
        if (Status is PersonalKnowledgeExportStatus.Expired or PersonalKnowledgeExportStatus.Cancelled) return;
        Status = PersonalKnowledgeExportStatus.Cancelled;
    }

    public void Expire() => Status = PersonalKnowledgeExportStatus.Expired;
    public void ClearBlob() => BlobName = null;
    public void ClearTemporaryFile() => TemporaryFilePending = false;
    public void RecordCleanup(DateTime now) => LastCleanupAtUtc = now;

    public List<PersonalKnowledgeExportArtifact> GetRetiredArtifacts() => string.IsNullOrWhiteSpace(RetiredArtifactsJson)
        ? new List<PersonalKnowledgeExportArtifact>()
        : JsonSerializer.Deserialize<List<PersonalKnowledgeExportArtifact>>(RetiredArtifactsJson)!;

    public PersonalKnowledgeExportArtifact? GetArtifact(Guid runId) => RunId == runId
        ? new PersonalKnowledgeExportArtifact(runId, BlobName, TemporaryFilePending, RunStopped)
        : GetRetiredArtifacts().FirstOrDefault(x => x.RunId == runId);

    public void RetireCurrentArtifact()
    {
        if (RunId.HasValue && (BlobName != null || TemporaryFilePending))
        {
            var artifacts = GetRetiredArtifacts();
            if (artifacts.All(x => x.RunId != RunId))
                artifacts.Add(new PersonalKnowledgeExportArtifact(RunId.Value, BlobName, TemporaryFilePending, RunStopped));
            SetRetiredArtifacts(artifacts);
        }
        BlobName = null;
        TemporaryFilePending = false;
    }

    public void MarkRunStopped(Guid runId)
    {
        var artifact = GetArtifact(runId);
        if (artifact != null) ApplyCleanup(artifact with { RunStopped = true });
    }

    public void ApplyCleanup(PersonalKnowledgeExportArtifact artifact)
    {
        if (RunId == artifact.RunId)
        {
            BlobName = artifact.BlobName;
            TemporaryFilePending = artifact.TemporaryFilePending;
            RunStopped = artifact.RunStopped;
            return;
        }
        var artifacts = GetRetiredArtifacts();
        var index = artifacts.FindIndex(x => x.RunId == artifact.RunId);
        if (index < 0) return;
        if (artifact.RunStopped && artifact.BlobName == null && !artifact.TemporaryFilePending) artifacts.RemoveAt(index);
        else artifacts[index] = artifact;
        SetRetiredArtifacts(artifacts);
    }

    private void SetRetiredArtifacts(List<PersonalKnowledgeExportArtifact> artifacts)
    {
        HasRetiredArtifacts = artifacts.Count != 0;
        RetiredArtifactsJson = HasRetiredArtifacts ? JsonSerializer.Serialize(artifacts) : null;
    }
}

public sealed record PersonalKnowledgeExportArtifact(Guid RunId, string? BlobName, bool TemporaryFilePending, bool RunStopped);
