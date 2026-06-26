using System;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.Organization;

public interface IPositionAppService : IApplicationService
{
    [Authorize(KnowPulsePermissions.Organization.Positions)]
    Task<PagedResultDto<PositionDto>> GetListAsync(PagedAndSortedResultRequestDto input);
    [Authorize(KnowPulsePermissions.Organization.Positions)]
    Task<PositionDto> GetAsync(Guid id);
    [Authorize(KnowPulsePermissions.Organization.Positions)]
    Task<PositionDto> CreateAsync(CreateUpdatePositionDto input);
    [Authorize(KnowPulsePermissions.Organization.Positions)]
    Task<PositionDto> UpdateAsync(Guid id, CreateUpdatePositionDto input);
    [Authorize(KnowPulsePermissions.Organization.Positions)]
    Task DeleteAsync(Guid id);
}
