using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.AiModelManagement;

/// <summary>
/// 模型目录管理应用服务。该服务只暴露 alias、元数据和环境变量名，不返回真实供应商 key。
/// </summary>
public interface IAiModelCatalogAppService : IApplicationService
{
    [Authorize(KnowPulsePermissions.AiModels.View)]
    Task<AiModelCatalogOverviewDto> GetOverviewAsync();

    [Authorize(KnowPulsePermissions.AiModels.View)]
    Task<PagedResultDto<AiModelDeploymentDto>> GetDeploymentsAsync(GetAiModelDeploymentListInput input);

    [Authorize(KnowPulsePermissions.AiModels.View)]
    Task<AiModelDeploymentDto> GetDeploymentAsync(Guid id);

    [Authorize(KnowPulsePermissions.AiModels.Manage)]
    Task<AiModelDeploymentDto> CreateDeploymentAsync(CreateUpdateAiModelDeploymentDto input);

    [Authorize(KnowPulsePermissions.AiModels.Manage)]
    Task<AiModelDeploymentDto> UpdateDeploymentAsync(Guid id, CreateUpdateAiModelDeploymentDto input);

    [Authorize(KnowPulsePermissions.AiModels.Manage)]
    Task<AiModelDeploymentDto> SetDeploymentActiveAsync(Guid id, bool isActive);

    [Authorize(KnowPulsePermissions.AiModels.SwitchDefaults)]
    Task<AiModelUsageBindingDto> SetUsageBindingAsync(SetAiModelUsageBindingDto input);

    [Authorize(KnowPulsePermissions.AiModels.Test)]
    Task<AiModelHealthSnapshotDto> TestDeploymentAsync(Guid id);

    [Authorize(KnowPulsePermissions.AiModels.Test)]
    Task<IReadOnlyList<AiModelHealthSnapshotDto>> TestAllAsync();
}

/// <summary>
/// 业务调用模型解析器。RAG、AI 出题和 embedding pipeline 只依赖用途选择，不直接读静态 alias。
/// </summary>
public interface IAiModelResolver
{
    Task<AiModelSelection> ResolveAsync(AiModelUsage usage, CancellationToken cancellationToken = default);

    Task<AiModelSelection> ResolveEmbeddingAsync(CancellationToken cancellationToken = default);
}

public class AiModelCatalogOverviewDto
{
    public IReadOnlyList<AiModelDeploymentDto> Deployments { get; set; } = Array.Empty<AiModelDeploymentDto>();

    public IReadOnlyList<AiModelUsageBindingDto> Bindings { get; set; } = Array.Empty<AiModelUsageBindingDto>();

    public IReadOnlyList<AiModelHealthSnapshotDto> HealthSnapshots { get; set; } = Array.Empty<AiModelHealthSnapshotDto>();

    public string LiteLlmBaseUrl { get; set; } = string.Empty;

    public string LiteLlmAdminUrl { get; set; } = string.Empty;
}

public class AiModelDeploymentDto : FullAuditedEntityDto<Guid>
{
    public Guid? TenantId { get; set; }

    public string Alias { get; set; } = string.Empty;

    public AiModelProviderType ProviderType { get; set; }

    public AiModelCapability Capability { get; set; }

    public string UpstreamModelName { get; set; } = string.Empty;

    public string ModelVersion { get; set; } = string.Empty;

    public string? ApiKeyEnvironmentVariable { get; set; }

    public string? BaseUrl { get; set; }

    public int? ContextWindowTokens { get; set; }

    public int? MaxOutputTokens { get; set; }

    public int? EmbeddingDimension { get; set; }

    public decimal? PromptTokenPriceUsd { get; set; }

    public decimal? CompletionTokenPriceUsd { get; set; }

    public bool IsActive { get; set; }

    public string? Description { get; set; }

    public AiModelHealthStatus HealthStatus { get; set; }

    public DateTime? LastCheckedTime { get; set; }

    public int? LastLatencyMs { get; set; }

    public string? LastFailureReason { get; set; }
}

public class AiModelUsageBindingDto : FullAuditedEntityDto<Guid>
{
    public Guid? TenantId { get; set; }

    public AiModelUsage Usage { get; set; }

    public Guid DeploymentId { get; set; }

    public string DeploymentAlias { get; set; } = string.Empty;

    public AiModelCapability Capability { get; set; }

    public string BusinessModelVersion { get; set; } = string.Empty;

    public int MaxOutputTokens { get; set; }

    public double Temperature { get; set; }

    public IReadOnlyList<string> FallbackAliases { get; set; } = Array.Empty<string>();

    public bool IsActive { get; set; }
}

public class AiModelHealthSnapshotDto : FullAuditedEntityDto<Guid>
{
    public Guid? TenantId { get; set; }

    public Guid DeploymentId { get; set; }

    public string DeploymentAlias { get; set; } = string.Empty;

    public AiModelHealthStatus Status { get; set; }

    public DateTime CheckedTime { get; set; }

    public int? LatencyMs { get; set; }

    public string? FailureReason { get; set; }

    public string? GatewayModelInfoJson { get; set; }
}

public class GetAiModelDeploymentListInput : PagedAndSortedResultRequestDto
{
    public string? Keyword { get; set; }

    public AiModelCapability? Capability { get; set; }

    public AiModelProviderType? ProviderType { get; set; }

    public bool? IsActive { get; set; }
}

public class CreateUpdateAiModelDeploymentDto
{
    [Required]
    [StringLength(AiModelConsts.MaxAliasLength)]
    public string Alias { get; set; } = string.Empty;

    public AiModelProviderType ProviderType { get; set; }

    public AiModelCapability Capability { get; set; }

    [Required]
    [StringLength(AiModelConsts.MaxUpstreamModelNameLength)]
    public string UpstreamModelName { get; set; } = string.Empty;

    [Required]
    [StringLength(AiModelConsts.MaxModelVersionLength)]
    public string ModelVersion { get; set; } = string.Empty;

    [StringLength(AiModelConsts.MaxEnvironmentVariableLength)]
    public string? ApiKeyEnvironmentVariable { get; set; }

    [StringLength(AiModelConsts.MaxBaseUrlLength)]
    public string? BaseUrl { get; set; }

    public int? ContextWindowTokens { get; set; }

    public int? MaxOutputTokens { get; set; }

    public int? EmbeddingDimension { get; set; }

    public decimal? PromptTokenPriceUsd { get; set; }

    public decimal? CompletionTokenPriceUsd { get; set; }

    public bool IsActive { get; set; } = true;

    [StringLength(AiModelConsts.MaxDescriptionLength)]
    public string? Description { get; set; }
}

public class SetAiModelUsageBindingDto
{
    public AiModelUsage Usage { get; set; }

    public Guid DeploymentId { get; set; }

    [Required]
    [StringLength(AiModelConsts.MaxBusinessModelVersionLength)]
    public string BusinessModelVersion { get; set; } = string.Empty;

    public int MaxOutputTokens { get; set; }

    public double Temperature { get; set; }

    public List<string> FallbackAliases { get; set; } = new();
}

public class AiModelSelection
{
    public Guid? DeploymentId { get; set; }

    public string Provider { get; set; } = "LiteLLM";

    public string Alias { get; set; } = string.Empty;

    public AiModelCapability Capability { get; set; }

    public AiModelUsage Usage { get; set; }

    public string BusinessModelVersion { get; set; } = string.Empty;

    public int? EmbeddingDimension { get; set; }

    public string? CollectionName { get; set; }

    public int MaxOutputTokens { get; set; }

    public double Temperature { get; set; }

    public IReadOnlyList<string> FallbackAliases { get; set; } = Array.Empty<string>();

    public bool IsHealthy { get; set; }
}
