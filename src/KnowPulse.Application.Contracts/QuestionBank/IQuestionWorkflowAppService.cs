using System;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Services;

namespace KnowPulse.QuestionBank;

/// <summary>
/// 题目生产工作流接口。
/// 调用方只表达创建草稿、保存、提交、审核和可用性变更意图；创建来源、状态约束和审核记录由服务端统一决定。
/// </summary>
public interface IQuestionWorkflowAppService : IApplicationService
{
    /// <summary>
    /// 创建人工题目草稿。创建来源固定为人工，不接受调用方指定。
    /// </summary>
    [Authorize(KnowPulsePermissions.QuestionBank.Questions)]
    Task<QuestionDto> CreateManualDraftAsync(SaveQuestionDraftDto input);

    /// <summary>
    /// 将已通过校验且有引用依据的 AI 候选题采纳为可编辑草稿。
    /// </summary>
    [Authorize(KnowPulsePermissions.QuestionBank.AiGeneration)]
    Task<QuestionDto> AdoptAiCandidateAsync(Guid candidateId);

    /// <summary>
    /// 保存题目草稿内容。题目原始创建来源不会被修改。
    /// </summary>
    [Authorize(KnowPulsePermissions.QuestionBank.Questions)]
    Task<QuestionDto> SaveDraftAsync(Guid id, SaveQuestionDraftDto input);

    /// <summary>
    /// 提交草稿进入审核队列。
    /// </summary>
    [Authorize(KnowPulsePermissions.QuestionBank.Questions)]
    Task<QuestionDto> SubmitAsync(Guid id);

    /// <summary>
    /// 对待审核题执行通过、退回或驳回决策。
    /// </summary>
    [Authorize(KnowPulsePermissions.QuestionBank.Reviews)]
    Task<QuestionDto> DecideReviewAsync(Guid id, DecideQuestionReviewDto input);

    /// <summary>
    /// 停用或归档题目。
    /// </summary>
    [Authorize(KnowPulsePermissions.QuestionBank.Questions)]
    Task<QuestionDto> ChangeAvailabilityAsync(Guid id, ChangeQuestionAvailabilityDto input);
}
