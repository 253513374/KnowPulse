using System;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using KnowPulse.QuestionBank;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.Controllers.QuestionBank;

/// <summary>
/// 题库题目 HTTP API。
/// 控制器只负责稳定 REST 路由和权限入口，题目定义校验、状态流转和审计记录由应用服务处理。
/// 所有接口都要求题库题目管理权限；审核动作额外要求题目审核权限。
/// </summary>
[Authorize(KnowPulsePermissions.QuestionBank.Questions)]
[Route("api/question-bank/questions")]
public class QuestionsController : KnowPulseController
{
    private readonly IQuestionAppService _questionAppService;
    private readonly IQuestionWorkflowAppService _questionWorkflowAppService;

    /// <summary>
    /// 创建题库题目控制器。
    /// </summary>
    /// <param name="questionAppService">题库应用服务。</param>
    public QuestionsController(
        IQuestionAppService questionAppService,
        IQuestionWorkflowAppService questionWorkflowAppService)
    {
        _questionAppService = questionAppService;
        _questionWorkflowAppService = questionWorkflowAppService;
    }

    /// <summary>
    /// 分页查询题目，支持按题型、状态、难度、岗位、知识点、创建来源和关键字筛选。
    /// </summary>
    /// <param name="input">分页、排序和筛选条件。</param>
    /// <returns>题目分页结果。</returns>
    [HttpGet]
    public Task<PagedResultDto<QuestionDto>> GetListAsync([FromQuery] GetQuestionListInput input)
    {
        return _questionAppService.GetListAsync(input);
    }

    /// <summary>
    /// 分页查询正式题库。服务端强制只返回审核通过且可用于组卷的题目。
    /// </summary>
    /// <param name="input">分页、排序和筛选条件。</param>
    /// <returns>正式题库分页结果。</returns>
    [HttpGet("formal")]
    public Task<PagedResultDto<QuestionDto>> GetFormalListAsync([FromQuery] GetQuestionListInput input)
    {
        return _questionAppService.GetFormalListAsync(input);
    }

    /// <summary>
    /// 查询题目详情，包含选项、答案、解析、知识点、来源和操作记录。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <returns>题目详情。</returns>
    [HttpGet("{id:guid}")]
    public Task<QuestionDto> GetAsync(Guid id)
    {
        return _questionAppService.GetAsync(id);
    }

    /// <summary>
    /// 查询题目来源引用。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <returns>题目来源引用列表。</returns>
    [HttpGet("{id:guid}/sources")]
    public Task<ListResultDto<QuestionSourceDto>> GetSourcesAsync(Guid id)
    {
        return _questionAppService.GetSourcesAsync(id);
    }

    /// <summary>
    /// 查询题目审核记录。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <returns>题目审核记录列表。</returns>
    [HttpGet("{id:guid}/reviews")]
    public Task<ListResultDto<QuestionReviewDto>> GetReviewsAsync(Guid id)
    {
        return _questionAppService.GetReviewsAsync(id);
    }

    /// <summary>
    /// 创建题目草稿。
    /// </summary>
    /// <param name="input">题目创建输入。</param>
    /// <returns>创建后的题目详情。</returns>
    [HttpPost]
    public Task<QuestionDto> CreateAsync([FromBody] CreateQuestionDto input)
    {
        return _questionWorkflowAppService.CreateManualDraftAsync(input);
    }

    /// <summary>
    /// 更新题目草稿或将已停用、驳回、退回修改的题目重新改回草稿。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <param name="input">题目更新输入。</param>
    /// <returns>更新后的题目详情。</returns>
    [HttpPut("{id:guid}")]
    public Task<QuestionDto> UpdateAsync(Guid id, [FromBody] UpdateQuestionDto input)
    {
        return _questionWorkflowAppService.SaveDraftAsync(id, input);
    }

    /// <summary>
    /// 删除题目。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <returns>异步删除任务。</returns>
    [HttpDelete("{id:guid}")]
    public Task DeleteAsync(Guid id)
    {
        return _questionAppService.DeleteAsync(id);
    }

    /// <summary>
    /// 提交题目进入待审核状态。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <returns>提交审核后的题目详情。</returns>
    [HttpPost("{id:guid}/submit-review")]
    public Task<QuestionDto> SubmitReviewAsync(Guid id)
    {
        return _questionWorkflowAppService.SubmitAsync(id);
    }

    [HttpPost("{id:guid}/confirm")]
    public Task<QuestionDto> ConfirmAsync(Guid id)
    {
        return _questionWorkflowAppService.ConfirmAsync(id);
    }

    /// <summary>
    /// 审核通过题目，使其进入正式题库并可用于后续组卷。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <param name="input">审核意见输入。</param>
    /// <returns>审核通过后的题目详情。</returns>
    [Authorize(KnowPulsePermissions.QuestionBank.Reviews)]
    [HttpPost("{id:guid}/approve")]
    public Task<QuestionDto> ApproveAsync(Guid id, [FromBody] ReviewQuestionDto input)
    {
        return _questionWorkflowAppService.DecideReviewAsync(id, new DecideQuestionReviewDto
        {
            Decision = QuestionReviewDecision.Approve,
            Comment = input.Comment
        });
    }

    /// <summary>
    /// 驳回题目。驳回题不能进入正式题库或组卷。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <param name="input">审核意见输入。</param>
    /// <returns>驳回后的题目详情。</returns>
    [Authorize(KnowPulsePermissions.QuestionBank.Reviews)]
    [HttpPost("{id:guid}/reject")]
    public Task<QuestionDto> RejectAsync(Guid id, [FromBody] ReviewQuestionDto input)
    {
        return _questionWorkflowAppService.DecideReviewAsync(id, new DecideQuestionReviewDto
        {
            Decision = QuestionReviewDecision.Reject,
            Comment = input.Comment
        });
    }

    /// <summary>
    /// 退回题目修改。修改后需要重新提交审核。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <param name="input">审核意见输入。</param>
    /// <returns>退回修改后的题目详情。</returns>
    [Authorize(KnowPulsePermissions.QuestionBank.Reviews)]
    [HttpPost("{id:guid}/return-for-revision")]
    public Task<QuestionDto> ReturnForRevisionAsync(Guid id, [FromBody] ReviewQuestionDto input)
    {
        return _questionWorkflowAppService.DecideReviewAsync(id, new DecideQuestionReviewDto
        {
            Decision = QuestionReviewDecision.ReturnForRevision,
            Comment = input.Comment
        });
    }

    /// <summary>
    /// 停用题目。停用题不再进入后续新试卷筛选。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <returns>停用后的题目详情。</returns>
    [HttpPost("{id:guid}/disable")]
    public Task<QuestionDto> DisableAsync(Guid id)
    {
        return _questionWorkflowAppService.ChangeAvailabilityAsync(id, new ChangeQuestionAvailabilityDto
        {
            Action = QuestionAvailabilityAction.Disable
        });
    }

    /// <summary>
    /// 归档题目。归档后题目不可继续编辑或停用。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <returns>归档后的题目详情。</returns>
    [HttpPost("{id:guid}/archive")]
    public Task<QuestionDto> ArchiveAsync(Guid id)
    {
        return _questionWorkflowAppService.ChangeAvailabilityAsync(id, new ChangeQuestionAvailabilityDto
        {
            Action = QuestionAvailabilityAction.Archive
        });
    }
}
