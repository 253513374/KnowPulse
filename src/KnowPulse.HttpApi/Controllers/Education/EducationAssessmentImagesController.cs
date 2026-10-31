using System;
using System.Threading.Tasks;
using KnowPulse.Education;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Auditing;
using Volo.Abp.Content;

namespace KnowPulse.Controllers.Education;

[Authorize(EducationPermissions.AssessmentParticipate)]
[DisableAuditing]
[Route("api/education/assessment/attempts/{attemptId:guid}/images")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class EducationAssessmentImagesController : KnowPulseController
{
    private readonly IEducationAssessmentAppService _assessments;
    public EducationAssessmentImagesController(IEducationAssessmentAppService assessments) => _assessments = assessments;

    // Native downloadFile uses GET; the existing service retains all ownership and source checks.
    [HttpGet("{attachmentId:guid}")]
    public Task<IRemoteStreamContent> DownloadAsync(Guid attemptId, Guid attachmentId)
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.Pragma = "no-cache";
        return _assessments.DownloadAnswerImageAsync(attemptId, attachmentId);
    }
}
