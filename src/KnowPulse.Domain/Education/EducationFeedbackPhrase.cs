using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

public class EducationFeedbackPhrase : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid? CurrentRevisionId { get; private set; }
    public int RevisionNumber { get; private set; }
    public bool IsArchived { get; private set; }
    protected EducationFeedbackPhrase() { }
    public EducationFeedbackPhrase(Guid id, Guid? tenantId, Guid owner) : base(EducationRules.RequireId(id))
    { TenantId = tenantId; OwnerUserId = EducationRules.RequireId(owner); }

    public void Advance(EducationFeedbackPhraseRevision revision)
    {
        if (revision.PhraseId != Id || revision.OwnerUserId != OwnerUserId || revision.TenantId != TenantId ||
            revision.RevisionNumber != RevisionNumber + 1 || revision.BasedOnRevisionId != CurrentRevisionId || RevisionNumber == 0 && revision.IsArchived)
            throw new BusinessException(EducationFeedbackErrorCodes.Invalid);
        CurrentRevisionId = revision.Id; RevisionNumber = revision.RevisionNumber; IsArchived = revision.IsArchived;
    }
}

public class EducationFeedbackPhraseRevision : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid PhraseId { get; private set; }
    public int RevisionNumber { get; private set; }
    public Guid? BasedOnRevisionId { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public bool IsArchived { get; private set; }
    public string RequestFingerprint { get; private set; } = string.Empty;
    public DateTime RecordedAtUtc { get; private set; }
    protected EducationFeedbackPhraseRevision() { }
    public EducationFeedbackPhraseRevision(Guid id, EducationFeedbackPhrase phrase, string text, bool archived, string fingerprint, DateTime recordedAtUtc)
        : base(EducationRules.RequireId(id))
    {
        TenantId = phrase.TenantId; OwnerUserId = phrase.OwnerUserId; PhraseId = phrase.Id;
        RevisionNumber = checked(phrase.RevisionNumber + 1); BasedOnRevisionId = phrase.CurrentRevisionId;
        Text = Check.NotNullOrWhiteSpace(text, nameof(text), 4000); IsArchived = archived;
        RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint); RecordedAtUtc = EducationRules.RequireUtc(recordedAtUtc);
    }
}
