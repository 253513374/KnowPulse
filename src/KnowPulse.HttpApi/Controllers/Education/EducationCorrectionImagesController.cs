using System;
using System.Threading.Tasks;
using KnowPulse.Education;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Auditing;
using Volo.Abp.Content;

namespace KnowPulse.Controllers.Education;

[Authorize(EducationPermissions.AssessmentParticipate)]
[Authorize(EducationPermissions.LearningParticipate)]
[DisableAuditing]
[Route("api/education/corrections/deliveries/{deliveryId:guid}/images")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class EducationCorrectionImagesController : KnowPulseController
{
    private readonly IEducationCorrectionAppService _corrections;
    public EducationCorrectionImagesController(IEducationCorrectionAppService corrections) => _corrections = corrections;

    [HttpGet("{attachmentId:guid}")]
    public Task<IRemoteStreamContent> DownloadAsync(Guid deliveryId, Guid attachmentId)
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.Pragma = "no-cache";
        return _corrections.DownloadMyCorrectionImageAsync(deliveryId, attachmentId);
    }
}
