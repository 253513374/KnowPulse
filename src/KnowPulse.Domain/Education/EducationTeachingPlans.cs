using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

public class EducationTeachingPlan : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid SchoolId { get; private set; }
    public Guid AcademicTermId { get; private set; }
    public Guid SchoolClassId { get; private set; }
    public Guid TeachingAssignmentId { get; private set; }
    public Guid StudentEnrollmentId { get; private set; }
    public Guid SchoolMembershipId { get; private set; }
    public Guid EducationSubjectId { get; private set; }
    public Guid TextbookVersionId { get; private set; }
    public string TeacherName { get; private set; } = "";
    public string StudentName { get; private set; } = "";
    public string ClassName { get; private set; } = "";
    public string SubjectName { get; private set; } = "";
    public int RevisionNumber { get; private set; }
    public Guid? CurrentRevisionId { get; private set; }
    public Guid? PublishedRevisionId { get; private set; }
    public Guid? LastPublishedRevisionId { get; private set; }
    public Guid? PublicRevisionId { get; private set; }
    public EducationPlanExecutionState ExecutionState { get; private set; }
    public DateTime? ExecutionChangedAtUtc { get; private set; }
    public Guid? CurrentDecisionId { get; private set; }
    public int DecisionRevisionNumber { get; private set; }
    protected EducationTeachingPlan() { }
    public EducationTeachingPlan(Guid id, EducationTeachingPlanBasis basis, Guid term, Guid schoolClass, string teacherName, string className) : base(EducationRules.RequireId(id))
    {
        var scope = basis.Curriculum;
        TenantId = scope.TenantId; OwnerUserId = EducationRules.RequireId(scope.TeacherUserId); StudentUserId = EducationRules.RequireId(basis.Student.StudentUserId);
        SchoolId = EducationRules.RequireId(scope.SchoolId); AcademicTermId = EducationRules.RequireId(term); SchoolClassId = EducationRules.RequireId(schoolClass);
        TeachingAssignmentId = EducationRules.RequireId(scope.TeachingAssignmentId); StudentEnrollmentId = EducationRules.RequireId(basis.Student.StudentEnrollmentId);
        SchoolMembershipId = EducationRules.RequireId(basis.Student.SchoolMembershipId); EducationSubjectId = EducationRules.RequireId(scope.EducationSubjectId);
        TextbookVersionId = EducationRules.RequireId(scope.TextbookVersionId); TeacherName = Check.NotNullOrWhiteSpace(teacherName, nameof(teacherName), 256);
        StudentName = Check.NotNullOrWhiteSpace(basis.Student.StudentName, nameof(basis), 256); ClassName = Check.NotNullOrWhiteSpace(className, nameof(className), 128);
        SubjectName = Check.NotNullOrWhiteSpace(scope.SubjectName, nameof(basis), 128);
    }
    public EducationCurriculumSnapshot Authority() => new() { TenantId = TenantId, SchoolId = SchoolId, TeacherUserId = OwnerUserId,
        TeachingAssignmentId = TeachingAssignmentId, EducationSubjectId = EducationSubjectId, TextbookVersionId = TextbookVersionId };
    public void Advance(EducationTeachingPlanRevision revision)
    {
        if (revision.PlanId != Id || revision.OwnerUserId != OwnerUserId || revision.StudentUserId != StudentUserId || revision.TenantId != TenantId ||
            revision.RevisionNumber != RevisionNumber + 1 || revision.BasedOnRevisionId != CurrentRevisionId || revision.ExpectedConcurrencyStamp != ConcurrencyStamp) throw Invalid();
        if (revision.Kind != EducationTeachingPlanRevisionKind.Draft && revision.SourceRevisionId != CurrentRevisionId) throw Invalid();
        if (revision.Kind == EducationTeachingPlanRevisionKind.Published && ExecutionState is EducationPlanExecutionState.Ended or EducationPlanExecutionState.Terminated) throw Invalid();
        RevisionNumber = revision.RevisionNumber; CurrentRevisionId = revision.Id;
        if (revision.Kind == EducationTeachingPlanRevisionKind.Published) { PublishedRevisionId = revision.Id; LastPublishedRevisionId = revision.Id; PublicRevisionId = revision.Id; }
        if (revision.Kind == EducationTeachingPlanRevisionKind.Withdrawn) { PublishedRevisionId = null; PublicRevisionId = revision.Id; }
    }
    public void AdvanceDecision(EducationTeachingPlanDecisionRevision revision)
    {
        if (revision.PlanId != Id || revision.OwnerUserId != OwnerUserId || revision.StudentUserId != StudentUserId || revision.TenantId != TenantId ||
            revision.RevisionNumber != DecisionRevisionNumber + 1 || revision.BasedOnDecisionId != CurrentDecisionId || revision.Before != ExecutionState ||
            revision.PublishedRevisionId != PublishedRevisionId || revision.ExpectedConcurrencyStamp != ConcurrencyStamp) throw Invalid();
        if (ExecutionState != revision.After) ExecutionChangedAtUtc = revision.RecordedAtUtc;
        ExecutionState = revision.After; CurrentDecisionId = revision.Id; DecisionRevisionNumber = revision.RevisionNumber;
    }
    private static BusinessException Invalid() => new(EducationTeachingPlanErrors.Invalid);
}

public class EducationTeachingPlanRevision : CreationAuditedEntity<Guid>, IMultiTenant
{
    private EducationTeachingPlanBasis? _basis;
    private HashSet<EducationTeachingPlanQuestion>? _questions;
    public Guid? TenantId { get; private set; }
    public Guid PlanId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public int RevisionNumber { get; private set; }
    public Guid? BasedOnRevisionId { get; private set; }
    public Guid? SourceRevisionId { get; private set; }
    public EducationTeachingPlanRevisionKind Kind { get; private set; }
    public Guid? GenerationJobId { get; private set; }
    public string? BasisJson { get; private set; }
    public string? ContentJson { get; private set; }
    public string? InternalNotes { get; private set; }
    public string? Reason { get; private set; }
    public string RequestFingerprint { get; private set; } = "";
    public string ExpectedConcurrencyStamp { get; private set; } = "";
    public DateTime RecordedAtUtc { get; private set; }
    public EducationTeachingPlanBasis? Basis() => BasisJson == null ? null : _basis ??= EducationLearningReportJson.Read<EducationTeachingPlanBasis>(BasisJson, EducationLearningReportJson.MaxStudentBytes);
    public bool ContainsQuestion(EducationTeachingPlanQuestion question) => (_questions ??= Basis()?.Questions.ToHashSet() ?? []).Contains(question);
    public EducationTeachingPlanContent? Content() => ContentJson == null ? null : EducationLearningReportJson.Read<EducationTeachingPlanContent>(ContentJson, 256 * 1024);
    protected EducationTeachingPlanRevision() { }
    public EducationTeachingPlanRevision(Guid id, EducationTeachingPlan plan, EducationTeachingPlanRevisionKind kind, EducationTeachingPlanBasis? basis,
        EducationTeachingPlanContent? content, string? internalNotes, string? reason, EducationTeachingPlanRevision? source, string stamp, string fingerprint, DateTime now) : base(EducationRules.RequireId(id))
    {
        if (!Enum.IsDefined(kind) || stamp != plan.ConcurrencyStamp || kind == EducationTeachingPlanRevisionKind.Draft && source != null || source != null && (source.PlanId != plan.Id || source.Id != plan.CurrentRevisionId) ||
            kind == EducationTeachingPlanRevisionKind.Confirmed && source?.Kind != EducationTeachingPlanRevisionKind.Draft ||
            kind == EducationTeachingPlanRevisionKind.Published && source?.Kind != EducationTeachingPlanRevisionKind.Confirmed ||
            kind == EducationTeachingPlanRevisionKind.Withdrawn && (!plan.PublishedRevisionId.HasValue || string.IsNullOrWhiteSpace(reason)) ||
            plan.LastPublishedRevisionId.HasValue && kind != EducationTeachingPlanRevisionKind.Draft && string.IsNullOrWhiteSpace(reason)) throw Invalid();
        if (kind != EducationTeachingPlanRevisionKind.Withdrawn)
        {
            if (basis == null || content == null || basis.Curriculum.TenantId != plan.TenantId || basis.Curriculum.TeacherUserId != plan.OwnerUserId || basis.Curriculum.SchoolId != plan.SchoolId ||
                basis.Curriculum.TeachingAssignmentId != plan.TeachingAssignmentId || basis.Curriculum.TextbookVersionId != plan.TextbookVersionId || basis.Curriculum.EducationSubjectId != plan.EducationSubjectId ||
                basis.Student.StudentUserId != plan.StudentUserId || basis.Student.StudentEnrollmentId != plan.StudentEnrollmentId || basis.Student.SchoolMembershipId != plan.SchoolMembershipId) throw Invalid();
            content.Validate(); EducationAssessmentTaskRevision.RequireFingerprint(basis.Fingerprint);
            if (content.TargetPointRevisionIds.Any(x => !basis.Points.Any(p => p.Point.KnowledgePointRevisionId == x))) throw Invalid();
            if (kind != EducationTeachingPlanRevisionKind.Draft && content.EndsAtUtc <= now) throw Invalid();
            if (kind != EducationTeachingPlanRevisionKind.Draft && content.TargetPointRevisionIds.Any(x => basis.Points.Single(p => p.Point.KnowledgePointRevisionId == x).State is EducationMasteryState.Unassessed or EducationMasteryState.InsufficientEvidence) && content.Steps[0].Kind != EducationTeachingPlanStepKind.Diagnostic)
                throw new BusinessException(EducationTeachingPlanErrors.DiagnosticRequired);
            BasisJson = EducationLearningReportJson.Write(basis, EducationLearningReportJson.MaxStudentBytes); ContentJson = EducationLearningReportJson.Write(content, 256 * 1024);
            if (source != null && kind is EducationTeachingPlanRevisionKind.Confirmed or EducationTeachingPlanRevisionKind.Published && (source.BasisJson != BasisJson || source.ContentJson != ContentJson || source.InternalNotes != internalNotes)) throw Invalid();
        }
        TenantId = plan.TenantId; PlanId = plan.Id; OwnerUserId = plan.OwnerUserId; StudentUserId = plan.StudentUserId;
        RevisionNumber = checked(plan.RevisionNumber + 1); BasedOnRevisionId = plan.CurrentRevisionId; SourceRevisionId = source?.Id; Kind = kind;
        GenerationJobId = source?.GenerationJobId;
        InternalNotes = Check.Length(internalNotes, nameof(internalNotes), 4000); Reason = Check.Length(reason, nameof(reason), 2048);
        ExpectedConcurrencyStamp = stamp; RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint); RecordedAtUtc = EducationRules.RequireUtc(now);
    }
    private static BusinessException Invalid() => new(EducationTeachingPlanErrors.Invalid);

    public void LinkGeneration(EducationTeachingPlanGenerationJob job)
    {
        if (Kind != EducationTeachingPlanRevisionKind.Draft || GenerationJobId.HasValue || job.Status != EducationPlanGenerationStatus.Completed || job.PlanId != PlanId || job.TenantId != TenantId ||
            job.OwnerUserId != OwnerUserId || job.StudentUserId != StudentUserId || job.Basis().Fingerprint != Basis()?.Fingerprint) throw Invalid();
        GenerationJobId = job.Id;
    }
}

public class EducationTeachingPlanQuestionExposure : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid PlanId { get; private set; }
    public Guid PlanRevisionId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid SchoolId { get; private set; }
    public Guid EducationSubjectId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid TaskRevisionId { get; private set; }
    public Guid RecipientId { get; private set; }
    public Guid ExamPaperId { get; private set; }
    public Guid PaperQuestionId { get; private set; }
    public Guid OriginalQuestionId { get; private set; }
    public Guid DefinitionRevisionId { get; private set; }
    public DateTime ExposedAtUtc { get; private set; }
    protected EducationTeachingPlanQuestionExposure() { }
    public EducationTeachingPlanQuestionExposure(EducationTeachingPlan plan, EducationTeachingPlanRevision revision, EducationTeachingPlanQuestion question, DateTime now)
        : base(EducationStudentGrowthSnapshot.StableId($"teaching-plan-context-v1|{revision.Id:D}|{question.PaperQuestionId:D}|{plan.StudentUserId:D}|{question.RecipientId:D}"))
    {
        if (plan.PublishedRevisionId != revision.Id || revision.PlanId != plan.Id || revision.Kind != EducationTeachingPlanRevisionKind.Published ||
            !revision.ContainsQuestion(question) || now < revision.RecordedAtUtc) throw new BusinessException(EducationTeachingPlanErrors.Invalid);
        TenantId = plan.TenantId; PlanId = plan.Id; PlanRevisionId = revision.Id; StudentUserId = plan.StudentUserId; SchoolId = plan.SchoolId; EducationSubjectId = plan.EducationSubjectId;
        TaskId = question.TaskId; TaskRevisionId = question.TaskRevisionId; RecipientId = question.RecipientId; ExamPaperId = question.ExamPaperId;
        PaperQuestionId = question.PaperQuestionId; OriginalQuestionId = question.OriginalQuestionId; DefinitionRevisionId = question.DefinitionRevisionId;
        ExposedAtUtc = EducationRules.RequireUtc(now);
    }
    public EducationLearningQuestionExposure Evidence(EducationTeachingPlanHintReviewRevision? review = null)
    {
        if (review != null && (review.TenantId != TenantId || review.ExposureId != Id || review.StudentUserId != StudentUserId ||
            review.PlanId != PlanId || review.PlanRevisionId != PlanRevisionId))
            throw new BusinessException(EducationLearningErrorCodes.InvalidEvidence);
        return new(Id, StudentUserId, OriginalQuestionId, ExposedAtUtc, review?.Use ?? EducationLearningExposureUse.Unverified, review?.Id);
    }
}
