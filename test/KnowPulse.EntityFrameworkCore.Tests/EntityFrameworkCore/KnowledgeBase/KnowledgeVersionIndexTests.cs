using System;
using System.Threading.Tasks;
using KnowPulse.KnowledgeBase;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.KnowledgeBase;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class KnowledgeVersionIndexTests : KnowPulseEntityFrameworkCoreTestBase
{
    [Fact]
    public async Task Should_Reject_Multiple_Current_Published_Versions_For_Same_Item()
    {
        using var scope = ServiceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<KnowPulseDbContext>();

        var item = new KnowledgeItem(Guid.NewGuid(), null, "Policy");
        var firstVersion = new KnowledgeVersion(Guid.NewGuid(), null, item.Id, 1, "v1");
        var secondVersion = new KnowledgeVersion(Guid.NewGuid(), null, item.Id, 2, "v2");

        firstVersion.Publish(DateTime.UtcNow);
        secondVersion.Publish(DateTime.UtcNow);

        dbContext.KnowledgeItems.Add(item);
        dbContext.KnowledgeVersions.AddRange(firstVersion, secondVersion);

        await Should.ThrowAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }
}
