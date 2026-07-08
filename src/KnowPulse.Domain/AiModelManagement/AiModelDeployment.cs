using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.AiModelManagement;

/// <summary>
/// KnowPulse 业务侧模型目录记录。真实供应商 key 由环境变量或 LiteLLM 托管，不进入该聚合。
/// </summary>
public class AiModelDeployment : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public const int MaxAliasLength = AiModelConsts.MaxAliasLength;
    public const int MaxUpstreamModelNameLength = AiModelConsts.MaxUpstreamModelNameLength;
    public const int MaxModelVersionLength = AiModelConsts.MaxModelVersionLength;
    public const int MaxEnvironmentVariableLength = AiModelConsts.MaxEnvironmentVariableLength;
    public const int MaxBaseUrlLength = AiModelConsts.MaxBaseUrlLength;
    public const int MaxDescriptionLength = AiModelConsts.MaxDescriptionLength;

    public Guid? TenantId { get; protected set; }

    public string Alias { get; private set; } = string.Empty;

    public AiModelProviderType ProviderType { get; private set; }

    public AiModelCapability Capability { get; private set; }

    public string UpstreamModelName { get; private set; } = string.Empty;

    public string ModelVersion { get; private set; } = string.Empty;

    public string? ApiKeyEnvironmentVariable { get; private set; }

    public string? BaseUrl { get; private set; }

    public int? ContextWindowTokens { get; private set; }

    public int? MaxOutputTokens { get; private set; }

    public int? EmbeddingDimension { get; private set; }

    public decimal? PromptTokenPriceUsd { get; private set; }

    public decimal? CompletionTokenPriceUsd { get; private set; }

    public bool IsActive { get; private set; }

    public string? Description { get; private set; }

    protected AiModelDeployment()
    {
    }

    public AiModelDeployment(
        Guid id,
        Guid? tenantId,
        string alias,
        AiModelProviderType providerType,
        AiModelCapability capability,
        string upstreamModelName,
        string modelVersion,
        string? apiKeyEnvironmentVariable = null,
        string? baseUrl = null,
        int? contextWindowTokens = null,
        int? maxOutputTokens = null,
        int? embeddingDimension = null,
        decimal? promptTokenPriceUsd = null,
        decimal? completionTokenPriceUsd = null,
        bool isActive = true,
        string? description = null)
        : base(id)
    {
        TenantId = tenantId;
        Update(
            alias,
            providerType,
            capability,
            upstreamModelName,
            modelVersion,
            apiKeyEnvironmentVariable,
            baseUrl,
            contextWindowTokens,
            maxOutputTokens,
            embeddingDimension,
            promptTokenPriceUsd,
            completionTokenPriceUsd,
            description);
        IsActive = isActive;
    }

    public void Update(
        string alias,
        AiModelProviderType providerType,
        AiModelCapability capability,
        string upstreamModelName,
        string modelVersion,
        string? apiKeyEnvironmentVariable,
        string? baseUrl,
        int? contextWindowTokens,
        int? maxOutputTokens,
        int? embeddingDimension,
        decimal? promptTokenPriceUsd,
        decimal? completionTokenPriceUsd,
        string? description)
    {
        ValidatePositiveOrNull(contextWindowTokens, nameof(contextWindowTokens));
        ValidatePositiveOrNull(maxOutputTokens, nameof(maxOutputTokens));
        ValidatePositiveOrNull(embeddingDimension, nameof(embeddingDimension));
        ValidatePriceOrNull(promptTokenPriceUsd, nameof(promptTokenPriceUsd));
        ValidatePriceOrNull(completionTokenPriceUsd, nameof(completionTokenPriceUsd));

        Alias = Check.NotNullOrWhiteSpace(alias, nameof(alias), MaxAliasLength);
        ProviderType = providerType;
        Capability = capability;
        UpstreamModelName = Check.NotNullOrWhiteSpace(upstreamModelName, nameof(upstreamModelName), MaxUpstreamModelNameLength);
        ModelVersion = Check.NotNullOrWhiteSpace(modelVersion, nameof(modelVersion), MaxModelVersionLength);
        ApiKeyEnvironmentVariable = NormalizeOptional(apiKeyEnvironmentVariable, nameof(apiKeyEnvironmentVariable), MaxEnvironmentVariableLength);
        BaseUrl = NormalizeOptional(baseUrl, nameof(baseUrl), MaxBaseUrlLength);
        ContextWindowTokens = contextWindowTokens;
        MaxOutputTokens = maxOutputTokens;
        EmbeddingDimension = embeddingDimension;
        PromptTokenPriceUsd = promptTokenPriceUsd;
        CompletionTokenPriceUsd = completionTokenPriceUsd;
        Description = Check.Length(description, nameof(description), MaxDescriptionLength);
    }

    public void Activate()
    {
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    private static string? NormalizeOptional(string? value, string parameterName, int maxLength)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : Check.Length(value.Trim(), parameterName, maxLength);
    }

    private static void ValidatePositiveOrNull(int? value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ValidatePriceOrNull(decimal? value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
