using Volo.Abp.Account;
using Volo.Abp.Mapperly;
using KnowPulse.AiModelManagement;
using KnowPulse.RagAI;
using Volo.Abp.BlobStoring;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SettingManagement;
using Volo.Abp.TenantManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using KnowPulse.Teaching;

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
        context.Services.Configure<WeChatMiniProgramOptions>(configuration.GetSection(WeChatMiniProgramOptions.SectionName));
        context.Services.Configure<RagAiOptions>(configuration.GetSection(RagAiOptions.SectionName));
        context.Services.AddTransient<IKnowledgeEmbeddingClient, LiteLlmKnowledgeEmbeddingClient>();
        context.Services.AddTransient<IRagChatClient, LiteLlmRagChatClient>();
        // 显式使用生产构造函数，避免 DI 选中接受未配置 HttpClient 的测试构造函数。
        context.Services.AddTransient<IKnowledgeVectorStore>(provider =>
            new QdrantKnowledgeVectorStore(provider.GetRequiredService<IOptions<RagAiOptions>>()));
        context.Services.AddTransient<IKnowledgeVectorStoreMaintenance>(provider =>
            new QdrantKnowledgeVectorStore(provider.GetRequiredService<IOptions<RagAiOptions>>()));
        context.Services.AddTransient<IKnowledgeDocumentMarkdownConverter, MarkItDownKnowledgeDocumentMarkdownConverter>();
        context.Services.AddTransient<IAiModelResolver, AiModelResolver>();
        context.Services.AddTransient<IKnowledgeRetriever, KnowledgeRetriever>();
        context.Services.AddTransient<IRagAnswerService, RagAnswerService>();
        context.Services.AddMapperlyObjectMapper<KnowPulseApplicationModule>();
    }
}
