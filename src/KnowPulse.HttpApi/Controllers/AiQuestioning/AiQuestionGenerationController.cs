using System;
using System.Threading.Tasks;
using KnowPulse.AiQuestioning;
using KnowPulse.Permissions;
using KnowPulse.QuestionBank;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.Controllers.AiQuestioning;

/// <summary>
/// AI 候选题生成 HTTP API 控制器。
/// 控制器只做路由和权限边界暴露，实际生成、校验、citation 追溯和提交审核逻辑在应用服务中实现。
/// </summary>
[Authorize(KnowPulsePermissions.QuestionBank.AiGeneration)]
[Route("api/question-bank/ai-generation")]
public class AiQuestionGenerationController : KnowPulseController
{
    private readonly IAiQuestionGenerationAppService _generationAppService;

    /// <summary>
    /// 创建 AI 候选题生成控制器。
    /// </summary>
    public AiQuestionGenerationController(IAiQuestionGenerationAppService generationAppService)
    {
        _generationAppService = generationAppService;
    }

    /// <summary>
    /// 查询当前 AI 出题模型配置。
    /// </summary>
    [HttpGet("model-configuration")]
    public Task<AiQuestionModelConfigurationDto> GetModelConfigurationAsync()
    {
        return _generationAppService.GetModelConfigurationAsync();
    }

    /// <summary>
    /// 分页查询 AI 出题 prompt 模板版本。
    /// </summary>
    [HttpGet("prompt-templates")]
    public Task<PagedResultDto<AiQuestionPromptTemplateDto>> GetPromptTemplatesAsync(
        [FromQuery] GetAiQuestionPromptTemplateListInput input)
    {
        return _generationAppService.GetPromptTemplatesAsync(input);
    }

    /// <summary>
    /// 分页查询 AI 出题模型调用日志。
    /// </summary>
    [HttpGet("call-logs")]
    public Task<PagedResultDto<AiQuestionModelCallLogDto>> GetCallLogsAsync(
        [FromQuery] GetAiQuestionModelCallLogListInput input)
    {
        return _generationAppService.GetCallLogsAsync(input);
    }

    /// <summary>
    /// 分页查询 AI 出题任务。
    /// 返回任务摘要、候选题和模型调用日志摘要，用于候选题审核页面。
    /// </summary>
    [HttpGet("tasks")]
    public Task<PagedResultDto<AiQuestionGenerationTaskDto>> GetListAsync([FromQuery] GetAiQuestionGenerationTaskListInput input)
    {
        return _generationAppService.GetListAsync(input);
    }

    /// <summary>
    /// 查询单个 AI 出题任务详情。
    /// </summary>
    [HttpGet("tasks/{id:guid}")]
    public Task<AiQuestionGenerationTaskDto> GetAsync(Guid id)
    {
        return _generationAppService.GetAsync(id);
    }

    /// <summary>
    /// 查询单个 AI 候选题详情。
    /// </summary>
    [HttpGet("candidates/{id:guid}")]
    public Task<AiQuestionCandidateDto> GetCandidateAsync(Guid id)
    {
        return _generationAppService.GetCandidateAsync(id);
    }

    /// <summary>
    /// 持久化待执行的 AI 出题任务，由后台处理候选，候选不会直接进入正式题库。
    /// </summary>
    [HttpPost("tasks")]
    public Task<AiQuestionGenerationTaskDto> CreateAsync([FromBody] CreateAiQuestionGenerationTaskDto input)
    {
        return _generationAppService.CreateAsync(input);
    }

    /// <summary>
    /// 重试指定 AI 出题任务。
    /// 重试恢复失败运行，不删除已编辑或已采纳候选。
    /// </summary>
    [HttpPost("tasks/{id:guid}/retry")]
    public Task<AiQuestionGenerationTaskDto> RetryAsync(Guid id)
    {
        return _generationAppService.RetryAsync(id);
    }

    [HttpPost("tasks/{id:guid}/regenerate")]
    public Task<AiQuestionGenerationTaskDto> RegenerateAsync(Guid id, [FromBody] RegenerateAiQuestionGenerationTaskDto input)
    {
        return _generationAppService.RegenerateAsync(id, input);
    }

    [HttpPut("candidates/{id:guid}")]
    public Task<AiQuestionCandidateDto> UpdateCandidateAsync(Guid id, [FromBody] UpdateAiQuestionCandidateDto input)
    {
        return _generationAppService.UpdateCandidateAsync(id, input);
    }

    /// <summary>
    /// 将候选题采纳为可编辑草稿，不自动提交审核。
    /// </summary>
    [HttpPost("candidates/{candidateId:guid}/adopt-draft")]
    public Task<QuestionDto> AdoptCandidateAsDraftAsync(Guid candidateId)
    {
        return _generationAppService.AdoptCandidateAsDraftAsync(candidateId);
    }

    /// <summary>
    /// 旧版兼容入口。当前只将候选题采纳为草稿，不再自动提交审核。
    /// </summary>
    [HttpPost("candidates/{candidateId:guid}/submit-review")]
    public Task<QuestionDto> SubmitCandidateForReviewAsync(Guid candidateId, [FromBody] SubmitAiQuestionCandidateDto input)
    {
        return _generationAppService.SubmitCandidateForReviewAsync(candidateId, input);
    }
}
