using System;
using System.Threading.Tasks;
using KnowPulse.KnowledgeIndexing;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Application.Dtos;

namespace KnowPulse.Controllers.KnowledgeIndexing;

/// <summary>
/// 知识索引任务 HTTP API。
/// Controller 只暴露稳定路由和授权标记，状态校验、归属校验和审计规则全部委托给应用服务。
/// </summary>
[Authorize(KnowPulsePermissions.KnowledgeIndexing.IndexJobs)]
[Route("api/knowledge")]
public class KnowledgeIndexJobsController : KnowPulseController
{
    private readonly IKnowledgeIndexJobAppService _indexJobAppService;

    /// <summary>
    /// 创建知识索引任务控制器。
    /// </summary>
    public KnowledgeIndexJobsController(IKnowledgeIndexJobAppService indexJobAppService)
    {
        _indexJobAppService = indexJobAppService;
    }

    /// <summary>
    /// 查询索引任务列表。
    /// </summary>
    [HttpGet("index-jobs")]
    public Task<PagedResultDto<KnowledgeIndexJobDto>> GetListAsync([FromQuery] GetKnowledgeIndexJobListInput input)
    {
        return _indexJobAppService.GetListAsync(input);
    }

    /// <summary>
    /// 查询单条索引任务。
    /// </summary>
    [HttpGet("index-jobs/{id:guid}")]
    public Task<KnowledgeIndexJobDto> GetAsync(Guid id)
    {
        return _indexJobAppService.GetAsync(id);
    }

    /// <summary>
    /// 查询指定知识版本的索引任务。
    /// </summary>
    [HttpGet("versions/{knowledgeVersionId:guid}/index-jobs")]
    public Task<PagedResultDto<KnowledgeIndexJobDto>> GetByVersionAsync(
        Guid knowledgeVersionId,
        [FromQuery] PagedAndSortedResultRequestDto input)
    {
        // 前端知识版本页只知道 version id，这个便捷路由避免页面自行拼复杂筛选 DTO。
        return _indexJobAppService.GetListAsync(new GetKnowledgeIndexJobListInput
        {
            KnowledgeVersionId = knowledgeVersionId,
            SkipCount = input.SkipCount,
            MaxResultCount = input.MaxResultCount,
            Sorting = input.Sorting
        });
    }

    /// <summary>
    /// 为指定知识版本创建手动重建任务。
    /// </summary>
    [HttpPost("versions/{knowledgeVersionId:guid}/reindex")]
    public Task<KnowledgeIndexJobDto> ReindexVersionAsync(Guid knowledgeVersionId)
    {
        // 手动重建是管理员操作入口；应用服务会创建新的索引任务并保留旧任务历史。
        return _indexJobAppService.ReindexVersionAsync(knowledgeVersionId);
    }

    /// <summary>
    /// 创建索引任务。
    /// </summary>
    [HttpPost("index-jobs")]
    public Task<KnowledgeIndexJobDto> CreateAsync([FromBody] CreateKnowledgeIndexJobDto input)
    {
        return _indexJobAppService.CreateAsync(input);
    }

    /// <summary>
    /// 从失败任务创建重试任务。
    /// </summary>
    [HttpPost("index-jobs/{id:guid}/retry")]
    public Task<KnowledgeIndexJobDto> RetryAsync(Guid id)
    {
        return _indexJobAppService.RetryAsync(id);
    }

    /// <summary>
    /// 标记任务开始处理。
    /// </summary>
    [HttpPost("index-jobs/{id:guid}/start")]
    public Task<KnowledgeIndexJobDto> StartAsync(Guid id)
    {
        return _indexJobAppService.StartAsync(id);
    }

    /// <summary>
    /// 标记任务完成索引。
    /// </summary>
    [HttpPost("index-jobs/{id:guid}/indexed")]
    public Task<KnowledgeIndexJobDto> MarkIndexedAsync(Guid id)
    {
        return _indexJobAppService.MarkIndexedAsync(id);
    }

    /// <summary>
    /// 标记任务失败。
    /// </summary>
    [HttpPost("index-jobs/{id:guid}/failed")]
    public Task<KnowledgeIndexJobDto> MarkFailedAsync(Guid id, [FromBody] MarkKnowledgeIndexJobFailedDto input)
    {
        return _indexJobAppService.MarkFailedAsync(id, input);
    }
}
