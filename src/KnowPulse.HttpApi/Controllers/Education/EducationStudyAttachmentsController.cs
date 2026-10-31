using System;
using System.Threading.Tasks;
using KnowPulse.Education;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Auditing;
using Volo.Abp.Content;

namespace KnowPulse.Controllers.Education;

[Authorize(EducationPermissions.LearningParticipate)]
[DisableAuditing]
[Route("api/education/study/recipients/{recipientId:guid}/materials/{materialId:guid}/attachments")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class EducationStudyAttachmentsController : KnowPulseController
{
    private readonly IEducationStudyTaskAppService _study;
    public EducationStudyAttachmentsController(IEducationStudyTaskAppService study) => _study = study;

    // Native downloadFile uses GET; the service rechecks recipient, publication and source after Blob access.
    [HttpGet("{attachmentId:guid}")]
    public Task<IRemoteStreamContent> DownloadAsync(Guid recipientId, Guid materialId, Guid attachmentId)
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.Pragma = "no-cache";
        return _study.DownloadMyMaterialAsync(recipientId, materialId, attachmentId);
    }
}
