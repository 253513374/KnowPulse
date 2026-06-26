using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;

namespace KnowPulse.Organization;

/// <summary>
/// 用户组织关系应用服务，负责维护用户与部门、岗位、职级之间的关联。
/// 该服务会校验关联对象是否存在，并在设置主组织关系时清理同一用户的其他主关系。
/// </summary>
[Authorize(KnowPulsePermissions.Organization.UserAssignments)]
public class UserOrganizationAssignmentAppService : KnowPulseAppService, IUserOrganizationAssignmentAppService
{
    private readonly IRepository<UserOrganizationAssignment, Guid> _assignmentRepository;
    private readonly IRepository<IdentityUser, Guid> _userRepository;
    private readonly IRepository<Department, Guid> _departmentRepository;
    private readonly IRepository<Position, Guid> _positionRepository;
    private readonly IRepository<JobLevel, Guid> _jobLevelRepository;

    public UserOrganizationAssignmentAppService(
        IRepository<UserOrganizationAssignment, Guid> assignmentRepository,
        IRepository<IdentityUser, Guid> userRepository,
        IRepository<Department, Guid> departmentRepository,
        IRepository<Position, Guid> positionRepository,
        IRepository<JobLevel, Guid> jobLevelRepository)
    {
        _assignmentRepository = assignmentRepository;
        _userRepository = userRepository;
        _departmentRepository = departmentRepository;
        _positionRepository = positionRepository;
        _jobLevelRepository = jobLevelRepository;
    }

    /// <summary>
    /// 分页查询用户组织关系，支持按用户、部门和启用状态过滤。
    /// </summary>
    public virtual async Task<PagedResultDto<UserOrganizationAssignmentDto>> GetListAsync(GetUserOrganizationAssignmentListInput input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Organization.UserAssignments);
        var queryable = await _assignmentRepository.GetQueryableAsync();

        if (input.UserId.HasValue)
        {
            queryable = queryable.Where(x => x.UserId == input.UserId.Value);
        }

        if (input.DepartmentId.HasValue)
        {
            queryable = queryable.Where(x => x.DepartmentId == input.DepartmentId.Value);
        }

        if (input.IsActive.HasValue)
        {
            queryable = queryable.Where(x => x.IsActive == input.IsActive.Value);
        }

        var totalCount = await AsyncExecuter.CountAsync(queryable);
        var items = await AsyncExecuter.ToListAsync(
            queryable
                .OrderBy(x => x.UserId)
                .ThenBy(x => x.DepartmentId)
                .Skip(input.SkipCount)
                .Take(input.MaxResultCount));

        return new PagedResultDto<UserOrganizationAssignmentDto>(totalCount, items.Select(MapToDto).ToList());
    }

    /// <summary>
    /// 获取单个用户组织关系详情。
    /// </summary>
    public virtual async Task<UserOrganizationAssignmentDto> GetAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Organization.UserAssignments);
        return MapToDto(await _assignmentRepository.GetAsync(id));
    }

    /// <summary>
    /// 创建用户组织关系。若新关系被设置为有效主关系，会先清理该用户已有主关系。
    /// </summary>
    public virtual async Task<UserOrganizationAssignmentDto> CreateAsync(CreateUpdateUserOrganizationAssignmentDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Organization.UserAssignments);
        await ValidateReferencesAsync(input);

        var isPrimary = input.IsPrimary && input.IsActive;
        if (isPrimary)
        {
            await ClearOtherPrimaryAssignmentsAsync(input.UserId);
        }

        var assignment = new UserOrganizationAssignment(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            input.UserId,
            input.DepartmentId,
            input.PositionId,
            input.JobLevelId,
            input.SecurityLevel,
            isPrimary);

        assignment.SetActive(input.IsActive);

        return MapToDto(await _assignmentRepository.InsertAsync(assignment, autoSave: true));
    }

    /// <summary>
    /// 更新用户组织关系。更新用户编号时同样会按新用户维度维护主关系唯一性。
    /// </summary>
    public virtual async Task<UserOrganizationAssignmentDto> UpdateAsync(Guid id, CreateUpdateUserOrganizationAssignmentDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Organization.UserAssignments);
        await ValidateReferencesAsync(input);

        var assignment = await _assignmentRepository.GetAsync(id);
        var isPrimary = input.IsPrimary && input.IsActive;
        if (isPrimary)
        {
            await ClearOtherPrimaryAssignmentsAsync(input.UserId, id);
        }

        assignment.ChangeOrganization(input.DepartmentId, input.PositionId, input.JobLevelId);
        assignment.ChangeUser(input.UserId);
        assignment.SetSecurityLevel(input.SecurityLevel);
        assignment.SetPrimary(isPrimary);
        assignment.SetActive(input.IsActive);

        return MapToDto(await _assignmentRepository.UpdateAsync(assignment, autoSave: true));
    }

    /// <summary>
    /// 删除用户组织关系。
    /// </summary>
    public virtual async Task DeleteAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Organization.UserAssignments);
        await _assignmentRepository.DeleteAsync(id);
    }

    /// <summary>
    /// 校验组织关系引用的用户、部门、岗位和职级存在，避免保存悬空外键语义。
    /// </summary>
    private async Task ValidateReferencesAsync(CreateUpdateUserOrganizationAssignmentDto input)
    {
        await _userRepository.GetAsync(input.UserId);
        await _departmentRepository.GetAsync(input.DepartmentId);

        if (input.PositionId.HasValue)
        {
            await _positionRepository.GetAsync(input.PositionId.Value);
        }

        if (input.JobLevelId.HasValue)
        {
            await _jobLevelRepository.GetAsync(input.JobLevelId.Value);
        }
    }

    /// <summary>
    /// 清理同一用户的其他主组织关系，保证页面和权限上下文都只看到一个主关系。
    /// </summary>
    private async Task ClearOtherPrimaryAssignmentsAsync(Guid userId, Guid? excludedAssignmentId = null)
    {
        var queryable = await _assignmentRepository.GetQueryableAsync();
        var primaryAssignments = await AsyncExecuter.ToListAsync(queryable.Where(x =>
            x.UserId == userId &&
            x.IsPrimary &&
            (!excludedAssignmentId.HasValue || x.Id != excludedAssignmentId.Value)));

        foreach (var primaryAssignment in primaryAssignments)
        {
            primaryAssignment.SetPrimary(false);
            await _assignmentRepository.UpdateAsync(primaryAssignment);
        }
    }

    /// <summary>
    /// 将用户组织关系领域对象映射为 DTO。
    /// </summary>
    private static UserOrganizationAssignmentDto MapToDto(UserOrganizationAssignment assignment)
    {
        return new UserOrganizationAssignmentDto
        {
            Id = assignment.Id,
            CreationTime = assignment.CreationTime,
            CreatorId = assignment.CreatorId,
            LastModificationTime = assignment.LastModificationTime,
            LastModifierId = assignment.LastModifierId,
            IsDeleted = assignment.IsDeleted,
            DeleterId = assignment.DeleterId,
            DeletionTime = assignment.DeletionTime,
            UserId = assignment.UserId,
            DepartmentId = assignment.DepartmentId,
            PositionId = assignment.PositionId,
            JobLevelId = assignment.JobLevelId,
            SecurityLevel = assignment.SecurityLevel,
            IsPrimary = assignment.IsPrimary,
            IsActive = assignment.IsActive
        };
    }
}
