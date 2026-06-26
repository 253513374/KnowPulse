using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace KnowPulse.KnowledgeBase;

/// <summary>
/// 知识分类应用服务，维护知识库目录结构的基础主数据。
/// MVP-0 只维护父级编号和启用状态，复杂树校验可在后续迭代补充。
/// </summary>
[Authorize(KnowPulsePermissions.KnowledgeBase.Categories)]
public class KnowledgeCategoryAppService : KnowPulseAppService, IKnowledgeCategoryAppService
{
    private readonly IRepository<KnowledgeCategory, Guid> _categoryRepository;

    public KnowledgeCategoryAppService(IRepository<KnowledgeCategory, Guid> categoryRepository)
    {
        _categoryRepository = categoryRepository;
    }

    /// <summary>
    /// 按分类编码分页获取知识分类列表。
    /// </summary>
    public virtual async Task<PagedResultDto<KnowledgeCategoryDto>> GetListAsync(PagedAndSortedResultRequestDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Categories);
        var queryable = await _categoryRepository.GetQueryableAsync();
        var totalCount = await AsyncExecuter.CountAsync(queryable);
        var items = await AsyncExecuter.ToListAsync(queryable.OrderBy(x => x.Code).Skip(input.SkipCount).Take(input.MaxResultCount));

        return new PagedResultDto<KnowledgeCategoryDto>(totalCount, items.Select(MapToDto).ToList());
    }

    /// <summary>
    /// 获取单个知识分类详情。
    /// </summary>
    public virtual async Task<KnowledgeCategoryDto> GetAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Categories);
        return MapToDto(await _categoryRepository.GetAsync(id));
    }

    /// <summary>
    /// 创建知识分类。
    /// </summary>
    public virtual async Task<KnowledgeCategoryDto> CreateAsync(CreateUpdateKnowledgeCategoryDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Categories);
        var category = new KnowledgeCategory(GuidGenerator.Create(), CurrentTenant.Id, input.Code, input.Name, input.ParentId);
        category.SetActive(input.IsActive);

        return MapToDto(await _categoryRepository.InsertAsync(category, autoSave: true));
    }

    /// <summary>
    /// 更新知识分类编码、名称、父级和启用状态。
    /// </summary>
    public virtual async Task<KnowledgeCategoryDto> UpdateAsync(Guid id, CreateUpdateKnowledgeCategoryDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Categories);
        var category = await _categoryRepository.GetAsync(id);
        category.ChangeCode(input.Code);
        category.Rename(input.Name);
        category.MoveTo(input.ParentId);
        category.SetActive(input.IsActive);

        return MapToDto(await _categoryRepository.UpdateAsync(category, autoSave: true));
    }

    /// <summary>
    /// 删除知识分类。当前不会级联删除分类下的知识文档。
    /// </summary>
    public virtual async Task DeleteAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Categories);
        await _categoryRepository.DeleteAsync(id);
    }

    /// <summary>
    /// 将知识分类领域对象映射为 DTO。
    /// </summary>
    private static KnowledgeCategoryDto MapToDto(KnowledgeCategory category)
    {
        return new KnowledgeCategoryDto
        {
            Id = category.Id,
            CreationTime = category.CreationTime,
            CreatorId = category.CreatorId,
            LastModificationTime = category.LastModificationTime,
            LastModifierId = category.LastModifierId,
            IsDeleted = category.IsDeleted,
            DeleterId = category.DeleterId,
            DeletionTime = category.DeletionTime,
            ParentId = category.ParentId,
            Code = category.Code,
            Name = category.Name,
            IsActive = category.IsActive
        };
    }
}
