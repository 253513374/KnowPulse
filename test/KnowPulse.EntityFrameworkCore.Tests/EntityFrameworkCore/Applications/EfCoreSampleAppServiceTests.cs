using KnowPulse.Samples;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class EfCoreSampleAppServiceTests : SampleAppServiceTests<KnowPulseEntityFrameworkCoreTestModule>
{

}
