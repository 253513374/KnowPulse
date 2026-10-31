using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Services;

namespace KnowPulse.Education;

public interface IEducationMasteryRuleAppService : IApplicationService
{
    [Authorize(EducationPermissions.SchoolManage)] Task<EducationMasteryRuleDto> GetAsync(Guid schoolId, Guid subjectId);
    [Authorize(EducationPermissions.SchoolManage)] Task<EducationMasteryRuleRevisionDto> SaveAsync(Guid schoolId, Guid subjectId, SaveEducationMasteryRuleDto input);
    [Authorize(EducationPermissions.SchoolManage)] Task<EducationMasteryRuleRevisionDto?> GetOperationAsync(Guid schoolId, Guid subjectId, Guid operationId);
}
public class SaveEducationMasteryRuleDto
{
    public Guid OperationId { get; set; }
    public Guid? ExpectedRevisionId { get; set; }
    [StringLength(40)] public string? ConcurrencyStamp { get; set; }
    public bool IsEnabled { get; set; }
    [Range(1, 730)] public int WindowDays { get; set; }
    [Range(1, 1000)] public int MinimumIndependentQuestionCount { get; set; }
    [Range(1, 1000)] public int MinimumAssessmentCount { get; set; }
    [Range(1, 730)] public int MaximumLatestEvidenceAgeDays { get; set; }
    public decimal TargetRatio { get; set; }
    [Required, StringLength(2048)] public string Reason { get; set; } = string.Empty;
    public bool ConfirmSchoolPolicy { get; set; }
}
public class EducationMasteryRuleDto
{
    public Guid SchoolId { get; set; }
    public Guid EducationSubjectId { get; set; }
    public string? ConcurrencyStamp { get; set; }
    public EducationMasteryRuleRevisionDto? Current { get; set; }
}
public class EducationMasteryRuleRevisionDto
{
    public Guid Id { get; set; }
    public int RevisionNumber { get; set; }
    public Guid? BasedOnRevisionId { get; set; }
    public bool IsEnabled { get; set; }
    public int WindowDays { get; set; }
    public int MinimumIndependentQuestionCount { get; set; }
    public int MinimumAssessmentCount { get; set; }
    public int MaximumLatestEvidenceAgeDays { get; set; }
    public decimal TargetRatio { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public DateTime RecordedAtUtc { get; set; }
}
