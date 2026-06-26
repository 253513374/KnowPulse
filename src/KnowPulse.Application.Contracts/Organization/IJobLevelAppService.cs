using System;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace KnowPulse.Organization;

public interface IJobLevelAppService : IApplicationService
{
    [Authorize(KnowPulsePermissions.Organization.JobLevels)]
    Task<PagedResultDto<JobLevelDto>> GetListAsync(PagedAndSortedResultRequestDto input);
    [Authorize(KnowPulsePermissions.Organization.JobLevels)]
    Task<JobLevelDto> GetAsync(Guid id);
    [Authorize(KnowPulsePermissions.Organization.JobLevels)]
    Task<JobLevelDto> CreateAsync(CreateUpdateJobLevelDto input);
    [Authorize(KnowPulsePermissions.Organization.JobLevels)]
    Task<JobLevelDto> UpdateAsync(Guid id, CreateUpdateJobLevelDto input);
    [Authorize(KnowPulsePermissions.Organization.JobLevels)]
    Task DeleteAsync(Guid id);
}
