using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

public class EducationStudentGrowthSnapshot : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid SchoolId { get; private set; }
    public Guid AcademicTermId { get; private set; }
    public Guid SchoolClassId { get; private set; }
    public Guid SchoolMembershipId { get; private set; }
    public Guid StudentEnrollmentId { get; private set; }
    public Guid TeachingAssignmentId { get; private set; }
    public Guid TeacherUserId { get; private set; }
    public Guid EducationSubjectId { get; private set; }
    public Guid TextbookVersionId { get; private set; }
    public Guid ContextId { get; private set; }
    public DateTime WindowStartsAtUtc { get; private set; }
    public DateTime WindowEndsAtUtc { get; private set; }
    public DateTime CalculatedAtUtc { get; private set; }
    public string SourceFingerprint { get; private set; } = "";
    public string ResultJson { get; private set; } = "";
    protected EducationStudentGrowthSnapshot() { }
    public static Guid StableId(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));
    public static Guid ContextFor(Guid? tenant, Guid student, Guid enrollment, Guid teaching, Guid book)
        => StableId($"student-growth-context-v1|{tenant:D}|{student:D}|{enrollment:D}|{teaching:D}|{book:D}");
    public static Guid PointFor(Guid context, Guid revision) => StableId($"student-growth-point-v1|{context:D}|{revision:D}");
    public EducationStudentGrowthSnapshot(Guid? tenant, Guid student, Guid enrollment, Guid membership, Guid term, Guid schoolClass,
        EducationCurriculumSnapshot scope, string fingerprint, EducationStudentGrowthValue value)
        : base(StableId($"student-growth-snapshot-v1|{tenant:D}|{student:D}|{fingerprint}"))
    {
        if (scope == null || value == null || value.Context == null || value.Points == null || value.Assessments == null || scope.TenantId != tenant || value.Context.Id != ContextFor(tenant, student, enrollment, scope.TeachingAssignmentId, scope.TextbookVersionId) ||
            value.SnapshotId != Guid.Empty || value.WindowStartsAtUtc.Kind != DateTimeKind.Utc || value.WindowEndsAtUtc.Kind != DateTimeKind.Utc ||
            value.CalculatedAtUtc.Kind != DateTimeKind.Utc || value.WindowStartsAtUtc >= value.WindowEndsAtUtc || value.WindowEndsAtUtc > value.CalculatedAtUtc ||
            value.CalculationVersion != EducationLearningAnalysis.AlgorithmVersion || value.RuleMissing != (value.Rule == null) ||
            value.Points.Count > 3000 || value.Points.Select(x => x.Id).Distinct().Count() != value.Points.Count ||
            value.Points.Any(x => x.Id == Guid.Empty || x.RevisionNumber < 1 || x.ObtainedScore < 0 || x.MaxScore < x.ObtainedScore ||
                x.Ratio != (x.MaxScore == 0 ? (decimal?)null : x.ObtainedScore / x.MaxScore) || (x.MaxScore == 0) != (x.State == EducationMasteryState.Unassessed) ||
                (x.MaxScore == 0 ? x.IndependentQuestionCount != 0 || x.AssessmentCount != 0 : x.IndependentQuestionCount < 1 || x.AssessmentCount < 1) ||
                !Enum.IsDefined(x.State) || x.IndependentQuestionCount < 0 || x.AssessmentCount < 0 || x.UnverifiedQuestionCount < 0 || x.PracticeQuestionCount < 0 || x.InvalidQuestionCount < 0) ||
            value.Assessments.Select(x => x.TaskId).Distinct().Count() != value.Assessments.Count ||
            value.Assessments.Any(x => !Enum.IsDefined(x.Visibility) || x.Visibility == Exam.ExamResultVisibility.Hidden || x.ResultReleaseId == Guid.Empty ||
                x.TaskId == Guid.Empty || x.RecipientId == Guid.Empty || x.EffectiveAtUtc.Kind != DateTimeKind.Utc || x.EffectiveAtUtc < value.WindowStartsAtUtc || x.EffectiveAtUtc >= value.WindowEndsAtUtc ||
                (x.IsAbsent ? x.TotalScore.HasValue || x.MaxScore.HasValue : !x.TotalScore.HasValue || !x.MaxScore.HasValue || x.TotalScore < 0 || x.MaxScore <= 0 || x.TotalScore > x.MaxScore)))
            throw new BusinessException(EducationLearningReportErrors.Invalid);
        TenantId = tenant; StudentUserId = EducationRules.RequireId(student); StudentEnrollmentId = EducationRules.RequireId(enrollment);
        SchoolMembershipId = EducationRules.RequireId(membership); AcademicTermId = EducationRules.RequireId(term); SchoolClassId = EducationRules.RequireId(schoolClass);
        SchoolId = EducationRules.RequireId(scope.SchoolId); TeachingAssignmentId = EducationRules.RequireId(scope.TeachingAssignmentId); TeacherUserId = EducationRules.RequireId(scope.TeacherUserId);
        EducationSubjectId = EducationRules.RequireId(scope.EducationSubjectId); TextbookVersionId = EducationRules.RequireId(scope.TextbookVersionId); ContextId = value.Context.Id;
        WindowStartsAtUtc = value.WindowStartsAtUtc; WindowEndsAtUtc = value.WindowEndsAtUtc; CalculatedAtUtc = value.CalculatedAtUtc;
        SourceFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint);
        ResultJson = EducationLearningReportJson.Write(value with { SnapshotId = Id }, EducationLearningReportJson.MaxStudentBytes);
    }
    public EducationStudentGrowthValue Result() => EducationLearningReportJson.Read<EducationStudentGrowthValue>(ResultJson, EducationLearningReportJson.MaxStudentBytes);
}
