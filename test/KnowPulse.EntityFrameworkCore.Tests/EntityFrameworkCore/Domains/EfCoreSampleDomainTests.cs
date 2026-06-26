using KnowPulse.Samples;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Domains;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class EfCoreSampleDomainTests : SampleDomainTests<KnowPulseEntityFrameworkCoreTestModule>
{

}
