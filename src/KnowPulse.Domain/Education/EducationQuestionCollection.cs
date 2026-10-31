using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

public class EducationQuestionCollection : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid SchoolId { get; private set; }
    public Guid RecipientId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid TaskRevisionId { get; private set; }
    public Guid ExamPaperId { get; private set; }
    public Guid PaperQuestionId { get; private set; }
    public Guid? ResultReleaseId { get; private set; }
    public Guid? PreviousOperationId { get; private set; }
    public int RevisionNumber { get; private set; }
    public bool IsCollected { get; private set; }
    public string RequestFingerprint { get; private set; } = string.Empty;
    public DateTime RecordedAtUtc { get; private set; }
    protected EducationQuestionCollection() { }

    public EducationQuestionCollection(Guid id, EducationAssessmentRecipient recipient, EducationAssessmentTaskRevision revision, Guid questionId,
        bool collected, Guid? releaseId, EducationQuestionCollection? previous, string fingerprint, DateTime at) : base(EducationRules.RequireId(id))
    {
        if (recipient.TenantId != revision.TenantId || recipient.TaskId != revision.TaskId || recipient.TaskRevisionId != revision.Id || recipient.SchoolId != revision.SchoolId ||
            collected != releaseId.HasValue || releaseId == Guid.Empty || previous == null && !collected || previous != null &&
            (previous.TenantId != recipient.TenantId || previous.StudentUserId != recipient.StudentUserId || previous.RecipientId != recipient.Id || previous.PaperQuestionId != questionId ||
             previous.TaskRevisionId != revision.Id || previous.IsCollected == collected || previous.RecordedAtUtc > at))
            throw new BusinessException(EducationQuestionCollectionErrors.Invalid);
        TenantId = recipient.TenantId; StudentUserId = recipient.StudentUserId; SchoolId = revision.SchoolId;
        RecipientId = recipient.Id; TaskId = revision.TaskId; TaskRevisionId = revision.Id; ExamPaperId = revision.ExamPaperId;
        PaperQuestionId = EducationRules.RequireId(questionId); ResultReleaseId = releaseId; PreviousOperationId = previous?.Id;
        RevisionNumber = checked((previous?.RevisionNumber ?? 0) + 1); IsCollected = collected;
        RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint); RecordedAtUtc = EducationRules.RequireUtc(at);
    }
}
