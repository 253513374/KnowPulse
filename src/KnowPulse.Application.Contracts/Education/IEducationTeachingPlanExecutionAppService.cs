using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.Education;

public interface IEducationTeachingPlanExecutionAppService : IApplicationService
{
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage)] Task<EducationPlanExecutionReviewDto> GetExecutionReviewAsync(Guid planId);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage)] Task<EducationPlanDecisionDto> DecideExecutionAsync(Guid planId, DecideEducationPlanExecutionDto input);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage)] Task<EducationPlanDecisionDto?> GetExecutionOperationAsync(Guid planId, Guid operationId);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage)] Task<PagedResultDto<EducationPlanDecisionDto>> GetExecutionHistoryAsync(Guid planId, Guid? expectedDecisionId, PagedResultRequestDto input);
}
public class DecideEducationPlanExecutionDto
{
    public Guid OperationId { get; set; }
    public Guid ExpectedPublishedRevisionId { get; set; }
    public Guid? ExpectedDecisionId { get; set; }
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = "";
    [Required, StringLength(64)] public string EvidenceFingerprint { get; set; } = "";
    public EducationPlanDecision Decision { get; set; }
    [Required, StringLength(2000)] public string Reason { get; set; } = "";
    public bool ConfirmReviewedEvidenceAndUncertainty { get; set; }
}
public class EducationPlanExecutionReviewDto
{
    public Guid PlanId { get; set; }
    public string StudentName { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public string Title { get; set; } = "";
    public EducationPlanExecutionState State { get; set; }
    public Guid? CurrentDecisionId { get; set; }
    public bool AdjustmentNeedsPublication { get; set; }
    public bool IsCurrentPublication { get; set; }
    public string ConcurrencyStamp { get; set; } = "";
    public string EvidenceFingerprint { get; set; } = "";
    public EducationPlanExecutionEvidence Evidence { get; set; } = new();
}
public class EducationPlanDecisionDto : EntityDto<Guid>
{
    public Guid PlanId { get; set; }
    public Guid PublishedRevisionId { get; set; }
    public Guid? BasedOnDecisionId { get; set; }
    public int RevisionNumber { get; set; }
    public EducationPlanDecision Decision { get; set; }
    public EducationPlanExecutionState Before { get; set; }
    public EducationPlanExecutionState After { get; set; }
    public string Reason { get; set; } = "";
    public EducationPlanExecutionEvidence Evidence { get; set; } = new();
    public string RequestFingerprint { get; set; } = "";
    public DateTime RecordedAtUtc { get; set; }
}
