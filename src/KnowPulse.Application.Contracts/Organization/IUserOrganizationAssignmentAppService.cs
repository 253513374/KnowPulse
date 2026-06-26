using System;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.Organization;

public interface IUserOrganizationAssignmentAppService : IApplicationService
{
    [Authorize(KnowPulsePermissions.Organization.UserAssignments)]
    Task<PagedResultDto<UserOrganizationAssignmentDto>> GetListAsync(GetUserOrganizationAssignmentListInput input);
    [Authorize(KnowPulsePermissions.Organization.UserAssignments)]
    Task<UserOrganizationAssignmentDto> GetAsync(Guid id);
    [Authorize(KnowPulsePermissions.Organization.UserAssignments)]
    Task<UserOrganizationAssignmentDto> CreateAsync(CreateUpdateUserOrganizationAssignmentDto input);
    [Authorize(KnowPulsePermissions.Organization.UserAssignments)]
    Task<UserOrganizationAssignmentDto> UpdateAsync(Guid id, CreateUpdateUserOrganizationAssignmentDto input);
    [Authorize(KnowPulsePermissions.Organization.UserAssignments)]
    Task DeleteAsync(Guid id);
}
