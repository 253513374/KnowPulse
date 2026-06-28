using System;
using KnowPulse.KnowledgeBase;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.KnowledgeBase;

public class KnowledgeFeedbackTests
{
    [Fact]
    public void Should_Start_As_Submitted()
    {
        var feedback = CreateFeedback();

        feedback.Status.ShouldBe(KnowledgeFeedbackStatus.Submitted);
        feedback.LinkedKnowledgeVersionId.ShouldBeNull();
        feedback.HandlerUserId.ShouldBeNull();
    }

    [Fact]
    public void Should_Process_Link_Resolve_And_Close()
    {
        var handlerUserId = Guid.NewGuid();
        var linkedVersionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var feedback = CreateFeedback();

        feedback.StartProcessing(handlerUserId, now, "checking");
        feedback.Status.ShouldBe(KnowledgeFeedbackStatus.Processing);
        feedback.HandlerUserId.ShouldBe(handlerUserId);

        feedback.LinkKnowledgeVersion(handlerUserId, now.AddMinutes(1), linkedVersionId, "draft created");
        feedback.LinkedKnowledgeVersionId.ShouldBe(linkedVersionId);
        feedback.Status.ShouldBe(KnowledgeFeedbackStatus.Processing);

        feedback.Resolve(handlerUserId, now.AddMinutes(2), "fixed in draft");
        feedback.Status.ShouldBe(KnowledgeFeedbackStatus.Resolved);
        feedback.Resolution.ShouldBe("fixed in draft");

        feedback.Close(handlerUserId, now.AddMinutes(3), "accepted");
        feedback.Status.ShouldBe(KnowledgeFeedbackStatus.Closed);
        feedback.ClosedTime.ShouldBe(now.AddMinutes(3));
        feedback.CloseReason.ShouldBe("accepted");
    }

    [Fact]
    public void Should_Not_Process_Closed_Feedback()
    {
        var feedback = CreateFeedback();
        var handlerUserId = Guid.NewGuid();
        feedback.Close(handlerUserId, DateTime.UtcNow);

        var exception = Should.Throw<BusinessException>(() =>
            feedback.StartProcessing(handlerUserId, DateTime.UtcNow));

        exception.Code.ShouldBe(KnowPulseDomainErrorCodes.KnowledgeFeedbackCannotProcess);
    }

    [Fact]
    public void Should_Not_Link_Closed_Feedback()
    {
        var feedback = CreateFeedback();
        var handlerUserId = Guid.NewGuid();
        feedback.Close(handlerUserId, DateTime.UtcNow);

        var exception = Should.Throw<BusinessException>(() =>
            feedback.LinkKnowledgeVersion(handlerUserId, DateTime.UtcNow, Guid.NewGuid()));

        exception.Code.ShouldBe(KnowPulseDomainErrorCodes.KnowledgeFeedbackClosed);
    }

    private static KnowledgeFeedback CreateFeedback()
    {
        return new KnowledgeFeedback(
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            KnowledgeFeedbackType.Error,
            "错别字",
            "制度名称写错了");
    }
}
