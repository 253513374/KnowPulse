using System;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using KnowPulse.RagAI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.Controllers.RagAI;

/// <summary>
/// RAG 质量评测 HTTP API。
/// </summary>
[Route("api/ai/rag/evaluations")]
[Authorize(KnowPulsePermissions.Rag.Evaluations)]
public class RagEvaluationsController : KnowPulseController
{
    private readonly IRagEvaluationAppService _evaluationAppService;

    public RagEvaluationsController(IRagEvaluationAppService evaluationAppService)
    {
        _evaluationAppService = evaluationAppService;
    }

    /// <summary>
    /// 创建 RAG 评测用例。
    /// </summary>
    /// <param name="input">评测用例创建输入。</param>
    /// <returns>创建后的评测用例。</returns>
    [HttpPost("cases")]
    public Task<RagEvaluationCaseDto> CreateCaseAsync([FromBody] CreateRagEvaluationCaseDto input)
    {
        return _evaluationAppService.CreateCaseAsync(input);
    }

    /// <summary>
    /// 编辑 RAG 评测用例。
    /// </summary>
    /// <param name="id">评测用例 ID。</param>
    /// <param name="input">评测用例更新输入。</param>
    /// <returns>更新后的评测用例。</returns>
    [HttpPut("cases/{id:guid}")]
    public Task<RagEvaluationCaseDto> UpdateCaseAsync(Guid id, [FromBody] UpdateRagEvaluationCaseDto input)
    {
        return _evaluationAppService.UpdateCaseAsync(id, input);
    }

    /// <summary>
    /// 查询单条 RAG 评测用例。
    /// </summary>
    /// <param name="id">评测用例 ID。</param>
    /// <returns>评测用例详情。</returns>
    [HttpGet("cases/{id:guid}")]
    public Task<RagEvaluationCaseDto> GetCaseAsync(Guid id)
    {
        return _evaluationAppService.GetCaseAsync(id);
    }

    /// <summary>
    /// 分页查询 RAG 评测用例。
    /// </summary>
    /// <param name="input">用例筛选和分页条件。</param>
    /// <returns>评测用例分页结果。</returns>
    [HttpGet("cases")]
    public Task<PagedResultDto<RagEvaluationCaseDto>> GetCaseListAsync([FromQuery] GetRagEvaluationCaseListInput input)
    {
        return _evaluationAppService.GetCaseListAsync(input);
    }

    /// <summary>
    /// 执行一次 RAG 质量评测。
    /// </summary>
    /// <param name="input">评测运行输入。</param>
    /// <returns>评测运行详情。</returns>
    [HttpPost("runs")]
    public Task<RagEvaluationRunDto> RunAsync([FromBody] RunRagEvaluationDto input)
    {
        return _evaluationAppService.RunAsync(input);
    }

    /// <summary>
    /// 查询单次 RAG 评测运行及逐用例结果。
    /// </summary>
    /// <param name="id">评测运行 ID。</param>
    /// <returns>评测运行详情和逐用例结果。</returns>
    [HttpGet("runs/{id:guid}")]
    public Task<RagEvaluationRunDto> GetRunAsync(Guid id)
    {
        return _evaluationAppService.GetRunAsync(id);
    }

    /// <summary>
    /// 分页查询 RAG 评测运行记录。
    /// </summary>
    /// <param name="input">运行记录筛选和分页条件。</param>
    /// <returns>评测运行分页结果。</returns>
    [HttpGet("runs")]
    public Task<PagedResultDto<RagEvaluationRunDto>> GetRunListAsync([FromQuery] GetRagEvaluationRunListInput input)
    {
        return _evaluationAppService.GetRunListAsync(input);
    }
}
