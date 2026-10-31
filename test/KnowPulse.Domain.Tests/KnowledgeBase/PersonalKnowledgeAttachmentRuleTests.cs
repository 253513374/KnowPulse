using System;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.KnowledgeBase;

public class PersonalKnowledgeAttachmentRuleTests
{
    [Fact]
    public void Draft_Should_Allow_Attachment_Changes()
    {
        var version = new KnowledgeVersion(Guid.NewGuid(), null, Guid.NewGuid(), 1, "Draft");
        version.EnsureCanChangePersonalAttachments();
    }

    [Theory]
    [InlineData(KnowledgeVersionStatus.Published)]
    [InlineData(KnowledgeVersionStatus.Withdrawn)]
    [InlineData(KnowledgeVersionStatus.Archived)]
    [InlineData(KnowledgeVersionStatus.Disabled)]
    public void Non_Draft_Should_Reject_Attachment_Changes(KnowledgeVersionStatus status)
    {
        var version = new KnowledgeVersion(Guid.NewGuid(), null, Guid.NewGuid(), 1, "Frozen");
        version.Publish(DateTime.UtcNow);
        switch (status)
        {
            case KnowledgeVersionStatus.Withdrawn: version.Withdraw(DateTime.UtcNow); break;
            case KnowledgeVersionStatus.Archived: version.Archive(DateTime.UtcNow); break;
            case KnowledgeVersionStatus.Disabled: version.Disable(DateTime.UtcNow); break;
        }
        Should.Throw<BusinessException>(() => version.EnsureCanChangePersonalAttachments())
            .Code.ShouldBe(KnowPulseDomainErrorCodes.PersonalKnowledgeAttachmentRequiresDraft);
    }
}
