using System;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.KnowledgeBase;

public class CreatePersonalKnowledgeExportDto
{
    public Guid IdempotencyKey { get; set; }
}

public class PersonalKnowledgeExportDto : EntityDto<Guid>
{
    public PersonalKnowledgeExportStatus Status { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? SnapshotAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? LastHeartbeatAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public int RetryCount { get; set; }
    public string? FailureStage { get; set; }
    public string? FailureCode { get; set; }
    public long? FileSize { get; set; }
    public bool CanRetry { get; set; }
    public bool CanDownload { get; set; }
}
