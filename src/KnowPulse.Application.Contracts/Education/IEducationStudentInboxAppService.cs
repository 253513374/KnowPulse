using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.Education;

public interface IEducationStudentInboxAppService : IApplicationService
{
    [Authorize] Task<List<EducationInboxSource>> GetSourcesAsync(EducationInboxScopeDto input);
    [Authorize] Task<PagedResultDto<EducationInboxMessageDto>> GetMyListAsync(GetEducationInboxInput input);
    [Authorize] Task<EducationInboxMessageDto> ReadMyAsync(ReadEducationInboxInput input);
}
public class EducationInboxScopeDto
{
    public Guid SchoolId { get; set; }
}
public class GetEducationInboxInput : PagedResultRequestDto
{
    public Guid SchoolId { get; set; }
    public EducationInboxSource Source { get; set; }
}
public class ReadEducationInboxInput : EducationInboxScopeDto
{
    public EducationInboxSource Source { get; set; }
    public Guid MessageId { get; set; }
}
public class EducationInboxMessageDto : EntityDto<Guid>
{
    public Guid SchoolId { get; set; }
    public EducationInboxSource Source { get; set; }
    public EducationInboxMessageKind Kind { get; set; }
    public Guid ReferenceId { get; set; }
    public Guid PublicationId { get; set; }
    public Guid? ResultReleaseId { get; set; }
    public Guid? StepId { get; set; }
    public DateTime AvailableAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public bool CanOpen { get; set; }
    public string WeChatDelivery { get; set; } = "NotConfigured";
}
