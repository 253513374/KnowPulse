using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace KnowPulse.KnowledgeBase;

/// <summary>
/// 本人知识库入口。所有者由登录身份确定，不能通过请求参数访问或代建他人的默认库。
/// </summary>
[Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
public class KnowledgeLibraryAppService : KnowPulseAppService, IKnowledgeLibraryAppService
{
    private readonly IRepository<KnowledgeLibrary, Guid> _libraryRepository;
    private readonly IRepository<KnowledgeItem, Guid> _itemRepository;
    private readonly IRepository<KnowledgeCategory, Guid> _categoryRepository;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IDataFilter _dataFilter;

    public KnowledgeLibraryAppService(
        IRepository<KnowledgeLibrary, Guid> libraryRepository,
        IRepository<KnowledgeItem, Guid> itemRepository,
        IRepository<KnowledgeCategory, Guid> categoryRepository,
        IUnitOfWorkManager unitOfWorkManager,
        IDataFilter dataFilter)
    {
        _libraryRepository = libraryRepository;
        _itemRepository = itemRepository;
        _categoryRepository = categoryRepository;
        _unitOfWorkManager = unitOfWorkManager;
        _dataFilter = dataFilter;
    }

    public virtual async Task<PersonalKnowledgeUploadPolicyDto> GetUploadPolicyAsync()
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.PersonalLibrary);
        var options = LazyServiceProvider.LazyGetRequiredService<IOptions<PersonalKnowledgeAttachmentOptions>>().Value;
        options.EnsureValid();
        return new PersonalKnowledgeUploadPolicyDto
        {
            MaxFileBytes = options.MaxFileBytes,
            MaxFilesPerVersion = options.MaxFilesPerVersion
        };
    }

    public virtual async Task<KnowledgeLibraryDto> GetMyLibraryAsync()
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.PersonalLibrary);
        var library = await EnsureMyLibraryAsync();
        return new KnowledgeLibraryDto
        {
            Id = library.Id,
            OwnerUserId = library.OwnerUserId,
            Name = library.Name
        };
    }

    public virtual async Task<PagedResultDto<KnowledgeItemDto>> GetMyItemsAsync(GetPersonalKnowledgeItemListInput input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.PersonalLibrary);
        var library = await EnsureMyLibraryAsync();
        var query = (await _itemRepository.GetQueryableAsync())
            .Where(x => !x.IsDeleted && x.TenantId == library.TenantId &&
                        x.LibraryId == library.Id &&
                        x.OwnerUserId == library.OwnerUserId);

        ValidateOrganizationFilter(input);
        if (input.MaterialType.HasValue) query = query.Where(x => x.MaterialType == input.MaterialType.Value);
        if (input.MaterialYear.HasValue) query = query.Where(x => x.MaterialYear == input.MaterialYear.Value);
        if (!string.IsNullOrWhiteSpace(input.SubjectName))
        {
            var subject = input.SubjectName.Trim();
            query = query.Where(x => x.SubjectName == subject);
        }
        if (!string.IsNullOrWhiteSpace(input.Tag))
        {
            var tag = "\n" + input.Tag.Trim() + "\n";
            query = query.Where(x => x.PersonalTags != null && x.PersonalTags.Contains(tag));
        }

        if (!string.IsNullOrWhiteSpace(input.Keyword))
        {
            var keyword = input.Keyword.Trim();
            var versions = await LazyServiceProvider.LazyGetRequiredService<IRepository<KnowledgeVersion, Guid>>().GetQueryableAsync();
            // ponytail: 本人正文先用关系型 Contains；个人资料量增长后改为有同一owner过滤的全文索引。
            query = query.Where(x => x.Title.Contains(keyword) || (x.Summary != null && x.Summary.Contains(keyword)) ||
                (x.SubjectName != null && x.SubjectName.Contains(keyword)) || (x.PersonalTags != null && x.PersonalTags.Contains(keyword)) ||
                versions.Any(version => !version.IsDeleted && version.TenantId == library.TenantId && version.KnowledgeItemId == x.Id &&
                    version.ContentText != null && version.ContentText.Contains(keyword)));
        }

        var totalCount = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(query
            .OrderByDescending(x => x.CreationTime)
            .ThenBy(x => x.Id)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount));

        return new PagedResultDto<KnowledgeItemDto>(totalCount, items.Select(MapItemToDto).ToList());
    }

    public virtual async Task<KnowledgeItemDto> CreateItemAsync(CreateKnowledgeItemDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.PersonalLibrary);
        var library = await EnsureMyLibraryAsync();
        if (input.LibraryId.HasValue && input.LibraryId != library.Id)
        {
            throw new AbpAuthorizationException("Only the current user's personal library can be used.");
        }

        if (input.CategoryId.HasValue)
        {
            var category = await _categoryRepository.GetAsync(input.CategoryId.Value);
            if (category.TenantId != library.TenantId)
            {
                throw new AbpAuthorizationException("The category belongs to another tenant.");
            }
        }

        var item = new KnowledgeItem(
            GuidGenerator.Create(),
            library.TenantId,
            input.Title,
            input.CategoryId,
            input.Summary,
            library.Id,
            library.OwnerUserId);
        if (input.Organization != null) await ApplyOrganizationAsync(item, input.Organization);
        return MapItemToDto(await _itemRepository.InsertAsync(item, autoSave: true));
    }

    public virtual async Task<KnowledgeItemDto> UpdateOrganizationAsync(Guid itemId, UpdatePersonalKnowledgeOrganizationInput input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.PersonalLibrary);
        var item = await _itemRepository.GetAsync(itemId);
        await EnsurePersonalKnowledgeOwnerAsync(item);
        if (!item.LibraryId.HasValue || !item.OwnerUserId.HasValue || item.IsDeleted)
            throw new AbpAuthorizationException("An available personal item owned by this account is required.");
        if (string.IsNullOrWhiteSpace(input.ConcurrencyStamp) || input.ConcurrencyStamp != item.ConcurrencyStamp)
            throw new BusinessException(PersonalKnowledgeOrganizationErrorCodes.OrganizationChanged);
        await ApplyOrganizationAsync(item, input);
        return MapItemToDto(await _itemRepository.UpdateAsync(item, autoSave: true));
    }

    // 仅已有个人创建入口复用，不产生 HTTP 方法；整理信息不能改变访问策略或版本。
    internal async Task ApplyOrganizationAsync(KnowledgeItem item, PersonalKnowledgeOrganizationInput input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.PersonalLibrary);
        await EnsurePersonalKnowledgeOwnerAsync(item);
        if (!item.LibraryId.HasValue || !item.OwnerUserId.HasValue || input.CustomTags == null ||
            input.CustomTags.Count > KnowledgeItem.MaxPersonalTags)
            throw new BusinessException(PersonalKnowledgeOrganizationErrorCodes.InvalidOrganization);
        item.OrganizePersonalMaterial(input.MaterialType, input.MaterialYear, input.SubjectName, input.CustomTags);
    }

    public virtual async Task<PersonalKnowledgeOrganizationOptionsDto> GetOrganizationOptionsAsync()
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.PersonalLibrary);
        var library = await EnsureMyLibraryAsync();
        var own = (await _itemRepository.GetQueryableAsync()).Where(x => !x.IsDeleted && x.TenantId == library.TenantId &&
            x.LibraryId == library.Id && x.OwnerUserId == library.OwnerUserId);
        var subjects = await AsyncExecuter.ToListAsync(own.Where(x => x.SubjectName != null).Select(x => x.SubjectName!)
            .Distinct().OrderBy(x => x).Take(100));
        var tagTexts = await AsyncExecuter.ToListAsync(own.Where(x => x.PersonalTags != null).Select(x => x.PersonalTags!)
            .Distinct().OrderBy(x => x).Take(200));
        // 建议项有界；自由填写仍支持本人全部合法学科和标签，不变成权限或全局标签表。
        return new PersonalKnowledgeOrganizationOptionsDto
        {
            Subjects = subjects,
            Tags = tagTexts.SelectMany(x => x.Split('\n', StringSplitOptions.RemoveEmptyEntries)).Distinct(StringComparer.Ordinal)
                .OrderBy(x => x).Take(100).ToList()
        };
    }

    private static void ValidateOrganizationFilter(GetPersonalKnowledgeItemListInput input)
    {
        if ((input.MaterialType.HasValue && !Enum.IsDefined(input.MaterialType.Value)) ||
            (input.MaterialYear.HasValue && input.MaterialYear is < 1 or > 9999) ||
            (input.Keyword?.Length > 256) || input.SubjectName?.Length > KnowledgeItem.MaxSubjectNameLength ||
            input.SubjectName?.Any(char.IsControl) == true || input.Tag?.Length > KnowledgeTag.MaxNameLength || input.Tag?.Any(char.IsControl) == true)
            throw new BusinessException(PersonalKnowledgeOrganizationErrorCodes.InvalidOrganization);
    }

    private async Task<KnowledgeLibrary> EnsureMyLibraryAsync()
    {
        if (!CurrentUser.Id.HasValue || CurrentUser.Id == Guid.Empty)
        {
            throw new AbpAuthorizationException("An identified user is required for a personal library.");
        }

        var ownerUserId = CurrentUser.Id.Value;
        var tenantId = CurrentTenant.Id;
        try
        {
            // 独立事务保证唯一键竞争不会破坏外层业务事务。Id 与 OwnerUserId 均有数据库唯一性兜底。
            using var uow = _unitOfWorkManager.Begin(
                new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true);
            using var softDeleteFilter = _dataFilter.Disable<ISoftDelete>();
            // 只按当前认证账号的确定性主键查找，发现归属不同租户时拒绝，不自动创建第二个库。
            using var tenantFilter = _dataFilter.Disable<IMultiTenant>();
            var library = await _libraryRepository.FindAsync(ownerUserId);
            if (library == null)
            {
                library = await _libraryRepository.InsertAsync(new KnowledgeLibrary(ownerUserId, tenantId), autoSave: true);
            }
            else
            {
                EnsureLibraryOwnership(library, ownerUserId, tenantId);
                if (library.IsDeleted)
                {
                    library.Restore();
                    await _libraryRepository.UpdateAsync(library, autoSave: true);
                }
            }

            await uow.CompleteAsync();
            return library;
        }
        catch (Exception exception) when (exception is not AbpAuthorizationException && exception is not OperationCanceledException)
        {
            // 失败的事务与跟踪实体已释放。只有另一次请求已建立完全相同的有效库时，才能将竞争视为成功。
            using var recoveryUow = _unitOfWorkManager.Begin(
                new AbpUnitOfWorkOptions { IsTransactional = false }, requiresNew: true);
            using var tenantFilter = _dataFilter.Disable<IMultiTenant>();
            var existing = await _libraryRepository.FindAsync(ownerUserId);
            if (existing == null || existing.IsDeleted)
            {
                throw;
            }

            EnsureLibraryOwnership(existing, ownerUserId, tenantId);
            await recoveryUow.CompleteAsync();
            return existing;
        }
    }

    private static void EnsureLibraryOwnership(KnowledgeLibrary library, Guid ownerUserId, Guid? tenantId)
    {
        if (library.Id != ownerUserId || library.OwnerUserId != ownerUserId || library.TenantId != tenantId)
        {
            throw new AbpAuthorizationException("The personal library does not belong to the current account and tenant.");
        }
    }

    private static KnowledgeItemDto MapItemToDto(KnowledgeItem item)
    {
        return new KnowledgeItemDto
        {
            Id = item.Id,
            CreationTime = item.CreationTime,
            CreatorId = item.CreatorId,
            LastModificationTime = item.LastModificationTime,
            LastModifierId = item.LastModifierId,
            IsDeleted = item.IsDeleted,
            DeleterId = item.DeleterId,
            DeletionTime = item.DeletionTime,
            LibraryId = item.LibraryId,
            OwnerUserId = item.OwnerUserId,
            ConcurrencyStamp = item.ConcurrencyStamp,
            MaterialType = item.MaterialType,
            MaterialYear = item.MaterialYear,
            SubjectName = item.SubjectName,
            CustomTags = item.GetPersonalTags().ToList(),
            CategoryId = item.CategoryId,
            CurrentPublishedVersionId = item.CurrentPublishedVersionId,
            Title = item.Title,
            Summary = item.Summary,
            IsActive = item.IsActive
        };
    }
}
