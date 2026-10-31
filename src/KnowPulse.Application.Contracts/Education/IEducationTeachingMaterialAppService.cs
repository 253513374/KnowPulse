using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace KnowPulse.Education;

public interface IEducationTeachingMaterialAppService : IApplicationService
{
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<PagedResultDto<EducationMaterialSourceDto>> GetMaterialSourcesAsync(Guid planId, GetEducationMaterialSourcesDto input);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<EducationMaterialSourcePreviewDto> PreviewMaterialSourceAsync(Guid planId, EducationMaterialSourceDto input);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<EducationMaterialContentDto> GetTeacherMaterialAsync(Guid planId, Guid revisionId, Guid materialId);
    [Authorize(EducationPermissions.TeachingPlanManage), Authorize(EducationPermissions.AnalysisManage), Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
    Task<IRemoteStreamContent> DownloadTeacherMaterialAsync(Guid planId, Guid revisionId, Guid materialId, Guid attachmentId);
    [Authorize(EducationPermissions.TeachingPlanView), Authorize(EducationPermissions.LearningParticipate)]
    Task<EducationMaterialContentDto> GetMyMaterialAsync(Guid activityId, Guid materialId);
    [Authorize(EducationPermissions.TeachingPlanView), Authorize(EducationPermissions.LearningParticipate)]
    Task<IRemoteStreamContent> DownloadMyMaterialAsync(Guid activityId, Guid materialId, Guid attachmentId);
}
public class GetEducationMaterialSourcesDto : PagedResultRequestDto
{
    public bool SchoolShared { get; set; }
    [StringLength(128)] public string? Filter { get; set; }
}
public class EducationMaterialSourceDto : EntityDto<Guid>
{
    public Guid KnowledgeItemId { get; set; }
    public Guid? SchoolKnowledgeShareId { get; set; }
    [StringLength(256)] public string Title { get; set; } = "";
    public int VersionNumber { get; set; }
}
public class EducationMaterialAttachmentDto : EntityDto<Guid>
{
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
}
public class EducationMaterialSourcePreviewDto
{
    public EducationMaterialSourceDto Source { get; set; } = new();
    public string? ContentText { get; set; }
    public List<EducationMaterialAttachmentDto> Attachments { get; set; } = [];
}
public class EducationMaterialContentDto : EntityDto<Guid>
{
    public string Title { get; set; } = "";
    public int? VersionNumber { get; set; }
    public bool IsAvailable { get; set; }
    public string? ContentText { get; set; }
    public List<EducationMaterialAttachmentDto> Attachments { get; set; } = [];
}
