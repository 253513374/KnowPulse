using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace KnowPulse.KnowledgeBase;

/// <summary>
/// 知识标签应用服务，维护知识库标签主数据。
/// 标签用于知识横向归类和后续检索增强，不直接承载知识正文。
/// </summary>
[Authorize(KnowPulsePermissions.KnowledgeBase.Tags)]
public class KnowledgeTagAppService : KnowPulseAppService, IKnowledgeTagAppService
{
    private readonly IRepository<KnowledgeTag, Guid> _tagRepository;

    public KnowledgeTagAppService(IRepository<KnowledgeTag, Guid> tagRepository)
    {
        _tagRepository = tagRepository;
    }

    /// <summary>
    /// 按标签名称分页获取标签列表。
    /// </summary>
    public virtual async Task<PagedResultDto<KnowledgeTagDto>> GetListAsync(PagedAndSortedResultRequestDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Tags);
        var queryable = await _tagRepository.GetQueryableAsync();
        var totalCount = await AsyncExecuter.CountAsync(queryable);
        var items = await AsyncExecuter.ToListAsync(queryable.OrderBy(x => x.Name).Skip(input.SkipCount).Take(input.MaxResultCount));

        return new PagedResultDto<KnowledgeTagDto>(totalCount, items.Select(MapToDto).ToList());
    }

    /// <summary>
    /// 获取单个标签详情。
    /// </summary>
    public virtual async Task<KnowledgeTagDto> GetAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Tags);
        return MapToDto(await _tagRepository.GetAsync(id));
    }

    /// <summary>
    /// 创建标签。
    /// </summary>
    public virtual async Task<KnowledgeTagDto> CreateAsync(CreateUpdateKnowledgeTagDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Tags);
        var tag = new KnowledgeTag(GuidGenerator.Create(), CurrentTenant.Id, input.Name);
        tag.SetActive(input.IsActive);

        return MapToDto(await _tagRepository.InsertAsync(tag, autoSave: true));
    }

    /// <summary>
    /// 更新标签名称和启用状态。
    /// </summary>
    public virtual async Task<KnowledgeTagDto> UpdateAsync(Guid id, CreateUpdateKnowledgeTagDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Tags);
        var tag = await _tagRepository.GetAsync(id);
        tag.Rename(input.Name);
        tag.SetActive(input.IsActive);

        return MapToDto(await _tagRepository.UpdateAsync(tag, autoSave: true));
    }

    /// <summary>
    /// 删除标签。
    /// </summary>
    public virtual async Task DeleteAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Tags);
        await _tagRepository.DeleteAsync(id);
    }

    /// <summary>
    /// 将标签领域对象映射为 DTO。
    /// </summary>
    private static KnowledgeTagDto MapToDto(KnowledgeTag tag)
    {
        return new KnowledgeTagDto
        {
            Id = tag.Id,
            CreationTime = tag.CreationTime,
            CreatorId = tag.CreatorId,
            LastModificationTime = tag.LastModificationTime,
            LastModifierId = tag.LastModifierId,
            IsDeleted = tag.IsDeleted,
            DeleterId = tag.DeleterId,
            DeletionTime = tag.DeletionTime,
            Name = tag.Name,
            IsActive = tag.IsActive
        };
    }
}
