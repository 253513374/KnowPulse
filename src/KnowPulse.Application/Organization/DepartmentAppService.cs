using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace KnowPulse.Organization;

/// <summary>
/// 部门应用服务，提供部门主数据的分页查询、详情、创建、更新和删除能力。
/// 所有入口都会显式检查 MVP-0 部门权限，避免只依赖菜单权限。
/// </summary>
[Authorize(KnowPulsePermissions.Organization.Departments)]
public class DepartmentAppService : KnowPulseAppService, IDepartmentAppService
{
    private readonly IRepository<Department, Guid> _departmentRepository;

    public DepartmentAppService(IRepository<Department, Guid> departmentRepository)
    {
        _departmentRepository = departmentRepository;
    }

    /// <summary>
    /// 按部门编码分页获取部门列表，供 Blazor 管理页面展示。
    /// </summary>
    public virtual async Task<PagedResultDto<DepartmentDto>> GetListAsync(PagedAndSortedResultRequestDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Organization.Departments);
        var queryable = await _departmentRepository.GetQueryableAsync();
        var totalCount = await AsyncExecuter.CountAsync(queryable);
        var items = await AsyncExecuter.ToListAsync(
            queryable
                .OrderBy(x => x.Code)
                .Skip(input.SkipCount)
                .Take(input.MaxResultCount));

        return new PagedResultDto<DepartmentDto>(totalCount, items.Select(MapToDto).ToList());
    }

    /// <summary>
    /// 获取单个部门详情。
    /// </summary>
    public virtual async Task<DepartmentDto> GetAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Organization.Departments);
        return MapToDto(await _departmentRepository.GetAsync(id));
    }

    /// <summary>
    /// 创建部门，并保存编码、名称、父级和安全等级。
    /// </summary>
    public virtual async Task<DepartmentDto> CreateAsync(CreateUpdateDepartmentDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Organization.Departments);
        var department = new Department(GuidGenerator.Create(), CurrentTenant.Id, input.Code, input.Name, input.ParentId, input.SecurityLevel);
        department.SetActive(input.IsActive);

        return MapToDto(await _departmentRepository.InsertAsync(department, autoSave: true));
    }

    /// <summary>
    /// 更新部门主数据。安全等级校验由领域对象统一执行。
    /// </summary>
    public virtual async Task<DepartmentDto> UpdateAsync(Guid id, CreateUpdateDepartmentDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Organization.Departments);
        var department = await _departmentRepository.GetAsync(id);
        department.ChangeCode(input.Code);
        department.Rename(input.Name);
        department.MoveTo(input.ParentId);
        department.SetSecurityLevel(input.SecurityLevel);
        department.SetActive(input.IsActive);

        return MapToDto(await _departmentRepository.UpdateAsync(department, autoSave: true));
    }

    /// <summary>
    /// 删除部门。ABP 仓储会根据实体能力处理软删除和审计字段。
    /// </summary>
    public virtual async Task DeleteAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Organization.Departments);
        await _departmentRepository.DeleteAsync(id);
    }

    /// <summary>
    /// 将部门领域对象映射为 DTO，保持 API 输出字段清晰可审计。
    /// </summary>
    private static DepartmentDto MapToDto(Department department)
    {
        return new DepartmentDto
        {
            Id = department.Id,
            CreationTime = department.CreationTime,
            CreatorId = department.CreatorId,
            LastModificationTime = department.LastModificationTime,
            LastModifierId = department.LastModifierId,
            IsDeleted = department.IsDeleted,
            DeleterId = department.DeleterId,
            DeletionTime = department.DeletionTime,
            ParentId = department.ParentId,
            Code = department.Code,
            Name = department.Name,
            SecurityLevel = department.SecurityLevel,
            IsActive = department.IsActive
        };
    }
}
