using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.Education;

public interface IEducationStudyHintReviewAppService : IApplicationService
{
    [Authorize(EducationPermissions.LearningManage), Authorize(EducationPermissions.AssessmentManage), Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<PagedResultDto<EducationStudyHintCandidateDto>> GetHintCandidatesAsync(Guid recipientId, PagedResultRequestDto input);
    [Authorize(EducationPermissions.LearningManage), Authorize(EducationPermissions.AssessmentManage), Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<EducationStudyHintContextDto> GetHintContextAsync(Guid recipientId, Guid paperQuestionId);
    [Authorize(EducationPermissions.LearningManage), Authorize(EducationPermissions.AssessmentManage), Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<EducationStudyHintReviewRevisionDto> SaveHintReviewAsync(Guid recipientId, Guid paperQuestionId, SaveEducationHintReviewDto input);
    [Authorize(EducationPermissions.LearningManage), Authorize(EducationPermissions.AssessmentManage), Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<EducationStudyHintReviewRevisionDto?> GetHintReviewOperationAsync(Guid recipientId, Guid paperQuestionId, Guid operationId);
}
public class EducationStudyHintCandidateDto : EntityDto<Guid>
{
    public string AssessmentTitle { get; set; } = "";
    public string QuestionTitle { get; set; } = "";
    public int DisplayOrder { get; set; }
}
public class EducationStudyHintReviewRevisionDto : EducationEvidenceDecisionRevisionDto
{
    public Guid RecipientId { get; set; }
    public Guid PaperQuestionId { get; set; }
    public EducationLearningExposureUse Use { get; set; }
}
public class EducationStudyHintContextDto
{
    public Guid RecipientId { get; set; }
    public Guid TaskId { get; set; }
    public Guid PublicationId { get; set; }
    public int VersionNumber { get; set; }
    public string StudentName { get; set; } = "";
    public DateTime ExposedAtUtc { get; set; }
    public string TaskTitle { get; set; } = "";
    public string Instructions { get; set; } = "";
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public int EstimatedMinutes { get; set; }
    public string SubjectName { get; set; } = "";
    public string TextbookTitle { get; set; } = "";
    public string ClassName { get; set; } = "";
    public bool MaterialsAvailable { get; set; }
    public List<EducationStudyMaterialDisplay> Materials { get; set; } = [];
    public EducationFormalQuestionPreviewDto Question { get; set; } = new();
    public Guid DefinitionRevisionId { get; set; }
    public string? ConcurrencyStamp { get; set; }
    public EducationStudyHintReviewRevisionDto? Current { get; set; }
}
