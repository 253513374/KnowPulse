using KnowPulse.EntityFrameworkCore;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;

namespace KnowPulse.DbMigrator;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(KnowPulseEntityFrameworkCoreModule),
    typeof(KnowPulseApplicationContractsModule)
    )]
public class KnowPulseDbMigratorModule : AbpModule
{
}
