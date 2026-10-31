using Localization.Resources.AbpUi;
using KnowPulse.Localization;
using KnowPulse.HttpApi.Education;
using KnowPulse.HttpApi.KnowledgeBase;
using KnowPulse.HttpApi.WeChat;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Account;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Identity;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement.HttpApi;
using Volo.Abp.SettingManagement;
using Volo.Abp.TenantManagement;

namespace KnowPulse;

[DependsOn(
    typeof(KnowPulseApplicationContractsModule),
    typeof(AbpAccountHttpApiModule),
    typeof(AbpIdentityHttpApiModule),
    typeof(AbpPermissionManagementHttpApiModule),
    typeof(AbpTenantManagementHttpApiModule),
    typeof(AbpFeatureManagementHttpApiModule),
    typeof(AbpSettingManagementHttpApiModule)
    )]
public class KnowPulseHttpApiModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        ConfigureLocalization();
        Configure<MvcOptions>(options => options.Filters.Add<EducationStudentGrowthNoStoreFilter>());
        Configure<MvcOptions>(options => options.Filters.Add<KnowledgeResponseNoStoreFilter>());
        Configure<MvcOptions>(options => options.Filters.Add<WeChatAccountNoStoreFilter>());
    }

    private void ConfigureLocalization()
    {
        Configure<AbpLocalizationOptions>(options =>
        {
            options.Resources
                .Get<KnowPulseResource>()
                .AddBaseTypes(
                    typeof(AbpUiResource)
                );
        });
    }
}
