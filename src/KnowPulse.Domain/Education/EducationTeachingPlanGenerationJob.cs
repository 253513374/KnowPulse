using System;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

public class EducationTeachingPlanGenerationJob : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public const string CurrentPromptVersion = "education-plan-draft/v1";
    public string PromptVersion { get; private set; } = "";
    public Guid? TenantId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid PlanId { get; private set; }
    public string StudentName { get; private set; } = "";
    public string SubjectName { get; private set; } = "";
    public string TextbookTitle { get; private set; } = "";
    public Guid ReportId { get; private set; }
    public Guid ReportSnapshotId { get; private set; }
    public Guid EducationSubjectId { get; private set; }
    public Guid TextbookVersionId { get; private set; }
    public Guid SchoolId { get; private set; }
    public Guid AcademicTermId { get; private set; }
    public Guid SchoolClassId { get; private set; }
    public Guid TeachingAssignmentId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid StudentEnrollmentId { get; private set; }
    public Guid SchoolMembershipId { get; private set; }
    public Guid? RetryOfJobId { get; private set; }
    public string RequestFingerprint { get; private set; } = "";
    [DisableAuditing] public string RequestJson { get; private set; } = "";
    [DisableAuditing] public string BasisJson { get; private set; } = "";
    [DisableAuditing] public string? ContentJson { get; private set; }
    public EducationPlanGenerationStatus Status { get; private set; }
    public EducationPlanGenerationFailure Failure { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public Guid? RunId { get; private set; }
    public DateTime? HeartbeatAtUtc { get; private set; }
    public DateTime? LeaseExpiresAtUtc { get; private set; }
    public string? ModelAlias { get; private set; }
    public string? ModelProvider { get; private set; }
    public string? ModelVersion { get; private set; }
    public int? OutputTokenLimit { get; private set; }
    public double? Temperature { get; private set; }
    public Guid? DeploymentId { get; private set; }
    public string? PromptHash { get; private set; }
    public string? OutputHash { get; private set; }
    public int? PromptTokens { get; private set; }
    public int? CompletionTokens { get; private set; }
    public long? DurationMilliseconds { get; private set; }
    public decimal? EstimatedCostUsd { get; private set; }
    public bool UsageMayBeIncomplete { get; private set; }
    public EducationPlanGenerationRequest Request() => EducationLearningReportJson.Read<EducationPlanGenerationRequest>(RequestJson, 32000);
    public EducationCurriculumSnapshot Authority() => new() { TenantId = TenantId, TeacherUserId = OwnerUserId, SchoolId = SchoolId,
        TeachingAssignmentId = TeachingAssignmentId, EducationSubjectId = EducationSubjectId, TextbookVersionId = TextbookVersionId };
    public EducationTeachingPlanBasis Basis() => EducationLearningReportJson.Read<EducationTeachingPlanBasis>(BasisJson, EducationLearningReportJson.MaxStudentBytes);
    public EducationTeachingPlanContent? Content() => ContentJson == null ? null : EducationLearningReportJson.Read<EducationTeachingPlanContent>(ContentJson, 256 * 1024);
    protected EducationTeachingPlanGenerationJob() { }
    public EducationTeachingPlanGenerationJob(Guid id, Guid planId, EducationPlanGenerationRequest request, EducationTeachingPlanBasis basis, Guid term, Guid schoolClass, string fingerprint, DateTime now, Guid? retryOf = null)
        : base(EducationRules.RequireId(id))
    {
        request.Validate(basis); EducationAssessmentTaskRevision.RequireFingerprint(basis.Fingerprint); var scope = basis.Curriculum;
        TenantId = scope.TenantId; OwnerUserId = EducationRules.RequireId(scope.TeacherUserId); PlanId = EducationRules.RequireId(planId);
        StudentName = Check.NotNullOrWhiteSpace(basis.Student.StudentName, nameof(basis), 256); SubjectName = Check.NotNullOrWhiteSpace(scope.SubjectName, nameof(basis), 128); TextbookTitle = Check.NotNullOrWhiteSpace(scope.TextbookTitle, nameof(basis), 256);
        ReportId = request.ReportId; ReportSnapshotId = request.ReportSnapshotId; EducationSubjectId = EducationRules.RequireId(scope.EducationSubjectId); TextbookVersionId = EducationRules.RequireId(scope.TextbookVersionId);
        SchoolId = EducationRules.RequireId(scope.SchoolId); AcademicTermId = EducationRules.RequireId(term); SchoolClassId = EducationRules.RequireId(schoolClass);
        TeachingAssignmentId = EducationRules.RequireId(scope.TeachingAssignmentId); StudentUserId = EducationRules.RequireId(basis.Student.StudentUserId);
        StudentEnrollmentId = EducationRules.RequireId(basis.Student.StudentEnrollmentId); SchoolMembershipId = EducationRules.RequireId(basis.Student.SchoolMembershipId);
        RetryOfJobId = retryOf; if (retryOf == Guid.Empty || retryOf == id) throw Invalid();
        RequestJson = EducationLearningReportJson.Write(request, 32000); BasisJson = EducationLearningReportJson.Write(basis, EducationLearningReportJson.MaxStudentBytes);
        RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint); RequestedAtUtc = EducationRules.RequireUtc(now); PromptVersion = CurrentPromptVersion;
    }
    public bool Claim(Guid runId, DateTime now, int leaseSeconds)
    {
        EducationRules.RequireId(runId); EducationRules.RequireUtc(now); if (leaseSeconds is < 10 or > 600) throw Invalid();
        if (Status == EducationPlanGenerationStatus.Processing && LeaseExpiresAtUtc <= now) { Fail(RunId!.Value, EducationPlanGenerationFailure.LeaseExpired, now); return false; }
        if (Status != EducationPlanGenerationStatus.Pending) return false;
        Status = EducationPlanGenerationStatus.Processing; RunId = runId; HeartbeatAtUtc = now; LeaseExpiresAtUtc = now.AddSeconds(leaseSeconds); return true;
    }
    public bool Heartbeat(Guid runId, DateTime now, int leaseSeconds)
    {
        if (!Owned(runId, now) || leaseSeconds is < 10 or > 600) return false;
        HeartbeatAtUtc = now; LeaseExpiresAtUtc = now.AddSeconds(leaseSeconds); return true;
    }
    public void RecordModel(Guid runId, string provider, string alias, string version, Guid? deploymentId, string promptHash, int outputTokenLimit, double temperature, DateTime now)
    {
        RequireRun(runId, now); if (ModelAlias != null) throw Invalid();
        ModelAlias = Check.NotNullOrWhiteSpace(alias, nameof(alias), 128); ModelVersion = Check.NotNullOrWhiteSpace(version, nameof(version), 64); DeploymentId = deploymentId;
        ModelProvider = Check.NotNullOrWhiteSpace(provider, nameof(provider), 64); if (outputTokenLimit is < 1 or > 8192 || !double.IsFinite(temperature) || temperature is < 0 or > 2) throw Invalid();
        OutputTokenLimit = outputTokenLimit; Temperature = temperature;
        PromptHash = EducationAssessmentTaskRevision.RequireFingerprint(promptHash); UsageMayBeIncomplete = true;
    }
    public void RecordUsage(Guid runId, int? promptTokens, int? completionTokens, long duration, decimal? cost, string? outputHash, DateTime now)
    {
        RequireRun(runId, now); if (promptTokens < 0 || completionTokens < 0 || duration < 0 || cost < 0 || ModelAlias == null) throw Invalid();
        PromptTokens = promptTokens; CompletionTokens = completionTokens; DurationMilliseconds = duration; EstimatedCostUsd = cost;
        OutputHash = outputHash == null ? null : EducationAssessmentTaskRevision.RequireFingerprint(outputHash); UsageMayBeIncomplete = !promptTokens.HasValue || !completionTokens.HasValue;
    }
    public void Complete(Guid runId, EducationTeachingPlanContent content, DateTime now)
    {
        RequireRun(runId, now); EducationTeachingPlanGenerationOutput.Validate(content, Request(), Basis());
        if (ModelAlias == null || OutputHash == null) throw Invalid();
        ContentJson = EducationLearningReportJson.Write(content, 256 * 1024); Status = EducationPlanGenerationStatus.Completed; CompletedAtUtc = now; LeaseExpiresAtUtc = null;
    }
    public void Fail(Guid runId, EducationPlanGenerationFailure failure, DateTime now)
    {
        EducationRules.RequireUtc(now);
        if (Status != EducationPlanGenerationStatus.Processing || RunId != runId || failure == EducationPlanGenerationFailure.None || !Enum.IsDefined(failure)) throw Invalid();
        Failure = failure; Status = EducationPlanGenerationStatus.Failed; CompletedAtUtc = now; LeaseExpiresAtUtc = null; ContentJson = null;
    }
    private bool Owned(Guid runId, DateTime now) => now.Kind == DateTimeKind.Utc && Status == EducationPlanGenerationStatus.Processing && RunId == runId && LeaseExpiresAtUtc > now;
    private void RequireRun(Guid runId, DateTime now) { if (!Owned(runId, now)) throw new BusinessException(EducationPlanGenerationErrors.Changed); }
    private static BusinessException Invalid() => new(EducationPlanGenerationErrors.Invalid);
}
