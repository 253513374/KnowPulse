using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace KnowPulse.Organization;

/// <summary>
/// 职级应用服务，维护职级编码、名称、排序、安全等级和启用状态。
/// 职级数据用于组织关系建模，并为后续学习路径和考试能力模型预留基础数据。
/// </summary>
[Authorize(KnowPulsePermissions.Organization.JobLevels)]
public class JobLevelAppService : KnowPulseAppService, IJobLevelAppService
{
    private readonly IRepository<JobLevel, Guid> _jobLevelRepository;

    public JobLevelAppService(IRepository<JobLevel, Guid> jobLevelRepository)
    {
        _jobLevelRepository = jobLevelRepository;
    }

    /// <summary>
    /// 按排序号和编码分页获取职级列表。
    /// </summary>
    public virtual async Task<PagedResultDto<JobLevelDto>> GetListAsync(PagedAndSortedResultRequestDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Organization.JobLevels);
        var queryable = await _jobLevelRepository.GetQueryableAsync();
        var totalCount = await AsyncExecuter.CountAsync(queryable);
        var items = await AsyncExecuter.ToListAsync(queryable.OrderBy(x => x.SortOrder).ThenBy(x => x.Code).Skip(input.SkipCount).Take(input.MaxResultCount));

        return new PagedResultDto<JobLevelDto>(totalCount, items.Select(MapToDto).ToList());
    }

    /// <summary>
    /// 获取单个职级详情。
    /// </summary>
    public virtual async Task<JobLevelDto> GetAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Organization.JobLevels);
        return MapToDto(await _jobLevelRepository.GetAsync(id));
    }

    /// <summary>
    /// 创建职级。
    /// </summary>
    public virtual async Task<JobLevelDto> CreateAsync(CreateUpdateJobLevelDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Organization.JobLevels);
        var jobLevel = new JobLevel(GuidGenerator.Create(), CurrentTenant.Id, input.Code, input.Name, input.SortOrder, input.SecurityLevel);
        jobLevel.SetActive(input.IsActive);

        return MapToDto(await _jobLevelRepository.InsertAsync(jobLevel, autoSave: true));
    }

    /// <summary>
    /// 更新职级主数据。安全等级不允许为负数，校验由领域对象执行。
    /// </summary>
    public virtual async Task<JobLevelDto> UpdateAsync(Guid id, CreateUpdateJobLevelDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Organization.JobLevels);
        var jobLevel = await _jobLevelRepository.GetAsync(id);
        jobLevel.ChangeCode(input.Code);
        jobLevel.Rename(input.Name);
        jobLevel.SetSortOrder(input.SortOrder);
        jobLevel.SetSecurityLevel(input.SecurityLevel);
        jobLevel.SetActive(input.IsActive);

        return MapToDto(await _jobLevelRepository.UpdateAsync(jobLevel, autoSave: true));
    }

    /// <summary>
    /// 删除职级。
    /// </summary>
    public virtual async Task DeleteAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.Organization.JobLevels);
        await _jobLevelRepository.DeleteAsync(id);
    }

    /// <summary>
    /// 将职级领域对象映射为 DTO。
    /// </summary>
    private static JobLevelDto MapToDto(JobLevel jobLevel)
    {
        return new JobLevelDto
        {
            Id = jobLevel.Id,
            CreationTime = jobLevel.CreationTime,
            CreatorId = jobLevel.CreatorId,
            LastModificationTime = jobLevel.LastModificationTime,
            LastModifierId = jobLevel.LastModifierId,
            IsDeleted = jobLevel.IsDeleted,
            DeleterId = jobLevel.DeleterId,
            DeletionTime = jobLevel.DeletionTime,
            Code = jobLevel.Code,
            Name = jobLevel.Name,
            SortOrder = jobLevel.SortOrder,
            SecurityLevel = jobLevel.SecurityLevel,
            IsActive = jobLevel.IsActive
        };
    }
}
