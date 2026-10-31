using System;
using KnowPulse.AiQuestioning;
using KnowPulse.QuestionBank;
using KnowPulse.RagAI;
using Shouldly;
using Xunit;

namespace KnowPulse.QuestionBank;

public class PersonalDerivedOwnerTests
{
    [Fact]
    public void Question_Edit_Should_Preserve_Immutable_Personal_Owner()
    {
        var owner = Guid.NewGuid();
        var question = new Question(Guid.NewGuid(), null, QuestionType.SingleChoice,
            QuestionDifficulty.Normal, QuestionCreationSource.Manual, "Title", "Stem");
        question.PersonalOwnerUserId.ShouldBeNull();
        question.RestrictToPersonalOwner(owner);
        question.UpdateDraft(QuestionType.SingleChoice, QuestionDifficulty.Normal, "Edited", "Edited stem", null, 0, null);
        question.RestrictToPersonalOwner(owner);
        Should.Throw<ArgumentException>(() => question.RestrictToPersonalOwner(Guid.NewGuid()));
        Should.Throw<ArgumentException>(() => question.RestrictToPersonalOwner(Guid.Empty));
        question.PersonalOwnerUserId.ShouldBe(owner);
    }

    [Fact]
    public void Failed_Generation_Task_Should_Preserve_Its_Input_Owner()
    {
        var owner = Guid.NewGuid();
        var task = new AiQuestionGenerationTask(Guid.NewGuid(), null, Guid.NewGuid(), "v1", "test", "model", "v1",
            QuestionType.SingleChoice, QuestionDifficulty.Normal, 1, 3, "Personal input", "Personal input",
            null, 0, null);
        task.RestrictToPersonalOwner(owner);
        task.RestrictToPersonalOwner(owner);
        Should.Throw<ArgumentException>(() => task.RestrictToPersonalOwner(Guid.NewGuid()));
        Should.Throw<ArgumentException>(() => task.RestrictToPersonalOwner(Guid.Empty));
        task.PersonalOwnerUserId.ShouldBe(owner);
    }

    [Fact]
    public void Evaluation_Case_And_Run_Should_Not_Transfer_Ownership()
    {
        var owner = Guid.NewGuid();
        var evaluationCase = new RagEvaluationCase(Guid.NewGuid(), null, "Case", "Personal question", true, "{}", "[]", "[]", null);
        var run = new RagEvaluationRun(Guid.NewGuid(), null, "Run", owner, DateTime.UtcNow);
        evaluationCase.RestrictToPersonalOwner(owner);
        run.RestrictToPersonalOwner(owner);
        Should.Throw<ArgumentException>(() => evaluationCase.RestrictToPersonalOwner(Guid.NewGuid()));
        Should.Throw<ArgumentException>(() => run.RestrictToPersonalOwner(Guid.NewGuid()));
        Should.Throw<ArgumentException>(() => evaluationCase.RestrictToPersonalOwner(Guid.Empty));
        Should.Throw<ArgumentException>(() => run.RestrictToPersonalOwner(Guid.Empty));
        evaluationCase.PersonalOwnerUserId.ShouldBe(owner);
        run.PersonalOwnerUserId.ShouldBe(owner);
    }
}
