using System;
using System.Linq;
using System.Text.Json;
using KnowPulse.Exam;
using KnowPulse.QuestionBank;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.Education;

public class EducationAssessmentResponseTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);
    private static string Fingerprint => new('a', 64);

    [Fact]
    public void Complete_Question_Responses_Preserve_Raw_Text_Blank_Identity_And_Choice_Order()
    {
        var blankId = Guid.NewGuid();
        var secondBlankId = Guid.NewGuid();
        var blank = new EducationAssessmentResponseValue
        {
            Type = QuestionType.FillBlank, Blanks = [new() { BlankId = blankId, Text = "  同一答案\n大小写 AbC  " }]
        };
        blank.ValidateForQuestion(QuestionType.FillBlank, [], [blankId, secondBlankId], []);
        var restored = EducationAssessmentResponseValue.Parse(blank.ToCanonicalJson());
        restored.Blanks.Single().BlankId.ShouldBe(blankId);
        restored.Blanks.Single().Text.ShouldBe("  同一答案\n大小写 AbC  ");
        restored.Blanks.Count.ShouldBe(1);

        var choice = new EducationAssessmentResponseValue { Type = QuestionType.MultipleChoice, SelectedOptionKeys = ["C", "A"] };
        choice.ValidateForQuestion(QuestionType.MultipleChoice, ["A", "B", "C"], [], []);
        EducationAssessmentResponseValue.Parse(choice.ToCanonicalJson()).SelectedOptionKeys.ShouldBe(["C", "A"]);
        var subjective = Text(" \n原始解答与单位 cm²\t ");
        subjective.ValidateForQuestion(QuestionType.Subjective, [], [], []);
        EducationAssessmentResponseValue.Parse(subjective.ToCanonicalJson()).Text.ShouldBe(subjective.Text);
    }

    [Fact]
    public void Duplicate_And_Unknown_Blank_Choice_Or_Image_Ids_Are_Rejected_Not_Normalized()
    {
        var blankId = Guid.NewGuid();
        Invalid(() => new EducationAssessmentResponseValue
        {
            Type = QuestionType.FillBlank, Blanks = [new() { BlankId = blankId, Text = "one" }, new() { BlankId = blankId, Text = "two" }]
        }.ValidateForQuestion(QuestionType.FillBlank, [], [blankId], []));
        Invalid(() => new EducationAssessmentResponseValue
        {
            Type = QuestionType.FillBlank, Blanks = [new() { BlankId = Guid.NewGuid(), Text = "text" }]
        }.ValidateForQuestion(QuestionType.FillBlank, [], [blankId], []));
        Invalid(() => new EducationAssessmentResponseValue { Type = QuestionType.MultipleChoice, SelectedOptionKeys = ["A", "A"] }
            .ValidateForQuestion(QuestionType.MultipleChoice, ["A"], [], []));
        Invalid(() => new EducationAssessmentResponseValue { Type = QuestionType.MultipleChoice, SelectedOptionKeys = [" A "] }
            .ValidateForQuestion(QuestionType.MultipleChoice, ["A"], [], []));
        var image = Guid.NewGuid();
        Invalid(() => new EducationAssessmentResponseValue
        {
            Type = QuestionType.Subjective, Images = [new() { AttachmentId = image }, new() { AttachmentId = image }]
        }.ValidateForQuestion(QuestionType.Subjective, [], [], [image]));
        Invalid(() => new EducationAssessmentResponseValue
        {
            Type = QuestionType.Subjective, Images = [new() { AttachmentId = Guid.NewGuid() }]
        }.ValidateForQuestion(QuestionType.Subjective, [], [], [image]));
    }

    [Fact]
    public void Response_Shapes_Enforce_The_Actual_Frozen_Question_Type()
    {
        Invalid(() => Text("answer").ValidateForQuestion(QuestionType.FillBlank, [], [], []));
        Invalid(() => new EducationAssessmentResponseValue { Type = QuestionType.SingleChoice, SelectedOptionKeys = ["A", "B"] }
            .ValidateForQuestion(QuestionType.SingleChoice, ["A", "B"], [], []));
        Invalid(() => new EducationAssessmentResponseValue { Type = QuestionType.FillBlank, Images = [new() { AttachmentId = Guid.NewGuid() }] }
            .ToCanonicalJson());
        Invalid(() => new EducationAssessmentResponseValue { Type = QuestionType.Subjective, SelectedOptionKeys = ["A"] }.ToCanonicalJson());
        Invalid(() => new EducationAssessmentResponseValue { Type = (QuestionType)99 }.ToCanonicalJson());
        Invalid(() => new EducationAssessmentResponseValue { Type = QuestionType.Subjective, Text = new('x', EducationAssessmentResponseValue.MaxTextLength + 1) }
            .ToCanonicalJson());
        var trueFalse = new EducationAssessmentResponseValue { Type = QuestionType.TrueFalse, SelectedOptionKeys = ["false"] };
        trueFalse.ValidateForQuestion(QuestionType.TrueFalse, ["true", "false"], [], []);
        trueFalse.SelectedOptionKeys.ShouldBe(["false"]);
    }

    [Theory]
    [InlineData("{\"type\":4,\"surprise\":true}")]
    [InlineData("{\"type\":4,\"type\":3}")]
    [InlineData("{\"schemaVersion\":2,\"type\":4}")]
    [InlineData("{\"type\":\"4\"}")]
    [InlineData("{\"type\":4,\"images\":null}")]
    [InlineData("not-json")]
    public void Invalid_Response_Wire_Data_Cannot_Be_Reinterpreted_As_An_Empty_Valid_Response(string json)
    {
        Invalid(() => EducationAssessmentResponseValue.Parse(json));
    }

    [Fact]
    public void A_Stable_Save_Revision_Freezes_Its_Raw_Response_Hash_And_Original_Global_Versions()
    {
        var attempt = Attempt();
        var question = Guid.NewGuid();
        var input = Text("first raw answer");
        var operation = Guid.NewGuid();
        var first = Revision(attempt, question, input, id: operation);
        var current = new EducationAssessmentCurrentResponse(Guid.NewGuid(), first);
        var frozenJson = first.ResponseJson;
        var frozenHash = first.ResponseHash;
        attempt.RecordResponse(first, Now);
        input.Text = "later mutable UI text";
        var second = Revision(attempt, question, Text("teacher sees second raw answer"), Now.AddSeconds(1));
        attempt.RecordResponse(second, Now.AddSeconds(1));
        current.Advance(second);

        first.Id.ShouldBe(operation);
        first.ExpectedResponseSetVersion.ShouldBe(0);
        first.ResultResponseSetVersion.ShouldBe(1);
        first.ResponseJson.ShouldBe(frozenJson);
        first.ResponseHash.ShouldBe(frozenHash);
        EducationAssessmentResponseValue.Parse(first.ResponseJson).Text.ShouldBe("first raw answer");
        current.CurrentRevisionId.ShouldBe(second.Id);
        current.ResponseSetVersion.ShouldBe(2);
        attempt.ResponseSetVersion.ShouldBe(2);
        attempt.LastSavedAtUtc.ShouldBe(Now.AddSeconds(1));
        attempt.StartedAtUtc.ShouldBe(Now);
        attempt.ExpiresAtUtc.ShouldBe(Now.AddHours(1));
    }

    [Fact]
    public void Global_Cas_Rejects_A_Stale_Device_Even_When_It_Was_Saving_A_Different_Question()
    {
        var attempt = Attempt();
        var first = Revision(attempt, Guid.NewGuid(), Text("first"));
        var concurrent = Revision(attempt, Guid.NewGuid(), Text("stale second question"));
        attempt.RecordResponse(first, Now);
        Should.Throw<BusinessException>(() => attempt.RecordResponse(concurrent, Now))
            .Code.ShouldBe(EducationAssessmentErrorCodes.ResponseVersionConflict);
        Should.Throw<BusinessException>(() => attempt.EnsureCanEdit(Now.AddSeconds(1), 0))
            .Code.ShouldBe(EducationAssessmentErrorCodes.ResponseVersionConflict);
        Should.Throw<BusinessException>(() => attempt.EnsureCanSeal(EducationAssessmentSubmissionCause.Manual, Now.AddSeconds(1), 0))
            .Code.ShouldBe(EducationAssessmentErrorCodes.ResponseVersionConflict);
        attempt.ResponseSetVersion.ShouldBe(1);
    }

    [Fact]
    public void A_Save_Prepared_Before_Cutoff_Cannot_Be_Confirmed_At_Cutoff()
    {
        var attempt = Attempt();
        var revision = Revision(attempt, Guid.NewGuid(), Text("prepared before cutoff"));
        Should.Throw<BusinessException>(() => attempt.RecordResponse(revision, attempt.ExpiresAtUtc))
            .Code.ShouldBe(EducationAssessmentErrorCodes.AttemptExpired);
        attempt.ResponseSetVersion.ShouldBe(0);
        attempt.LastSavedAtUtc.ShouldBeNull();
        attempt.SubmissionId.ShouldBeNull();
    }

    [Fact]
    public void A_Current_Question_Pointer_Cannot_Advance_To_Another_Question_Or_An_Older_Save()
    {
        var attempt = Attempt();
        var question = Guid.NewGuid();
        var first = Revision(attempt, question, Text("one"));
        attempt.RecordResponse(first, Now);
        var current = new EducationAssessmentCurrentResponse(Guid.NewGuid(), first);
        var second = Revision(attempt, question, Text("two"), Now.AddSeconds(1));
        attempt.RecordResponse(second, Now.AddSeconds(1));
        current.Advance(second);
        current.Advance(second);
        Should.Throw<BusinessException>(() => current.Advance(first))
            .Code.ShouldBe(EducationAssessmentErrorCodes.ResponseVersionConflict);
        var other = Revision(attempt, Guid.NewGuid(), Text("other"), Now.AddSeconds(2));
        Invalid(() => current.Advance(other));
        var foreign = Revision(Attempt(), question, Text("foreign"));
        Invalid(() => current.Advance(foreign));
        Invalid(() => Attempt().RecordResponse(foreign, Now));
        current.CurrentRevisionId.ShouldBe(second.Id);
    }

    [Fact]
    public void Image_Upload_Confirmation_Is_Separate_From_Answer_Association_And_Preserves_Image_Order_And_Rotation()
    {
        var attempt = Attempt();
        var question = Guid.NewGuid();
        var first = Attachment(attempt, question);
        var second = Attachment(attempt, question);
        first.IsUploaded.ShouldBeFalse();
        first.UploadedAtUtc.ShouldBeNull();
        var response = Text("image answer");
        response.Images = [new() { AttachmentId = second.Id, RotationDegrees = 90 }, new() { AttachmentId = first.Id, RotationDegrees = 270 }];
        Invalid(() => response.ValidateForQuestion(QuestionType.Subjective, [], [], []));
        first.MarkUploaded(attempt, Now.AddSeconds(1));
        second.MarkUploaded(attempt, Now.AddSeconds(2));
        attempt.ResponseSetVersion.ShouldBe(0);
        attempt.LastSavedAtUtc.ShouldBeNull();
        response.ValidateForQuestion(QuestionType.Subjective, [], [], [first.Id, second.Id]);
        var revision = Revision(attempt, question, response, Now.AddSeconds(3));
        attempt.RecordResponse(revision, Now.AddSeconds(3));
        response.Images.Reverse();
        response.Images[0].RotationDegrees = 0;
        var restored = EducationAssessmentResponseValue.Parse(revision.ResponseJson);
        restored.Images.Select(x => x.AttachmentId).ShouldBe([second.Id, first.Id]);
        restored.Images.Select(x => x.RotationDegrees).ShouldBe([90, 270]);
        first.MarkUploaded(attempt, attempt.ExpiresAtUtc.AddHours(1));
        first.UploadedAtUtc.ShouldBe(Now.AddSeconds(1));
    }

    [Fact]
    public void Pending_Images_Cannot_Be_Confirmed_At_The_Deadline_Or_On_Another_Students_Attempt()
    {
        var attempt = Attempt();
        var pending = Attachment(attempt, Guid.NewGuid());
        Should.Throw<BusinessException>(() => pending.MarkUploaded(attempt, attempt.ExpiresAtUtc))
            .Code.ShouldBe(EducationAssessmentErrorCodes.AttemptExpired);
        pending.IsUploaded.ShouldBeFalse();
        Should.Throw<BusinessException>(() => pending.MarkUploaded(Attempt(), Now.AddSeconds(1)))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidAttachment);
        pending.RequestedAtUtc.ShouldBe(Now);
        pending.ContentHash.ShouldBe(Fingerprint);
        pending.BlobName.ShouldBe("education-answers/original-image");
        Should.Throw<BusinessException>(() => new EducationAssessmentAnswerAttachment(Guid.NewGuid(), attempt, Guid.NewGuid(),
            "bad.txt", "text/plain", 4, Fingerprint, "blob", Fingerprint, Now))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidAttachment);
        var response = Text("image");
        response.Images.Add(new() { AttachmentId = pending.Id, RotationDegrees = 45 });
        Invalid(() => response.ToCanonicalJson());
    }

    [Fact]
    public void Manual_Submission_Freezes_All_Question_Pointers_And_Unanswered_Questions_Require_Confirmation()
    {
        var attempt = Attempt();
        var answered = Guid.NewGuid();
        var unanswered = Guid.NewGuid();
        var revision = Revision(attempt, answered, Text("saved"));
        attempt.RecordResponse(revision, Now);
        var current = new EducationAssessmentCurrentResponse(Guid.NewGuid(), revision);
        Should.Throw<BusinessException>(() => Submission(attempt, [answered, unanswered], [current], [revision], confirmed: false))
            .Code.ShouldBe(EducationAssessmentErrorCodes.IncompleteSubmission);
        var submission = Submission(attempt, [answered, unanswered], [current], [revision]);
        var manifest = JsonSerializer.Deserialize<EducationAssessmentSubmissionManifestEntry[]>(submission.ManifestJson)!;
        manifest.Single(x => x.PaperQuestionId == answered).ResponseRevisionId.ShouldBe(revision.Id);
        manifest.Single(x => x.PaperQuestionId == answered).ResponseHash.ShouldBe(revision.ResponseHash);
        manifest.Single(x => x.PaperQuestionId == unanswered).ResponseRevisionId.ShouldBeNull();
        manifest.Single(x => x.PaperQuestionId == unanswered).ResponseHash.ShouldBeNull();
        attempt.Seal(submission);
        attempt.Seal(submission);
        attempt.Status.ShouldBe(EducationAssessmentAttemptStatus.Submitted);
        attempt.SubmissionId.ShouldBe(submission.Id);
        attempt.ResponseSetVersion.ShouldBe(1);
        submission.EffectiveAtUtc.ShouldBe(Now);
        submission.SealedAtUtc.ShouldBe(Now);
        Should.Throw<BusinessException>(() => attempt.EnsureCanEdit(Now.AddSeconds(1), 1))
            .Code.ShouldBe(EducationAssessmentErrorCodes.AttemptClosed);
    }

    [Fact]
    public void Submission_Cannot_Replace_The_Current_Revision_With_An_Older_Receipt_Or_Foreign_Question()
    {
        var attempt = Attempt();
        var question = Guid.NewGuid();
        var first = Revision(attempt, question, Text("one"));
        attempt.RecordResponse(first, Now);
        var current = new EducationAssessmentCurrentResponse(Guid.NewGuid(), first);
        var second = Revision(attempt, question, Text("two"), Now.AddSeconds(1));
        attempt.RecordResponse(second, Now.AddSeconds(1));
        current.Advance(second);
        Should.Throw<BusinessException>(() => Submission(attempt, [question], [current], [first], now: Now.AddSeconds(2)))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidSubmission);
        Should.Throw<BusinessException>(() => Submission(attempt, [Guid.NewGuid()], [current], [second], now: Now.AddSeconds(2)))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidSubmission);
        Should.Throw<BusinessException>(() => Submission(attempt, [question], [current], [second], now: Now.AddSeconds(2), paperVersion: 2))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidSubmission);
        var foreignRevision = Revision(Attempt(), question, Text("foreign"));
        var foreignCurrent = new EducationAssessmentCurrentResponse(Guid.NewGuid(), foreignRevision);
        Should.Throw<BusinessException>(() => Submission(attempt, [question], [foreignCurrent], [foreignRevision], now: Now.AddSeconds(2)))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidSubmission);
    }

    [Fact]
    public void An_Autosaved_Empty_Response_Is_Unanswered_Even_Though_It_Has_A_Save_Receipt()
    {
        var attempt = Attempt();
        var question = Guid.NewGuid();
        var revision = Revision(attempt, question, Text(" \n\t "));
        attempt.RecordResponse(revision, Now);
        var current = new EducationAssessmentCurrentResponse(Guid.NewGuid(), revision);
        Should.Throw<BusinessException>(() => Submission(attempt, [question], [current], [revision], confirmed: false))
            .Code.ShouldBe(EducationAssessmentErrorCodes.IncompleteSubmission);
        var submission = Submission(attempt, [question], [current], [revision]);
        submission.ManifestJson.ShouldContain(revision.Id.ToString());
        EducationAssessmentResponseValue.Parse(revision.ResponseJson).Text.ShouldBe(" \n\t ");
    }

    [Fact]
    public void Manual_And_Deadline_Sealing_Use_One_Close_Boundary_And_Preserve_The_Actual_Winner()
    {
        var attempt = Attempt();
        var question = Guid.NewGuid();
        var manual = Submission(attempt, [question], [], []);
        var deadline = Submission(attempt, [question], [], [], EducationAssessmentSubmissionCause.Deadline,
            false, attempt.ExpiresAtUtc.AddMinutes(5));
        attempt.Seal(manual);
        Should.Throw<BusinessException>(() => attempt.Seal(deadline))
            .Code.ShouldBe(EducationAssessmentErrorCodes.AttemptClosed);
        attempt.SubmissionId.ShouldBe(manual.Id);

        var second = Attempt();
        Should.Throw<BusinessException>(() => Submission(second, [question], [], [], EducationAssessmentSubmissionCause.Deadline,
            false, second.ExpiresAtUtc.AddTicks(-1)))
            .Code.ShouldBe(EducationAssessmentErrorCodes.OutsideWindow);
        Should.Throw<BusinessException>(() => Submission(second, [question], [], [], now: second.ExpiresAtUtc))
            .Code.ShouldBe(EducationAssessmentErrorCodes.AttemptExpired);
        var cutoff = Submission(second, [question], [], [], EducationAssessmentSubmissionCause.Deadline,
            false, second.ExpiresAtUtc.AddMinutes(10));
        second.Seal(cutoff);
        second.Seal(cutoff);
        cutoff.EffectiveAtUtc.ShouldBe(second.ExpiresAtUtc);
        cutoff.SealedAtUtc.ShouldBe(second.ExpiresAtUtc.AddMinutes(10));
        cutoff.ResponseSetVersion.ShouldBe(0);
        cutoff.Cause.ShouldBe(EducationAssessmentSubmissionCause.Deadline);
        second.SubmissionId.ShouldBe(cutoff.Id);
        second.StartedAtUtc.ShouldBe(Now);
        second.ExpiresAtUtc.ShouldBe(Now.AddHours(1));
    }

    [Fact]
    public void A_Submission_Prepared_Before_A_Later_Save_Cannot_Seal_A_Stale_Manifest()
    {
        var attempt = Attempt();
        var question = Guid.NewGuid();
        var first = Revision(attempt, question, Text("one"));
        attempt.RecordResponse(first, Now);
        var current = new EducationAssessmentCurrentResponse(Guid.NewGuid(), first);
        var prepared = Submission(attempt, [question], [current], [first]);
        var second = Revision(attempt, question, Text("two"), Now.AddSeconds(1));
        attempt.RecordResponse(second, Now.AddSeconds(1));
        current.Advance(second);
        Should.Throw<BusinessException>(() => attempt.Seal(prepared))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidSubmission);
        attempt.SubmissionId.ShouldBeNull();
        var actual = Submission(attempt, [question], [current], [second], now: Now.AddSeconds(2));
        attempt.Seal(actual);
        actual.ManifestJson.ShouldContain(second.Id.ToString());
        actual.ManifestJson.ShouldNotContain(first.Id.ToString());
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Save_Image_Confirmation_And_Sealing_All_Require_Server_Utc(DateTimeKind kind)
    {
        var attempt = Attempt();
        var invalid = DateTime.SpecifyKind(Now, kind);
        Should.Throw<BusinessException>(() => Revision(attempt, Guid.NewGuid(), Text("text"), invalid))
            .Code.ShouldBe(EducationErrorCodes.UtcRequired);
        var attachment = Attachment(attempt, Guid.NewGuid());
        Should.Throw<BusinessException>(() => attachment.MarkUploaded(attempt, invalid))
            .Code.ShouldBe(EducationErrorCodes.UtcRequired);
        Should.Throw<BusinessException>(() => Submission(attempt, [Guid.NewGuid()], [], [], now: invalid))
            .Code.ShouldBe(EducationErrorCodes.UtcRequired);
    }

    private static void Invalid(Action action) => Should.Throw<BusinessException>(action).Code.ShouldBe(EducationAssessmentErrorCodes.InvalidResponse);
    private static EducationAssessmentResponseValue Text(string text) => new() { Type = QuestionType.Subjective, Text = text };
    private static EducationAssessmentResponseRevision Revision(EducationAssessmentAttempt attempt, Guid question,
        EducationAssessmentResponseValue response, DateTime? now = null, Guid? id = null)
        => new(id ?? Guid.NewGuid(), attempt, question, 1, attempt.ResponseSetVersion, response, Fingerprint, now ?? Now);
    private static EducationAssessmentAnswerAttachment Attachment(EducationAssessmentAttempt attempt, Guid question)
        => new(Guid.NewGuid(), attempt, question, "answer.png", "image/png", 10, Fingerprint,
            "education-answers/original-image", Fingerprint, Now);
    private static EducationAssessmentSubmission Submission(EducationAssessmentAttempt attempt, Guid[] questions,
        EducationAssessmentCurrentResponse[] current, EducationAssessmentResponseRevision[] revisions,
        EducationAssessmentSubmissionCause cause = EducationAssessmentSubmissionCause.Manual, bool confirmed = true,
        DateTime? now = null, int paperVersion = 1)
        => new(Guid.NewGuid(), attempt, paperVersion, cause, Fingerprint, confirmed, questions, current, revisions, now ?? Now);
    private static EducationAssessmentAttempt Attempt()
    {
        var revision = new EducationAssessmentTaskRevision(Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), 2,
            Guid.NewGuid(), "课堂测评", "保存并提交本人作答", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "七年级一班",
            Guid.NewGuid(), Guid.NewGuid(), "{}", EducationAssessmentRecipientScope.FullClass, [], Now, Now.AddHours(2),
            60, 1, ExamResultVisibility.Hidden, false, Fingerprint, Now, Now, Fingerprint);
        var recipient = new EducationAssessmentRecipient(Guid.NewGuid(), revision, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "学生", "student");
        return new EducationAssessmentAttempt(Guid.NewGuid(), revision, recipient, Now);
    }
}
