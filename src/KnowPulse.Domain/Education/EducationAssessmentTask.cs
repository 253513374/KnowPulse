using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using KnowPulse.Exam;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

/// <summary>只有当前修订指针可推进；内容、发布回执及固定名单独立保留。</summary>
public class EducationAssessmentTask : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid? CurrentRevisionId { get; private set; }
    public int CurrentRevisionNumber { get; private set; }
    public Guid? PublishedRevisionId { get; private set; }
    public Guid? ClosureId { get; private set; }
    public Guid? CurrentResultReleaseId { get; private set; }
    public int ConfirmedGradeSetVersion { get; private set; }
    public string CreateFingerprint { get; private set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; private set; }

    protected EducationAssessmentTask() { }

    public void RecordConfirmedGrade(EducationAssessmentQuestionGrade grade, EducationAssessmentGradeRevision revision)
    {
        if (grade.TaskId != Id || grade.TenantId != TenantId || grade.OwnerUserId != OwnerUserId ||
            revision.QuestionGradeId != grade.Id || grade.CurrentConfirmedRevisionId != revision.Id ||
            revision.ExpectedGradeSetVersion != ConfirmedGradeSetVersion || revision.ResultGradeSetVersion != ConfirmedGradeSetVersion + 1)
            throw new BusinessException(EducationAssessmentErrorCodes.InvalidGrade);
        ConfirmedGradeSetVersion++;
    }

    public void Close(EducationAssessmentClosure closure)
    {
        if (ClosureId.HasValue || closure.TaskId != Id || closure.TenantId != TenantId || closure.TaskRevisionId != PublishedRevisionId)
            throw new BusinessException(EducationAssessmentErrorCodes.InvalidResultRelease);
        ClosureId = closure.Id;
    }

    public void Release(EducationAssessmentResultRelease release)
    {
        if (release.TaskId != Id || release.TenantId != TenantId || release.ClosureId != ClosureId ||
            release.TaskRevisionId != PublishedRevisionId || release.PreviousReleaseId != CurrentResultReleaseId ||
            release.ConfirmedGradeSetVersion != ConfirmedGradeSetVersion)
            throw new BusinessException(EducationAssessmentErrorCodes.InvalidResultRelease);
        CurrentResultReleaseId = release.Id;
    }

    public EducationAssessmentTask(Guid id, Guid? tenantId, Guid ownerUserId, string createFingerprint, DateTime now)
        : base(EducationRules.RequireId(id))
    {
        TenantId = tenantId;
        OwnerUserId = EducationRules.RequireId(ownerUserId);
        CreateFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(createFingerprint);
        UpdatedAtUtc = EducationRules.RequireUtc(now);
    }

    public void AdvanceDraft(EducationAssessmentTaskRevision revision)
    {
        if (revision.PublishedAtUtc.HasValue) throw new BusinessException(EducationAssessmentErrorCodes.InvalidRevision);
        if (PublishedRevisionId.HasValue) throw new BusinessException(EducationAssessmentErrorCodes.AlreadyPublished);
        Advance(revision);
    }

    public void Publish(EducationAssessmentTaskRevision revision)
    {
        if (!revision.PublishedAtUtc.HasValue) throw new BusinessException(EducationAssessmentErrorCodes.InvalidRevision);
        if (PublishedRevisionId == revision.Id && CurrentRevisionId == revision.Id && revision.TaskId == Id &&
            revision.TenantId == TenantId && revision.OwnerUserId == OwnerUserId &&
            revision.RevisionNumber == CurrentRevisionNumber) return;
        if (PublishedRevisionId.HasValue) throw new BusinessException(EducationAssessmentErrorCodes.AlreadyPublished);
        if (!CurrentRevisionId.HasValue)
            throw new BusinessException(EducationAssessmentErrorCodes.InvalidRevision);
        Advance(revision);
        PublishedRevisionId = revision.Id;
    }

    private void Advance(EducationAssessmentTaskRevision revision)
    {
        if (revision.TaskId != Id || revision.TenantId != TenantId || revision.OwnerUserId != OwnerUserId ||
            revision.RevisionNumber != CurrentRevisionNumber + 1 || revision.BasedOnRevisionId != CurrentRevisionId ||
            revision.RecordedAtUtc < UpdatedAtUtc)
            throw new BusinessException(EducationAssessmentErrorCodes.InvalidRevision);
        CurrentRevisionId = revision.Id;
        CurrentRevisionNumber = revision.RevisionNumber;
        UpdatedAtUtc = revision.RecordedAtUtc;
    }
}

/// <summary>Id 是创建本修订的稳定操作号；草稿保存和发布均只新增修订。</summary>
public class EducationAssessmentTaskRevision : CreationAuditedEntity<Guid>, IMultiTenant
{
    public const int MaxRecipients = 3000;
    public Guid? TenantId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid TaskId { get; private set; }
    public int RevisionNumber { get; private set; }
    public Guid? BasedOnRevisionId { get; private set; }
    public string RequestFingerprint { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Instructions { get; private set; } = string.Empty;
    public Guid SchoolId { get; private set; }
    public Guid AcademicTermId { get; private set; }
    public Guid SchoolClassId { get; private set; }
    public string SchoolClassName { get; private set; } = string.Empty;
    public Guid TeachingAssignmentId { get; private set; }
    public Guid ExamPaperId { get; private set; }
    public string CurriculumSnapshotJson { get; private set; } = string.Empty;
    public EducationAssessmentRecipientScope RecipientScope { get; private set; }
    public string SelectedStudentIdsJson { get; private set; } = "[]";
    public DateTime StartsAtUtc { get; private set; }
    public DateTime EndsAtUtc { get; private set; }
    public int DurationMinutes { get; private set; }
    public int MaxAttempts { get; private set; }
    public ExamResultVisibility ResultVisibility { get; private set; }
    public bool AllowWrongQuestionCollection { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public DateTime? PublishedAtUtc { get; private set; }
    public string? RosterFingerprint { get; private set; }

    protected EducationAssessmentTaskRevision() { }

    public EducationAssessmentTaskRevision(Guid id, Guid? tenantId, Guid ownerUserId, Guid taskId, int revisionNumber,
        Guid? basedOnRevisionId, string title, string instructions, Guid schoolId, Guid academicTermId,
        Guid schoolClassId, string schoolClassName, Guid teachingAssignmentId, Guid examPaperId,
        string curriculumSnapshotJson, EducationAssessmentRecipientScope recipientScope,
        IReadOnlyCollection<Guid> selectedStudentUserIds, DateTime startsAtUtc, DateTime endsAtUtc,
        int durationMinutes, int maxAttempts, ExamResultVisibility resultVisibility,
        bool allowWrongQuestionCollection, string requestFingerprint, DateTime now,
        DateTime? publishedAtUtc = null, string? rosterFingerprint = null) : base(EducationRules.RequireId(id))
    {
        if (revisionNumber < 1 || basedOnRevisionId == Guid.Empty || basedOnRevisionId == id ||
            (revisionNumber == 1) != !basedOnRevisionId.HasValue)
            throw new BusinessException(EducationAssessmentErrorCodes.InvalidRevision);
        EducationRules.ValidatePeriod(startsAtUtc, endsAtUtc);
        EducationRules.RequireUtc(now);
        if (durationMinutes is < 1 or > 1440 || maxAttempts is < 1 or > 20 || !Enum.IsDefined(resultVisibility))
            throw new BusinessException(EducationAssessmentErrorCodes.InvalidDefinition);
        if (publishedAtUtc.HasValue)
        {
            EducationRules.RequireUtc(publishedAtUtc.Value);
            if (!basedOnRevisionId.HasValue || publishedAtUtc.Value != now || publishedAtUtc.Value >= endsAtUtc)
                throw new BusinessException(EducationAssessmentErrorCodes.InvalidRevision);
            RosterFingerprint = RequireFingerprint(rosterFingerprint);
        }
        else if (rosterFingerprint != null)
            throw new BusinessException(EducationAssessmentErrorCodes.InvalidRevision);
        if (selectedStudentUserIds == null || !Enum.IsDefined(recipientScope) ||
            (recipientScope == EducationAssessmentRecipientScope.FullClass && selectedStudentUserIds.Count != 0) ||
            (recipientScope == EducationAssessmentRecipientScope.SelectedStudents && selectedStudentUserIds.Count == 0) ||
            selectedStudentUserIds.Any(x => x == Guid.Empty) || selectedStudentUserIds.Distinct().Count() != selectedStudentUserIds.Count)
            throw new BusinessException(EducationAssessmentErrorCodes.InvalidRecipientScope);
        if (selectedStudentUserIds.Count > MaxRecipients)
            throw new BusinessException(EducationAssessmentErrorCodes.CapacityExceeded);

        TenantId = tenantId;
        OwnerUserId = EducationRules.RequireId(ownerUserId);
        TaskId = EducationRules.RequireId(taskId);
        RevisionNumber = revisionNumber;
        BasedOnRevisionId = basedOnRevisionId;
        RequestFingerprint = RequireFingerprint(requestFingerprint);
        Title = Check.NotNullOrWhiteSpace(title, nameof(title), 256).Trim();
        Instructions = Check.NotNullOrWhiteSpace(instructions, nameof(instructions), 8192).Trim();
        SchoolId = EducationRules.RequireId(schoolId);
        AcademicTermId = EducationRules.RequireId(academicTermId);
        SchoolClassId = EducationRules.RequireId(schoolClassId);
        SchoolClassName = Check.NotNullOrWhiteSpace(schoolClassName, nameof(schoolClassName), 128);
        TeachingAssignmentId = EducationRules.RequireId(teachingAssignmentId);
        ExamPaperId = EducationRules.RequireId(examPaperId);
        CurriculumSnapshotJson = Check.NotNullOrWhiteSpace(curriculumSnapshotJson, nameof(curriculumSnapshotJson), 200000);
        RecipientScope = recipientScope;
        SelectedStudentIdsJson = JsonSerializer.Serialize(selectedStudentUserIds.OrderBy(x => x).ToArray());
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        DurationMinutes = durationMinutes;
        MaxAttempts = maxAttempts;
        ResultVisibility = resultVisibility;
        AllowWrongQuestionCollection = allowWrongQuestionCollection;
        RecordedAtUtc = now;
        PublishedAtUtc = publishedAtUtc;
    }

    public void EnsureCanStart(DateTime now)
    {
        EducationRules.RequireUtc(now);
        if (!PublishedAtUtc.HasValue || now < PublishedAtUtc.Value || now < StartsAtUtc || now >= EndsAtUtc)
            throw new BusinessException(EducationAssessmentErrorCodes.OutsideWindow);
    }

    public DateTime GetAttemptDeadline(DateTime startedAtUtc)
    {
        EnsureCanStart(startedAtUtc);
        var taskRemaining = EndsAtUtc - startedAtUtc;
        var duration = TimeSpan.FromMinutes(DurationMinutes);
        return taskRemaining <= duration ? EndsAtUtc : startedAtUtc.Add(duration);
    }

    internal static string RequireFingerprint(string? fingerprint)
    {
        if (fingerprint == null || fingerprint.Length != 64 || fingerprint.Any(x => !Uri.IsHexDigit(x)))
            throw new BusinessException(EducationAssessmentErrorCodes.InvalidDefinition);
        return fingerprint;
    }
}

/// <summary>发布时固定实际账号、成员和在班关系；开始次数由此聚合的 CAS 保护。</summary>
public class EducationAssessmentRecipient : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid TaskRevisionId { get; private set; }
    public Guid SchoolId { get; private set; }
    public Guid SchoolClassId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid SchoolMembershipId { get; private set; }
    public Guid StudentEnrollmentId { get; private set; }
    public string StudentName { get; private set; } = string.Empty;
    public string AccountName { get; private set; } = string.Empty;
    public int StartedAttemptCount { get; private set; }
    public Guid? CurrentAttemptId { get; private set; }

    protected EducationAssessmentRecipient() { }

    public EducationAssessmentRecipient(Guid id, EducationAssessmentTaskRevision revision, Guid studentUserId,
        Guid schoolMembershipId, Guid studentEnrollmentId, string studentName, string accountName)
        : base(EducationRules.RequireId(id))
    {
        if (!revision.PublishedAtUtc.HasValue) throw new BusinessException(EducationAssessmentErrorCodes.InvalidRevision);
        if (revision.RecipientScope == EducationAssessmentRecipientScope.SelectedStudents &&
            !(JsonSerializer.Deserialize<Guid[]>(revision.SelectedStudentIdsJson) ?? []).Contains(studentUserId))
            throw new BusinessException(EducationAssessmentErrorCodes.InvalidRecipientScope);
        TenantId = revision.TenantId;
        TaskId = revision.TaskId;
        TaskRevisionId = revision.Id;
        SchoolId = revision.SchoolId;
        SchoolClassId = revision.SchoolClassId;
        StudentUserId = EducationRules.RequireId(studentUserId);
        SchoolMembershipId = EducationRules.RequireId(schoolMembershipId);
        StudentEnrollmentId = EducationRules.RequireId(studentEnrollmentId);
        StudentName = Check.NotNullOrWhiteSpace(studentName, nameof(studentName), 256);
        AccountName = Check.NotNullOrWhiteSpace(accountName, nameof(accountName), 256);
    }

    public void RecordStartedAttempt(EducationAssessmentAttempt attempt)
    {
        if (attempt.TenantId != TenantId || attempt.TaskId != TaskId || attempt.TaskRevisionId != TaskRevisionId ||
            attempt.RecipientId != Id || attempt.StudentUserId != StudentUserId)
            throw new BusinessException(EducationAssessmentErrorCodes.RecipientUnavailable);
        if (CurrentAttemptId == attempt.Id && StartedAttemptCount == attempt.AttemptNumber) return;
        // No submit or retake use case exists in this slice: another start cannot reset this attempt's clock.
        if (StartedAttemptCount != 0 || attempt.AttemptNumber != 1)
            throw new BusinessException(EducationAssessmentErrorCodes.AlreadyStarted);
        StartedAttemptCount = attempt.AttemptNumber;
        CurrentAttemptId = attempt.Id;
    }
}

/// <summary>本次开始及截止不可修改；恢复不产生新次数，不进入企业 ExamPlan。</summary>
public class EducationAssessmentAttempt : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid TaskRevisionId { get; private set; }
    public Guid RecipientId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid ExamPaperId { get; private set; }
    public int AttemptNumber { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public int ResponseSetVersion { get; private set; }
    public DateTime? LastSavedAtUtc { get; private set; }
    public Guid? SubmissionId { get; private set; }
    public EducationAssessmentAttemptStatus Status => SubmissionId.HasValue
        ? EducationAssessmentAttemptStatus.Submitted : EducationAssessmentAttemptStatus.InProgress;

    protected EducationAssessmentAttempt() { }

    public EducationAssessmentAttempt(Guid id, EducationAssessmentTaskRevision revision,
        EducationAssessmentRecipient recipient, DateTime startedAtUtc) : base(EducationRules.RequireId(id))
    {
        if (revision.TenantId != recipient.TenantId || revision.TaskId != recipient.TaskId ||
            revision.Id != recipient.TaskRevisionId || revision.SchoolId != recipient.SchoolId ||
            revision.SchoolClassId != recipient.SchoolClassId)
            throw new BusinessException(EducationAssessmentErrorCodes.RecipientUnavailable);
        if (recipient.StartedAttemptCount != 0 || recipient.CurrentAttemptId.HasValue)
            throw new BusinessException(EducationAssessmentErrorCodes.AlreadyStarted);
        TenantId = revision.TenantId;
        TaskId = revision.TaskId;
        TaskRevisionId = revision.Id;
        RecipientId = recipient.Id;
        StudentUserId = recipient.StudentUserId;
        ExamPaperId = revision.ExamPaperId;
        AttemptNumber = 1;
        StartedAtUtc = EducationRules.RequireUtc(startedAtUtc);
        ExpiresAtUtc = revision.GetAttemptDeadline(startedAtUtc);
    }

    public void EnsureCanResume(DateTime now)
    {
        EducationRules.RequireUtc(now);
        if (SubmissionId.HasValue) throw new BusinessException(EducationAssessmentErrorCodes.AttemptClosed);
        if (now < StartedAtUtc) throw new BusinessException(EducationAssessmentErrorCodes.OutsideWindow);
        if (now >= ExpiresAtUtc) throw new BusinessException(EducationAssessmentErrorCodes.AttemptExpired);
    }

    public void EnsureCanEdit(DateTime now, int expectedResponseSetVersion)
    {
        EnsureCanResume(now);
        if (expectedResponseSetVersion < 0 || expectedResponseSetVersion != ResponseSetVersion)
            throw new BusinessException(EducationAssessmentErrorCodes.ResponseVersionConflict);
    }

    public void RecordResponse(EducationAssessmentResponseRevision revision, DateTime now)
    {
        EnsureCanEdit(now, revision.ExpectedResponseSetVersion);
        if (revision.TenantId != TenantId || revision.AttemptId != Id || revision.StudentUserId != StudentUserId ||
            revision.ExamPaperId != ExamPaperId || revision.ResultResponseSetVersion != ResponseSetVersion + 1 ||
            revision.SavedAtUtc < StartedAtUtc || revision.SavedAtUtc >= ExpiresAtUtc || revision.SavedAtUtc > now ||
            (LastSavedAtUtc.HasValue && revision.SavedAtUtc < LastSavedAtUtc.Value))
            throw new BusinessException(EducationAssessmentErrorCodes.InvalidResponse);
        ResponseSetVersion = revision.ResultResponseSetVersion;
        LastSavedAtUtc = revision.SavedAtUtc;
    }

    public void EnsureCanSeal(EducationAssessmentSubmissionCause cause, DateTime now, int expectedResponseSetVersion)
    {
        EducationRules.RequireUtc(now);
        if (!Enum.IsDefined(cause)) throw new BusinessException(EducationAssessmentErrorCodes.InvalidSubmission);
        if (SubmissionId.HasValue) throw new BusinessException(EducationAssessmentErrorCodes.AttemptClosed);
        if (expectedResponseSetVersion < 0 || expectedResponseSetVersion != ResponseSetVersion)
            throw new BusinessException(EducationAssessmentErrorCodes.ResponseVersionConflict);
        if (cause == EducationAssessmentSubmissionCause.Manual) EnsureCanResume(now);
        else if (now < ExpiresAtUtc) throw new BusinessException(EducationAssessmentErrorCodes.OutsideWindow);
    }

    public void Seal(EducationAssessmentSubmission submission)
    {
        if (submission.TenantId != TenantId || submission.AttemptId != Id || submission.StudentUserId != StudentUserId ||
            submission.ExamPaperId != ExamPaperId || submission.ResponseSetVersion != ResponseSetVersion)
            throw new BusinessException(EducationAssessmentErrorCodes.InvalidSubmission);
        if (SubmissionId == submission.Id) return;
        EnsureCanSeal(submission.Cause, submission.SealedAtUtc, submission.ResponseSetVersion);
        SubmissionId = submission.Id;
    }
}
