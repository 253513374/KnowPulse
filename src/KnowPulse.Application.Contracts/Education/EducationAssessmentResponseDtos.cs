using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.Education;

public class SaveEducationAssessmentResponseDto
{
    public Guid OperationId { get; set; }
    public Guid PaperQuestionId { get; set; }
    [Range(1, int.MaxValue)] public int PaperSnapshotVersion { get; set; }
    [Range(0, int.MaxValue)] public int ExpectedResponseSetVersion { get; set; }
    [Required] public EducationAssessmentResponseValue Response { get; set; } = new();
}

public class SubmitEducationAssessmentDto
{
    public Guid OperationId { get; set; }
    [Range(1, int.MaxValue)] public int PaperSnapshotVersion { get; set; }
    [Range(0, int.MaxValue)] public int ExpectedResponseSetVersion { get; set; }
    public bool ConfirmSavedOnly { get; set; }
    [StringLength(64, MinimumLength = 64)] public string? ExpectedPreparationFingerprint { get; set; }
}

public class EducationAssessmentSubmissionPreviewDto
{
    public Guid AttemptId { get; set; }
    public int PaperSnapshotVersion { get; set; }
    public int ResponseSetVersion { get; set; }
    public DateTime ServerNowUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public bool CanSubmit { get; set; }
    public Guid? SubmissionId { get; set; }
    public string Fingerprint { get; set; } = "";
    public List<EducationAssessmentSubmissionQuestionDto> Questions { get; set; } = [];
    public List<EducationAssessmentAnswerImageDto> UnlinkedImages { get; set; } = [];
}

public class EducationAssessmentSubmissionQuestionDto : EntityDto<Guid>
{
    public int DisplayOrder { get; set; }
    public string Title { get; set; } = "";
    public Guid? SavedResponseRevisionId { get; set; }
    public bool IsUnanswered { get; set; }
    public int UnansweredBlankCount { get; set; }
    public int SavedImageCount { get; set; }
}

public class UploadEducationAssessmentAnswerImageDto
{
    public Guid OperationId { get; set; }
    public Guid PaperQuestionId { get; set; }
    [Required, StringLength(64, MinimumLength = 64)] public string ContentHash { get; set; } = string.Empty;
}

/// <summary>精确保存操作的冻结回执，内容是本人原始作答。</summary>
public class EducationAssessmentResponseRevisionDto : EntityDto<Guid>
{
    public Guid AttemptId { get; set; }
    public Guid PaperQuestionId { get; set; }
    public int PaperSnapshotVersion { get; set; }
    public int ExpectedResponseSetVersion { get; set; }
    public int ResultResponseSetVersion { get; set; }
    public string RequestFingerprint { get; set; } = string.Empty;
    public string ResponseHash { get; set; } = string.Empty;
    public EducationAssessmentResponseValue Response { get; set; } = new();
    public DateTime SavedAtUtc { get; set; }
}

public class EducationAssessmentSubmissionDto : EntityDto<Guid>
{
    public Guid AttemptId { get; set; }
    public int PaperSnapshotVersion { get; set; }
    public int ResponseSetVersion { get; set; }
    public EducationAssessmentSubmissionCause Cause { get; set; }
    public string RequestFingerprint { get; set; } = string.Empty;
    public bool ConfirmedSavedOnly { get; set; }
    public DateTime EffectiveAtUtc { get; set; }
    public DateTime SealedAtUtc { get; set; }
    public List<EducationAssessmentSubmissionManifestEntry> Manifest { get; set; } = [];
}

/// <summary>本人答卷当前的图片选择限制，实际上传仍由服务端验证。</summary>
public class EducationAssessmentAnswerImagePolicyDto
{
    public long MaxFileBytes { get; set; }
    public int MaxImagesPerResponse { get; set; }
    public List<string> AllowedContentTypes { get; set; } = [];
}

/// <summary>上传确认和当前答案关联分开；待完成上传不算有效答案附件。</summary>
public class EducationAssessmentAnswerImageDto : EntityDto<Guid>
{
    public Guid AttemptId { get; set; }
    public Guid PaperQuestionId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public DateTime RequestedAtUtc { get; set; }
    public bool IsUploaded { get; set; }
    public DateTime? UploadedAtUtc { get; set; }
    public Guid? CurrentResponseRevisionId { get; set; }
}

public class GetEducationAssessmentDraftOptionsInput : PagedResultRequestDto
{
    [Required] public EducationCurriculumScopeDto Scope { get; set; } = new();

    public GetEducationAssessmentDraftOptionsInput() { MaxResultCount = 20; }
}

public class EducationAssessmentDraftOptionsDto
{
    public DateTime EarliestStartsAtUtc { get; set; }
    public DateTime LatestEndsAtUtc { get; set; }
    public List<EducationAssessmentRosterStudentDto> Students { get; set; } = [];
    public List<EducationAssessmentSavedPaperOptionDto> SavedPapers { get; set; } = [];
    public bool HasMoreSavedPapers { get; set; }
    public int NextPaperSkipCount { get; set; }
}

public class EducationAssessmentSavedPaperOptionDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int QuestionCount { get; set; }
    public decimal TotalScore { get; set; }
}
