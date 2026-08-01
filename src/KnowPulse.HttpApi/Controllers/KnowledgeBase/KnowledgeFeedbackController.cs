using System;
using System.Threading.Tasks;
using KnowPulse.KnowledgeBase;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.Controllers.KnowledgeBase;

/// <summary>
/// 知识反馈 HTTP API。
/// 提交接口面向可访问知识的用户；处理、关闭和关联版本接口面向知识反馈管理员。
/// </summary>
[Route("api/knowledge/feedback")]
public class KnowledgeFeedbackController : KnowPulseController
{
    private readonly IKnowledgeFeedbackAppService _feedbackAppService;

    public KnowledgeFeedbackController(IKnowledgeFeedbackAppService feedbackAppService)
    {
        _feedbackAppService = feedbackAppService;
    }

    /// <summary>
    /// 提交知识反馈。
    /// </summary>
    /// <param name="input">反馈提交输入。</param>
    /// <returns>提交后的反馈记录。</returns>
    [Authorize(KnowPulsePermissions.KnowledgeBase.Items)]
    [HttpPost]
    public Task<KnowledgeFeedbackDto> SubmitAsync([FromBody] SubmitKnowledgeFeedbackDto input)
    {
        return _feedbackAppService.SubmitAsync(input);
    }

    /// <summary>
    /// 查询知识反馈列表。
    /// </summary>
    /// <param name="input">反馈筛选和分页条件。</param>
    /// <returns>反馈分页结果。</returns>
    [Authorize(KnowPulsePermissions.KnowledgeBase.Feedback)]
    [HttpGet]
    public Task<PagedResultDto<KnowledgeFeedbackDto>> GetListAsync([FromQuery] GetKnowledgeFeedbackListInput input)
    {
        return _feedbackAppService.GetListAsync(input);
    }

    /// <summary>
    /// 查询单条知识反馈。
    /// </summary>
    /// <param name="id">反馈 ID。</param>
    /// <returns>反馈详情。</returns>
    [Authorize(KnowPulsePermissions.KnowledgeBase.Feedback)]
    [HttpGet("{id:guid}")]
    public Task<KnowledgeFeedbackDto> GetAsync(Guid id)
    {
        return _feedbackAppService.GetAsync(id);
    }

    /// <summary>
    /// 接手处理知识反馈。
    /// </summary>
    /// <param name="id">反馈 ID。</param>
    /// <param name="input">接手处理输入。</param>
    /// <returns>处理后的反馈详情。</returns>
    [Authorize(KnowPulsePermissions.KnowledgeBase.Feedback)]
    [HttpPost("{id:guid}/process")]
    public Task<KnowledgeFeedbackDto> ProcessAsync(Guid id, [FromBody] ProcessKnowledgeFeedbackDto input)
    {
        return _feedbackAppService.ProcessAsync(id, input);
    }

    /// <summary>
    /// 从已接手反馈创建并自动关联修订草稿。
    /// </summary>
    [Authorize(KnowPulsePermissions.KnowledgeBase.Feedback)]
    [HttpPost("{id:guid}/create-revision")]
    public Task<KnowledgeFeedbackDto> CreateRevisionAsync(Guid id)
    {
        return _feedbackAppService.CreateRevisionAsync(id);
    }

    /// <summary>
    /// 标记知识反馈已处理。
    /// </summary>
    /// <param name="id">反馈 ID。</param>
    /// <param name="input">完成处理输入。</param>
    /// <returns>已处理的反馈详情。</returns>
    [Authorize(KnowPulsePermissions.KnowledgeBase.Feedback)]
    [HttpPost("{id:guid}/resolve")]
    public Task<KnowledgeFeedbackDto> ResolveAsync(Guid id, [FromBody] ResolveKnowledgeFeedbackDto input)
    {
        return _feedbackAppService.ResolveAsync(id, input);
    }

    /// <summary>
    /// 关闭知识反馈。
    /// </summary>
    /// <param name="id">反馈 ID。</param>
    /// <param name="input">关闭输入。</param>
    /// <returns>关闭后的反馈详情。</returns>
    [Authorize(KnowPulsePermissions.KnowledgeBase.Feedback)]
    [HttpPost("{id:guid}/close")]
    public Task<KnowledgeFeedbackDto> CloseAsync(Guid id, [FromBody] CloseKnowledgeFeedbackDto input)
    {
        return _feedbackAppService.CloseAsync(id, input);
    }

    /// <summary>
    /// 为知识反馈关联修订版本。
    /// </summary>
    /// <param name="id">反馈 ID。</param>
    /// <param name="input">关联版本输入。</param>
    /// <returns>关联后的反馈详情。</returns>
    [Authorize(KnowPulsePermissions.KnowledgeBase.Feedback)]
    [HttpPost("{id:guid}/link-version")]
    public Task<KnowledgeFeedbackDto> LinkVersionAsync(Guid id, [FromBody] LinkKnowledgeFeedbackVersionDto input)
    {
        return _feedbackAppService.LinkVersionAsync(id, input);
    }
}
