using Volo.Abp.Modularity;

namespace KnowPulse;

[DependsOn(
    typeof(KnowPulseDomainModule),
    typeof(KnowPulseTestBaseModule)
)]
public class KnowPulseDomainTestModule : AbpModule
{

}
