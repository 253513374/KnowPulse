using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

public class EducationLearningReport : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid SchoolId { get; private set; }
    public Guid TeachingAssignmentId { get; private set; }
    public Guid AcademicTermId { get; private set; }
    public Guid SchoolClassId { get; private set; }
    public Guid EducationSubjectId { get; private set; }
    public Guid TextbookVersionId { get; private set; }
    public DateTime WindowStartsAtUtc { get; private set; }
    public DateTime WindowEndsAtUtc { get; private set; }
    public string SelectedTaskIdsJson { get; private set; } = "[]";
    public string CreateRequestFingerprint { get; private set; } = string.Empty;
    public int Generation { get; private set; }
    public Guid? CurrentJobId { get; private set; }
    public Guid? CurrentSnapshotId { get; private set; }
    protected EducationLearningReport() { }
    public EducationLearningReport(Guid id, EducationLearningReportSource source, IReadOnlyCollection<Guid> tasks, string fingerprint) : base(EducationRules.RequireId(id))
    {
        ValidateSource(source); if (tasks.Count > 500 || tasks.Contains(Guid.Empty) || tasks.Distinct().Count() != tasks.Count) throw Invalid();
        TenantId = source.TenantId; OwnerUserId = source.OwnerUserId; SchoolId = source.Curriculum.SchoolId; TeachingAssignmentId = source.Curriculum.TeachingAssignmentId;
        AcademicTermId = source.AcademicTermId; SchoolClassId = source.SchoolClassId; EducationSubjectId = source.Curriculum.EducationSubjectId; TextbookVersionId = source.Curriculum.TextbookVersionId;
        WindowStartsAtUtc = source.WindowStartsAtUtc; WindowEndsAtUtc = source.WindowEndsAtUtc;
        SelectedTaskIdsJson = EducationLearningReportJson.Write(tasks.Order().ToList(), 32000);
        CreateRequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint);
    }
    public EducationCurriculumSnapshot Authority() => new() { TenantId = TenantId, TeacherUserId = OwnerUserId, SchoolId = SchoolId,
        TeachingAssignmentId = TeachingAssignmentId, EducationSubjectId = EducationSubjectId, TextbookVersionId = TextbookVersionId };
    public void Queue(EducationLearningReportJob job)
    {
        if (job.ReportId != Id || job.TenantId != TenantId || job.OwnerUserId != OwnerUserId || job.Generation != Generation + 1 ||
            job.PreviousJobId != CurrentJobId || job.ExpectedConcurrencyStamp != ConcurrencyStamp) throw Invalid();
        Generation = job.Generation; CurrentJobId = job.Id;
    }
    public void Publish(EducationLearningReportJob job, EducationLearningReportSnapshot snapshot)
    {
        if (job.ReportId != Id || job.Id != CurrentJobId || job.Generation != Generation || job.Status != EducationLearningReportJobStatus.Completed ||
            job.SnapshotId != snapshot.Id || snapshot.ReportId != Id || snapshot.JobId != job.Id || snapshot.OwnerUserId != OwnerUserId || snapshot.TenantId != TenantId ||
            snapshot.SourceFingerprint != job.SourceFingerprint) throw Invalid();
        CurrentSnapshotId = snapshot.Id;
    }
    internal static void ValidateSource(EducationLearningReportSource source)
    {
        if (source.SchemaVersion != 1 || !source.IncludesScoringData || source.TenantId == Guid.Empty || source.OwnerUserId == Guid.Empty ||
            source.Curriculum.SchoolId == Guid.Empty || source.Curriculum.TeachingAssignmentId == Guid.Empty || source.Curriculum.EducationSubjectId == Guid.Empty ||
            source.Curriculum.TextbookVersionId == Guid.Empty || source.Curriculum.Mappings == null || source.Curriculum.Mappings.Count is < 1 or > 3000 ||
            source.OwnerUserId != source.Curriculum.TeacherUserId || source.TenantId != source.Curriculum.TenantId || source.AcademicTermId == Guid.Empty || source.SchoolClassId == Guid.Empty ||
            source.WindowStartsAtUtc.Kind != DateTimeKind.Utc || source.WindowEndsAtUtc.Kind != DateTimeKind.Utc || source.WindowStartsAtUtc >= source.WindowEndsAtUtc ||
            source.CapturedAtUtc.Kind != DateTimeKind.Utc || source.WindowEndsAtUtc > source.CapturedAtUtc || source.Students == null || source.Students.Count > EducationAssessmentTaskRevision.MaxRecipients ||
            source.Students.Any(x => x.Student.StudentUserId == Guid.Empty || x.Student.StudentEnrollmentId == Guid.Empty || x.Student.SchoolMembershipId == Guid.Empty) ||
            source.Students.Select(x => x.Student.StudentUserId).Distinct().Count() != source.Students.Count) throw Invalid();
        EducationAssessmentTaskRevision.RequireFingerprint(source.Fingerprint);
    }
    private static BusinessException Invalid() => new(EducationLearningReportErrors.Invalid);
}

public class EducationLearningReportJob : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public const int AutoAttempts = 3;
    public Guid? TenantId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid ReportId { get; private set; }
    public Guid? PreviousJobId { get; private set; }
    public Guid? RetryOfJobId { get; private set; }
    public int Generation { get; private set; }
    public string SourceJson { get; private set; } = string.Empty;
    public string CurriculumJson { get; private set; } = string.Empty;
    public string SourceFingerprint { get; private set; } = string.Empty;
    public string RequestFingerprint { get; private set; } = string.Empty;
    public string ExpectedConcurrencyStamp { get; private set; } = string.Empty;
    public EducationLearningReportJobStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public int CompletedStudentCount { get; private set; }
    public int StudentCount { get; private set; }
    public Guid? RunId { get; private set; }
    public Guid? SnapshotId { get; private set; }
    public DateTime? LeaseExpiresAtUtc { get; private set; }
    public DateTime? HeartbeatAtUtc { get; private set; }
    public DateTime NextAttemptAtUtc { get; private set; }
    public DateTime NextSourceCheckAtUtc { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public EducationLearningReportFailureStage FailureStage { get; private set; }
    public bool CapacityExceeded { get; private set; }
    protected EducationLearningReportJob() { }
    public EducationLearningReportJob(Guid id, EducationLearningReport report, EducationLearningReportSource source, string fingerprint, string stamp,
        DateTime now, Guid? retryOf = null) : base(EducationRules.RequireId(id))
    {
        EducationLearningReport.ValidateSource(source); EducationRules.RequireUtc(now);
        if (stamp != report.ConcurrencyStamp || source.TenantId != report.TenantId || source.OwnerUserId != report.OwnerUserId || source.Curriculum.SchoolId != report.SchoolId ||
            source.Curriculum.TeachingAssignmentId != report.TeachingAssignmentId || source.Curriculum.EducationSubjectId != report.EducationSubjectId || source.Curriculum.TextbookVersionId != report.TextbookVersionId ||
            source.AcademicTermId != report.AcademicTermId || source.SchoolClassId != report.SchoolClassId || source.WindowStartsAtUtc != report.WindowStartsAtUtc ||
            source.WindowEndsAtUtc != report.WindowEndsAtUtc || source.CapturedAtUtc > now || retryOf == Guid.Empty) throw Invalid();
        TenantId = report.TenantId; OwnerUserId = report.OwnerUserId; ReportId = report.Id; PreviousJobId = report.CurrentJobId; RetryOfJobId = retryOf;
        Generation = checked(report.Generation + 1); SourceJson = EducationLearningReportJson.Write(source, EducationLearningReportJson.MaxSourceBytes);
        CurriculumJson = EducationLearningReportJson.Write(source.Curriculum, EducationLearningReportJson.MaxSourceBytes);
        SourceFingerprint = source.Fingerprint; RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint);
        ExpectedConcurrencyStamp = stamp; RequestedAtUtc = now; NextAttemptAtUtc = now; NextSourceCheckAtUtc = now; StudentCount = source.Students.Count;
    }
    public EducationLearningReportSource Source() => EducationLearningReportJson.Read<EducationLearningReportSource>(SourceJson, EducationLearningReportJson.MaxSourceBytes);
    public void Claim(Guid runId, DateTime now)
    {
        EducationRules.RequireUtc(now);
        if (!(Status == EducationLearningReportJobStatus.Queued || Status == EducationLearningReportJobStatus.Failed && !CapacityExceeded && AttemptCount < AutoAttempts ||
            Status == EducationLearningReportJobStatus.Running && LeaseExpiresAtUtc <= now && AttemptCount < AutoAttempts) || NextAttemptAtUtc > now) throw Invalid();
        RunId = EducationRules.RequireId(runId); AttemptCount = checked(AttemptCount + 1); Status = EducationLearningReportJobStatus.Running;
        HeartbeatAtUtc = now; LeaseExpiresAtUtc = now.AddMinutes(2); CompletedStudentCount = 0; FailureStage = EducationLearningReportFailureStage.None; CapacityExceeded = false;
    }
    public void Heartbeat(Guid runId, int completed, DateTime now)
    {
        Run(runId, now);
        if (completed < CompletedStudentCount || completed > StudentCount) throw Invalid();
        CompletedStudentCount = completed; HeartbeatAtUtc = now; LeaseExpiresAtUtc = now.AddMinutes(2);
    }
    public void Complete(Guid runId, Guid snapshotId, DateTime now)
    {
        Run(runId, now); SnapshotId = EducationRules.RequireId(snapshotId); Status = EducationLearningReportJobStatus.Completed;
        CompletedStudentCount = StudentCount; CompletedAtUtc = now; LeaseExpiresAtUtc = null;
    }
    public void Fail(Guid runId, EducationLearningReportFailureStage stage, bool capacity, DateTime now)
    {
        Run(runId, now); if (stage == EducationLearningReportFailureStage.None || !Enum.IsDefined(stage)) throw Invalid();
        Status = EducationLearningReportJobStatus.Failed; FailureStage = stage; CapacityExceeded = capacity; LeaseExpiresAtUtc = null;
        NextAttemptAtUtc = now.AddSeconds(Math.Min(300, Math.Pow(2, Math.Min(AttemptCount, 8))));
    }
    public void Stop(Guid runId, bool sourceChanged, DateTime now)
    {
        Run(runId, now); Status = sourceChanged ? EducationLearningReportJobStatus.SourceChanged : EducationLearningReportJobStatus.Superseded;
        LeaseExpiresAtUtc = null; CompletedAtUtc = now;
    }
    public void Expire(DateTime now)
    {
        EducationRules.RequireUtc(now);
        if (Status != EducationLearningReportJobStatus.Running || LeaseExpiresAtUtc > now || AttemptCount < AutoAttempts) throw Invalid();
        Status = EducationLearningReportJobStatus.Failed; FailureStage = EducationLearningReportFailureStage.Calculate; LeaseExpiresAtUtc = null; NextAttemptAtUtc = now;
    }
    public void ScheduleSourceCheck(DateTime now) => NextSourceCheckAtUtc = EducationRules.RequireUtc(now).AddSeconds(30);
    private void Run(Guid id, DateTime now)
    { EducationRules.RequireUtc(now); if (Status != EducationLearningReportJobStatus.Running || RunId != id || LeaseExpiresAtUtc <= now || now < HeartbeatAtUtc) throw Invalid(); }
    private static BusinessException Invalid() => new(EducationLearningReportErrors.Invalid);
}

public class EducationLearningReportSnapshot : CreationAuditedEntity<Guid>, IMultiTenant
{
    private EducationLearningReportSummary? _summary;
    public Guid? TenantId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid ReportId { get; private set; }
    public Guid JobId { get; private set; }
    public Guid SchoolId { get; private set; }
    public Guid SchoolClassId { get; private set; }
    public int Generation { get; private set; }
    public string SourceFingerprint { get; private set; } = string.Empty;
    public string SummaryJson { get; private set; } = string.Empty;
    public DateTime CalculatedAtUtc { get; private set; }
    protected EducationLearningReportSnapshot() { }
    public EducationLearningReportSnapshot(EducationLearningReportJob job, EducationLearningReport report, EducationLearningReportSummary summary) : base(job.Id)
    {
        if (job.ReportId != report.Id || job.OwnerUserId != report.OwnerUserId || job.TenantId != report.TenantId || summary.SourceFingerprint != job.SourceFingerprint || summary.Students.Count != job.StudentCount || summary.CalculatedAtUtc < job.RequestedAtUtc ||
            summary.Students.Select(x => x.Student.StudentUserId).Distinct().Count() != summary.Students.Count) throw new BusinessException(EducationLearningReportErrors.Invalid);
        TenantId = job.TenantId; OwnerUserId = job.OwnerUserId; ReportId = job.ReportId; JobId = job.Id; Generation = job.Generation;
        SchoolId = report.SchoolId; SchoolClassId = report.SchoolClassId;
        SourceFingerprint = job.SourceFingerprint; SummaryJson = EducationLearningReportJson.Write(summary, EducationLearningReportJson.MaxSourceBytes); _summary = summary;
        CalculatedAtUtc = EducationRules.RequireUtc(summary.CalculatedAtUtc);
    }
    public EducationLearningReportSummary Summary() => _summary ??= EducationLearningReportJson.Read<EducationLearningReportSummary>(SummaryJson, EducationLearningReportJson.MaxSourceBytes);
}

public class EducationLearningReportStudentSnapshot : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid ReportId { get; private set; }
    public Guid SnapshotId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid SchoolId { get; private set; }
    public Guid SchoolClassId { get; private set; }
    public Guid StudentEnrollmentId { get; private set; }
    public Guid SchoolMembershipId { get; private set; }
    public string ResultJson { get; private set; } = string.Empty;
    protected EducationLearningReportStudentSnapshot() { }
    public EducationLearningReportStudentSnapshot(EducationLearningReportSnapshot snapshot, EducationLearningReportStudentResult result) : base(IdFor(snapshot.Id, result.Student.StudentUserId))
    {
        if (!snapshot.Summary().Students.Any(x => x.Student == result.Student) || result.Analysis.CalculatedAtUtc != snapshot.CalculatedAtUtc) throw new BusinessException(EducationLearningReportErrors.Invalid);
        TenantId = snapshot.TenantId; OwnerUserId = snapshot.OwnerUserId; ReportId = snapshot.ReportId; SnapshotId = snapshot.Id;
        SchoolId = snapshot.SchoolId; SchoolClassId = snapshot.SchoolClassId;
        StudentUserId = EducationRules.RequireId(result.Student.StudentUserId); StudentEnrollmentId = EducationRules.RequireId(result.Student.StudentEnrollmentId); SchoolMembershipId = EducationRules.RequireId(result.Student.SchoolMembershipId);
        ResultJson = EducationLearningReportJson.Write(result, EducationLearningReportJson.MaxStudentBytes);
    }
    public EducationLearningReportStudentResult Result() => EducationLearningReportJson.Read<EducationLearningReportStudentResult>(ResultJson, EducationLearningReportJson.MaxStudentBytes);
    private static Guid IdFor(Guid snapshot, Guid student) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"education-report-student-v1|{snapshot:D}|{student:D}")).AsSpan(0, 16));
}
