using System;
using KnowPulse.RagAI;
using Shouldly;
using Xunit;

namespace KnowPulse.RagAI;

public class Mvp1RagEvaluationTests
{
    [Fact]
    public void Complete_Should_Fail_Run_When_Unauthorized_Recall_Detected()
    {
        var run = new RagEvaluationRun(
            Guid.NewGuid(),
            null,
            "RAG evaluation",
            Guid.NewGuid(),
            DateTime.UtcNow);

        run.Complete(
            DateTime.UtcNow.AddMinutes(1),
            totalCaseCount: 1,
            passedCaseCount: 0,
            failedCaseCount: 1,
            hitRate: 1,
            citationAccuracyRate: 1,
            noAnswerRejectionRate: 1,
            unauthorizedRecallRate: 1,
            unauthorizedRecallCount: 1,
            hardFailed: true,
            failureReason: "Unauthorized recall detected.");

        run.Status.ShouldBe(RagEvaluationRunStatus.Failed);
        run.HardFailed.ShouldBeTrue();
        run.UnauthorizedRecallRate.ShouldBe(1);
        run.UnauthorizedRecallCount.ShouldBe(1);
    }

    [Fact]
    public void Complete_Should_Pass_When_All_Metrics_Pass_And_No_Hard_Failure()
    {
        var run = new RagEvaluationRun(
            Guid.NewGuid(),
            null,
            "RAG evaluation",
            Guid.NewGuid(),
            DateTime.UtcNow);

        run.Complete(
            DateTime.UtcNow.AddMinutes(1),
            totalCaseCount: 2,
            passedCaseCount: 2,
            failedCaseCount: 0,
            hitRate: 1,
            citationAccuracyRate: 1,
            noAnswerRejectionRate: 1,
            unauthorizedRecallRate: 0,
            unauthorizedRecallCount: 0,
            hardFailed: false,
            failureReason: null);

        run.Status.ShouldBe(RagEvaluationRunStatus.Passed);
        run.HardFailed.ShouldBeFalse();
        run.FailureReason.ShouldBeNull();
    }
}
