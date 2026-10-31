using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.Education;

public interface IEducationTeachingPlanNoticeAppService : IApplicationService
{
    [Authorize(EducationPermissions.TeachingPlanView)] Task<PagedResultDto<EducationTeachingPlanNoticeDto>> GetMyPlanNoticesAsync(GetEducationPlanNoticesInput input);
    [Authorize(EducationPermissions.TeachingPlanView)] Task<EducationTeachingPlanNoticeDto> ReadMyPlanNoticeAsync(Guid noticeId, Guid schoolId);
}
public class GetEducationPlanNoticesInput : PagedResultRequestDto
{
    public Guid SchoolId { get; set; }
}
public class EducationTeachingPlanNoticeDto : EntityDto<Guid>
{
    public Guid SchoolId { get; set; }
    public Guid PlanId { get; set; }
    public Guid PublishedRevisionId { get; set; }
    public EducationTeachingPlanNoticeKind Kind { get; set; }
    public Guid? StepId { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public bool CanOpen { get; set; }
}
