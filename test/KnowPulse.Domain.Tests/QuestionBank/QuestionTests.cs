using System;
using KnowPulse.QuestionBank;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.QuestionBank;

public class QuestionTests
{
    [Fact]
    public void Should_Define_Minimum_Question_Types()
    {
        var values = Enum.GetValues<QuestionType>();

        values.ShouldContain(QuestionType.SingleChoice);
        values.ShouldContain(QuestionType.MultipleChoice);
        values.ShouldContain(QuestionType.TrueFalse);
        values.ShouldContain(QuestionType.FillBlank);
    }

    [Fact]
    public void Should_Define_Minimum_Question_Statuses()
    {
        var values = Enum.GetValues<QuestionStatus>();

        values.ShouldContain(QuestionStatus.Draft);
        values.ShouldContain(QuestionStatus.PendingReview);
        values.ShouldContain(QuestionStatus.Approved);
        values.ShouldContain(QuestionStatus.Rejected);
        values.ShouldContain(QuestionStatus.Disabled);
        values.ShouldContain(QuestionStatus.Archived);
    }

    [Fact]
    public void Should_Start_As_Draft_For_Manual_And_Ai_Questions()
    {
        var manual = CreateQuestion(QuestionCreationSource.Manual);
        var ai = CreateQuestion(QuestionCreationSource.AiGenerated);

        manual.Status.ShouldBe(QuestionStatus.Draft);
        ai.Status.ShouldBe(QuestionStatus.Draft);
    }

    [Fact]
    public void Should_Validate_Single_Choice_Answer()
    {
        var question = CreateQuestion(type: QuestionType.SingleChoice);
        var options = new[]
        {
            new QuestionOption(Guid.NewGuid(), null, question.Id, "A", "A option", 1),
            new QuestionOption(Guid.NewGuid(), null, question.Id, "B", "B option", 2)
        };

        question.SubmitForReview(
            options,
            new[] { new QuestionAnswer(Guid.NewGuid(), null, question.Id, "A", 1) },
            DateTime.UtcNow);

        question.Status.ShouldBe(QuestionStatus.PendingReview);

        var exception = Should.Throw<BusinessException>(() => question.EnsureDefinitionIsValid(
            options,
            new[]
            {
                new QuestionAnswer(Guid.NewGuid(), null, question.Id, "A", 1),
                new QuestionAnswer(Guid.NewGuid(), null, question.Id, "B", 2)
            }));
        exception.Code.ShouldBe(KnowPulseDomainErrorCodes.QuestionSingleChoiceRequiresOneAnswer);
    }

    [Fact]
    public void Should_Reject_Choice_Answer_Not_In_Options()
    {
        var question = CreateQuestion(type: QuestionType.MultipleChoice);
        var options = new[]
        {
            new QuestionOption(Guid.NewGuid(), null, question.Id, "A", "A option", 1),
            new QuestionOption(Guid.NewGuid(), null, question.Id, "B", "B option", 2)
        };

        var exception = Should.Throw<BusinessException>(() => question.SubmitForReview(
            options,
            new[] { new QuestionAnswer(Guid.NewGuid(), null, question.Id, "C", 1) },
            DateTime.UtcNow));

        exception.Code.ShouldBe(KnowPulseDomainErrorCodes.QuestionAnswerMustMatchOption);
    }

    [Fact]
    public void Choice_Questions_Should_Require_At_Least_Two_Options()
    {
        foreach (var type in new[] { QuestionType.SingleChoice, QuestionType.MultipleChoice })
        {
            var question = CreateQuestion(type: type);
            var exception = Should.Throw<BusinessException>(() => question.EnsureDefinitionIsValid(
                new[] { new QuestionOption(Guid.NewGuid(), null, question.Id, "A", "A option", 1) },
                new[] { new QuestionAnswer(Guid.NewGuid(), null, question.Id, "A", 1) }));

            exception.Code.ShouldBe(KnowPulseDomainErrorCodes.QuestionChoiceOptionsRequired);
        }
    }

    [Fact]
    public void Should_Validate_Multiple_Choice_Answer_Count()
    {
        var question = CreateQuestion(type: QuestionType.MultipleChoice);
        var options = ChoiceOptions(question.Id);

        var exception = Should.Throw<BusinessException>(() => question.EnsureDefinitionIsValid(
            options,
            new[] { new QuestionAnswer(Guid.NewGuid(), null, question.Id, "A", 1) }));
        exception.Code.ShouldBe(KnowPulseDomainErrorCodes.QuestionMultipleChoiceRequiresAtLeastTwoAnswers);

        question.EnsureDefinitionIsValid(
            options,
            new[]
            {
                new QuestionAnswer(Guid.NewGuid(), null, question.Id, "A", 1),
                new QuestionAnswer(Guid.NewGuid(), null, question.Id, "B", 2)
            });
    }

    [Fact]
    public void Should_Validate_TrueFalse_Answer()
    {
        var question = CreateQuestion(type: QuestionType.TrueFalse);

        question.EnsureDefinitionIsValid(
            Array.Empty<QuestionOption>(),
            new[] { new QuestionAnswer(Guid.NewGuid(), null, question.Id, "对", 1) });

        var exception = Should.Throw<BusinessException>(() => question.EnsureDefinitionIsValid(
            Array.Empty<QuestionOption>(),
            new[] { new QuestionAnswer(Guid.NewGuid(), null, question.Id, "A", 1) }));
        exception.Code.ShouldBe(KnowPulseDomainErrorCodes.QuestionTrueFalseAnswerInvalid);
    }

    [Fact]
    public void Should_Validate_Fill_Blank_Answer()
    {
        var question = CreateQuestion(type: QuestionType.FillBlank);

        question.EnsureDefinitionIsValid(
            Array.Empty<QuestionOption>(),
            new[] { new QuestionAnswer(Guid.NewGuid(), null, question.Id, "KnowPulse", 1) });

        var exception = Should.Throw<BusinessException>(() => question.EnsureDefinitionIsValid(
            Array.Empty<QuestionOption>(),
            Array.Empty<QuestionAnswer>()));
        exception.Code.ShouldBe(KnowPulseDomainErrorCodes.QuestionAnswerRequired);
    }

    [Fact]
    public void Should_Require_Knowledge_Or_Manual_Source()
    {
        var questionId = Guid.NewGuid();

        new QuestionSource(Guid.NewGuid(), null, questionId, Guid.NewGuid(), null, null, null, null, null, null)
            .KnowledgeItemId.ShouldNotBeNull();
        new QuestionSource(Guid.NewGuid(), null, questionId, null, Guid.NewGuid(), null, null, null, null, null)
            .KnowledgeVersionId.ShouldNotBeNull();
        new QuestionSource(Guid.NewGuid(), null, questionId, null, null, Guid.NewGuid(), null, null, null, null)
            .ChunkId.ShouldNotBeNull();
        new QuestionSource(Guid.NewGuid(), null, questionId, null, null, null, null, null, null, "manual source")
            .ManualSourceDescription.ShouldBe("manual source");

        var exception = Should.Throw<BusinessException>(() => new QuestionSource(
            Guid.NewGuid(),
            null,
            questionId,
            null,
            null,
            null,
            "source title only",
            null,
            null,
            null));
        exception.Code.ShouldBe(KnowPulseDomainErrorCodes.QuestionSourceRequired);
    }

    [Fact]
    public void Should_Approve_Only_From_Pending_Review()
    {
        var question = CreateQuestion();
        var exception = Should.Throw<BusinessException>(() => question.Approve(Guid.NewGuid(), DateTime.UtcNow));
        exception.Code.ShouldBe(KnowPulseDomainErrorCodes.QuestionReviewRequiresPendingStatus);

        Submit(question);
        question.Approve(Guid.NewGuid(), DateTime.UtcNow);

        question.Status.ShouldBe(QuestionStatus.Approved);
        question.ReviewedByUserId.ShouldNotBeNull();
        question.ReviewedTime.ShouldNotBeNull();
    }

    [Fact]
    public void Rejected_Question_Should_Not_Be_Usable_And_Can_Be_Submitted_Again()
    {
        var question = CreateQuestion();
        Submit(question);

        question.Reject(Guid.NewGuid(), DateTime.UtcNow);

        question.Status.ShouldBe(QuestionStatus.Rejected);
        question.IsUsableForExam.ShouldBeFalse();

        Submit(question);

        question.Status.ShouldBe(QuestionStatus.PendingReview);
    }

    [Fact]
    public void Submitted_Or_Approved_Question_Should_Not_Be_Submitted_Again()
    {
        var question = CreateQuestion();
        Submit(question);

        var pendingException = Should.Throw<BusinessException>(() => Submit(question));
        pendingException.Code.ShouldBe(KnowPulseDomainErrorCodes.QuestionSubmitRequiresDraftStatus);

        question.Approve(Guid.NewGuid(), DateTime.UtcNow);

        var approvedException = Should.Throw<BusinessException>(() => Submit(question));
        approvedException.Code.ShouldBe(KnowPulseDomainErrorCodes.QuestionSubmitRequiresDraftStatus);
    }

    [Fact]
    public void Updating_Approved_Question_Should_Return_To_Draft()
    {
        var question = CreateQuestion();
        Submit(question);
        question.Approve(Guid.NewGuid(), DateTime.UtcNow);

        question.UpdateDraft(
            QuestionType.SingleChoice,
            QuestionDifficulty.Hard,
            "Updated",
            "Updated stem",
            null,
            1,
            "tag");

        question.Status.ShouldBe(QuestionStatus.Draft);
        question.ReviewedByUserId.ShouldBeNull();
        question.ReviewedTime.ShouldBeNull();
    }

    [Fact]
    public void Archived_Question_Should_Not_Be_Editable_Or_Disabled()
    {
        var question = CreateQuestion();
        question.Archive(DateTime.UtcNow);

        var editException = Should.Throw<BusinessException>(() => question.UpdateDraft(
            QuestionType.SingleChoice,
            QuestionDifficulty.Hard,
            "Updated",
            "Updated stem",
            null,
            1,
            "tag"));
        editException.Code.ShouldBe(KnowPulseDomainErrorCodes.QuestionCannotEditArchived);

        var disableException = Should.Throw<BusinessException>(() => question.Disable(DateTime.UtcNow));
        disableException.Code.ShouldBe(KnowPulseDomainErrorCodes.QuestionCannotDisableArchived);
    }

    [Fact]
    public void Only_Approved_Questions_Should_Be_Usable_For_Exam()
    {
        var question = CreateQuestion();
        question.IsUsableForExam.ShouldBeFalse();

        Submit(question);
        question.IsUsableForExam.ShouldBeFalse();

        question.Approve(Guid.NewGuid(), DateTime.UtcNow);
        question.IsUsableForExam.ShouldBeTrue();

        question.Disable(DateTime.UtcNow);
        question.IsUsableForExam.ShouldBeFalse();

        question.Archive(DateTime.UtcNow);
        question.IsUsableForExam.ShouldBeFalse();
    }

    private static Question CreateQuestion(
        QuestionCreationSource source = QuestionCreationSource.Manual,
        QuestionType type = QuestionType.SingleChoice)
    {
        return new Question(
            Guid.NewGuid(),
            null,
            type,
            QuestionDifficulty.Normal,
            source,
            "Question title",
            "Question stem");
    }

    private static void Submit(Question question)
    {
        question.SubmitForReview(
            ChoiceOptions(question.Id),
            new[] { new QuestionAnswer(Guid.NewGuid(), null, question.Id, "A", 1) },
            DateTime.UtcNow);
    }

    private static QuestionOption[] ChoiceOptions(Guid questionId)
    {
        return new[]
        {
            new QuestionOption(Guid.NewGuid(), null, questionId, "A", "A option", 1),
            new QuestionOption(Guid.NewGuid(), null, questionId, "B", "B option", 2)
        };
    }
}
