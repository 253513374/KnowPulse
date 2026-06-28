using System;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.KnowledgeIndexing;

/// <summary>
/// 知识索引任务应用服务契约。
/// 该接口是管理端、后台任务和 HTTP API 操作索引任务的统一入口，
/// 所有方法都要求索引任务权限，避免普通知识库用户手动推进或篡改索引状态。
/// </summary>
public interface IKnowledgeIndexJobAppService : IApplicationService
{
    /// <summary>
    /// 按知识条目、知识版本或状态分页查询索引任务。
    /// </summary>
    [Authorize(KnowPulsePermissions.KnowledgeIndexing.IndexJobs)]
    Task<PagedResultDto<KnowledgeIndexJobDto>> GetListAsync(GetKnowledgeIndexJobListInput input);

    /// <summary>
    /// 读取单条索引任务明细，包含失败阶段、失败原因和重试信息。
    /// </summary>
    [Authorize(KnowPulsePermissions.KnowledgeIndexing.IndexJobs)]
    Task<KnowledgeIndexJobDto> GetAsync(Guid id);

    /// <summary>
    /// 创建一条索引任务。调用方必须提供匹配的知识条目和知识版本编号。
    /// </summary>
    [Authorize(KnowPulsePermissions.KnowledgeIndexing.IndexJobs)]
    Task<KnowledgeIndexJobDto> CreateAsync(CreateKnowledgeIndexJobDto input);

    /// <summary>
    /// 为指定知识版本创建手动重建任务。
    /// 该操作会保留历史任务，不会覆盖旧的失败或成功记录。
    /// </summary>
    [Authorize(KnowPulsePermissions.KnowledgeIndexing.IndexJobs)]
    Task<KnowledgeIndexJobDto> ReindexVersionAsync(Guid knowledgeVersionId);

    /// <summary>
    /// 从失败任务派生新的待处理任务，保留原任务的失败排障信息。
    /// </summary>
    [Authorize(KnowPulsePermissions.KnowledgeIndexing.IndexJobs)]
    Task<KnowledgeIndexJobDto> RetryAsync(Guid id);

    /// <summary>
    /// 将任务标记为处理中。MVP-1 中也用于管理员手动推进状态。
    /// </summary>
    [Authorize(KnowPulsePermissions.KnowledgeIndexing.IndexJobs)]
    Task<KnowledgeIndexJobDto> StartAsync(Guid id);

    /// <summary>
    /// 将任务标记为已完成索引，并清理上一轮失败信息。
    /// </summary>
    [Authorize(KnowPulsePermissions.KnowledgeIndexing.IndexJobs)]
    Task<KnowledgeIndexJobDto> MarkIndexedAsync(Guid id);

    /// <summary>
    /// 将任务标记为失败，并记录失败阶段、原因和可选的下一次重试时间。
    /// </summary>
    [Authorize(KnowPulsePermissions.KnowledgeIndexing.IndexJobs)]
    Task<KnowledgeIndexJobDto> MarkFailedAsync(Guid id, MarkKnowledgeIndexJobFailedDto input);
}
