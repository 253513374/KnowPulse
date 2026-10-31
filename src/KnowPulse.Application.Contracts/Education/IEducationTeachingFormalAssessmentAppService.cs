using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using KnowPulse.Exam;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.Education;

public interface IEducationTeachingFormalAssessmentAppService : IApplicationService
{
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), Authorize(EducationPermissions.AssessmentManage)]
    Task<PagedResultDto<EducationFormalCandidateDto>> GetFormalAssessmentCandidatesAsync(Guid activityId, PagedResultRequestDto input);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), Authorize(EducationPermissions.AssessmentManage)]
    Task<EducationFormalAssessmentPreviewDto> PreviewFormalAssessmentAsync(Guid activityId, Guid taskId);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), Authorize(EducationPermissions.AssessmentManage)]
    Task<EducationFormalBindingReceiptDto> BindFormalAssessmentAsync(Guid activityId, BindEducationFormalAssessmentDto input);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), Authorize(EducationPermissions.AssessmentManage)]
    Task<EducationFormalAssessmentDetailsDto?> GetFormalAssessmentAsync(Guid activityId);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), Authorize(EducationPermissions.AssessmentManage)]
    Task<EducationFormalReviewDto> ReviewFormalAssessmentAsync(Guid activityId, ReviewEducationFormalAssessmentDto input);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), Authorize(EducationPermissions.AssessmentManage)]
    Task<EducationFormalBindingReceiptDto?> GetFormalBindingOperationAsync(Guid activityId, Guid operationId);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), Authorize(EducationPermissions.AssessmentManage)]
    Task<EducationFormalReviewDto?> GetFormalReviewOperationAsync(Guid activityId, Guid operationId);
}
public class EducationFormalCandidateDto : EntityDto<Guid>
{
    public string Title { get; set; } = "";
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public int DurationMinutes { get; set; }
}
public class EducationFormalQuestionPreviewDto
{
    public Guid PaperQuestionId { get; set; }
    public string Title { get; set; } = "";
    public string Stem { get; set; } = "";
    public List<ExamPaperQuestionOptionDto> Options { get; set; } = [];
    public string? Analysis { get; set; }
    public List<string> ScoringPoints { get; set; } = [];
    public string? ReferenceAnswer { get; set; }
    public List<string> CorrectAnswers { get; set; } = [];
    public List<string> BlankAnswers { get; set; } = [];
}
public class EducationFormalAssessmentPreviewDto
{
    public Guid PlanId { get; set; }
    public Guid PlanRevisionId { get; set; }
    public Guid ActivityId { get; set; }
    public Guid TaskId { get; set; }
    public Guid TaskRevisionId { get; set; }
    public Guid RecipientId { get; set; }
    public Guid ExamPaperId { get; set; }
    public int PaperSnapshotVersion { get; set; }
    public string TaskTitle { get; set; } = "";
    public Guid? ExpectedActivityRevisionId { get; set; }
    public string ActivityConcurrencyStamp { get; set; } = "";
    public string PreviewFingerprint { get; set; } = "";
    public EducationStudentTeachingPlanContent PlanContent { get; set; } = new();
    public List<EducationFormalQuestionPreviewDto> Questions { get; set; } = [];
}
public class BindEducationFormalAssessmentDto
{
    public Guid OperationId { get; set; }
    public Guid TaskId { get; set; }
    public Guid ExpectedTaskRevisionId { get; set; }
    public int ExpectedPaperSnapshotVersion { get; set; }
    public Guid? ExpectedActivityRevisionId { get; set; }
    [Required, StringLength(40)] public string ActivityConcurrencyStamp { get; set; } = "";
    [Required, StringLength(64)] public string PreviewFingerprint { get; set; } = "";
    public bool ConfirmOriginalRelationships { get; set; }
    public bool ConfirmNewAssessmentRequirements { get; set; }
    public bool ConfirmExactPlanContainsNoHints { get; set; }
}
public class EducationFormalBindingReceiptDto : EntityDto<Guid>
{
    public Guid ActivityId { get; set; }
    public Guid TaskId { get; set; }
    public Guid TaskRevisionId { get; set; }
    public Guid RecipientId { get; set; }
    public int PaperSnapshotVersion { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public string RequestFingerprint { get; set; } = "";
}
public class ReviewEducationFormalAssessmentDto
{
    public Guid OperationId { get; set; }
    public Guid? ExpectedReviewId { get; set; }
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = "";
    public bool IsEnabled { get; set; }
    public EducationLearningExposureUse PlanContextUse { get; set; } = EducationLearningExposureUse.Unverified;
    public bool ConfirmExactContext { get; set; }
    [Required, StringLength(2048)] public string Reason { get; set; } = "";
}
public class EducationFormalReviewDto : EntityDto<Guid>
{
    public Guid BindingId { get; set; }
    public int RevisionNumber { get; set; }
    public Guid? BasedOnReviewId { get; set; }
    public bool IsEnabled { get; set; }
    public EducationLearningExposureUse PlanContextUse { get; set; }
    public string Reason { get; set; } = "";
    public DateTime RecordedAtUtc { get; set; }
    public string RequestFingerprint { get; set; } = "";
}
public class EducationFormalAssessmentDetailsDto
{
    public EducationFormalBindingReceiptDto Binding { get; set; } = new();
    public EducationFormalAssessmentPreviewDto Preview { get; set; } = new();
    public string ConcurrencyStamp { get; set; } = "";
    public EducationFormalReviewDto? CurrentReview { get; set; }
}
public class EducationActivityFormalAssessmentDto
{
    public bool RequiresAssessmentPermission { get; set; }
    public bool CanOpen { get; set; }
    public bool IsStopped { get; set; }
    public bool CanStart { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? RecipientId { get; set; }
    public Guid? AttemptId { get; set; }
    public string? Title { get; set; }
    public DateTime? SealedAtUtc { get; set; }
}
