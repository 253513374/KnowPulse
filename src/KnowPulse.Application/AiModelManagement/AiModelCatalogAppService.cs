using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using KnowPulse.RagAI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace KnowPulse.AiModelManagement;

/// <summary>
/// 模型目录、用途绑定和健康检查应用服务。
/// </summary>
[Authorize(KnowPulsePermissions.AiModels.Default)]
public class AiModelCatalogAppService : KnowPulseAppService, IAiModelCatalogAppService
{
    private readonly IRepository<AiModelDeployment, Guid> _deploymentRepository;
    private readonly IRepository<AiModelUsageBinding, Guid> _bindingRepository;
    private readonly IRepository<AiModelHealthSnapshot, Guid> _healthRepository;
    private readonly IAiModelResolver _resolver;
    private readonly RagAiOptions _options;

    public AiModelCatalogAppService(
        IRepository<AiModelDeployment, Guid> deploymentRepository,
        IRepository<AiModelUsageBinding, Guid> bindingRepository,
        IRepository<AiModelHealthSnapshot, Guid> healthRepository,
        IAiModelResolver resolver,
        IOptions<RagAiOptions> options)
    {
        _deploymentRepository = deploymentRepository;
        _bindingRepository = bindingRepository;
        _healthRepository = healthRepository;
        _resolver = resolver;
        _options = options.Value;
    }

    public virtual async Task<AiModelCatalogOverviewDto> GetOverviewAsync()
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.AiModels.View);
        await EnsureSeedAsync();

        var deployments = await GetAllDeploymentsAsync();
        var bindings = await GetAllBindingsAsync();
        var latestHealth = await GetLatestHealthLookupAsync(deployments.Select(x => x.Id));

        return new AiModelCatalogOverviewDto
        {
            Deployments = deployments.Select(x => MapDeploymentToDto(x, latestHealth.GetValueOrDefault(x.Id))).ToList(),
            Bindings = bindings.Select(x => MapBindingToDto(x, deployments)).ToList(),
            HealthSnapshots = latestHealth.Values.Select(x => MapHealthToDto(x, deployments)).ToList(),
            LiteLlmBaseUrl = _options.LiteLlm.BaseUrl,
            LiteLlmAdminUrl = BuildLiteLlmAdminUrl(_options.LiteLlm.BaseUrl)
        };
    }

    public virtual async Task<PagedResultDto<AiModelDeploymentDto>> GetDeploymentsAsync(GetAiModelDeploymentListInput input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.AiModels.View);
        await EnsureSeedAsync();

        var queryable = await _deploymentRepository.GetQueryableAsync();
        if (!string.IsNullOrWhiteSpace(input.Keyword))
        {
            var keyword = input.Keyword.Trim();
            queryable = queryable.Where(x =>
                x.Alias.Contains(keyword) ||
                x.UpstreamModelName.Contains(keyword) ||
                (x.Description != null && x.Description.Contains(keyword)));
        }

        if (input.Capability.HasValue)
        {
            queryable = queryable.Where(x => x.Capability == input.Capability.Value);
        }

        if (input.ProviderType.HasValue)
        {
            queryable = queryable.Where(x => x.ProviderType == input.ProviderType.Value);
        }

        if (input.IsActive.HasValue)
        {
            queryable = queryable.Where(x => x.IsActive == input.IsActive.Value);
        }

        var totalCount = await AsyncExecuter.CountAsync(queryable);
        var deployments = await AsyncExecuter.ToListAsync(queryable
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Capability)
            .ThenBy(x => x.Alias)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount));
        var latestHealth = await GetLatestHealthLookupAsync(deployments.Select(x => x.Id));

        return new PagedResultDto<AiModelDeploymentDto>(
            totalCount,
            deployments.Select(x => MapDeploymentToDto(x, latestHealth.GetValueOrDefault(x.Id))).ToList());
    }

    public virtual async Task<AiModelDeploymentDto> GetDeploymentAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.AiModels.View);
        await EnsureSeedAsync();

        var deployment = await _deploymentRepository.GetAsync(id);
        var latestHealth = await GetLatestHealthAsync(id);
        return MapDeploymentToDto(deployment, latestHealth);
    }

    public virtual async Task<AiModelDeploymentDto> CreateDeploymentAsync(CreateUpdateAiModelDeploymentDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.AiModels.Manage);

        if (await FindDeploymentByAliasAsync(input.Alias) != null)
        {
            throw new UserFriendlyException($"Model alias '{input.Alias}' already exists.");
        }

        var deployment = new AiModelDeployment(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            input.Alias,
            input.ProviderType,
            input.Capability,
            input.UpstreamModelName,
            input.ModelVersion,
            input.ApiKeyEnvironmentVariable,
            input.BaseUrl,
            input.ContextWindowTokens,
            input.MaxOutputTokens,
            input.EmbeddingDimension,
            input.PromptTokenPriceUsd,
            input.CompletionTokenPriceUsd,
            input.IsActive,
            input.Description);

        await _deploymentRepository.InsertAsync(deployment, autoSave: true);
        return MapDeploymentToDto(deployment, null);
    }

    public virtual async Task<AiModelDeploymentDto> UpdateDeploymentAsync(Guid id, CreateUpdateAiModelDeploymentDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.AiModels.Manage);

        var duplicate = await FindDeploymentByAliasAsync(input.Alias);
        if (duplicate != null && duplicate.Id != id)
        {
            throw new UserFriendlyException($"Model alias '{input.Alias}' already exists.");
        }

        var deployment = await _deploymentRepository.GetAsync(id);
        deployment.Update(
            input.Alias,
            input.ProviderType,
            input.Capability,
            input.UpstreamModelName,
            input.ModelVersion,
            input.ApiKeyEnvironmentVariable,
            input.BaseUrl,
            input.ContextWindowTokens,
            input.MaxOutputTokens,
            input.EmbeddingDimension,
            input.PromptTokenPriceUsd,
            input.CompletionTokenPriceUsd,
            input.Description);

        if (input.IsActive)
        {
            deployment.Activate();
        }
        else
        {
            await EnsureCanDisableAsync(deployment.Id);
            deployment.Deactivate();
        }

        await _deploymentRepository.UpdateAsync(deployment, autoSave: true);
        return MapDeploymentToDto(deployment, await GetLatestHealthAsync(id));
    }

    public virtual async Task<AiModelDeploymentDto> SetDeploymentActiveAsync(Guid id, bool isActive)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.AiModels.Manage);

        var deployment = await _deploymentRepository.GetAsync(id);
        if (isActive)
        {
            deployment.Activate();
        }
        else
        {
            await EnsureCanDisableAsync(id);
            deployment.Deactivate();
        }

        await _deploymentRepository.UpdateAsync(deployment, autoSave: true);
        return MapDeploymentToDto(deployment, await GetLatestHealthAsync(id));
    }

    public virtual async Task<AiModelUsageBindingDto> SetUsageBindingAsync(SetAiModelUsageBindingDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.AiModels.SwitchDefaults);
        await EnsureSeedAsync();

        var deployment = await _deploymentRepository.GetAsync(input.DeploymentId);
        if (!deployment.IsActive)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.AiModelDeploymentInactive)
                .WithData("Alias", deployment.Alias);
        }

        EnsureUsageCapability(input.Usage, deployment.Capability);
        await EnsureEmbeddingSwitchIsSafeAsync(input, deployment);

        var fallbackAliases = AiModelJsonHelper.SerializeFallbackAliases(input.FallbackAliases);
        var existing = await FindBindingByUsageAsync(input.Usage);
        if (existing == null)
        {
            existing = await _bindingRepository.InsertAsync(
                new AiModelUsageBinding(
                    GuidGenerator.Create(),
                    CurrentTenant.Id,
                    input.Usage,
                    input.DeploymentId,
                    input.BusinessModelVersion,
                    Math.Max(0, input.MaxOutputTokens),
                    Math.Max(0, input.Temperature),
                    fallbackAliases,
                    isActive: true),
                autoSave: true);
        }
        else
        {
            existing.Update(
                input.DeploymentId,
                input.BusinessModelVersion,
                Math.Max(0, input.MaxOutputTokens),
                Math.Max(0, input.Temperature),
                fallbackAliases);
            existing.Activate();
            await _bindingRepository.UpdateAsync(existing, autoSave: true);
        }

        return MapBindingToDto(existing, new[] { deployment });
    }

    public virtual async Task<AiModelHealthSnapshotDto> TestDeploymentAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.AiModels.Test);

        var deployment = await _deploymentRepository.GetAsync(id);
        var snapshot = await ExecuteHealthCheckAsync(deployment, CancellationToken.None);
        return MapHealthToDto(snapshot, new[] { deployment });
    }

    public virtual async Task<IReadOnlyList<AiModelHealthSnapshotDto>> TestAllAsync()
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.AiModels.Test);
        await EnsureSeedAsync();

        var deployments = await GetAllDeploymentsAsync();
        var result = new List<AiModelHealthSnapshotDto>();
        foreach (var deployment in deployments)
        {
            var snapshot = await ExecuteHealthCheckAsync(deployment, CancellationToken.None);
            result.Add(MapHealthToDto(snapshot, deployments));
        }

        return result;
    }

    private async Task<AiModelHealthSnapshot> ExecuteHealthCheckAsync(
        AiModelDeployment deployment,
        CancellationToken cancellationToken)
    {
        if (!deployment.IsActive)
        {
            return await InsertHealthSnapshotAsync(
                deployment,
                AiModelHealthStatus.Disabled,
                latencyMs: null,
                failureReason: "Model deployment is disabled.",
                gatewayModelInfoJson: null,
                cancellationToken);
        }

        var envName = ReadOption(deployment.ApiKeyEnvironmentVariable, _options.LiteLlm.ApiKeyEnvironmentVariable);
        var apiKey = Environment.GetEnvironmentVariable(envName);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return await InsertHealthSnapshotAsync(
                deployment,
                AiModelHealthStatus.MissingKey,
                latencyMs: null,
                failureReason: $"API key environment variable '{envName}' is not configured.",
                gatewayModelInfoJson: null,
                cancellationToken);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var httpClient = CreateLiteLlmHttpClient(deployment, apiKey);
            var modelInfo = await ReadModelInfoAsync(httpClient, cancellationToken);
            var failureReason = deployment.Capability == AiModelCapability.Embedding
                ? await TestEmbeddingAsync(httpClient, deployment, cancellationToken)
                : await TestChatAsync(httpClient, deployment, cancellationToken);

            return await InsertHealthSnapshotAsync(
                deployment,
                failureReason == null ? AiModelHealthStatus.Healthy : AiModelHealthStatus.Failed,
                ToElapsedMilliseconds(stopwatch),
                failureReason,
                modelInfo,
                cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return await InsertHealthSnapshotAsync(
                deployment,
                AiModelHealthStatus.Failed,
                ToElapsedMilliseconds(stopwatch),
                "LiteLLM health check failed: " + ex.GetType().Name,
                gatewayModelInfoJson: null,
                cancellationToken);
        }
    }

    private HttpClient CreateLiteLlmHttpClient(AiModelDeployment deployment, string apiKey)
    {
        var baseUrl = ReadOption(deployment.BaseUrl, _options.LiteLlm.BaseUrl);
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(Math.Max(1, _options.LiteLlm.TimeoutSeconds))
        };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return httpClient;
    }

    private static async Task<string?> ReadModelInfoAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("model/info", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var value = await response.Content.ReadAsStringAsync(cancellationToken);
        return TrimInline(value, AiModelConsts.MaxGatewayModelInfoJsonLength);
    }

    private static async Task<string?> TestChatAsync(
        HttpClient httpClient,
        AiModelDeployment deployment,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            model = deployment.Alias,
            messages = new object[]
            {
                new { role = "system", content = "You are a health check endpoint." },
                new { role = "user", content = "Return ok." }
            },
            temperature = 0,
            max_tokens = Math.Max(1, deployment.MaxOutputTokens ?? 8)
        });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await httpClient.PostAsync("v1/chat/completions", content, cancellationToken);
        return response.IsSuccessStatusCode
            ? null
            : $"LiteLLM chat health check failed with HTTP {(int)response.StatusCode}.";
    }

    private static async Task<string?> TestEmbeddingAsync(
        HttpClient httpClient,
        AiModelDeployment deployment,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            model = deployment.Alias,
            input = "KnowPulse model health check."
        });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await httpClient.PostAsync("embeddings", content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return $"LiteLLM embedding health check failed with HTTP {(int)response.StatusCode}.";
        }

        if (!deployment.EmbeddingDimension.HasValue)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var actualDimension = document.RootElement
            .GetProperty("data")
            .EnumerateArray()
            .First()
            .GetProperty("embedding")
            .GetArrayLength();

        return actualDimension == deployment.EmbeddingDimension.Value
            ? null
            : $"Embedding dimension mismatch. Expected {deployment.EmbeddingDimension.Value}, actual {actualDimension}.";
    }

    private async Task<AiModelHealthSnapshot> InsertHealthSnapshotAsync(
        AiModelDeployment deployment,
        AiModelHealthStatus status,
        int? latencyMs,
        string? failureReason,
        string? gatewayModelInfoJson,
        CancellationToken cancellationToken)
    {
        return await _healthRepository.InsertAsync(
            new AiModelHealthSnapshot(
                GuidGenerator.Create(),
                CurrentTenant.Id,
                deployment.Id,
                status,
                DateTime.UtcNow,
                latencyMs,
                TrimInline(failureReason, AiModelConsts.MaxFailureReasonLength),
                gatewayModelInfoJson),
            autoSave: true,
            cancellationToken: cancellationToken);
    }

    private async Task EnsureSeedAsync()
    {
        await _resolver.ResolveAsync(AiModelUsage.RagAnswerChat);
    }

    private async Task EnsureCanDisableAsync(Guid deploymentId)
    {
        var queryable = await _bindingRepository.GetQueryableAsync();
        var isBound = await AsyncExecuter.AnyAsync(
            queryable.Where(x => x.DeploymentId == deploymentId && x.IsActive));
        if (isBound)
        {
            throw new UserFriendlyException("Cannot disable a model that is bound as a default business usage.");
        }
    }

    private async Task EnsureEmbeddingSwitchIsSafeAsync(
        SetAiModelUsageBindingDto input,
        AiModelDeployment nextDeployment)
    {
        if (input.Usage != AiModelUsage.KnowledgeEmbedding)
        {
            return;
        }

        var existing = await FindBindingByUsageAsync(AiModelUsage.KnowledgeEmbedding);
        if (existing == null || existing.DeploymentId == nextDeployment.Id)
        {
            return;
        }

        var currentDeployment = await _deploymentRepository.GetAsync(existing.DeploymentId);
        if (currentDeployment.EmbeddingDimension != nextDeployment.EmbeddingDimension)
        {
            throw new BusinessException(KnowPulseDomainErrorCodes.AiModelEmbeddingSwitchRequiresRebuild)
                .WithData("CurrentAlias", currentDeployment.Alias)
                .WithData("NextAlias", nextDeployment.Alias)
                .WithData("CurrentDimension", currentDeployment.EmbeddingDimension?.ToString() ?? "null")
                .WithData("NextDimension", nextDeployment.EmbeddingDimension?.ToString() ?? "null");
        }
    }

    private async Task<AiModelDeployment?> FindDeploymentByAliasAsync(string alias)
    {
        var normalized = alias.Trim();
        var queryable = await _deploymentRepository.GetQueryableAsync();
        return await AsyncExecuter.FirstOrDefaultAsync(queryable.Where(x => x.Alias == normalized));
    }

    private async Task<AiModelUsageBinding?> FindBindingByUsageAsync(AiModelUsage usage)
    {
        var queryable = await _bindingRepository.GetQueryableAsync();
        return await AsyncExecuter.FirstOrDefaultAsync(queryable.Where(x => x.Usage == usage && x.IsActive));
    }

    private async Task<List<AiModelDeployment>> GetAllDeploymentsAsync()
    {
        var queryable = await _deploymentRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(
            queryable
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.Capability)
                .ThenBy(x => x.Alias));
    }

    private async Task<List<AiModelUsageBinding>> GetAllBindingsAsync()
    {
        var queryable = await _bindingRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(
            queryable
                .Where(x => x.IsActive)
                .OrderBy(x => x.Usage));
    }

    private async Task<Dictionary<Guid, AiModelHealthSnapshot>> GetLatestHealthLookupAsync(IEnumerable<Guid> deploymentIds)
    {
        var ids = deploymentIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, AiModelHealthSnapshot>();
        }

        var queryable = await _healthRepository.GetQueryableAsync();
        var snapshots = await AsyncExecuter.ToListAsync(
            queryable
                .Where(x => ids.Contains(x.DeploymentId))
                .OrderByDescending(x => x.CheckedTime));

        return snapshots
            .GroupBy(x => x.DeploymentId)
            .ToDictionary(x => x.Key, x => x.First());
    }

    private async Task<AiModelHealthSnapshot?> GetLatestHealthAsync(Guid deploymentId)
    {
        var queryable = await _healthRepository.GetQueryableAsync();
        return await AsyncExecuter.FirstOrDefaultAsync(
            queryable
                .Where(x => x.DeploymentId == deploymentId)
                .OrderByDescending(x => x.CheckedTime));
    }

    private static AiModelDeploymentDto MapDeploymentToDto(
        AiModelDeployment deployment,
        AiModelHealthSnapshot? latestHealth)
    {
        return new AiModelDeploymentDto
        {
            Id = deployment.Id,
            CreationTime = deployment.CreationTime,
            CreatorId = deployment.CreatorId,
            LastModificationTime = deployment.LastModificationTime,
            LastModifierId = deployment.LastModifierId,
            IsDeleted = deployment.IsDeleted,
            DeleterId = deployment.DeleterId,
            DeletionTime = deployment.DeletionTime,
            TenantId = deployment.TenantId,
            Alias = deployment.Alias,
            ProviderType = deployment.ProviderType,
            Capability = deployment.Capability,
            UpstreamModelName = deployment.UpstreamModelName,
            ModelVersion = deployment.ModelVersion,
            ApiKeyEnvironmentVariable = deployment.ApiKeyEnvironmentVariable,
            BaseUrl = deployment.BaseUrl,
            ContextWindowTokens = deployment.ContextWindowTokens,
            MaxOutputTokens = deployment.MaxOutputTokens,
            EmbeddingDimension = deployment.EmbeddingDimension,
            PromptTokenPriceUsd = deployment.PromptTokenPriceUsd,
            CompletionTokenPriceUsd = deployment.CompletionTokenPriceUsd,
            IsActive = deployment.IsActive,
            Description = deployment.Description,
            HealthStatus = latestHealth?.Status ?? AiModelHealthStatus.Unknown,
            LastCheckedTime = latestHealth?.CheckedTime,
            LastLatencyMs = latestHealth?.LatencyMs,
            LastFailureReason = latestHealth?.FailureReason
        };
    }

    private static AiModelUsageBindingDto MapBindingToDto(
        AiModelUsageBinding binding,
        IReadOnlyCollection<AiModelDeployment> deployments)
    {
        var deployment = deployments.FirstOrDefault(x => x.Id == binding.DeploymentId);
        return new AiModelUsageBindingDto
        {
            Id = binding.Id,
            CreationTime = binding.CreationTime,
            CreatorId = binding.CreatorId,
            LastModificationTime = binding.LastModificationTime,
            LastModifierId = binding.LastModifierId,
            IsDeleted = binding.IsDeleted,
            DeleterId = binding.DeleterId,
            DeletionTime = binding.DeletionTime,
            TenantId = binding.TenantId,
            Usage = binding.Usage,
            DeploymentId = binding.DeploymentId,
            DeploymentAlias = deployment?.Alias ?? string.Empty,
            Capability = deployment?.Capability ?? AiModelCapability.Chat,
            BusinessModelVersion = binding.BusinessModelVersion,
            MaxOutputTokens = binding.MaxOutputTokens,
            Temperature = binding.Temperature,
            FallbackAliases = AiModelJsonHelper.DeserializeFallbackAliases(binding.FallbackAliasesJson),
            IsActive = binding.IsActive
        };
    }

    private static AiModelHealthSnapshotDto MapHealthToDto(
        AiModelHealthSnapshot snapshot,
        IReadOnlyCollection<AiModelDeployment> deployments)
    {
        return new AiModelHealthSnapshotDto
        {
            Id = snapshot.Id,
            CreationTime = snapshot.CreationTime,
            CreatorId = snapshot.CreatorId,
            LastModificationTime = snapshot.LastModificationTime,
            LastModifierId = snapshot.LastModifierId,
            IsDeleted = snapshot.IsDeleted,
            DeleterId = snapshot.DeleterId,
            DeletionTime = snapshot.DeletionTime,
            TenantId = snapshot.TenantId,
            DeploymentId = snapshot.DeploymentId,
            DeploymentAlias = deployments.FirstOrDefault(x => x.Id == snapshot.DeploymentId)?.Alias ?? string.Empty,
            Status = snapshot.Status,
            CheckedTime = snapshot.CheckedTime,
            LatencyMs = snapshot.LatencyMs,
            FailureReason = snapshot.FailureReason,
            GatewayModelInfoJson = snapshot.GatewayModelInfoJson
        };
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

    private static string BuildLiteLlmAdminUrl(string baseUrl)
    {
        return ReadOption(baseUrl, "http://localhost:4000").TrimEnd('/') + "/ui";
    }

    private static string ReadOption(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string? TrimInline(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength].TrimEnd();
    }

    private static int ToElapsedMilliseconds(Stopwatch stopwatch)
    {
        return stopwatch.ElapsedMilliseconds > int.MaxValue
            ? int.MaxValue
            : (int)stopwatch.ElapsedMilliseconds;
    }
}
