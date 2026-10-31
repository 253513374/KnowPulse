using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.Education;

public interface IEducationQuestionCollectionAppService : IApplicationService
{
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate)] Task<EducationQuestionCollectionDto> SetMyCollectionAsync(Guid recipientId, SetEducationQuestionCollectionDto input);
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate)] Task<EducationQuestionCollectionStateDto> GetMyCollectionAsync(Guid schoolId, Guid recipientId, Guid paperQuestionId);
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate)] Task<EducationQuestionCollectionDto?> GetMyCollectionOperationAsync(Guid schoolId, Guid recipientId, Guid operationId);
    [Authorize(EducationPermissions.AssessmentParticipate), Authorize(EducationPermissions.LearningParticipate)] Task<PagedResultDto<EducationQuestionCollectionDto>> GetMyCollectionsAsync(GetEducationQuestionCollectionsDto input);
}

public class SetEducationQuestionCollectionDto
{
    public Guid SchoolId { get; set; }
    public Guid OperationId { get; set; }
    public Guid PaperQuestionId { get; set; }
    public Guid? ExpectedCurrentOperationId { get; set; }
    public Guid? ExpectedResultReleaseId { get; set; }
    public bool IsCollected { get; set; }
}

public class GetEducationQuestionCollectionsDto : PagedResultRequestDto
{
    public Guid SchoolId { get; set; }
}

public class EducationQuestionCollectionStateDto
{
    public Guid SchoolId { get; set; }
    public Guid RecipientId { get; set; }
    public Guid PaperQuestionId { get; set; }
    public EducationQuestionCollectionDto? Current { get; set; }
    public bool CanCollect { get; set; }
    public Guid? ResultReleaseId { get; set; }
}

/// <summary>Body-free reference and exact operation receipt; never a source-content grant.</summary>
public class EducationQuestionCollectionDto : EntityDto<Guid>
{
    public Guid SchoolId { get; set; }
    public Guid RecipientId { get; set; }
    public Guid PaperQuestionId { get; set; }
    public int RevisionNumber { get; set; }
    public bool IsCollected { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public bool SourceAvailable { get; set; }
    public string TaskTitle { get; set; } = string.Empty;
}
