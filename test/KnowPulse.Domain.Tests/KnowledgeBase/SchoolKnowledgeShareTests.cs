using System;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.KnowledgeBase;

public class SchoolKnowledgeShareTests
{
    [Fact]
    public void Uses_Should_Require_Read_And_Reject_Unknown_Bits()
    {
        foreach (var uses in new[] { (SchoolKnowledgeShareUses)0, SchoolKnowledgeShareUses.Retrieve,
                     SchoolKnowledgeShareUses.Read | SchoolKnowledgeShareUses.GenerateQuestions, (SchoolKnowledgeShareUses)65 })
            Should.Throw<BusinessException>(() => Create(uses)).Code.ShouldBe(SchoolKnowledgeSharingErrorCodes.InvalidUses);

        var share = Create(SchoolKnowledgeShareUses.Read | SchoolKnowledgeShareUses.Retrieve);
        share.Allows(SchoolKnowledgeShareUses.Read).ShouldBeTrue();
        share.Allows(SchoolKnowledgeShareUses.Retrieve).ShouldBeTrue();
        share.Allows(SchoolKnowledgeShareUses.GenerateQuestions).ShouldBeFalse();
        share.Allows(SchoolKnowledgeShareUses.Download).ShouldBeFalse();
        share.Allows((SchoolKnowledgeShareUses)0).ShouldBeFalse();
    }

    [Fact]
    public void Revoke_Should_Keep_The_First_Receipt_And_Stop_Uses()
    {
        var share = Create(SchoolKnowledgeShareUses.Read | SchoolKnowledgeShareUses.Download);
        var first = share.SharedAtUtc.AddMinutes(1);
        share.Revoke(first);
        share.Revoke(first.AddMinutes(1));
        share.IsRevoked.ShouldBeTrue();
        share.RevokedAtUtc.ShouldBe(first);
        share.Allows(SchoolKnowledgeShareUses.Read).ShouldBeFalse();
        share.Allows(SchoolKnowledgeShareUses.Download).ShouldBeFalse();
        share.Title.ShouldBe("选定版本标题");
        share.Summary.ShouldBe("共享时摘要");
    }

    [Fact]
    public void Share_Should_Validate_Identifiers_And_Utc_Times()
    {
        Should.Throw<ArgumentException>(() => new SchoolKnowledgeShare(Guid.Empty, null, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "标题", null, SchoolKnowledgeShareUses.Read, DateTime.UtcNow));
        Should.Throw<ArgumentException>(() => new SchoolKnowledgeShare(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "标题", null, SchoolKnowledgeShareUses.Read, DateTime.Now));
        var share = Create(SchoolKnowledgeShareUses.Read);
        Should.Throw<ArgumentException>(() => share.Revoke(DateTime.Now));
        Should.Throw<ArgumentOutOfRangeException>(() => share.Revoke(share.SharedAtUtc.AddSeconds(-1)));
    }

    private static SchoolKnowledgeShare Create(SchoolKnowledgeShareUses uses) => new(Guid.NewGuid(), null,
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "选定版本标题", "共享时摘要", uses, DateTime.UtcNow);
}
