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

    [Fact]
    public void Repeated_Archive_Should_Retain_First_Archive_Time_And_Original_Content()
    {
        var now = DateTime.UtcNow;
        var version = new KnowledgeVersion(Guid.NewGuid(), null, Guid.NewGuid(), 1, "教学反思", "多年教学积累");
        version.Publish(now.AddDays(-1));

        version.Archive(now);
        version.Archive(now.AddDays(1));

        version.ArchivedTime.ShouldBe(now);
        version.Status.ShouldBe(KnowledgeVersionStatus.Archived);
        version.IsCurrentPublished.ShouldBeFalse();
        version.Title.ShouldBe("教学反思");
        version.ContentText.ShouldBe("多年教学积累");
        Should.Throw<BusinessException>(() => version.UpdateDraft("修改历史", "修改历史"))
            .Code.ShouldBe(KnowPulseDomainErrorCodes.KnowledgeVersionAlreadyArchived);
    }
}
