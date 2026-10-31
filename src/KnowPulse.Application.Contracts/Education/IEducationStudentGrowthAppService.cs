using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.Education;

public interface IEducationStudentGrowthAppService : IApplicationService
{
    [Authorize(EducationPermissions.AssessmentParticipate)] Task<ListResultDto<EducationStudentGrowthContext>> GetMyContextsAsync();
    [Authorize(EducationPermissions.AssessmentParticipate)] Task<EducationStudentGrowthValue> GetMyGrowthAsync(Guid contextId, GetEducationStudentGrowthInput input);
    [Authorize(EducationPermissions.AssessmentParticipate)] Task<EducationStudentGrowthEvidencePageDto> GetMyPointEvidenceAsync(Guid contextId, Guid snapshotId, Guid pointId, PagedResultRequestDto input);
    [Authorize(EducationPermissions.AssessmentParticipate)] Task<EducationStudentGrowthContextsForSchoolDto> GetMyContextsForSchoolAsync(Guid schoolId);
    [Authorize(EducationPermissions.AssessmentParticipate)] Task<EducationStudentGrowthForSchoolDto> GetMyGrowthForSchoolAsync(Guid contextId, Guid schoolId, GetEducationStudentGrowthInput input);
    [Authorize(EducationPermissions.AssessmentParticipate)] Task<EducationStudentGrowthEvidenceForSchoolDto> GetMyPointEvidenceForSchoolAsync(Guid contextId, Guid schoolId, Guid snapshotId, Guid pointId, PagedResultRequestDto input);
}

public class GetEducationStudentGrowthInput
{
    public DateTime WindowStartsAtUtc { get; set; }
    public DateTime WindowEndsAtUtc { get; set; }
}
public class EducationStudentGrowthContextsForSchoolDto
{
    public Guid SchoolId { get; set; }
    public List<EducationStudentGrowthContext> Items { get; set; } = [];
}
public class EducationStudentGrowthForSchoolDto
{
    public Guid SchoolId { get; set; }
    public EducationStudentGrowthValue Value { get; set; } = new();
}
public class EducationStudentGrowthEvidenceForSchoolDto
{
    public Guid SchoolId { get; set; }
    public EducationStudentGrowthEvidencePageDto Evidence { get; set; } = new();
}
public class EducationStudentGrowthEvidenceDto
{
    public Guid TaskId { get; set; }
    public Guid RecipientId { get; set; }
    public Guid PaperQuestionId { get; set; }
    public Guid ResultReleaseId { get; set; }
    public DateTime EffectiveAtUtc { get; set; }
    public decimal ObtainedScore { get; set; }
    public decimal MaxScore { get; set; }
    public EducationLearningExclusionReason ExclusionReason { get; set; }
}
public class EducationStudentGrowthEvidencePageDto : PagedResultDto<EducationStudentGrowthEvidenceDto>
{
    public bool HasRestrictedEvidence { get; set; }
}
