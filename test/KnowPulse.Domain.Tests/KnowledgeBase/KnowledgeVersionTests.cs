using System;
using KnowPulse.KnowledgeBase;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.KnowledgeBase;

public class KnowledgeVersionTests
{
    [Fact]
    public void Should_Start_As_Draft()
    {
        var version = new KnowledgeVersion(Guid.NewGuid(), null, Guid.NewGuid(), 1, "Policy");

        version.Status.ShouldBe(KnowledgeVersionStatus.Draft);
        version.IsCurrentPublished.ShouldBeFalse();
    }

    [Fact]
    public void Should_Publish_And_Withdraw()
    {
        var now = DateTime.UtcNow;
        var version = new KnowledgeVersion(Guid.NewGuid(), null, Guid.NewGuid(), 1, "Policy");

        version.Publish(now);

        version.Status.ShouldBe(KnowledgeVersionStatus.Published);
        version.PublishedTime.ShouldBe(now);
        version.IsCurrentPublished.ShouldBeTrue();

        version.Withdraw(now.AddMinutes(1));

        version.Status.ShouldBe(KnowledgeVersionStatus.Withdrawn);
        version.IsCurrentPublished.ShouldBeFalse();
    }

    [Fact]
    public void Should_Not_Publish_Archived_Version()
    {
        var version = new KnowledgeVersion(Guid.NewGuid(), null, Guid.NewGuid(), 1, "Policy");
        version.Archive(DateTime.UtcNow);

        var exception = Should.Throw<BusinessException>(() => version.Publish(DateTime.UtcNow));

        exception.Code.ShouldBe(KnowPulseDomainErrorCodes.KnowledgeVersionAlreadyArchived);
    }
}
