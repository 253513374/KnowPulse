using System;
using System.Linq;
using KnowPulse.Exam;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.Education;

public class EducationAssessmentTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Saving_And_Publishing_Advance_Only_The_Root_And_Preserve_Earlier_Definitions()
    {
        var root = Root();
        var first = Revision(root, 1, title: "首次草稿");
        root.AdvanceDraft(first);
        var second = Revision(root, 2, first.Id, title: "教师修订");
        root.AdvanceDraft(second);
        var published = Revision(root, 3, second.Id, published: true, title: "正式测评");
        root.Publish(published);
        root.Publish(published);

        first.Title.ShouldBe("首次草稿");
        first.PublishedAtUtc.ShouldBeNull();
        first.RosterFingerprint.ShouldBeNull();
        second.Title.ShouldBe("教师修订");
        published.PublishedAtUtc.ShouldBe(Now);
        root.CurrentRevisionId.ShouldBe(published.Id);
        root.PublishedRevisionId.ShouldBe(published.Id);
        root.CurrentRevisionNumber.ShouldBe(3);
        Should.Throw<BusinessException>(() => root.AdvanceDraft(Revision(root, 4, published.Id)))
            .Code.ShouldBe(EducationAssessmentErrorCodes.AlreadyPublished);
        Should.Throw<BusinessException>(() => root.Publish(Revision(root, 4, published.Id, published: true)))
            .Code.ShouldBe(EducationAssessmentErrorCodes.AlreadyPublished);
    }

    [Fact]
    public void A_Root_Cannot_Skip_History_Or_Advance_A_Foreign_Tenant_Or_Teachers_Revision()
    {
        var root = Root();
        var first = Revision(root, 1);
        root.AdvanceDraft(first);
        Should.Throw<BusinessException>(() => root.AdvanceDraft(Revision(root, 3, first.Id)))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidRevision);
        Should.Throw<BusinessException>(() => root.AdvanceDraft(Revision(root, 2, Guid.NewGuid())))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidRevision);
        Should.Throw<BusinessException>(() => root.AdvanceDraft(Revision(Root(), 2, first.Id)))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidRevision);
        var foreignTenant = new EducationAssessmentTask(root.Id, Guid.NewGuid(), root.OwnerUserId, Fingerprint(), Now);
        Should.Throw<BusinessException>(() => root.AdvanceDraft(Revision(foreignTenant, 2, first.Id)))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidRevision);
        var foreignTeacher = new EducationAssessmentTask(root.Id, root.TenantId, Guid.NewGuid(), Fingerprint(), Now);
        Should.Throw<BusinessException>(() => root.AdvanceDraft(Revision(foreignTeacher, 2, first.Id)))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidRevision);
        root.CurrentRevisionId.ShouldBe(first.Id);
    }

    [Fact]
    public void Publication_Requires_An_Existing_Draft_And_Exact_Immutable_Roster_Receipt()
    {
        var root = Root();
        Should.Throw<BusinessException>(() => root.Publish(Revision(root, 2, Guid.NewGuid(), published: true)))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidRevision);
        root.AdvanceDraft(Revision(root, 1));
        Should.Throw<BusinessException>(() => root.Publish(Revision(root, 2, root.CurrentRevisionId)))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidRevision);
        Should.Throw<BusinessException>(() => Revision(root, 2, root.CurrentRevisionId, published: true, rosterFingerprint: "not-a-sha256"))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidDefinition);
        Should.Throw<BusinessException>(() => Revision(root, 2, root.CurrentRevisionId, rosterFingerprint: Fingerprint()))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidRevision);
        Should.Throw<BusinessException>(() => Revision(root, 2, root.CurrentRevisionId, published: true, starts: Now.AddHours(-1), ends: Now))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidRevision);
    }

    [Fact]
    public void Full_Class_And_Explicit_Students_Are_Distinct_And_Selections_Are_Frozen()
    {
        var root = Root();
        var student = Guid.NewGuid();
        Should.Throw<BusinessException>(() => Revision(root, 1, students: [student]))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidRecipientScope);
        Should.Throw<BusinessException>(() => Revision(root, 1, recipientScope: EducationAssessmentRecipientScope.SelectedStudents))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidRecipientScope);
        Should.Throw<BusinessException>(() => Revision(root, 1, recipientScope: (EducationAssessmentRecipientScope)99))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidRecipientScope);
        Should.Throw<BusinessException>(() => Revision(root, 1, recipientScope: EducationAssessmentRecipientScope.SelectedStudents, students: [student, student]))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidRecipientScope);
        Should.Throw<BusinessException>(() => Revision(root, 1, recipientScope: EducationAssessmentRecipientScope.SelectedStudents, students: [Guid.Empty]))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidRecipientScope);
        Should.Throw<BusinessException>(() => Revision(root, 1, recipientScope: EducationAssessmentRecipientScope.SelectedStudents,
                students: Enumerable.Range(0, EducationAssessmentTaskRevision.MaxRecipients + 1).Select(_ => Guid.NewGuid()).ToArray()))
            .Code.ShouldBe(EducationAssessmentErrorCodes.CapacityExceeded);
        var selected = new[] { student, Guid.NewGuid() };
        var revision = Revision(root, 1, recipientScope: EducationAssessmentRecipientScope.SelectedStudents, students: selected);
        var frozen = revision.SelectedStudentIdsJson;
        selected[0] = Guid.NewGuid();
        revision.SelectedStudentIdsJson.ShouldBe(frozen);
    }

    [Fact]
    public void Fixed_Recipients_Require_Publication_And_Cannot_Expand_The_Selected_Students()
    {
        var root = Root();
        Should.Throw<BusinessException>(() => Recipient(Revision(root, 1)))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidRevision);
        var student = Guid.NewGuid();
        var published = Revision(root, 2, Guid.NewGuid(), published: true,
            recipientScope: EducationAssessmentRecipientScope.SelectedStudents, students: [student]);
        Should.Throw<BusinessException>(() => Recipient(published, Guid.NewGuid()))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidRecipientScope);
        var recipient = Recipient(published, student);
        recipient.StudentUserId.ShouldBe(student);
        recipient.TaskRevisionId.ShouldBe(published.Id);
        recipient.StudentName.ShouldBe("张同学");
        recipient.AccountName.ShouldBe("verified-student");
        recipient.StartedAttemptCount.ShouldBe(0);
        recipient.CurrentAttemptId.ShouldBeNull();
    }

    [Fact]
    public void Start_Window_Is_Server_Utc_And_Half_Open()
    {
        var published = Published();
        Should.Throw<BusinessException>(() => published.EnsureCanStart(Now.AddTicks(-1)))
            .Code.ShouldBe(EducationAssessmentErrorCodes.OutsideWindow);
        published.EnsureCanStart(Now);
        published.EnsureCanStart(published.EndsAtUtc.AddTicks(-1));
        Should.Throw<BusinessException>(() => published.EnsureCanStart(published.EndsAtUtc))
            .Code.ShouldBe(EducationAssessmentErrorCodes.OutsideWindow);
        Should.Throw<BusinessException>(() => Revision(Root(), 1).EnsureCanStart(Now))
            .Code.ShouldBe(EducationAssessmentErrorCodes.OutsideWindow);
    }

    [Fact]
    public void Attempt_Deadline_Is_The_Earlier_Of_Task_Close_And_Duration()
    {
        var published = Published();
        var first = new EducationAssessmentAttempt(Guid.NewGuid(), published, Recipient(published), Now);
        first.ExpiresAtUtc.ShouldBe(Now.AddMinutes(60));
        var late = new EducationAssessmentAttempt(Guid.NewGuid(), published, Recipient(published), published.EndsAtUtc.AddMinutes(-5));
        late.ExpiresAtUtc.ShouldBe(published.EndsAtUtc);
        first.TaskRevisionId.ShouldBe(published.Id);
        first.ExamPaperId.ShouldBe(published.ExamPaperId);
        first.AttemptNumber.ShouldBe(1);
    }

    [Fact]
    public void Repeated_Start_Records_Only_The_Winner_And_Resume_Does_Not_Reset_Its_Clock()
    {
        var published = Published();
        var recipient = Recipient(published);
        var winner = new EducationAssessmentAttempt(Guid.NewGuid(), published, recipient, Now);
        var concurrent = new EducationAssessmentAttempt(Guid.NewGuid(), published, recipient, Now.AddMinutes(1));
        recipient.RecordStartedAttempt(winner);
        recipient.RecordStartedAttempt(winner);
        Should.Throw<BusinessException>(() => recipient.RecordStartedAttempt(concurrent))
            .Code.ShouldBe(EducationAssessmentErrorCodes.AlreadyStarted);
        Should.Throw<BusinessException>(() => new EducationAssessmentAttempt(Guid.NewGuid(), published, recipient, Now.AddMinutes(2)))
            .Code.ShouldBe(EducationAssessmentErrorCodes.AlreadyStarted);
        winner.EnsureCanResume(Now.AddMinutes(30));
        winner.StartedAtUtc.ShouldBe(Now);
        winner.ExpiresAtUtc.ShouldBe(Now.AddHours(1));
        recipient.StartedAttemptCount.ShouldBe(1);
        recipient.CurrentAttemptId.ShouldBe(winner.Id);
        Should.Throw<BusinessException>(() => winner.EnsureCanResume(winner.ExpiresAtUtc))
            .Code.ShouldBe(EducationAssessmentErrorCodes.AttemptExpired);
        Should.Throw<BusinessException>(() => winner.EnsureCanResume(Now.AddTicks(-1)))
            .Code.ShouldBe(EducationAssessmentErrorCodes.OutsideWindow);
    }

    [Fact]
    public void An_Attempt_Cannot_Cross_Fixed_Recipient_Revision_Or_Student_Boundaries()
    {
        var published = Published();
        var recipient = Recipient(published);
        var attempt = new EducationAssessmentAttempt(Guid.NewGuid(), published, recipient, Now);
        Should.Throw<BusinessException>(() => Recipient(published).RecordStartedAttempt(attempt))
            .Code.ShouldBe(EducationAssessmentErrorCodes.RecipientUnavailable);
        Should.Throw<BusinessException>(() => new EducationAssessmentAttempt(Guid.NewGuid(), Published(), recipient, Now))
            .Code.ShouldBe(EducationAssessmentErrorCodes.RecipientUnavailable);
        recipient.StartedAttemptCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Non_Utc_Times_Cannot_Create_Start_Or_Resume_A_Real_Attempt(DateTimeKind kind)
    {
        var invalid = DateTime.SpecifyKind(Now, kind);
        Should.Throw<BusinessException>(() => new EducationAssessmentTask(Guid.NewGuid(), null, Guid.NewGuid(), Fingerprint(), invalid))
            .Code.ShouldBe(EducationErrorCodes.UtcRequired);
        Should.Throw<BusinessException>(() => Revision(Root(), 1, starts: invalid))
            .Code.ShouldBe(EducationErrorCodes.UtcRequired);
        var published = Published();
        var recipient = Recipient(published);
        Should.Throw<BusinessException>(() => new EducationAssessmentAttempt(Guid.NewGuid(), published, recipient, invalid))
            .Code.ShouldBe(EducationErrorCodes.UtcRequired);
        var attempt = new EducationAssessmentAttempt(Guid.NewGuid(), published, recipient, Now);
        Should.Throw<BusinessException>(() => attempt.EnsureCanResume(invalid))
            .Code.ShouldBe(EducationErrorCodes.UtcRequired);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1441, 1)]
    [InlineData(60, 0)]
    [InlineData(60, 21)]
    public void Invalid_Duration_Or_Attempt_Rules_Are_Not_Frozen(int minutes, int attempts)
    {
        Should.Throw<BusinessException>(() => Revision(Root(), 1, durationMinutes: minutes, maxAttempts: attempts))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidDefinition);
    }

    [Fact]
    public void Invalid_Operation_Definitions_And_Self_Referencing_Revisions_Are_Rejected()
    {
        Should.Throw<BusinessException>(() => new EducationAssessmentTask(Guid.NewGuid(), null, Guid.NewGuid(), new string('z', 64), Now))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidDefinition);
        Should.Throw<BusinessException>(() => Revision(Root(), 1, resultVisibility: (ExamResultVisibility)99))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidDefinition);
        Should.Throw<BusinessException>(() => Revision(Root(), 1, starts: Now.AddHours(3), ends: Now.AddHours(2)))
            .Code.ShouldBe(EducationErrorCodes.InvalidEffectivePeriod);
        var id = Guid.NewGuid();
        Should.Throw<BusinessException>(() => Revision(Root(), 2, id, id: id))
            .Code.ShouldBe(EducationAssessmentErrorCodes.InvalidRevision);
    }

    private static string Fingerprint() => new('a', 64);
    private static EducationAssessmentTask Root() => new(Guid.NewGuid(), null, Guid.NewGuid(), Fingerprint(), Now);
    private static EducationAssessmentTaskRevision Published() => Revision(Root(), 2, Guid.NewGuid(), published: true);
    private static EducationAssessmentRecipient Recipient(EducationAssessmentTaskRevision revision, Guid? studentId = null)
        => new(Guid.NewGuid(), revision, studentId ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "张同学", "verified-student");
    private static EducationAssessmentTaskRevision Revision(EducationAssessmentTask root, int number, Guid? previous = null,
        bool published = false, string title = "正式测评", EducationAssessmentRecipientScope recipientScope = EducationAssessmentRecipientScope.FullClass,
        Guid[]? students = null, string? rosterFingerprint = null, DateTime? starts = null, DateTime? ends = null,
        int durationMinutes = 60, int maxAttempts = 1, ExamResultVisibility resultVisibility = ExamResultVisibility.Hidden, Guid? id = null)
        => new(id ?? Guid.NewGuid(), root.TenantId, root.OwnerUserId, root.Id, number, previous, title, "逐题作答",
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "七年级一班", Guid.NewGuid(), Guid.NewGuid(), "{}",
            recipientScope, students ?? [], starts ?? Now, ends ?? Now.AddHours(2), durationMinutes, maxAttempts,
            resultVisibility, false, Fingerprint(), Now, published ? Now : null,
            published ? rosterFingerprint ?? Fingerprint() : rosterFingerprint);
}
