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
[Route("api/education/practices/{id:guid}/images")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class EducationPracticeImagesController : KnowPulseController
{
    private readonly IEducationQuestionPracticeAppService _practices;
    public EducationPracticeImagesController(IEducationQuestionPracticeAppService practices) => _practices = practices;

    [HttpGet("{attachmentId:guid}")]
    public Task<IRemoteStreamContent> DownloadAsync(Guid id, Guid attachmentId)
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.Pragma = "no-cache";
        return _practices.DownloadMyPracticeImageAsync(id, attachmentId);
    }
}
