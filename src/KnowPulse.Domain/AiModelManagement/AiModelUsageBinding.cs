using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.AiModelManagement;

/// <summary>
/// 业务用途到模型部署的默认绑定。RAG、AI 出题、embedding 只依赖用途，不直接读静态 alias。
/// </summary>
public class AiModelUsageBinding : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public const int MaxBusinessModelVersionLength = AiModelConsts.MaxBusinessModelVersionLength;
    public const int MaxFallbackAliasesJsonLength = AiModelConsts.MaxFallbackAliasesJsonLength;

    public Guid? TenantId { get; protected set; }

    public AiModelUsage Usage { get; private set; }

    public Guid DeploymentId { get; private set; }

    public string BusinessModelVersion { get; private set; } = string.Empty;

    public int MaxOutputTokens { get; private set; }

    public double Temperature { get; private set; }

    public string? FallbackAliasesJson { get; private set; }

    public bool IsActive { get; private set; }

    protected AiModelUsageBinding()
    {
    }

    public AiModelUsageBinding(
        Guid id,
        Guid? tenantId,
        AiModelUsage usage,
        Guid deploymentId,
        string businessModelVersion,
        int maxOutputTokens,
        double temperature,
        string? fallbackAliasesJson = null,
        bool isActive = true)
        : base(id)
    {
        TenantId = tenantId;
        Usage = usage;
        Update(deploymentId, businessModelVersion, maxOutputTokens, temperature, fallbackAliasesJson);
        IsActive = isActive;
    }

    public void Update(
        Guid deploymentId,
        string businessModelVersion,
        int maxOutputTokens,
        double temperature,
        string? fallbackAliasesJson)
    {
        if (deploymentId == Guid.Empty)
        {
            throw new ArgumentException("Deployment id is required.", nameof(deploymentId));
        }

        if (maxOutputTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxOutputTokens));
        }

        if (temperature < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(temperature));
        }

        DeploymentId = deploymentId;
        BusinessModelVersion = Check.NotNullOrWhiteSpace(
            businessModelVersion,
            nameof(businessModelVersion),
            MaxBusinessModelVersionLength);
        MaxOutputTokens = maxOutputTokens;
        Temperature = temperature;
        FallbackAliasesJson = string.IsNullOrWhiteSpace(fallbackAliasesJson)
            ? null
            : Check.Length(fallbackAliasesJson, nameof(fallbackAliasesJson), MaxFallbackAliasesJsonLength);
    }

    public void Activate()
    {
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
    }
}
