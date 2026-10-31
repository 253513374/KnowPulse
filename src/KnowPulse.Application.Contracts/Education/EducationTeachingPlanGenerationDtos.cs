using System;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.Education;

public class GenerateEducationTeachingPlanDraftDto
{
    public Guid OperationId { get; set; }
    [Required] public EducationPlanGenerationRequest Request { get; set; } = new();
}
public class RetryEducationTeachingPlanGenerationDto
{
    public Guid OperationId { get; set; }
    [Required, StringLength(40)] public string ConcurrencyStamp { get; set; } = "";
}
public class EducationTeachingPlanGenerationListDto : EntityDto<Guid>
{
    public Guid PlanId { get; set; }
    public string StudentName { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public string TextbookTitle { get; set; } = "";
    public Guid? RetryOfJobId { get; set; }
    public EducationPlanGenerationStatus Status { get; set; }
    public EducationPlanGenerationFailure Failure { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
public class EducationTeachingPlanGenerationDto : EducationTeachingPlanGenerationListDto
{
    public bool HasSavedPlan { get; set; }
    public string ConcurrencyStamp { get; set; } = "";
    public DateTime? HeartbeatAtUtc { get; set; }
    public bool SourceIsCurrent { get; set; }
    public EducationPlanGenerationRequest Request { get; set; } = new();
    public EducationTeachingPlanBasis? Basis { get; set; }
    public EducationTeachingPlanContent? Content { get; set; }
    public string? ModelAlias { get; set; }
    public string? ModelProvider { get; set; }
    public string? ModelVersion { get; set; }
    public int? OutputTokenLimit { get; set; }
    public double? Temperature { get; set; }
    public string PromptVersion { get; set; } = "";
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public long? DurationMilliseconds { get; set; }
    public decimal? EstimatedCostUsd { get; set; }
    public bool UsageMayBeIncomplete { get; set; }
}
