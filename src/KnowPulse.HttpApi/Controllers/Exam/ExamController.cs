using System;
using System.Threading.Tasks;
using KnowPulse.Exam;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.Controllers.Exam;

[Authorize]
[Route("api/exam")]
public class ExamController(IExamAppService service) : KnowPulseController
{
    [HttpGet("my-plans")]
    public Task<PagedResultDto<ExamPlanDto>> GetMyPlanListAsync([FromQuery] GetExamPlanListInput input) => service.GetMyPlanListAsync(input);

    [HttpGet("my-plans/{id:guid}")]
    public Task<ExamPlanDto> GetMyPlanAsync(Guid id) => service.GetMyPlanAsync(id);

    [HttpPost("plans/{id:guid}/attempts")]
    public Task<ExamAttemptDto> StartAttemptAsync(Guid id) => service.StartAttemptAsync(id);

    [HttpGet("attempts/{id:guid}")]
    public Task<ExamAttemptDto> GetAttemptAsync(Guid id) => service.GetAttemptAsync(id);

    [HttpPut("attempts/{id:guid}/answers")]
    public Task<ExamAttemptDto> SaveAnswersAsync(Guid id, [FromBody] SaveExamAnswersDto input) => service.SaveAnswersAsync(id, input);

    [HttpPost("attempts/{id:guid}/submit")]
    public Task<ExamAttemptDto> SubmitAttemptAsync(Guid id, [FromBody] SubmitExamAttemptDto input) => service.SubmitAttemptAsync(id, input);
}
