using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.AiModelManagement;

/// <summary>
/// 模型连通性最近快照。用于后台快速判断缺 key、超时或网关返回异常。
/// </summary>
public class AiModelHealthSnapshot : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public const int MaxFailureReasonLength = AiModelConsts.MaxFailureReasonLength;
    public const int MaxGatewayModelInfoJsonLength = AiModelConsts.MaxGatewayModelInfoJsonLength;

    public Guid? TenantId { get; protected set; }

    public Guid DeploymentId { get; private set; }

    public AiModelHealthStatus Status { get; private set; }

    public DateTime CheckedTime { get; private set; }

    public int? LatencyMs { get; private set; }

    public string? FailureReason { get; private set; }

    public string? GatewayModelInfoJson { get; private set; }

    protected AiModelHealthSnapshot()
    {
    }

    public AiModelHealthSnapshot(
        Guid id,
        Guid? tenantId,
        Guid deploymentId,
        AiModelHealthStatus status,
        DateTime checkedTime,
        int? latencyMs,
        string? failureReason = null,
        string? gatewayModelInfoJson = null)
        : base(id)
    {
        if (deploymentId == Guid.Empty)
        {
            throw new ArgumentException("Deployment id is required.", nameof(deploymentId));
        }

        if (latencyMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(latencyMs));
        }

        TenantId = tenantId;
        DeploymentId = deploymentId;
        Status = status;
        CheckedTime = checkedTime;
        LatencyMs = latencyMs;
        FailureReason = Check.Length(failureReason, nameof(failureReason), MaxFailureReasonLength);
        GatewayModelInfoJson = Check.Length(gatewayModelInfoJson, nameof(gatewayModelInfoJson), MaxGatewayModelInfoJsonLength);
    }
}
