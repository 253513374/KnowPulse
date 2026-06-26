using Volo.Abp.Modularity;

namespace KnowPulse;

[DependsOn(
    typeof(KnowPulseApplicationModule),
    typeof(KnowPulseDomainTestModule)
)]
public class KnowPulseApplicationTestModule : AbpModule
{

}
