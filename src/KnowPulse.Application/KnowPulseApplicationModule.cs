using Volo.Abp.Account;
using Volo.Abp.Mapperly;
using KnowPulse.RagAI;
using Volo.Abp.BlobStoring;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SettingManagement;
using Volo.Abp.TenantManagement;
using Microsoft.Extensions.DependencyInjection;

namespace KnowPulse;

[DependsOn(
    typeof(KnowPulseDomainModule),
    typeof(AbpBlobStoringModule),
    typeof(AbpAccountApplicationModule),
    typeof(KnowPulseApplicationContractsModule),
    typeof(AbpIdentityApplicationModule),
    typeof(AbpPermissionManagementApplicationModule),
    typeof(AbpTenantManagementApplicationModule),
    typeof(AbpFeatureManagementApplicationModule),
    typeof(AbpSettingManagementApplicationModule)
    )]
public class KnowPulseApplicationModule : AbpModule
{
    /// <summary>
    /// 注册应用层 AutoMapper、RAG options 和 MVP-1 RAG 服务实现。
    /// </summary>
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        context.Services.Configure<RagAiOptions>(configuration.GetSection(RagAiOptions.SectionName));
        context.Services.AddTransient<IKnowledgeEmbeddingClient, LiteLlmKnowledgeEmbeddingClient>();
        context.Services.AddTransient<IRagChatClient, LiteLlmRagChatClient>();
        context.Services.AddTransient<IKnowledgeVectorStore, QdrantKnowledgeVectorStore>();
        context.Services.AddTransient<IKnowledgeVectorStoreMaintenance, QdrantKnowledgeVectorStore>();
        context.Services.AddTransient<IKnowledgeRetriever, KnowledgeRetriever>();
        context.Services.AddTransient<IRagAnswerService, RagAnswerService>();
        context.Services.AddMapperlyObjectMapper<KnowPulseApplicationModule>();
    }
}
