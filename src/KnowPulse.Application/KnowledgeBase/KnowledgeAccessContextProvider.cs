using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.Organization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Linq;
using Volo.Abp.Users;

namespace KnowPulse.KnowledgeBase;

/// <summary>
/// 组装当前用户的知识访问上下文，供知识列表、附件访问和 RAG 二次校验复用。
/// </summary>
public interface IKnowledgeAccessContextProvider
{
    /// <summary>
    /// 根据当前登录用户组装部门、岗位、角色和安全等级上下文。
    /// 未登录用户会得到空上下文，调用方应据此拒绝返回受限知识。
    /// </summary>
    Task<KnowledgeAccessContext> CreateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 默认知识访问上下文提供者。
/// 它把 ABP 当前用户、组织任职记录和 Identity 角色合并成 KnowledgeAccessEvaluator 可消费的上下文。
/// </summary>
public class KnowledgeAccessContextProvider : IKnowledgeAccessContextProvider, ITransientDependency
{
    private readonly IRepository<UserOrganizationAssignment, Guid> _assignmentRepository;
    private readonly IIdentityRoleRepository _roleRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    /// <summary>
    /// 创建知识访问上下文提供者。
    /// </summary>
    public KnowledgeAccessContextProvider(
        IRepository<UserOrganizationAssignment, Guid> assignmentRepository,
        IIdentityRoleRepository roleRepository,
        ICurrentUser currentUser,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _assignmentRepository = assignmentRepository;
        _roleRepository = roleRepository;
        _currentUser = currentUser;
        _asyncExecuter = asyncExecuter;
    }

    /// <summary>
    /// 根据当前登录用户生成知识访问上下文。
    /// </summary>
    public async Task<KnowledgeAccessContext> CreateAsync(CancellationToken cancellationToken = default)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.Id == null)
        {
            // 匿名用户没有组织和角色上下文，后续知识访问校验会按无权限处理。
            return new KnowledgeAccessContext(null);
        }

        var userId = _currentUser.Id.Value;
        var assignmentQueryable = await _assignmentRepository.GetQueryableAsync();
        var assignments = await _asyncExecuter.ToListAsync(
            assignmentQueryable.Where(x => x.UserId == userId && x.IsActive),
            cancellationToken);

        var departmentIds = assignments.Select(x => x.DepartmentId).ToHashSet();
        var positionIds = assignments.Where(x => x.PositionId.HasValue).Select(x => x.PositionId!.Value).ToHashSet();
        var securityLevel = assignments.Count == 0 ? 0 : assignments.Max(x => x.SecurityLevel);
        var roleIds = await GetCurrentRoleIdsAsync();

        return new KnowledgeAccessContext(userId, departmentIds, positionIds, roleIds, securityLevel);
    }

    /// <summary>
    /// 将当前用户角色名称解析为 Identity 角色编号集合。
    /// </summary>
    private async Task<IReadOnlySet<Guid>> GetCurrentRoleIdsAsync()
    {
        // CurrentUser.Roles 只有角色名称，权限匹配需要角色 Id，因此这里显式回 Identity 仓储解析。
        var roleNames = _currentUser.Roles?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();

        if (roleNames.Length == 0)
        {
            return new HashSet<Guid>();
        }

        var roles = await _roleRepository.GetListAsync(roleNames);
        return roles.Select(x => x.Id).ToHashSet();
    }
}
