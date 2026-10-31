using System;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.Education;

public class GetEducationTeachingPlanGenerationsInput : PagedResultRequestDto
{
    public Guid? PlanId { get; set; }
}
