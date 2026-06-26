using Volo.Abp.Account;
using Volo.Abp.Mapperly;
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
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddMapperlyObjectMapper<KnowPulseApplicationModule>();
    }
}
