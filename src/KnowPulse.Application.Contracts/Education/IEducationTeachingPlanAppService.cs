using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.Education;

public interface IEducationTeachingPlanAppService : IApplicationService
{
    [Authorize(EducationPermissions.TeachingPlanGenerate), Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage)] Task<EducationTeachingPlanGenerationDto> GenerateDraftAsync(Guid planId, GenerateEducationTeachingPlanDraftDto input);
    [Authorize(EducationPermissions.TeachingPlanGenerate), Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage)] Task<EducationTeachingPlanGenerationDto?> GetGenerationAsync(Guid generationId);
    [Authorize(EducationPermissions.TeachingPlanGenerate), Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage)] Task<PagedResultDto<EducationTeachingPlanGenerationListDto>> GetGenerationsAsync(GetEducationTeachingPlanGenerationsInput input);
    [Authorize(EducationPermissions.TeachingPlanGenerate), Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage)] Task<EducationTeachingPlanGenerationDto> RetryGenerationAsync(Guid generationId, RetryEducationTeachingPlanGenerationDto input);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage)] Task<EducationTeachingPlanRevisionDto> SaveDraftAsync(Guid planId, SaveEducationTeachingPlanDraftDto input);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage)] Task<EducationTeachingPlanDto> GetAsync(Guid id);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage)] Task<PagedResultDto<EducationTeachingPlanListDto>> GetListAsync(PagedResultRequestDto input);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage)] Task<EducationTeachingPlanRevisionDto> GetRevisionAsync(Guid planId, Guid revisionId);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage)] Task<EducationTeachingPlanRevisionDto?> GetOperationAsync(Guid planId, Guid operationId);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage)] Task<EducationTeachingPlanRevisionDto> ConfirmAsync(Guid planId, TransitionEducationTeachingPlanDto input);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage)] Task<EducationTeachingPlanRevisionDto> PublishAsync(Guid planId, TransitionEducationTeachingPlanDto input);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage)] Task<EducationTeachingPlanRevisionDto> WithdrawAsync(Guid planId, TransitionEducationTeachingPlanDto input);
    [Authorize(EducationPermissions.TeachingPlanView)] Task<PagedResultDto<EducationStudentTeachingPlanDto>> GetMyListAsync(PagedResultRequestDto input, Guid? schoolId = null);
    [Authorize(EducationPermissions.TeachingPlanView)] Task<EducationStudentTeachingPlanDto> OpenMyAsync(Guid planId, Guid expectedVersionId);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), Authorize(EducationPermissions.AssessmentManage)] Task<EducationTeachingPlanHintContextDto> GetHintContextAsync(Guid exposureId);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), Authorize(EducationPermissions.AssessmentManage)] Task<EducationTeachingPlanHintReviewRevisionDto> SaveHintReviewAsync(Guid exposureId, SaveEducationHintReviewDto input);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), Authorize(EducationPermissions.AssessmentManage)] Task<EducationTeachingPlanHintReviewRevisionDto?> GetHintReviewOperationAsync(Guid exposureId, Guid operationId);
}
public class SaveEducationTeachingPlanDraftDto
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public Guid? GenerationJobId { get; set; }
    public Guid OperationId { get; set; }
    public Guid ReportId { get; set; }
    public Guid ReportSnapshotId { get; set; }
    public Guid StudentUserId { get; set; }
    public Guid? ExpectedRevisionId { get; set; }
    [StringLength(40)] public string? ConcurrencyStamp { get; set; }
    [Required] public EducationTeachingPlanContent Content { get; set; } = new();
    [StringLength(4000)] public string? InternalNotes { get; set; }
}
public class TransitionEducationTeachingPlanDto
{
    public Guid OperationId { get; set; }
    public Guid ExpectedRevisionId { get; set; }
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = "";
    [StringLength(2048)] public string? Reason { get; set; }
    public bool ConfirmRecipientAndContent { get; set; }
    public bool ConfirmInlineMaterialRights { get; set; }
    public bool ConfirmStudentSafeText { get; set; }
}
public class EducationTeachingPlanRevisionDto : EntityDto<Guid>
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public Guid? GenerationJobId { get; set; }
    public Guid PlanId { get; set; }
    public int RevisionNumber { get; set; }
    public Guid? BasedOnRevisionId { get; set; }
    public Guid? SourceRevisionId { get; set; }
    public EducationTeachingPlanRevisionKind Kind { get; set; }
    public EducationTeachingPlanBasis? Basis { get; set; }
    public EducationTeachingPlanContent? Content { get; set; }
    public string? InternalNotes { get; set; }
    public string? Reason { get; set; }
    public string RequestFingerprint { get; set; } = "";
    public DateTime RecordedAtUtc { get; set; }
}
public class EducationTeachingPlanListDto : EntityDto<Guid>
{
    public EducationPlanExecutionState ExecutionState { get; set; }
    public string StudentName { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public int RevisionNumber { get; set; }
    public Guid? PublishedRevisionId { get; set; }
}
public class EducationTeachingPlanDto : EducationTeachingPlanListDto
{
    public Guid? LastPublishedRevisionId { get; set; }
    public string ConcurrencyStamp { get; set; } = "";
    public EducationTeachingPlanRevisionDto Current { get; set; } = new();
    public bool SourceIsCurrent { get; set; }
    public EducationTeachingPlanAvailability StudentAvailability { get; set; }
}
public class EducationStudentTeachingPlanDto : EntityDto<Guid>
{
    public EducationPlanExecutionState ExecutionState { get; set; }
    public DateTime? ExecutionChangedAtUtc { get; set; }
    public Guid SchoolId { get; set; }
    public Guid VersionId { get; set; }
    public int VersionNumber { get; set; }
    public string TeacherName { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public DateTime RecordedAtUtc { get; set; }
    public EducationTeachingPlanAvailability Availability { get; set; }
    public EducationStudentTeachingPlanContent? Content { get; set; }
}
public class EducationTeachingPlanHintReviewRevisionDto : EducationEvidenceDecisionRevisionDto
{
    public Guid ExposureId { get; set; }
    public Guid PlanRevisionId { get; set; }
    public EducationLearningExposureUse Use { get; set; }
}
public class EducationTeachingPlanHintContextDto
{
    public Guid ExposureId { get; set; }
    public Guid PlanId { get; set; }
    public Guid PlanRevisionId { get; set; }
    public int PlanRevisionNumber { get; set; }
    public DateTime ExposedAtUtc { get; set; }
    public EducationStudentTeachingPlanContent Content { get; set; } = new();
    public string QuestionTitle { get; set; } = "";
    public string QuestionStem { get; set; } = "";
    public string? ReferenceAnswer { get; set; }
    public System.Collections.Generic.List<string> CorrectAnswers { get; set; } = [];
    public System.Collections.Generic.List<string> BlankAnswers { get; set; } = [];
    public string? ConcurrencyStamp { get; set; }
    public EducationTeachingPlanHintReviewRevisionDto? Current { get; set; }
}
