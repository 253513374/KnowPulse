using System;
using System.Threading.Tasks;
using KnowPulse.Education;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Auditing;
using Volo.Abp.Content;

namespace KnowPulse.Controllers.Education;

[Authorize(EducationPermissions.TeachingPlanView)]
[Authorize(EducationPermissions.LearningParticipate)]
[DisableAuditing]
[Route("api/education/teaching/activities/{activityId:guid}/materials/{materialId:guid}/attachments")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class EducationPlanAttachmentsController : KnowPulseController
{
    private readonly IEducationTeachingMaterialAppService _materials;
    public EducationPlanAttachmentsController(IEducationTeachingMaterialAppService materials) => _materials = materials;

    [HttpGet("{attachmentId:guid}")]
    public Task<IRemoteStreamContent> DownloadAsync(Guid activityId, Guid materialId, Guid attachmentId)
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.Pragma = "no-cache";
        return _materials.DownloadMyMaterialAsync(activityId, materialId, attachmentId);
    }
}
