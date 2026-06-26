using Xunit;

namespace KnowPulse.EntityFrameworkCore;

[CollectionDefinition(KnowPulseTestConsts.CollectionDefinitionName)]
public class KnowPulseEntityFrameworkCoreCollection : ICollectionFixture<KnowPulseEntityFrameworkCoreFixture>
{

}
