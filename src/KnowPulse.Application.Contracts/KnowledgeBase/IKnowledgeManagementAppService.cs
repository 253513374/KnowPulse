using System;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Services;

namespace KnowPulse.KnowledgeBase;

/// <summary>
/// 知识管理聚合读服务。它只组合既有业务事实，不负责发布、索引或权限写入。
/// </summary>
public interface IKnowledgeManagementAppService : IApplicationService
{
    /// <summary>
    /// 查询当前用户可见的知识管理列表和状态概览。
    /// </summary>
    [Authorize(KnowPulsePermissions.KnowledgeBase.Items)]
    Task<KnowledgeManagementListResultDto> GetListAsync(GetKnowledgeManagementListInput input);

    /// <summary>
    /// 获取单个可见知识的聚合管理详情。
    /// </summary>
    [Authorize(KnowPulsePermissions.KnowledgeBase.Items)]
    Task<KnowledgeManagementDetailDto> GetAsync(Guid id);
}
