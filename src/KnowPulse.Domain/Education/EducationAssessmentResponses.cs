using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace KnowPulse.Education;

/// <summary>Id 就是保存操作号；本题原始响应与该操作的回执是同一不可变事实。</summary>
public class EducationAssessmentResponseRevision : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid AttemptId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid ExamPaperId { get; private set; }
    public Guid PaperQuestionId { get; private set; }
    public int PaperSnapshotVersion { get; private set; }
    public int ExpectedResponseSetVersion { get; private set; }
    public int ResultResponseSetVersion { get; private set; }
    public string RequestFingerprint { get; private set; } = string.Empty;
    public string ResponseJson { get; private set; } = string.Empty;
    public string ResponseHash { get; private set; } = string.Empty;
    public DateTime SavedAtUtc { get; private set; }

    protected EducationAssessmentResponseRevision() { }

    public EducationAssessmentResponseRevision(Guid id, EducationAssessmentAttempt attempt, Guid paperQuestionId,
        int paperSnapshotVersion, int expectedResponseSetVersion, EducationAssessmentResponseValue response,
        string requestFingerprint, DateTime savedAtUtc) : base(EducationRules.RequireId(id))
    {
        attempt.EnsureCanEdit(savedAtUtc, expectedResponseSetVersion);
        if (paperSnapshotVersion < 1 || expectedResponseSetVersion == int.MaxValue || response == null)
            throw new BusinessException(EducationAssessmentErrorCodes.InvalidResponse);
        TenantId = attempt.TenantId;
        AttemptId = attempt.Id;
        StudentUserId = attempt.StudentUserId;
        ExamPaperId = attempt.ExamPaperId;
        PaperQuestionId = EducationRules.RequireId(paperQuestionId);
        PaperSnapshotVersion = paperSnapshotVersion;
        ExpectedResponseSetVersion = expectedResponseSetVersion;
        ResultResponseSetVersion = expectedResponseSetVersion + 1;
        RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(requestFingerprint);
        ResponseJson = response.ToCanonicalJson();
        ResponseHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ResponseJson))).ToLowerInvariant();
        SavedAtUtc = savedAtUtc;
    }
}

/// <summary>每题只有当前修订指针；旧保存回执不随指针推进改变。</summary>
public class EducationAssessmentCurrentResponse : FullAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid AttemptId { get; private set; }
    public Guid ExamPaperId { get; private set; }
    public Guid PaperQuestionId { get; private set; }
    public Guid CurrentRevisionId { get; private set; }
    public int ResponseSetVersion { get; private set; }

    protected EducationAssessmentCurrentResponse() { }

    public EducationAssessmentCurrentResponse(Guid id, EducationAssessmentResponseRevision revision)
        : base(EducationRules.RequireId(id))
    {
        TenantId = revision.TenantId;
        AttemptId = revision.AttemptId;
        ExamPaperId = revision.ExamPaperId;
        PaperQuestionId = revision.PaperQuestionId;
        CurrentRevisionId = revision.Id;
        ResponseSetVersion = revision.ResultResponseSetVersion;
    }

    public void Advance(EducationAssessmentResponseRevision revision)
    {
        if (revision.TenantId != TenantId || revision.AttemptId != AttemptId || revision.ExamPaperId != ExamPaperId ||
            revision.PaperQuestionId != PaperQuestionId)
            throw new BusinessException(EducationAssessmentErrorCodes.InvalidResponse);
        if (CurrentRevisionId == revision.Id && ResponseSetVersion == revision.ResultResponseSetVersion) return;
        if (revision.ResultResponseSetVersion <= ResponseSetVersion)
            throw new BusinessException(EducationAssessmentErrorCodes.ResponseVersionConflict);
        CurrentRevisionId = revision.Id;
        ResponseSetVersion = revision.ResultResponseSetVersion;
    }
}

/// <summary>手动与到时封存共用一份提交事实；manifest 指向当时全部题的当前修订或未答。</summary>
public class EducationAssessmentSubmission : CreationAuditedEntity<Guid>, IMultiTenant
{
    public const int MaxQuestions = 3000;
    public const int MaxManifestJsonLength = 1000000;
    public Guid? TenantId { get; private set; }
    public Guid AttemptId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid ExamPaperId { get; private set; }
    public int PaperSnapshotVersion { get; private set; }
    public int ResponseSetVersion { get; private set; }
    public EducationAssessmentSubmissionCause Cause { get; private set; }
    public string RequestFingerprint { get; private set; } = string.Empty;
    public bool ConfirmedSavedOnly { get; private set; }
    public string ManifestJson { get; private set; } = string.Empty;
    public DateTime EffectiveAtUtc { get; private set; }
    public DateTime SealedAtUtc { get; private set; }

    protected EducationAssessmentSubmission() { }

    public EducationAssessmentSubmission(Guid id, EducationAssessmentAttempt attempt, int paperSnapshotVersion,
        EducationAssessmentSubmissionCause cause, string requestFingerprint, bool confirmedSavedOnly,
        IReadOnlyCollection<Guid> frozenPaperQuestionIds,
        IReadOnlyCollection<EducationAssessmentCurrentResponse> currentResponses,
        IReadOnlyCollection<EducationAssessmentResponseRevision> responseRevisions, DateTime now)
        : base(EducationRules.RequireId(id))
    {
        attempt.EnsureCanSeal(cause, now, attempt.ResponseSetVersion);
        if (paperSnapshotVersion < 1 || frozenPaperQuestionIds == null || currentResponses == null || responseRevisions == null ||
            frozenPaperQuestionIds.Count is < 1 or > MaxQuestions || frozenPaperQuestionIds.Any(x => x == Guid.Empty) ||
            frozenPaperQuestionIds.Distinct().Count() != frozenPaperQuestionIds.Count ||
            currentResponses.Count != responseRevisions.Count || currentResponses.Any(x => x == null) ||
            responseRevisions.Any(x => x == null) ||
            currentResponses.Select(x => x.PaperQuestionId).Distinct().Count() != currentResponses.Count ||
            responseRevisions.Select(x => x.Id).Distinct().Count() != responseRevisions.Count)
            throw Invalid();

        var frozenQuestions = frozenPaperQuestionIds.ToHashSet();
        var revisionsById = responseRevisions.ToDictionary(x => x.Id);
        foreach (var current in currentResponses)
        {
            if (current.TenantId != attempt.TenantId || current.AttemptId != attempt.Id || current.ExamPaperId != attempt.ExamPaperId ||
                !frozenQuestions.Contains(current.PaperQuestionId) ||
                !revisionsById.TryGetValue(current.CurrentRevisionId, out var revision) ||
                revision.TenantId != attempt.TenantId || revision.AttemptId != attempt.Id ||
                revision.StudentUserId != attempt.StudentUserId || revision.ExamPaperId != attempt.ExamPaperId ||
                revision.PaperQuestionId != current.PaperQuestionId || revision.PaperSnapshotVersion != paperSnapshotVersion ||
                revision.ResultResponseSetVersion != current.ResponseSetVersion ||
                revision.ResultResponseSetVersion > attempt.ResponseSetVersion || revision.SavedAtUtc > now ||
                revision.SavedAtUtc >= attempt.ExpiresAtUtc) throw Invalid();
        }
        if ((responseRevisions.Count == 0) != (attempt.ResponseSetVersion == 0) ||
            (responseRevisions.Count != 0 && responseRevisions.Max(x => x.ResultResponseSetVersion) != attempt.ResponseSetVersion))
            throw Invalid();

        if (cause == EducationAssessmentSubmissionCause.Manual && !confirmedSavedOnly &&
            (currentResponses.Count != frozenPaperQuestionIds.Count || responseRevisions.Any(x => EducationAssessmentResponseValue.Parse(x.ResponseJson).IsUnanswered())))
            throw new BusinessException(EducationAssessmentErrorCodes.IncompleteSubmission);
        if (cause == EducationAssessmentSubmissionCause.Deadline && confirmedSavedOnly) throw Invalid();
        var currentByQuestion = currentResponses.ToDictionary(x => x.PaperQuestionId);
        var manifest = frozenPaperQuestionIds.OrderBy(x => x).Select(questionId =>
        {
            var revision = currentByQuestion.TryGetValue(questionId, out var current) ? revisionsById[current.CurrentRevisionId] : null;
            return new EducationAssessmentSubmissionManifestEntry
            {
                PaperQuestionId = questionId, ResponseRevisionId = revision?.Id, ResponseHash = revision?.ResponseHash
            };
        }).ToArray();
        ManifestJson = JsonSerializer.Serialize(manifest);
        if (ManifestJson.Length > MaxManifestJsonLength) throw Invalid();
        TenantId = attempt.TenantId;
        AttemptId = attempt.Id;
        StudentUserId = attempt.StudentUserId;
        ExamPaperId = attempt.ExamPaperId;
        PaperSnapshotVersion = paperSnapshotVersion;
        ResponseSetVersion = attempt.ResponseSetVersion;
        Cause = cause;
        RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(requestFingerprint);
        ConfirmedSavedOnly = confirmedSavedOnly;
        EffectiveAtUtc = cause == EducationAssessmentSubmissionCause.Deadline ? attempt.ExpiresAtUtc : now;
        SealedAtUtc = now;
    }

    private static BusinessException Invalid() => new(EducationAssessmentErrorCodes.InvalidSubmission);
}

/// <summary>一张图片的稳定上传记录；Blob 成功和答案关联不是同一事实。</summary>
public class EducationAssessmentAnswerAttachment : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public const long MaxFileBytes = EducationAssessmentResponseValue.MaxImageFileBytes;
    public Guid? TenantId { get; private set; }
    public Guid AttemptId { get; private set; }
    public Guid StudentUserId { get; private set; }
    public Guid ExamPaperId { get; private set; }
    public Guid PaperQuestionId { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long Size { get; private set; }
    public string ContentHash { get; private set; } = string.Empty;
    public string BlobName { get; private set; } = string.Empty;
    public string RequestFingerprint { get; private set; } = string.Empty;
    public DateTime RequestedAtUtc { get; private set; }
    public DateTime? UploadedAtUtc { get; private set; }
    public bool IsUploaded => UploadedAtUtc.HasValue;

    protected EducationAssessmentAnswerAttachment() { }

    public EducationAssessmentAnswerAttachment(Guid id, EducationAssessmentAttempt attempt, Guid paperQuestionId,
        string fileName, string contentType, long size, string contentHash, string blobName,
        string requestFingerprint, DateTime requestedAtUtc) : base(EducationRules.RequireId(id))
    {
        attempt.EnsureCanEdit(requestedAtUtc, attempt.ResponseSetVersion);
        if (size is < 1 or > MaxFileBytes || contentType is not ("image/jpeg" or "image/png" or "image/webp") ||
            fileName == null || fileName.Contains('\r') || fileName.Contains('\n'))
            throw new BusinessException(EducationAssessmentErrorCodes.InvalidAttachment);
        TenantId = attempt.TenantId;
        AttemptId = attempt.Id;
        StudentUserId = attempt.StudentUserId;
        ExamPaperId = attempt.ExamPaperId;
        PaperQuestionId = EducationRules.RequireId(paperQuestionId);
        FileName = Check.NotNullOrWhiteSpace(fileName, nameof(fileName), 256);
        ContentType = contentType;
        Size = size;
        ContentHash = EducationAssessmentTaskRevision.RequireFingerprint(contentHash);
        BlobName = Check.NotNullOrWhiteSpace(blobName, nameof(blobName), 512);
        RequestFingerprint = EducationAssessmentTaskRevision.RequireFingerprint(requestFingerprint);
        RequestedAtUtc = requestedAtUtc;
    }

    public void MarkUploaded(EducationAssessmentAttempt attempt, DateTime now)
    {
        EducationRules.RequireUtc(now);
        if (attempt.TenantId != TenantId || attempt.Id != AttemptId || attempt.StudentUserId != StudentUserId ||
            attempt.ExamPaperId != ExamPaperId || now < RequestedAtUtc)
            throw new BusinessException(EducationAssessmentErrorCodes.InvalidAttachment);
        if (IsUploaded) return;
        attempt.EnsureCanEdit(now, attempt.ResponseSetVersion);
        UploadedAtUtc = now;
    }
}
