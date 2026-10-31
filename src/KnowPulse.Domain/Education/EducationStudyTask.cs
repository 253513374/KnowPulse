using System;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

public class EducationStudyTask : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid SchoolId { get; private set; }
    public Guid AcademicTermId { get; private set; }
    public Guid SchoolClassId { get; private set; }
    public Guid TeachingAssignmentId { get; private set; }
    public Guid EducationSubjectId { get; private set; }
    public Guid TextbookVersionId { get; private set; }
    public int RevisionNumber { get; private set; }
    public Guid? CurrentRevisionId { get; private set; }
    public Guid? PublishedRevisionId { get; private set; }
    public Guid? LastPublishedRevisionId { get; private set; }
    public EducationStudyTaskRevisionKind PublicationKind { get; private set; } = EducationStudyTaskRevisionKind.Published;
    protected EducationStudyTask() { }
    public EducationStudyTask(Guid id, EducationCurriculumSnapshot scope, TeachingAssignment assignment) : base(EducationRules.RequireId(id))
    {
        if (scope.TeacherUserId != assignment.TeacherUserId || scope.TeachingAssignmentId != assignment.Id || scope.SchoolId != assignment.SchoolId ||
            scope.TenantId != assignment.TenantId || scope.EducationSubjectId != assignment.EducationSubjectId) throw Invalid();
        TenantId = scope.TenantId; OwnerUserId = EducationRules.RequireId(scope.TeacherUserId); SchoolId = EducationRules.RequireId(scope.SchoolId);
        AcademicTermId = assignment.AcademicTermId; SchoolClassId = assignment.SchoolClassId; TeachingAssignmentId = assignment.Id;
        EducationSubjectId = scope.EducationSubjectId; TextbookVersionId = EducationRules.RequireId(scope.TextbookVersionId);
    }
    public EducationCurriculumSnapshot Authority() => new() { TenantId = TenantId, TeacherUserId = OwnerUserId, SchoolId = SchoolId, TeachingAssignmentId = TeachingAssignmentId,
        EducationSubjectId = EducationSubjectId, TextbookVersionId = TextbookVersionId };
    public void Advance(EducationStudyTaskRevision revision)
    {
        if (revision.TaskId != Id || revision.OwnerUserId != OwnerUserId || revision.TenantId != TenantId || revision.RevisionNumber != RevisionNumber + 1 ||
            revision.BasedOnRevisionId != CurrentRevisionId || revision.ExpectedConcurrencyStamp != ConcurrencyStamp ||
            revision.Kind == EducationStudyTaskRevisionKind.Withdrawn && PublishedRevisionId == null) throw Invalid();
        RevisionNumber = revision.RevisionNumber; CurrentRevisionId = revision.Id;
        if (revision.Kind == EducationStudyTaskRevisionKind.Published) { PublishedRevisionId = revision.Id; LastPublishedRevisionId = revision.Id; }
        if (revision.Kind == EducationStudyTaskRevisionKind.Withdrawn) PublishedRevisionId = null;
    }
    private static BusinessException Invalid() => new(EducationStudyTaskErrors.Invalid);
}

public class EducationStudyTaskRevision : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public int RevisionNumber { get; private set; }
    public Guid SchoolId { get; private set; }
    public Guid AcademicTermId { get; private set; }
    public Guid SchoolClassId { get; private set; }
    public Guid? BasedOnRevisionId { get; private set; }
    public Guid? SourceRevisionId { get; private set; }
    public EducationStudyTaskRevisionKind Kind { get; private set; }
    public string Title { get; private set; } = "";
    public string TeacherName { get; private set; } = "";
    public string ClassName { get; private set; } = "";
    public string ScopeJson { get; private set; } = "";
    public string? ContentJson { get; private set; }
    public DateTime StartsAtUtc { get; private set; }
    public DateTime EndsAtUtc { get; private set; }
    public string? PublicReason { get; private set; }
    public string? RosterFingerprint { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public string RequestFingerprint { get; private set; } = "";
    public string ExpectedConcurrencyStamp { get; private set; } = "";
    protected EducationStudyTaskRevision() { }
    public EducationStudyTaskContent? Content() => ContentJson == null ? null : EducationLearningReportJson.Read<EducationStudyTaskContent>(ContentJson, 256 * 1024);
    public EducationCurriculumSnapshot Scope() => EducationLearningReportJson.Read<EducationCurriculumSnapshot>(ScopeJson, EducationLearningReportJson.MaxSourceBytes);
    public EducationStudyTaskRevision(Guid id, EducationStudyTask task, EducationStudyTaskRevisionKind kind, EducationCurriculumSnapshot scope,
        EducationStudyTaskContent? content, string teacherName, string className, string? publicReason, EducationStudyTaskRevision? source,
        string? rosterFingerprint, string fingerprint, DateTime now) : base(EducationRules.RequireId(id))
    {
        if (!Enum.IsDefined(kind) || scope.Mappings == null || scope.Mappings.Count is < 1 or > 3000 || scope.TenantId != task.TenantId || scope.TeacherUserId != task.OwnerUserId || scope.SchoolId != task.SchoolId ||
            scope.TeachingAssignmentId != task.TeachingAssignmentId || scope.EducationSubjectId != task.EducationSubjectId || scope.TextbookVersionId != task.TextbookVersionId ||
            source != null && (source.TaskId != task.Id || source.Id != task.CurrentRevisionId || source.OwnerUserId != task.OwnerUserId || source.TenantId != task.TenantId) ||
            kind != EducationStudyTaskRevisionKind.Draft && source == null || kind == EducationStudyTaskRevisionKind.Published && source!.Kind != EducationStudyTaskRevisionKind.Draft ||
            kind == EducationStudyTaskRevisionKind.Withdrawn && (task.PublishedRevisionId == null || content != null) ||
            task.LastPublishedRevisionId != null && kind != EducationStudyTaskRevisionKind.Draft && string.IsNullOrWhiteSpace(publicReason)) throw Invalid();
        TenantId = task.TenantId; TaskId = task.Id; OwnerUserId = task.OwnerUserId; RevisionNumber = checked(task.RevisionNumber + 1);
        SchoolId = task.SchoolId; AcademicTermId = task.AcademicTermId; SchoolClassId = task.SchoolClassId;
        BasedOnRevisionId = task.CurrentRevisionId; SourceRevisionId = kind == EducationStudyTaskRevisionKind.Draft ? null : source!.Id; Kind = kind;
        ScopeJson = EducationLearningReportJson.Write(scope, EducationLearningReportJson.MaxSourceBytes); Title = content?.Title ?? source!.Title;
        TeacherName = Check.NotNullOrWhiteSpace(teacherName, nameof(teacherName), 256); ClassName = Check.NotNullOrWhiteSpace(className, nameof(className), 128);
        if (kind != EducationStudyTaskRevisionKind.Withdrawn) {
            if (content == null) throw Invalid(); content.Validate(kind == EducationStudyTaskRevisionKind.Published); StartsAtUtc = content.StartsAtUtc; EndsAtUtc = content.EndsAtUtc;
            ContentJson = EducationLearningReportJson.Write(content, 256 * 1024);
            if (kind == EducationStudyTaskRevisionKind.Published && (ContentJson != source!.ContentJson || ScopeJson != source.ScopeJson || EndsAtUtc <= now)) throw Invalid();
        } else { StartsAtUtc = source!.StartsAtUtc; EndsAtUtc = source.EndsAtUtc; }
        if (kind == EducationStudyTaskRevisionKind.Published) { EducationAssessmentTaskRevision.RequireFingerprint(rosterFingerprint!); RosterFingerprint = rosterFingerprint; }
        else if (rosterFingerprint != null) throw Invalid();
        PublicReason = string.IsNullOrWhiteSpace(publicReason) ? null : Check.Length(publicReason, nameof(publicReason), 2000);
        EducationAssessmentTaskRevision.RequireFingerprint(fingerprint); RequestFingerprint = fingerprint;
        ExpectedConcurrencyStamp = task.ConcurrencyStamp; RecordedAtUtc = EducationRules.RequireUtc(now);
    }
    private static BusinessException Invalid() => new(EducationStudyTaskErrors.Invalid);
}

public class EducationStudyRecipient : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid TaskRevisionId { get; private set; }
    public EducationStudyTaskRevisionKind PublicationKind { get; private set; } = EducationStudyTaskRevisionKind.Published;
    public Guid OwnerUserId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid SchoolId { get; private set; }
    public Guid AcademicTermId { get; private set; }
    public Guid SchoolClassId { get; private set; }
    public Guid SchoolMembershipId { get; private set; }
    public Guid StudentEnrollmentId { get; private set; }
    public string StudentName { get; private set; } = "";
    public DateTime AssignedAtUtc { get; private set; }
    public DateTime? FirstProvidedAtUtc { get; private set; }
    public int RevisionNumber { get; private set; }
    public Guid? CurrentRevisionId { get; private set; }
    public int StudentRevisionNumber { get; private set; }
    public Guid? CurrentStudentRevisionId { get; private set; }
    public Guid? CurrentFollowUpRevisionId { get; private set; }
    protected EducationStudyRecipient() { }
    public EducationStudyRecipient(EducationStudyTaskRevision published, Guid student, Guid member, Guid enrollment, string name)
        : base(EducationStudentGrowthSnapshot.StableId($"education-study-recipient-v1|{published.Id:D}|{student:D}"))
    {
        if (published.Kind != EducationStudyTaskRevisionKind.Published) throw new BusinessException(EducationStudyTaskErrors.Invalid);
        TenantId = published.TenantId; TaskId = published.TaskId; TaskRevisionId = published.Id; OwnerUserId = published.OwnerUserId;
        SchoolId = published.SchoolId; AcademicTermId = published.AcademicTermId; SchoolClassId = published.SchoolClassId;
        StudentUserId = EducationRules.RequireId(student); SchoolMembershipId = EducationRules.RequireId(member); StudentEnrollmentId = EducationRules.RequireId(enrollment);
        StudentName = Check.NotNullOrWhiteSpace(name, nameof(name), 256); AssignedAtUtc = published.RecordedAtUtc;
    }
    public void MarkProvided(DateTime now) { EducationRules.RequireUtc(now); if (now < AssignedAtUtc) throw new BusinessException(EducationStudyTaskErrors.Invalid); FirstProvidedAtUtc ??= now; }
    public void Advance(EducationStudyEvent revision)
    {
        if (revision.RecipientId != Id || revision.TenantId != TenantId || revision.OwnerUserId != OwnerUserId || revision.StudentUserId != StudentUserId ||
            revision.RevisionNumber != RevisionNumber + 1 || revision.BasedOnRevisionId != CurrentRevisionId || revision.ExpectedConcurrencyStamp != ConcurrencyStamp)
            throw new BusinessException(EducationStudyTaskErrors.Invalid);
        if (revision.Action == EducationTeachingActivityAction.TeacherFollowUp) { if (revision.ReplyToStudentRevisionId != CurrentStudentRevisionId) throw new BusinessException(EducationStudyTaskErrors.Invalid); CurrentFollowUpRevisionId = revision.Id; }
        else { if (revision.StudentRevisionNumber != StudentRevisionNumber + 1) throw new BusinessException(EducationStudyTaskErrors.Invalid); StudentRevisionNumber = revision.StudentRevisionNumber!.Value; CurrentStudentRevisionId = revision.Id; }
        CurrentRevisionId = revision.Id; RevisionNumber = revision.RevisionNumber;
    }
}

public class EducationStudyEvent : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid RecipientId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public int RevisionNumber { get; private set; }
    public int? StudentRevisionNumber { get; private set; }
    public Guid? BasedOnRevisionId { get; private set; }
    public Guid? ReplyToStudentRevisionId { get; private set; }
    public EducationTeachingActivityAction Action { get; private set; }
    public EducationTeachingActivityDifficulty? Difficulty { get; private set; }
    public string? SharedText { get; private set; }
    public string? InternalTeacherNote { get; private set; }
    public int? ReportedMinutes { get; private set; }
    public string RequestFingerprint { get; private set; } = "";
    public string ExpectedConcurrencyStamp { get; private set; } = "";
    public DateTime RecordedAtUtc { get; private set; }
    protected EducationStudyEvent() { }
    public EducationStudyEvent(Guid id, EducationStudyRecipient recipient, EducationStudyEvent? currentStudent, EducationTeachingActivityAction action,
        EducationTeachingActivityDifficulty? difficulty, string? sharedText, int? minutes, bool confirmSharing, bool confirmCompletion, string? teacherNote,
        Guid? replyToStudentId, string fingerprint, DateTime now) : base(EducationRules.RequireId(id))
    {
        if (!Enum.IsDefined(action) || recipient.CurrentStudentRevisionId != currentStudent?.Id || currentStudent != null &&
            (currentStudent.RecipientId != recipient.Id || currentStudent.StudentUserId != recipient.StudentUserId || currentStudent.OwnerUserId != recipient.OwnerUserId ||
             currentStudent.TenantId != recipient.TenantId || currentStudent.Action == EducationTeachingActivityAction.TeacherFollowUp) || now < recipient.AssignedAtUtc)
            throw Invalid();
        SharedText = string.IsNullOrWhiteSpace(sharedText) ? null : Check.Length(sharedText, nameof(sharedText), 2000);
        if (action == EducationTeachingActivityAction.TeacherFollowUp) {
            if (currentStudent?.Action != EducationTeachingActivityAction.Difficulty || replyToStudentId != currentStudent.Id || !confirmCompletion || SharedText != null || difficulty != null || minutes != null) throw Invalid();
            InternalTeacherNote = Check.NotNullOrWhiteSpace(teacherNote, nameof(teacherNote), 2000); ActorUserId = recipient.OwnerUserId;
        } else {
            if (recipient.FirstProvidedAtUtc == null || now < recipient.FirstProvidedAtUtc || teacherNote != null || replyToStudentId != null || SharedText != null && !confirmSharing) throw Invalid();
            if (action == EducationTeachingActivityAction.Started && (minutes != null || difficulty != null || currentStudent?.Action == EducationTeachingActivityAction.Completed && SharedText == null) ||
                action == EducationTeachingActivityAction.Completed && (currentStudent?.Action != EducationTeachingActivityAction.Started || !confirmCompletion || difficulty != null || minutes is < 1 or > 1440) ||
                action == EducationTeachingActivityAction.Difficulty && (SharedText == null || !confirmSharing || difficulty == null || !Enum.IsDefined(difficulty.Value) || minutes != null)) throw Invalid();
            StudentRevisionNumber = checked(recipient.StudentRevisionNumber + 1); ActorUserId = recipient.StudentUserId;
        }
        TenantId = recipient.TenantId; RecipientId = recipient.Id; StudentUserId = recipient.StudentUserId; OwnerUserId = recipient.OwnerUserId;
        RevisionNumber = checked(recipient.RevisionNumber + 1); BasedOnRevisionId = recipient.CurrentRevisionId; ReplyToStudentRevisionId = replyToStudentId;
        Action = action; Difficulty = difficulty; ReportedMinutes = minutes; ExpectedConcurrencyStamp = recipient.ConcurrencyStamp;
        EducationAssessmentTaskRevision.RequireFingerprint(fingerprint); RequestFingerprint = fingerprint; RecordedAtUtc = EducationRules.RequireUtc(now);
    }
    private static BusinessException Invalid() => new(EducationStudyTaskErrors.Invalid);
}
