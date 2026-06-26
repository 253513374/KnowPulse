using System;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.Organization;

public interface IDepartmentAppService : IApplicationService
{
    [Authorize(KnowPulsePermissions.Organization.Departments)]
    Task<PagedResultDto<DepartmentDto>> GetListAsync(PagedAndSortedResultRequestDto input);
    [Authorize(KnowPulsePermissions.Organization.Departments)]
    Task<DepartmentDto> GetAsync(Guid id);
    [Authorize(KnowPulsePermissions.Organization.Departments)]
    Task<DepartmentDto> CreateAsync(CreateUpdateDepartmentDto input);
    [Authorize(KnowPulsePermissions.Organization.Departments)]
    Task<DepartmentDto> UpdateAsync(Guid id, CreateUpdateDepartmentDto input);
    [Authorize(KnowPulsePermissions.Organization.Departments)]
    Task DeleteAsync(Guid id);
}
