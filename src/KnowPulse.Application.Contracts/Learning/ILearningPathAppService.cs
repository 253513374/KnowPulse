using System;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.Learning;

/// <summary>
/// 学习路径应用服务契约，提供管理端路径配置和个人学习中心读取能力。
/// </summary>
public interface ILearningPathAppService : IApplicationService
{
    /// <summary>
    /// 管理端分页查询学习路径配置。
    /// </summary>
    /// <param name="input">查询条件，包括关键字、启用状态和分页信息。</param>
    /// <returns>学习路径分页结果，包含完整分配和内容配置。</returns>
    [Authorize(KnowPulsePermissions.Learning.Manage)]
    Task<PagedResultDto<LearningPathDto>> GetListAsync(GetLearningPathListInput input);

    /// <summary>
    /// 管理端读取单条学习路径详情。
    /// </summary>
    /// <param name="id">学习路径 ID。</param>
    /// <returns>学习路径详情。</returns>
    [Authorize(KnowPulsePermissions.Learning.Manage)]
    Task<LearningPathDto> GetAsync(Guid id);

    /// <summary>
    /// 创建学习路径，并写入分配规则、必学知识、推荐知识和必考考试配置。
    /// </summary>
    /// <param name="input">创建输入。</param>
    /// <returns>创建后的学习路径详情。</returns>
    [Authorize(KnowPulsePermissions.Learning.Manage)]
    Task<LearningPathDto> CreateAsync(CreateLearningPathDto input);

    /// <summary>
    /// 编辑学习路径配置。配置项按最小闭环策略整体替换，并递增配置版本。
    /// </summary>
    /// <param name="id">学习路径 ID。</param>
    /// <param name="input">更新输入。</param>
    /// <returns>更新后的学习路径详情。</returns>
    [Authorize(KnowPulsePermissions.Learning.Manage)]
    Task<LearningPathDto> UpdateAsync(Guid id, UpdateLearningPathDto input);

    /// <summary>
    /// 停用学习路径。停用后普通用户个人学习中心不再显示该路径。
    /// </summary>
    /// <param name="id">学习路径 ID。</param>
    /// <returns>停用后的学习路径详情。</returns>
    [Authorize(KnowPulsePermissions.Learning.Manage)]
    Task<LearningPathDto> DeactivateAsync(Guid id);

    /// <summary>
    /// 查询当前用户可见的学习路径列表，只返回已启用且按组织关系命中的路径。
    /// </summary>
    /// <param name="input">查询条件和分页信息。</param>
    /// <returns>当前用户的学习路径分页结果。</returns>
    [Authorize(KnowPulsePermissions.Learning.View)]
    Task<PagedResultDto<LearningPathDto>> GetMyListAsync(GetLearningPathListInput input);

    /// <summary>
    /// 查询当前用户可见的单条学习路径详情。
    /// </summary>
    /// <param name="id">学习路径 ID。</param>
    /// <returns>当前用户可访问的学习路径详情。</returns>
    [Authorize(KnowPulsePermissions.Learning.View)]
    Task<LearningPathDto> GetMyAsync(Guid id);

    /// <summary>
    /// 记录当前用户阅读或完成学习路径下的指定知识版本。
    /// </summary>
    /// <param name="input">阅读记录输入。</param>
    /// <returns>更新阅读状态后的学习路径详情。</returns>
    [Authorize(KnowPulsePermissions.Learning.View)]
    Task<LearningPathDto> MarkKnowledgeReadAsync(MarkLearningKnowledgeReadDto input);
}
