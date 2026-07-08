using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.RagAI;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.AiModelManagement;

/// <summary>
/// 从模型目录和用途绑定解析业务调用要使用的 LiteLLM alias。
/// </summary>
public class AiModelResolver : IAiModelResolver
{
    private readonly IRepository<AiModelDeployment, Guid> _deploymentRepository;
    private readonly IRepository<AiModelUsageBinding, Guid> _bindingRepository;
    private readonly IRepository<AiModelHealthSnapshot, Guid> _healthRepository;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ICurrentTenant _currentTenant;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly RagAiOptions _options;

    public AiModelResolver(
        IRepository<AiModelDeployment, Guid> deploymentRepository,
        IRepository<AiModelUsageBinding, Guid> bindingRepository,
        IRepository<AiModelHealthSnapshot, Guid> healthRepository,
        IGuidGenerator guidGenerator,
        ICurrentTenant currentTenant,
        IAsyncQueryableExecuter asyncExecuter,
        IOptions<RagAiOptions> options)
    {
        _deploymentRepository = deploymentRepository;
        _bindingRepository = bindingRepository;
        _healthRepository = healthRepository;
        _guidGenerator = guidGenerator;
        _currentTenant = currentTenant;
        _asyncExecuter = asyncExecuter;
        _options = options.Value;
    }

    public Task<AiModelSelection> ResolveEmbeddingAsync(CancellationToken cancellationToken = default)
    {
        return ResolveAsync(AiModelUsage.KnowledgeEmbedding, cancellationToken);
    }

    public async Task<AiModelSelection> ResolveAsync(
        AiModelUsage usage,
        CancellationToken cancellationToken = default)
    {
        await EnsureDefaultCatalogAsync(cancellationToken);

        var binding = await FindBindingAsync(usage, cancellationToken);
        if (binding == null || !binding.IsActive)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.AiModelUsageBindingMissing)
                .WithData("Usage", usage.ToString());
        }

        var deployment = await _deploymentRepository.GetAsync(binding.DeploymentId, cancellationToken: cancellationToken);
        if (!deployment.IsActive)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.AiModelDeploymentInactive)
                .WithData("Alias", deployment.Alias);
        }

        EnsureUsageCapability(usage, deployment.Capability);
        var latestHealth = await GetLatestHealthAsync(deployment.Id, cancellationToken);

        return new AiModelSelection
        {
            DeploymentId = deployment.Id,
            Provider = deployment.ProviderType.ToString(),
            Alias = deployment.Alias,
            Capability = deployment.Capability,
            Usage = usage,
            BusinessModelVersion = binding.BusinessModelVersion,
            EmbeddingDimension = deployment.EmbeddingDimension,
            CollectionName = deployment.Capability == AiModelCapability.Embedding
                ? _options.Embedding.CollectionName
                : null,
            MaxOutputTokens = binding.MaxOutputTokens,
            Temperature = binding.Temperature,
            FallbackAliases = AiModelJsonHelper.DeserializeFallbackAliases(binding.FallbackAliasesJson),
            IsHealthy = latestHealth == null ||
                        latestHealth.Status == AiModelHealthStatus.Unknown ||
                        latestHealth.Status == AiModelHealthStatus.Healthy
        };
    }

    private async Task EnsureDefaultCatalogAsync(CancellationToken cancellationToken)
    {
        var chatAlias = ReadOption(_options.LiteLlm.ChatModel, "knowpulse-chat-default");
        var embeddingAlias = ReadOption(_options.Embedding.ModelName, ReadOption(_options.LiteLlm.EmbeddingModel, "knowpulse-embedding-default"));
        var embeddingFallbackAlias = _options.LiteLlm.FallbackModels
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && !string.Equals(x, embeddingAlias, StringComparison.OrdinalIgnoreCase))
            ?.Trim();

        var chatDeployment = await GetOrCreateDeploymentAsync(
            chatAlias,
            AiModelCapability.Chat,
            ReadOption(_options.LiteLlm.ChatModel, chatAlias),
            ReadOption(_options.AiQuestionGeneration.ModelVersion, "2026-06-mvp2-questioning"),
            _options.LiteLlm.ApiKeyEnvironmentVariable,
            _options.LiteLlm.BaseUrl,
            _options.LiteLlm.ChatMaxTokens,
            null,
            "Seeded from RagAI LiteLLM chat alias.",
            cancellationToken);

        var embeddingDeployment = await GetOrCreateDeploymentAsync(
            embeddingAlias,
            AiModelCapability.Embedding,
            ReadOption(_options.LiteLlm.EmbeddingModel, embeddingAlias),
            ReadOption(_options.Embedding.ModelVersion, "2026-06-mvp1"),
            _options.LiteLlm.ApiKeyEnvironmentVariable,
            _options.LiteLlm.BaseUrl,
            null,
            _options.Embedding.VectorDimension,
            "Seeded from RagAI LiteLLM embedding alias.",
            cancellationToken);

        AiModelDeployment? fallbackDeployment = null;
        if (!string.IsNullOrWhiteSpace(embeddingFallbackAlias))
        {
            fallbackDeployment = await GetOrCreateDeploymentAsync(
                embeddingFallbackAlias,
                AiModelCapability.Embedding,
                embeddingFallbackAlias,
                ReadOption(_options.Embedding.ModelVersion, "2026-06-mvp1"),
                _options.LiteLlm.ApiKeyEnvironmentVariable,
                _options.LiteLlm.BaseUrl,
                null,
                _options.Embedding.VectorDimension,
                "Seeded from RagAI LiteLLM fallback alias.",
                cancellationToken);
        }

        await GetOrCreateBindingAsync(
            AiModelUsage.RagAnswerChat,
            chatDeployment.Id,
            ReadOption(_options.Embedding.ModelVersion, "2026-06-mvp1"),
            Math.Max(1, _options.LiteLlm.ChatMaxTokens),
            _options.LiteLlm.ChatTemperature,
            Array.Empty<string>(),
            cancellationToken);

        await GetOrCreateBindingAsync(
            AiModelUsage.AiQuestionGeneration,
            chatDeployment.Id,
            ReadOption(_options.AiQuestionGeneration.ModelVersion, "2026-06-mvp2-questioning"),
            Math.Max(1, _options.AiQuestionGeneration.MaxOutputTokens),
            _options.AiQuestionGeneration.Temperature,
            Array.Empty<string>(),
            cancellationToken);

        await GetOrCreateBindingAsync(
            AiModelUsage.KnowledgeEmbedding,
            embeddingDeployment.Id,
            ReadOption(_options.Embedding.ModelVersion, "2026-06-mvp1"),
            0,
            0,
            fallbackDeployment == null ? Array.Empty<string>() : new[] { fallbackDeployment.Alias },
            cancellationToken);
    }

    private async Task<AiModelDeployment> GetOrCreateDeploymentAsync(
        string alias,
        AiModelCapability capability,
        string upstreamModelName,
        string modelVersion,
        string? apiKeyEnvironmentVariable,
        string? baseUrl,
        int? maxOutputTokens,
        int? embeddingDimension,
        string description,
        CancellationToken cancellationToken)
    {
        var existing = await FindDeploymentByAliasAsync(alias, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        return await _deploymentRepository.InsertAsync(
            new AiModelDeployment(
                _guidGenerator.Create(),
                _currentTenant.Id,
                alias,
                AiModelProviderType.LiteLLM,
                capability,
                upstreamModelName,
                modelVersion,
                apiKeyEnvironmentVariable,
                baseUrl,
                contextWindowTokens: null,
                maxOutputTokens,
                embeddingDimension,
                promptTokenPriceUsd: null,
                completionTokenPriceUsd: null,
                isActive: true,
                description),
            autoSave: true,
            cancellationToken: cancellationToken);
    }

    private async Task<AiModelUsageBinding> GetOrCreateBindingAsync(
        AiModelUsage usage,
        Guid deploymentId,
        string businessModelVersion,
        int maxOutputTokens,
        double temperature,
        string[] fallbackAliases,
        CancellationToken cancellationToken)
    {
        var existing = await FindBindingAsync(usage, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        return await _bindingRepository.InsertAsync(
            new AiModelUsageBinding(
                _guidGenerator.Create(),
                _currentTenant.Id,
                usage,
                deploymentId,
                businessModelVersion,
                maxOutputTokens,
                temperature,
                AiModelJsonHelper.SerializeFallbackAliases(fallbackAliases),
                isActive: true),
            autoSave: true,
            cancellationToken: cancellationToken);
    }

    private async Task<AiModelDeployment?> FindDeploymentByAliasAsync(
        string alias,
        CancellationToken cancellationToken)
    {
        var queryable = await _deploymentRepository.GetQueryableAsync();
        return await _asyncExecuter.FirstOrDefaultAsync(
            queryable.Where(x => x.Alias == alias),
            cancellationToken);
    }

    private async Task<AiModelUsageBinding?> FindBindingAsync(
        AiModelUsage usage,
        CancellationToken cancellationToken)
    {
        var queryable = await _bindingRepository.GetQueryableAsync();
        return await _asyncExecuter.FirstOrDefaultAsync(
            queryable.Where(x => x.Usage == usage && x.IsActive),
            cancellationToken);
    }

    private async Task<AiModelHealthSnapshot?> GetLatestHealthAsync(
        Guid deploymentId,
        CancellationToken cancellationToken)
    {
        var queryable = await _healthRepository.GetQueryableAsync();
        return await _asyncExecuter.FirstOrDefaultAsync(
            queryable
                .Where(x => x.DeploymentId == deploymentId)
                .OrderByDescending(x => x.CheckedTime),
            cancellationToken);
    }

    private static void EnsureUsageCapability(AiModelUsage usage, AiModelCapability capability)
    {
        var expected = usage == AiModelUsage.KnowledgeEmbedding
            ? AiModelCapability.Embedding
            : AiModelCapability.Chat;

        if (capability != expected)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.AiModelDeploymentCapabilityMismatch)
                .WithData("Usage", usage.ToString())
                .WithData("Expected", expected.ToString())
                .WithData("Actual", capability.ToString());
        }
    }

    private static string ReadOption(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
