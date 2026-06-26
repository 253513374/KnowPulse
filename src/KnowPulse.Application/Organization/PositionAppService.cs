using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace KnowPulse.Organization;

/// <summary>
/// 岗位应用服务，维护岗位主数据及其所属部门、安全等级和启用状态。
/// 岗位数据会被知识权限范围引用，因此写操作统一通过领域方法校验。
/// </summary>
[Authorize(KnowPulsePermissions.Organization.Positions)]
public class PositionAppService : KnowPulseAppService, IPositionAppService
{
    private readonly IRepository<Position, Guid> _positionRepository;

    public PositionAppService(IRepository<Position, Guid> positionRepository)
    {
        _positionRepository = positionRepository;
    }

    /// <summary>
    /// 按岗位编码分页获取岗位列表。
    /// </summary>
    public virtual async Task<PagedResultDto<PositionDto>> GetListAsync(PagedAndSortedResultRequestDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Organization.Positions);
        var queryable = await _positionRepository.GetQueryableAsync();
        var totalCount = await AsyncExecuter.CountAsync(queryable);
        var items = await AsyncExecuter.ToListAsync(queryable.OrderBy(x => x.Code).Skip(input.SkipCount).Take(input.MaxResultCount));

        return new PagedResultDto<PositionDto>(totalCount, items.Select(MapToDto).ToList());
    }

    /// <summary>
    /// 获取单个岗位详情。
    /// </summary>
    public virtual async Task<PositionDto> GetAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Organization.Positions);
        return MapToDto(await _positionRepository.GetAsync(id));
    }

    /// <summary>
    /// 创建岗位，并可选绑定所属部门。
    /// </summary>
    public virtual async Task<PositionDto> CreateAsync(CreateUpdatePositionDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Organization.Positions);
        var position = new Position(GuidGenerator.Create(), CurrentTenant.Id, input.Code, input.Name, input.DepartmentId, input.SecurityLevel);
        position.SetActive(input.IsActive);

        return MapToDto(await _positionRepository.InsertAsync(position, autoSave: true));
    }

    /// <summary>
    /// 更新岗位编码、名称、部门、安全等级和启用状态。
    /// </summary>
    public virtual async Task<PositionDto> UpdateAsync(Guid id, CreateUpdatePositionDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Organization.Positions);
        var position = await _positionRepository.GetAsync(id);
        position.ChangeCode(input.Code);
        position.Rename(input.Name);
        position.AssignDepartment(input.DepartmentId);
        position.SetSecurityLevel(input.SecurityLevel);
        position.SetActive(input.IsActive);

        return MapToDto(await _positionRepository.UpdateAsync(position, autoSave: true));
    }

    /// <summary>
    /// 删除岗位。删除前是否存在业务引用当前交由数据库和后续业务规则兜底。
    /// </summary>
    public virtual async Task DeleteAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Organization.Positions);
        await _positionRepository.DeleteAsync(id);
    }

    /// <summary>
    /// 将岗位领域对象映射为 DTO。
    /// </summary>
    private static PositionDto MapToDto(Position position)
    {
        return new PositionDto
        {
            Id = position.Id,
            CreationTime = position.CreationTime,
            CreatorId = position.CreatorId,
            LastModificationTime = position.LastModificationTime,
            LastModifierId = position.LastModifierId,
            IsDeleted = position.IsDeleted,
            DeleterId = position.DeleterId,
            DeletionTime = position.DeletionTime,
            DepartmentId = position.DepartmentId,
            Code = position.Code,
            Name = position.Name,
            SecurityLevel = position.SecurityLevel,
            IsActive = position.IsActive
        };
    }
}
