using System;
using System.Threading.Tasks;
using KnowPulse.Learning;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.Controllers.Learning;

/// <summary>
/// 学习路径 HTTP API。
/// 管理接口返回完整配置；个人接口只返回当前用户被分配到且可访问的学习内容。
/// </summary>
[Route("api/learning/paths")]
public class LearningPathsController : KnowPulseController
{
    private readonly ILearningPathAppService _learningPathAppService;

    public LearningPathsController(ILearningPathAppService learningPathAppService)
    {
        _learningPathAppService = learningPathAppService;
    }

    /// <summary>
    /// 管理端分页查询学习路径配置。
    /// </summary>
    /// <param name="input">查询条件和分页参数。</param>
    /// <returns>学习路径分页结果。</returns>
    [Authorize(KnowPulsePermissions.Learning.Manage)]
    [HttpGet]
    public Task<PagedResultDto<LearningPathDto>> GetListAsync([FromQuery] GetLearningPathListInput input)
    {
        return _learningPathAppService.GetListAsync(input);
    }

    /// <summary>
    /// 查询当前用户分配到的学习路径。
    /// </summary>
    /// <param name="input">查询条件和分页参数。</param>
    /// <returns>当前用户可见的学习路径分页结果。</returns>
    [Authorize(KnowPulsePermissions.Learning.View)]
    [HttpGet("my")]
    public Task<PagedResultDto<LearningPathDto>> GetMyListAsync([FromQuery] GetLearningPathListInput input)
    {
        return _learningPathAppService.GetMyListAsync(input);
    }

    /// <summary>
    /// 管理端查询学习路径详情。
    /// </summary>
    /// <param name="id">学习路径 ID。</param>
    /// <returns>学习路径详情。</returns>
    [Authorize(KnowPulsePermissions.Learning.Manage)]
    [HttpGet("{id:guid}")]
    public Task<LearningPathDto> GetAsync(Guid id)
    {
        return _learningPathAppService.GetAsync(id);
    }

    /// <summary>
    /// 查询当前用户被分配到的单条学习路径详情。
    /// </summary>
    /// <param name="id">学习路径 ID。</param>
    /// <returns>当前用户可见的学习路径详情。</returns>
    [Authorize(KnowPulsePermissions.Learning.View)]
    [HttpGet("my/{id:guid}")]
    public Task<LearningPathDto> GetMyAsync(Guid id)
    {
        return _learningPathAppService.GetMyAsync(id);
    }

    /// <summary>
    /// 记录当前用户在学习路径下阅读了指定知识版本。
    /// </summary>
    /// <param name="input">阅读记录输入。</param>
    /// <returns>更新阅读状态后的学习路径详情。</returns>
    [Authorize(KnowPulsePermissions.Learning.View)]
    [HttpPost("my/knowledge-read")]
    public Task<LearningPathDto> MarkKnowledgeReadAsync([FromBody] MarkLearningKnowledgeReadDto input)
    {
        return _learningPathAppService.MarkKnowledgeReadAsync(input);
    }

    /// <summary>
    /// 创建学习路径配置。
    /// </summary>
    /// <param name="input">创建输入。</param>
    /// <returns>创建后的学习路径详情。</returns>
    [Authorize(KnowPulsePermissions.Learning.Manage)]
    [HttpPost]
    public Task<LearningPathDto> CreateAsync([FromBody] CreateLearningPathDto input)
    {
        return _learningPathAppService.CreateAsync(input);
    }

    /// <summary>
    /// 编辑学习路径配置。配置项会整体替换，并递增路径配置版本。
    /// </summary>
    /// <param name="id">学习路径 ID。</param>
    /// <param name="input">更新输入。</param>
    /// <returns>更新后的学习路径详情。</returns>
    [Authorize(KnowPulsePermissions.Learning.Manage)]
    [HttpPut("{id:guid}")]
    public Task<LearningPathDto> UpdateAsync(Guid id, [FromBody] UpdateLearningPathDto input)
    {
        return _learningPathAppService.UpdateAsync(id, input);
    }

    /// <summary>
    /// 停用学习路径。停用后普通用户不再看到该路径。
    /// </summary>
    /// <param name="id">学习路径 ID。</param>
    /// <returns>停用后的学习路径详情。</returns>
    [Authorize(KnowPulsePermissions.Learning.Manage)]
    [HttpPost("{id:guid}/deactivate")]
    public Task<LearningPathDto> DeactivateAsync(Guid id)
    {
        return _learningPathAppService.DeactivateAsync(id);
    }
}
