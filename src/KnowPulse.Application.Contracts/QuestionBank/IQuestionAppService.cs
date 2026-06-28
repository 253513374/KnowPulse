using System;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.QuestionBank;

/// <summary>
/// 题库题目应用服务契约。
/// 面向后台管理端和 HTTP API 暴露题目分页筛选、详情、来源、审核记录、CRUD、状态流转、停用和归档能力。
/// </summary>
public interface IQuestionAppService : IApplicationService
{
    /// <summary>
    /// 分页查询题库题目。
    /// 支持按题型、状态、难度、创建来源、适用岗位、知识点和关键字组合筛选。
    /// </summary>
    /// <param name="input">题目分页、排序和筛选条件。</param>
    /// <returns>符合筛选条件的题目分页结果，列表项包含题目详情、选项、答案、解析、知识点、来源和审核记录。</returns>
    [Authorize(KnowPulsePermissions.QuestionBank.Questions)]
    Task<PagedResultDto<QuestionDto>> GetListAsync(GetQuestionListInput input);

    /// <summary>
    /// 分页查询正式题库题目。
    /// 服务端会强制限定为审核通过且可用于新试卷组卷的题目，调用方传入的状态筛选不会扩大正式题范围。
    /// </summary>
    /// <param name="input">正式题库分页、排序和筛选条件。</param>
    /// <returns>审核通过且可用于考试组卷的题目分页结果。</returns>
    [Authorize(KnowPulsePermissions.QuestionBank.Questions)]
    Task<PagedResultDto<QuestionDto>> GetFormalListAsync(GetQuestionListInput input);

    /// <summary>
    /// 获取单个题目详情。
    /// 返回题干、题型、难度、适用岗位、安全等级、标签、选项、标准答案、解析、知识点、来源和审核记录。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <returns>题目详情 DTO。</returns>
    [Authorize(KnowPulsePermissions.QuestionBank.Questions)]
    Task<QuestionDto> GetAsync(Guid id);

    /// <summary>
    /// 获取题目的来源引用列表。
    /// 来源可以指向知识条目、知识版本、知识分块，也可以是人工录入的来源说明。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <returns>题目来源引用列表。</returns>
    [Authorize(KnowPulsePermissions.QuestionBank.Questions)]
    Task<ListResultDto<QuestionSourceDto>> GetSourcesAsync(Guid id);

    /// <summary>
    /// 获取题目的审核和状态操作记录。
    /// 包含提交审核、审核通过、驳回、退回修改、停用和归档等动作。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <returns>按创建时间排序的题目审核记录列表。</returns>
    [Authorize(KnowPulsePermissions.QuestionBank.Questions)]
    Task<ListResultDto<QuestionReviewDto>> GetReviewsAsync(Guid id);

    /// <summary>
    /// 创建题目草稿。
    /// 创建时会校验题型、选项和标准答案的组合是否符合领域规则。
    /// </summary>
    /// <param name="input">题目创建输入，包括题干、选项、答案、解析、知识点和来源。</param>
    /// <returns>创建后的题目详情。</returns>
    [Authorize(KnowPulsePermissions.QuestionBank.Questions)]
    Task<QuestionDto> CreateAsync(CreateQuestionDto input);

    /// <summary>
    /// 更新题目内容。
    /// 已审核通过、已驳回、已退回修改或已停用的题目被编辑后会回到草稿状态；已归档题目不能编辑。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <param name="input">题目更新输入。</param>
    /// <returns>更新后的题目详情。</returns>
    [Authorize(KnowPulsePermissions.QuestionBank.Questions)]
    Task<QuestionDto> UpdateAsync(Guid id, UpdateQuestionDto input);

    /// <summary>
    /// 删除题目。
    /// 当前删除行为由 ABP 仓储和领域实体审计能力处理。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <returns>异步操作任务。</returns>
    [Authorize(KnowPulsePermissions.QuestionBank.Questions)]
    Task DeleteAsync(Guid id);

    /// <summary>
    /// 提交题目进入待审核状态。
    /// 提交前会再次校验题型、选项和标准答案的完整性。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <returns>提交审核后的题目详情。</returns>
    [Authorize(KnowPulsePermissions.QuestionBank.Questions)]
    Task<QuestionDto> SubmitReviewAsync(Guid id);

    /// <summary>
    /// 审核通过题目。
    /// 通过后题目进入正式题库，可被后续新试卷组卷使用。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <param name="input">审核意见输入。</param>
    /// <returns>审核通过后的题目详情。</returns>
    [Authorize(KnowPulsePermissions.QuestionBank.Reviews)]
    Task<QuestionDto> ApproveAsync(Guid id, ReviewQuestionDto input);

    /// <summary>
    /// 驳回题目。
    /// 驳回题不会进入正式题库，也不会被新试卷组卷选中。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <param name="input">审核意见输入。</param>
    /// <returns>驳回后的题目详情。</returns>
    [Authorize(KnowPulsePermissions.QuestionBank.Reviews)]
    Task<QuestionDto> RejectAsync(Guid id, ReviewQuestionDto input);

    /// <summary>
    /// 退回题目修改。
    /// 退回后编辑者可修改题目内容，并需要重新提交审核。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <param name="input">审核意见输入。</param>
    /// <returns>退回修改后的题目详情。</returns>
    [Authorize(KnowPulsePermissions.QuestionBank.Reviews)]
    Task<QuestionDto> ReturnForRevisionAsync(Guid id, ReviewQuestionDto input);

    /// <summary>
    /// 停用题目。
    /// 停用题保留历史记录，但不再进入后续新试卷筛选；已归档题目不能停用。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <returns>停用后的题目详情。</returns>
    [Authorize(KnowPulsePermissions.QuestionBank.Questions)]
    Task<QuestionDto> DisableAsync(Guid id);

    /// <summary>
    /// 归档题目。
    /// 归档后题目保留审计和历史引用，不再允许编辑或停用。
    /// </summary>
    /// <param name="id">题目唯一标识。</param>
    /// <returns>归档后的题目详情。</returns>
    [Authorize(KnowPulsePermissions.QuestionBank.Questions)]
    Task<QuestionDto> ArchiveAsync(Guid id);
}
