using System;
using System.Reflection;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.RagAI;

public class PrivateKnowledgeInternalBoundaryTests
{
    [Theory]
    [InlineData(typeof(KnowledgeIngestionPipeline))]
    [InlineData(typeof(KnowledgeEmbeddingPipeline))]
    [InlineData(typeof(KnowledgeRetriever))]
    [InlineData(typeof(KnowledgeSourceAccessGuard))]
    [InlineData(typeof(KnowPulse.KnowledgeBase.PersonalKnowledgeExportProcessor))]
    public void Internal_Pipelines_Must_Not_Be_Exposed_By_Conventional_Controllers(Type serviceType)
    {
        // These services may run as a background worker; authorized app services/controllers own the HTTP boundary.
        var remoteService = serviceType.GetCustomAttribute<RemoteServiceAttribute>();
        remoteService.ShouldNotBeNull();
        remoteService.IsEnabled.ShouldBeFalse();
    }
}
