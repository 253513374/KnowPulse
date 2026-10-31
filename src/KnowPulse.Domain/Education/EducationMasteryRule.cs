using System;
using System.Security.Cryptography;
using System.Text;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

public class EducationMasteryRule : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid SchoolId { get; private set; }
    public Guid EducationSubjectId { get; private set; }
    public int RevisionNumber { get; private set; }
    public Guid? CurrentRevisionId { get; private set; }
    protected EducationMasteryRule() { }
    public EducationMasteryRule(Guid? tenant, Guid school, Guid subject) : base(IdFor(tenant, school, subject))
    { TenantId = tenant; SchoolId = school; EducationSubjectId = subject; }
    public void Advance(EducationMasteryRuleRevision revision)
    {
        if (revision.RuleId != Id || revision.TenantId != TenantId || revision.SchoolId != SchoolId || revision.EducationSubjectId != EducationSubjectId ||
            revision.RevisionNumber != RevisionNumber + 1 || revision.BasedOnRevisionId != CurrentRevisionId || revision.ExpectedConcurrencyStamp != ConcurrencyStamp)
            throw new BusinessException(EducationLearningErrorCodes.InvalidRule);
        CurrentRevisionId = revision.Id; RevisionNumber = revision.RevisionNumber;
    }
    public static Guid IdFor(Guid? tenant, Guid school, Guid subject)
    {
        EducationRules.RequireId(school); EducationRules.RequireId(subject); if (tenant == Guid.Empty) throw new BusinessException(EducationLearningErrorCodes.InvalidRule);
        return new(SHA256.HashData(Encoding.UTF8.GetBytes($"education-mastery-rule-v1|{tenant:D}|{school:D}|{subject:D}")).AsSpan(0, 16));
    }
}

public class EducationMasteryRuleRevision : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid RuleId { get; private set; }
    public Guid SchoolId { get; private set; }
    public Guid EducationSubjectId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public int RevisionNumber { get; private set; }
    public Guid? BasedOnRevisionId { get; private set; }
    public bool IsEnabled { get; private set; }
    public int WindowDays { get; private set; }
    public int MinimumIndependentQuestionCount { get; private set; }
    public int MinimumAssessmentCount { get; private set; }
    public int MaximumLatestEvidenceAgeDays { get; private set; }
    public decimal TargetRatio { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string ExpectedConcurrencyStamp { get; private set; } = string.Empty;
    public string RequestFingerprint { get; private set; } = string.Empty;
    public DateTime RecordedAtUtc { get; private set; }
    protected EducationMasteryRuleRevision() { }
    public EducationMasteryRuleRevision(Guid id, EducationMasteryRule root, Guid actor, bool enabled, int days, int minimumQuestions, int minimumAssessments,
        int maximumAge, decimal target, string reason, string stamp, string fingerprint, DateTime now) : base(EducationRules.RequireId(id))
    {
        if (root.ConcurrencyStamp != stamp || days is < 1 or > 730 || minimumQuestions is < 1 or > 1000 || minimumAssessments is < 1 or > 1000 ||
            maximumAge < 1 || maximumAge > days || target is <= 0 or > 1 || decimal.Round(target, 4) != target) throw new BusinessException(EducationLearningErrorCodes.InvalidRule);
        TenantId = root.TenantId; RuleId = root.Id; SchoolId = root.SchoolId; EducationSubjectId = root.EducationSubjectId; ActorUserId = EducationRules.RequireId(actor);
        RevisionNumber = checked(root.RevisionNumber + 1); BasedOnRevisionId = root.CurrentRevisionId; IsEnabled = enabled; WindowDays = days;
        MinimumIndependentQuestionCount = minimumQuestions; MinimumAssessmentCount = minimumAssessments; MaximumLatestEvidenceAgeDays = maximumAge; TargetRatio = target;
        Reason = Check.NotNullOrWhiteSpace(reason, nameof(reason), 2048); ExpectedConcurrencyStamp = Check.NotNullOrWhiteSpace(stamp, nameof(stamp), 40);
        RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint); RecordedAtUtc = EducationRules.RequireUtc(now);
    }
    public EducationMasteryCalculationRule? CalculationRule() => IsEnabled ? new() { Id = Id, TenantId = TenantId, SchoolId = SchoolId, EducationSubjectId = EducationSubjectId,
        RevisionNumber = RevisionNumber, WindowDays = WindowDays, MinimumIndependentQuestionCount = MinimumIndependentQuestionCount,
        MinimumAssessmentCount = MinimumAssessmentCount, MaximumLatestEvidenceAgeDays = MaximumLatestEvidenceAgeDays, TargetRatio = TargetRatio } : null;
}
