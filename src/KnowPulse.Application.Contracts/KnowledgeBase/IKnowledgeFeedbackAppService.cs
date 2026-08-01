using System;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.KnowledgeBase;

/// <summary>
/// 知识反馈应用服务契约，负责用户提交反馈和管理员处理纠错闭环。
/// </summary>
public interface IKnowledgeFeedbackAppService : IApplicationService
{
    /// <summary>
    /// 当前用户提交知识反馈。提交前会校验知识可见性。
    /// </summary>
    /// <param name="input">反馈提交输入。</param>
    /// <returns>提交后的反馈记录。</returns>
    [Authorize(KnowPulsePermissions.KnowledgeBase.Items)]
    Task<KnowledgeFeedbackDto> SubmitAsync(SubmitKnowledgeFeedbackDto input);

    /// <summary>
    /// 管理端分页查询知识反馈。
    /// </summary>
    /// <param name="input">筛选和分页条件。</param>
    /// <returns>反馈分页结果。</returns>
    [Authorize(KnowPulsePermissions.KnowledgeBase.Feedback)]
    Task<PagedResultDto<KnowledgeFeedbackDto>> GetListAsync(GetKnowledgeFeedbackListInput input);

    /// <summary>
    /// 管理端读取单条知识反馈详情。
    /// </summary>
    /// <param name="id">反馈 ID。</param>
    /// <returns>反馈详情。</returns>
    [Authorize(KnowPulsePermissions.KnowledgeBase.Feedback)]
    Task<KnowledgeFeedbackDto> GetAsync(Guid id);

    /// <summary>
    /// 管理员接手处理反馈，并可记录初步处理备注。
    /// </summary>
    /// <param name="id">反馈 ID。</param>
    /// <param name="input">接手处理输入。</param>
    /// <returns>处理后的反馈详情。</returns>
    [Authorize(KnowPulsePermissions.KnowledgeBase.Feedback)]
    Task<KnowledgeFeedbackDto> ProcessAsync(Guid id, ProcessKnowledgeFeedbackDto input);

    /// <summary>
    /// 从已接手的反馈创建修订草稿。系统复制来源版本正文并自动关联反馈，
    /// 管理员不需要查找或填写版本 ID。
    /// </summary>
    /// <param name="id">反馈 ID。</param>
    /// <returns>已关联新修订草稿的反馈详情。</returns>
    [Authorize(KnowPulsePermissions.KnowledgeBase.Feedback)]
    Task<KnowledgeFeedbackDto> CreateRevisionAsync(Guid id);

    /// <summary>
    /// 管理员在关联修订版本已经发布并完成索引后标记反馈已处理。
    /// </summary>
    /// <param name="id">反馈 ID。</param>
    /// <param name="input">完成处理输入。</param>
    /// <returns>已处理的反馈详情。</returns>
    [Authorize(KnowPulsePermissions.KnowledgeBase.Feedback)]
    Task<KnowledgeFeedbackDto> ResolveAsync(Guid id, ResolveKnowledgeFeedbackDto input);

    /// <summary>
    /// 管理员关闭反馈。关闭后不能继续处理或关联修订版本。
    /// </summary>
    /// <param name="id">反馈 ID。</param>
    /// <param name="input">关闭输入。</param>
    /// <returns>关闭后的反馈详情。</returns>
    [Authorize(KnowPulsePermissions.KnowledgeBase.Feedback)]
    Task<KnowledgeFeedbackDto> CloseAsync(Guid id, CloseKnowledgeFeedbackDto input);

    /// <summary>
    /// 管理员为反馈关联同一知识条目下的修订版本。
    /// </summary>
    /// <param name="id">反馈 ID。</param>
    /// <param name="input">关联版本输入。</param>
    /// <returns>关联后的反馈详情。</returns>
    [Authorize(KnowPulsePermissions.KnowledgeBase.Feedback)]
    Task<KnowledgeFeedbackDto> LinkVersionAsync(Guid id, LinkKnowledgeFeedbackVersionDto input);
}
