using System;
using KnowPulse.QuestionBank;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.AiQuestioning;

public class AiQuestionGenerationRecoveryTests
{
    [Fact]
    public void Lease_Should_Reject_Active_Claim_And_Block_Expired_Worker_After_Recovery()
    {
        var task = Task();
        var now = DateTime.UtcNow;
        var first = Guid.NewGuid();
        task.TryClaim(first, now, 30, 3).ShouldBeTrue();
        task.TryClaim(Guid.NewGuid(), now.AddSeconds(29), 30, 3).ShouldBeFalse();
        task.Heartbeat(first, now.AddSeconds(20));
        task.TryClaim(Guid.NewGuid(), now.AddSeconds(40), 30, 3).ShouldBeFalse();
        var second = Guid.NewGuid();
        task.TryClaim(second, now.AddSeconds(51), 30, 3).ShouldBeTrue();
        task.AttemptCount.ShouldBe(2);
        task.RunNumber.ShouldBe(1);
        Should.Throw<InvalidOperationException>(() => task.EnsureRun(first));
        task.EnsureRun(second);
        task.MarkGenerated(1, now.AddSeconds(52));
        task.TryClaim(Guid.NewGuid(), now.AddMinutes(2), 30, 3).ShouldBeFalse();
    }

    [Fact]
    public void Retry_Should_Respect_Delay_And_Bounded_Attempts()
    {
        var task = Task();
        var now = DateTime.UtcNow;
        task.TryClaim(Guid.NewGuid(), now, 30, 2).ShouldBeTrue();
        task.MarkFailed("chat", "KnowPulse:AiGeneration:ModelFailed", now);
        task.QueueRetry(now.AddSeconds(15), 2);
        task.TryClaim(Guid.NewGuid(), now.AddSeconds(14), 30, 2).ShouldBeFalse();
        task.TryClaim(Guid.NewGuid(), now.AddSeconds(15), 30, 2).ShouldBeTrue();
        task.MarkFailed("chat", "KnowPulse:AiGeneration:ModelFailed", now.AddSeconds(16));
        Should.Throw<BusinessException>(() => task.QueueRetry(now.AddSeconds(17), 2)).Code.ShouldBe("KnowPulse:AiGeneration:RetryLimit");
        task.AttemptCount.ShouldBe(2);
        task.RunNumber.ShouldBe(1);
    }

    [Fact]
    public void Regeneration_Should_Remember_Keys_And_Refuse_To_Evict_The_Bounded_Ledger()
    {
        var task = Task();
        var now = DateTime.UtcNow;
        task.TryClaim(Guid.NewGuid(), now, 30, 3);
        task.MarkGenerated(1, now);
        var operation = Guid.NewGuid();
        task.QueueRegeneration(operation, now, 1).ShouldBeTrue();
        task.RunNumber.ShouldBe(2);
        task.AttemptCount.ShouldBe(0);
        task.StartedTime.ShouldBeNull();
        task.QueueRegeneration(operation, now, 1).ShouldBeFalse();
        task.TryClaim(Guid.NewGuid(), now, 30, 3);
        task.MarkGenerated(1, now);
        Should.Throw<BusinessException>(() => task.QueueRegeneration(Guid.NewGuid(), now, 1)).Code.ShouldBe("KnowPulse:AiGeneration:RegenerationLimit");
        task.QueueRegeneration(operation, now, 1).ShouldBeFalse();
        task.RunNumber.ShouldBe(2);
        task.Status.ShouldBe(AiQuestionGenerationTaskStatus.Generated);
    }

    [Fact]
    public void Candidate_Edits_Should_Keep_Source_Context_And_Lock_Adopted_Content()
    {
        var candidate = new AiQuestionCandidate(Guid.NewGuid(), null, Guid.NewGuid(), AiQuestionCandidateStatus.Generated,
            QuestionType.SingleChoice, QuestionDifficulty.Normal, "原题", "原题干", null, 0, null, null, "[]", "[]", "[]", null);
        candidate.SetGenerationContext(1, 1, "{\"frozen\":true}", "[]", "[]", 5, "Practice", true);
        candidate.Edit("教师修订", "修订题干", "修订解析", "[]", "[]", null);
        candidate.IsEdited.ShouldBeTrue();
        candidate.ContentRevision.ShouldBe(1);
        candidate.CurriculumSnapshotJson.ShouldBe("{\"frozen\":true}");
        candidate.Score.ShouldBe(5);
        candidate.MarkAdoptedAsDraft(Guid.NewGuid());
        Should.Throw<BusinessException>(() => candidate.Edit("覆盖采纳", "题干", null, "[]", "[]", null)).Code.ShouldBe("KnowPulse:AiGeneration:CandidateLocked");
        candidate.Title.ShouldBe("教师修订");
        candidate.ContentRevision.ShouldBe(1);
    }

    [Fact]
    public void Worker_State_Timestamps_Should_Reject_Local_And_Unspecified_Time()
    {
        var task = Task();
        Should.Throw<ArgumentException>(() => task.TryClaim(Guid.NewGuid(), DateTime.Now, 30, 3));
        Should.Throw<ArgumentException>(() => task.QueueRegeneration(Guid.NewGuid(), new DateTime(2026, 10, 7), 3));
        task.Status.ShouldBe(AiQuestionGenerationTaskStatus.Pending);
    }

    [Fact]
    public void Scores_Should_Preserve_Two_Decimal_Confirmation_Without_Silent_Rounding()
    {
        var task = new AiQuestionGenerationTask(Guid.NewGuid(), null, Guid.NewGuid(), "v1", "test", "model", "v1",
            QuestionType.SingleChoice, QuestionDifficulty.Normal, 1, 3, "教学经验", "教学经验", null, 0, null);
        Should.Throw<ArgumentOutOfRangeException>(() => task.InitializeExecution(new string('a', 64), null, null, 0.015m, "Practice"));
        task.RequestFingerprint.ShouldBeNull();
        task.InitializeExecution(new string('a', 64), null, null, 0.01m, "Practice");
        task.Score.ShouldBe(0.01m);
        var candidate = new AiQuestionCandidate(Guid.NewGuid(), null, task.Id, AiQuestionCandidateStatus.Generated,
            QuestionType.SingleChoice, QuestionDifficulty.Normal, "题目", "题干", null, 0, null, null, "[]", "[]", "[]", null);
        Should.Throw<ArgumentOutOfRangeException>(() => candidate.SetGenerationContext(1, 1, null, "[]", "[]", 0.015m, "Practice", false));
        candidate.RunNumber.ShouldBe(0);
        candidate.SetGenerationContext(1, 1, null, "[]", "[]", 5.25m, "Practice", false);
        candidate.Score.ShouldBe(5.25m);
    }

    private static AiQuestionGenerationTask Task()
    {
        var task = new AiQuestionGenerationTask(Guid.NewGuid(), null, Guid.NewGuid(), "v1", "test", "model", "v1",
            QuestionType.SingleChoice, QuestionDifficulty.Normal, 1, 3, "教学经验", "教学经验", null, 0, null);
        task.RestrictToPersonalOwner(Guid.NewGuid());
        task.InitializeExecution(new string('a', 64), null, null, 1, "Practice");
        return task;
    }
}
