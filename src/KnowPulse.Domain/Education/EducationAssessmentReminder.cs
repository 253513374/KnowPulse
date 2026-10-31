using System;
using KnowPulse.Exam;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

public class EducationAssessmentReminder : AggregateRoot<Guid>, IMultiTenant
{
    public const int MinimumIntervalMinutes = 60;
    public Guid? TenantId { get; private set; }
    public Guid SchoolId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid TaskRevisionId { get; private set; }
    public Guid RecipientId { get; private set; }
    public Guid TeacherUserId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public string RequestFingerprint { get; private set; } = string.Empty;
    public EducationAssessmentReminderKind Kind { get; private set; }
    public Guid? ResultReleaseId { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public DateTime AvailableAtUtc { get; private set; }
    public DateTime? ReadAtUtc { get; private set; }

    protected EducationAssessmentReminder() { }

    public EducationAssessmentReminder(Guid id, EducationAssessmentRecipient recipient,
        EducationAssessmentTaskRevision revision, string fingerprint, DateTime now) : base(EducationRules.RequireId(id))
    {
        if (!revision.PublishedAtUtc.HasValue || recipient.TaskId != revision.TaskId || recipient.TaskRevisionId != revision.Id ||
            recipient.SchoolId != revision.SchoolId || recipient.TenantId != revision.TenantId) throw new BusinessException(EducationAssessmentErrorCodes.ReminderUnavailable);
        TenantId = recipient.TenantId; SchoolId = recipient.SchoolId; TaskId = recipient.TaskId; TaskRevisionId = revision.Id;
        RecipientId = recipient.Id; TeacherUserId = revision.OwnerUserId; StudentUserId = recipient.StudentUserId;
        RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(fingerprint); RecordedAtUtc = EducationRules.RequireUtc(now);
        AvailableAtUtc = RecordedAtUtc;
    }

    public static EducationAssessmentReminder Assigned(Guid id, EducationAssessmentRecipient recipient, EducationAssessmentTaskRevision revision, string fingerprint, DateTime now)
        => new(id, recipient, revision, fingerprint, now) { Kind = EducationAssessmentReminderKind.Assignment };

    public static EducationAssessmentReminder ResultReleased(Guid id, EducationAssessmentRecipient recipient, EducationAssessmentTaskRevision revision,
        EducationAssessmentResultRelease release, EducationAssessmentClosureEntry entry, string fingerprint, DateTime now)
    {
        if (release.ConfiguredVisibility == ExamResultVisibility.Hidden || !release.ScoreOpensAtUtc.HasValue || release.TenantId != recipient.TenantId ||
            release.TaskId != recipient.TaskId || release.TaskRevisionId != revision.Id || release.ConfirmedByUserId != revision.OwnerUserId ||
            entry.RecipientId != recipient.Id || entry.StudentUserId != recipient.StudentUserId) throw new BusinessException(EducationAssessmentErrorCodes.ReminderUnavailable);
        return new(id, recipient, revision, fingerprint, now) { Kind = EducationAssessmentReminderKind.ResultReleased, ResultReleaseId = release.Id,
            AvailableAtUtc = release.ScoreOpensAtUtc.Value > now ? release.ScoreOpensAtUtc.Value : now };
    }

    public void MarkRead(DateTime now)
    {
        EducationRules.RequireUtc(now);
        if (now < AvailableAtUtc) throw new BusinessException(EducationAssessmentErrorCodes.ReminderUnavailable);
        ReadAtUtc ??= now;
    }
}
