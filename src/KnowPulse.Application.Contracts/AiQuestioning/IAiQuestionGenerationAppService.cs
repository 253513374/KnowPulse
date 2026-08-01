using System;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using KnowPulse.QuestionBank;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.AiQuestioning;

/// <summary>
/// AI 候选题生成应用服务契约。
/// 该服务只负责生成和管理“候选题”，候选题提交后仍进入题库审核流，
/// 不允许直接成为正式题或被考试引用。
/// </summary>
public interface IAiQuestionGenerationAppService : IApplicationService
{
    /// <summary>
    /// 查询当前 AI 出题模型配置。
    /// 返回模型 alias、prompt 版本和调用限制，不返回真实模型 key。
    /// </summary>
    [Authorize(KnowPulsePermissions.QuestionBank.AiGeneration)]
    Task<AiQuestionModelConfigurationDto> GetModelConfigurationAsync();

    /// <summary>
    /// 分页查询 AI 出题 prompt 模板版本。
    /// 用于管理端查看当前可用模板和历史版本。
    /// </summary>
    [Authorize(KnowPulsePermissions.QuestionBank.AiGeneration)]
    Task<PagedResultDto<AiQuestionPromptTemplateDto>> GetPromptTemplatesAsync(GetAiQuestionPromptTemplateListInput input);

    /// <summary>
    /// 分页查询 AI 出题模型调用日志。
    /// 日志只返回摘要、hash 和模型元数据，不包含完整 prompt 或完整模型响应。
    /// </summary>
    [Authorize(KnowPulsePermissions.QuestionBank.AiGeneration)]
    Task<PagedResultDto<AiQuestionModelCallLogDto>> GetCallLogsAsync(GetAiQuestionModelCallLogListInput input);

    /// <summary>
    /// 分页查询 AI 出题任务。
    /// 返回任务摘要、候选题列表和模型调用摘要，供管理页面查看生成结果和失败原因。
    /// </summary>
    [Authorize(KnowPulsePermissions.QuestionBank.AiGeneration)]
    Task<PagedResultDto<AiQuestionGenerationTaskDto>> GetListAsync(GetAiQuestionGenerationTaskListInput input);

    /// <summary>
    /// 查询单个 AI 出题任务详情。
    /// 详情包含候选题 citation 和相似题提示，但不包含完整 prompt 或完整知识正文。
    /// </summary>
    [Authorize(KnowPulsePermissions.QuestionBank.AiGeneration)]
    Task<AiQuestionGenerationTaskDto> GetAsync(Guid id);

    /// <summary>
    /// 查询单个候选题详情。
    /// 用于页面局部刷新或后续候选题独立处理入口。
    /// </summary>
    [Authorize(KnowPulsePermissions.QuestionBank.AiGeneration)]
    Task<AiQuestionCandidateDto> GetCandidateAsync(Guid id);

    /// <summary>
    /// 创建并执行 AI 出题任务。
    /// 当前 MVP-2 同步执行 RAG 检索和模型调用；后续切换后台任务时可保持同一契约。
    /// </summary>
    [Authorize(KnowPulsePermissions.QuestionBank.AiGeneration)]
    Task<AiQuestionGenerationTaskDto> CreateAsync(CreateAiQuestionGenerationTaskDto input);

    /// <summary>
    /// 重试失败或需要重新生成的 AI 出题任务。
    /// 重试会清理该任务现有候选题后重新执行生成流程，保留任务本身和调用日志。
    /// </summary>
    [Authorize(KnowPulsePermissions.QuestionBank.AiGeneration)]
    Task<AiQuestionGenerationTaskDto> RetryAsync(Guid id);

    /// <summary>
    /// 将候选题采纳为可编辑的题库草稿。
    /// 采纳不会自动提交审核，用户仍需检查题目内容并主动提交。
    /// </summary>
    [Authorize(KnowPulsePermissions.QuestionBank.AiGeneration)]
    Task<QuestionDto> AdoptCandidateAsDraftAsync(Guid candidateId);

    /// <summary>
    /// 旧版兼容入口。当前行为与“采纳为草稿”一致，不再自动提交审核。
    /// </summary>
    [Authorize(KnowPulsePermissions.QuestionBank.AiGeneration)]
    Task<QuestionDto> SubmitCandidateForReviewAsync(Guid candidateId, SubmitAiQuestionCandidateDto input);
}
