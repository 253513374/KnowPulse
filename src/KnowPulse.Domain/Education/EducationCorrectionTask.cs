using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

public class EducationCorrectionTask : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid QuestionGradeId { get; private set; }
    public Guid SourceTaskId { get; private set; }
    public Guid SourceTaskRevisionId { get; private set; }
    public Guid AssessmentRecipientId { get; private set; }
    public Guid SubmissionId { get; private set; }
    public Guid PaperQuestionId { get; private set; }
    public Guid SchoolId { get; private set; }
    public Guid AcademicTermId { get; private set; }
    public Guid SchoolClassId { get; private set; }
    public Guid TeachingAssignmentId { get; private set; }
    public Guid EducationSubjectId { get; private set; }
    public Guid SchoolMembershipId { get; private set; }
    public Guid StudentEnrollmentId { get; private set; }
    public int RevisionNumber { get; private set; }
    public Guid? CurrentRevisionId { get; private set; }
    public Guid? PublishedRevisionId { get; private set; }
    public Guid? LastPublishedRevisionId { get; private set; }
    public EducationCorrectionRevisionKind PublicationKind { get; private set; } = EducationCorrectionRevisionKind.Published;
    protected EducationCorrectionTask() { }
    public EducationCorrectionTask(Guid id, EducationAssessmentQuestionGrade grade, EducationAssessmentAttempt attempt, EducationAssessmentRecipient recipient,
        EducationAssessmentTaskRevision source, EducationCurriculumSnapshot scope) : base(EducationRules.RequireId(id))
    {
        if (grade.TenantId != attempt.TenantId || grade.TenantId != source.TenantId || grade.TenantId != recipient.TenantId || grade.OwnerUserId != source.OwnerUserId ||
            grade.StudentUserId != attempt.StudentUserId || grade.StudentUserId != recipient.StudentUserId || attempt.RecipientId != recipient.Id || attempt.SubmissionId != grade.SubmissionId ||
            grade.TaskId != source.TaskId || grade.TaskRevisionId != source.Id || attempt.TaskRevisionId != source.Id || recipient.TaskRevisionId != source.Id ||
            scope.TenantId != grade.TenantId || scope.TeacherUserId != grade.OwnerUserId || scope.SchoolId != source.SchoolId || scope.TeachingAssignmentId != source.TeachingAssignmentId) throw Invalid();
        TenantId = grade.TenantId; OwnerUserId = grade.OwnerUserId; StudentUserId = grade.StudentUserId; QuestionGradeId = grade.Id; SubmissionId = grade.SubmissionId;
        SourceTaskId = grade.TaskId; SourceTaskRevisionId = grade.TaskRevisionId; AssessmentRecipientId = recipient.Id; PaperQuestionId = grade.PaperQuestionId;
        SchoolId = source.SchoolId; AcademicTermId = source.AcademicTermId; SchoolClassId = source.SchoolClassId; TeachingAssignmentId = source.TeachingAssignmentId; EducationSubjectId = scope.EducationSubjectId;
        SchoolMembershipId = recipient.SchoolMembershipId; StudentEnrollmentId = recipient.StudentEnrollmentId;
    }
    public EducationCurriculumSnapshot Authority() => new() { TenantId = TenantId, TeacherUserId = OwnerUserId, SchoolId = SchoolId, TeachingAssignmentId = TeachingAssignmentId, EducationSubjectId = EducationSubjectId };
    public void Advance(EducationCorrectionTaskRevision revision)
    {
        if (revision.TaskId != Id || revision.TenantId != TenantId || revision.OwnerUserId != OwnerUserId || revision.StudentUserId != StudentUserId || revision.RevisionNumber != RevisionNumber + 1 ||
            revision.BasedOnRevisionId != CurrentRevisionId || revision.ExpectedConcurrencyStamp != ConcurrencyStamp || revision.Kind == EducationCorrectionRevisionKind.Withdrawn && PublishedRevisionId == null) throw Invalid();
        RevisionNumber = revision.RevisionNumber; CurrentRevisionId = revision.Id;
        if (revision.Kind == EducationCorrectionRevisionKind.Published) PublishedRevisionId = LastPublishedRevisionId = revision.Id;
        if (revision.Kind == EducationCorrectionRevisionKind.Withdrawn) PublishedRevisionId = null;
    }
    private static BusinessException Invalid() => new(EducationCorrectionErrors.Invalid);
}

public class EducationCorrectionTaskRevision : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public int RevisionNumber { get; private set; }
    public Guid? BasedOnRevisionId { get; private set; }
    public Guid? SourceRevisionId { get; private set; }
    public EducationCorrectionRevisionKind Kind { get; private set; }
    public Guid QuestionGradeId { get; private set; }
    public Guid GradeRevisionId { get; private set; }
    public Guid ResultReleaseId { get; private set; }
    public string? ContentJson { get; private set; }
    public string? PublicReason { get; private set; }
    public string StudentName { get; private set; } = "";
    public string ClassName { get; private set; } = "";
    public string SubjectName { get; private set; } = "";
    public string TeacherName { get; private set; } = "";
    public string RequestFingerprint { get; private set; } = "";
    public string ExpectedConcurrencyStamp { get; private set; } = "";
    public DateTime RecordedAtUtc { get; private set; }
    protected EducationCorrectionTaskRevision() { }
    public EducationCorrectionInstructions? Content() => ContentJson == null ? null : EducationLearningReportJson.Read<EducationCorrectionInstructions>(ContentJson, 64 * 1024);
    public EducationCorrectionTaskRevision(Guid id, EducationCorrectionTask root, EducationCorrectionRevisionKind kind, EducationCorrectionInstructions? content,
        Guid gradeRevisionId, Guid releaseId, string studentName, string className, string subjectName, string teacherName, string? publicReason,
        EducationCorrectionTaskRevision? source, string fingerprint, DateTime now) : base(EducationRules.RequireId(id))
    {
        if (!Enum.IsDefined(kind) || source != null && (source.TaskId != root.Id || source.Id != root.CurrentRevisionId || source.TenantId != root.TenantId || source.OwnerUserId != root.OwnerUserId) ||
            kind != EducationCorrectionRevisionKind.Draft && source == null || kind == EducationCorrectionRevisionKind.Published && source!.Kind != EducationCorrectionRevisionKind.Draft ||
            kind == EducationCorrectionRevisionKind.Withdrawn && (root.PublishedRevisionId == null || content != null) || root.LastPublishedRevisionId != null && kind != EducationCorrectionRevisionKind.Draft && string.IsNullOrWhiteSpace(publicReason)) throw Invalid();
        TenantId = root.TenantId; TaskId = root.Id; OwnerUserId = root.OwnerUserId; StudentUserId = root.StudentUserId; QuestionGradeId = root.QuestionGradeId;
        RevisionNumber = checked(root.RevisionNumber + 1); BasedOnRevisionId = root.CurrentRevisionId; SourceRevisionId = kind == EducationCorrectionRevisionKind.Draft ? null : source!.Id; Kind = kind;
        GradeRevisionId = EducationRules.RequireId(gradeRevisionId); ResultReleaseId = EducationRules.RequireId(releaseId);
        if (kind != EducationCorrectionRevisionKind.Withdrawn) { if (content == null) throw Invalid(); content.Validate(); ContentJson = EducationLearningReportJson.Write(content, 64 * 1024); }
        if (kind == EducationCorrectionRevisionKind.Published && (ContentJson != source!.ContentJson || GradeRevisionId != source.GradeRevisionId || ResultReleaseId != source.ResultReleaseId || content!.DueAtUtc <= now)) throw Invalid();
        StudentName = Check.NotNullOrWhiteSpace(studentName, nameof(studentName), 256); ClassName = Check.NotNullOrWhiteSpace(className, nameof(className), 128);
        SubjectName = Check.NotNullOrWhiteSpace(subjectName, nameof(subjectName), 128); TeacherName = Check.NotNullOrWhiteSpace(teacherName, nameof(teacherName), 256);
        PublicReason = string.IsNullOrWhiteSpace(publicReason) ? null : Check.Length(publicReason, nameof(publicReason), 2000);
        RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint); ExpectedConcurrencyStamp = root.ConcurrencyStamp; RecordedAtUtc = EducationRules.RequireUtc(now);
    }
    private static BusinessException Invalid() => new(EducationCorrectionErrors.Invalid);
}
