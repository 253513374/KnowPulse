using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.AiModelManagement;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

/// <summary>
/// AI 模型目录、用途绑定和健康检查集成测试。
/// </summary>
[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class AiModelManagementAppServiceTests : KnowPulseEntityFrameworkCoreTestBase
{
    private readonly IAiModelResolver _resolver;
    private readonly IAiModelCatalogAppService _catalogAppService;

    public AiModelManagementAppServiceTests()
    {
        _resolver = GetRequiredService<IAiModelResolver>();
        _catalogAppService = GetRequiredService<IAiModelCatalogAppService>();
    }

    [Fact]
    public async Task Resolver_Should_Seed_And_Return_Default_Models_By_Usage()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var ragChat = await _resolver.ResolveAsync(AiModelUsage.RagAnswerChat);
            ragChat.Provider.ShouldBe("LiteLLM");
            ragChat.Alias.ShouldBe("knowpulse-chat-default");
            ragChat.BusinessModelVersion.ShouldBe("2026-06-mvp1");
            ragChat.Capability.ShouldBe(AiModelCapability.Chat);

            var aiQuestion = await _resolver.ResolveAsync(AiModelUsage.AiQuestionGeneration);
            aiQuestion.Alias.ShouldBe("knowpulse-chat-default");
            aiQuestion.BusinessModelVersion.ShouldBe("2026-06-mvp2-questioning");

            var embedding = await _resolver.ResolveEmbeddingAsync();
            embedding.Alias.ShouldBe("knowpulse-embedding-default");
            embedding.BusinessModelVersion.ShouldBe("2026-06-mvp1");
            embedding.EmbeddingDimension.ShouldBe(1536);
            embedding.FallbackAliases.ShouldContain("knowpulse-embedding-fallback");
        });
    }

    [Fact]
    public async Task SetUsageBindingAsync_Should_Reject_Disabled_Model()
    {
        var disabled = await _catalogAppService.CreateDeploymentAsync(new CreateUpdateAiModelDeploymentDto
        {
            Alias = "knowpulse-chat-disabled-test",
            ProviderType = AiModelProviderType.LiteLLM,
            Capability = AiModelCapability.Chat,
            UpstreamModelName = "disabled/upstream",
            ModelVersion = "2026-07-disabled",
            ApiKeyEnvironmentVariable = "KNOWPULSE_TEST_DISABLED_KEY",
            IsActive = false
        });

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _catalogAppService.SetUsageBindingAsync(new SetAiModelUsageBindingDto
            {
                Usage = AiModelUsage.RagAnswerChat,
                DeploymentId = disabled.Id,
                BusinessModelVersion = "2026-07-disabled",
                MaxOutputTokens = 128,
                Temperature = 0.1
            }));

        exception.Code.ShouldBe(KnowPulseDomainErrorCodes.AiModelDeploymentInactive);
    }

    [Fact]
    public async Task SetUsageBindingAsync_Should_Block_Embedding_Dimension_Change()
    {
        var overview = await _catalogAppService.GetOverviewAsync();
        var currentEmbedding = overview.Deployments.Single(x => x.Alias == "knowpulse-embedding-default");
        var nextEmbedding = await _catalogAppService.CreateDeploymentAsync(new CreateUpdateAiModelDeploymentDto
        {
            Alias = "knowpulse-embedding-3072-test",
            ProviderType = AiModelProviderType.LiteLLM,
            Capability = AiModelCapability.Embedding,
            UpstreamModelName = "openai/text-embedding-3-large",
            ModelVersion = "2026-07-embedding-3072",
            ApiKeyEnvironmentVariable = "KNOWPULSE_TEST_EMBEDDING_KEY",
            EmbeddingDimension = (currentEmbedding.EmbeddingDimension ?? 1536) + 1536,
            IsActive = true
        });

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            _catalogAppService.SetUsageBindingAsync(new SetAiModelUsageBindingDto
            {
                Usage = AiModelUsage.KnowledgeEmbedding,
                DeploymentId = nextEmbedding.Id,
                BusinessModelVersion = "2026-07-embedding-3072",
                MaxOutputTokens = 0,
                Temperature = 0
            }));

        exception.Code.ShouldBe(KnowPulseDomainErrorCodes.AiModelEmbeddingSwitchRequiresRebuild);
    }

    [Fact]
    public async Task TestDeploymentAsync_Should_Return_MissingKey_When_Environment_Variable_Is_Not_Configured()
    {
        var envName = "KNOWPULSE_TEST_MISSING_KEY_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(envName, null);

        var deployment = await _catalogAppService.CreateDeploymentAsync(new CreateUpdateAiModelDeploymentDto
        {
            Alias = "knowpulse-chat-missing-key-test-" + Guid.NewGuid().ToString("N")[..8],
            ProviderType = AiModelProviderType.LiteLLM,
            Capability = AiModelCapability.Chat,
            UpstreamModelName = "missing/key-model",
            ModelVersion = "2026-07-missing-key",
            ApiKeyEnvironmentVariable = envName,
            MaxOutputTokens = 16,
            IsActive = true
        });

        var health = await _catalogAppService.TestDeploymentAsync(deployment.Id);

        health.Status.ShouldBe(AiModelHealthStatus.MissingKey);
        health.DeploymentAlias.ShouldBe(deployment.Alias);
        health.FailureReason.ShouldNotBeNull();
        health.FailureReason.ShouldContain(envName);
    }
}
