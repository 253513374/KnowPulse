using Volo.Abp.Modularity;

namespace KnowPulse;

public abstract class KnowPulseApplicationTestBase<TStartupModule> : KnowPulseTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
