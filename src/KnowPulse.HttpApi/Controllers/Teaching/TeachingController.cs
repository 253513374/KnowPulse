using System;
using System.Threading.Tasks;
using KnowPulse.Teaching;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.Controllers.Teaching;

[Authorize]
[Route("api/teaching")]
public class TeachingController(ITeachingAppService service) : KnowPulseController
{
    [HttpGet("invitation")]
    public Task<TeacherInvitationDto> GetInvitationAsync() => service.GetInvitationAsync();

    [HttpGet("invitations/{code}")]
    public Task<TeacherInvitationDto> LookupInvitationAsync(string code) => service.LookupInvitationAsync(code);

    [HttpGet("students")]
    public Task<PagedResultDto<TeachingStudentDto>> GetStudentsAsync([FromQuery] GetTeachingStudentListInput input) => service.GetStudentsAsync(input);

    [HttpPost("apply")]
    public Task<TeachingStudentDto> ApplyAsync([FromBody] ApplyTeacherInvitationDto input) => service.ApplyAsync(input);

    [HttpPost("relationships/{id:guid}/confirm")]
    public Task<TeachingStudentDto> ConfirmAsync(Guid id) => service.ConfirmAsync(id);

    [HttpDelete("relationships/{id:guid}")]
    public Task<TeachingStudentDto> RemoveAsync(Guid id) => service.RemoveAsync(id);

    [HttpGet("my-teachers")]
    public Task<ListResultDto<TeachingStudentDto>> GetMyTeachersAsync() => service.GetMyTeachersAsync();
}
