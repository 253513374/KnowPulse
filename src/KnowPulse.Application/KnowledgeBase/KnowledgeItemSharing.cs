using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Permissions;
using KnowPulse.KnowledgeIndexing;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace KnowPulse.KnowledgeBase;

public partial class KnowledgeItemAppService
{
    [UnitOfWork]
    public virtual async Task<KnowledgeVersionDto> UpdateDraftVersionAsync(Guid id, Guid versionId, CreateKnowledgeVersionDto input)
    {
        await GetItemForOperationAsync(id, KnowPulsePermissions.KnowledgeBase.Versions);
        var version = await GetVersionOfItemAsync(id, versionId);
        if (version.Status != KnowledgeVersionStatus.Draft)
            throw new UserFriendlyException("只能修改资料草稿，已发布内容请创建新版本。");
        version.UpdateDraft(input.Title, input.ContentText);
        return MapToDto(await _versionRepository.UpdateAsync(version, autoSave: true));
    }

    [UnitOfWork]
    public virtual async Task<KnowledgeIndexJobDto> RetryProcessingAsync(Guid id)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Versions);
        var item = await GetOwnedItemAsync(id);
        if (!item.CurrentPublishedVersionId.HasValue)
            throw new UserFriendlyException("请先保存需要处理的资料版本。");
        var query = await _indexJobRepository.GetQueryableAsync();
        var active = await AsyncExecuter.FirstOrDefaultAsync(query.Where(x =>
            x.KnowledgeVersionId == item.CurrentPublishedVersionId &&
            (x.Status == KnowledgeIndexJobStatus.Pending || x.Status == KnowledgeIndexJobStatus.Processing)));
        if (active != null) return MapToDto(active);
        return MapToDto(await CreateIndexJobAsync(item.Id, item.CurrentPublishedVersionId.Value,
            KnowledgeIndexJobTriggerType.ManualRebuild));
    }

    private async Task<List<KnowledgeAccessPolicyRule>> ValidateTeachingPolicyAsync(
        KnowledgeItem item, List<KnowledgeAccessPolicyRule> rules)
    {
        if (!item.OwnerTeacherId.HasValue) return rules;
        if (rules.Any(x => x.ScopeType != KnowledgePermissionScopeType.User || !x.TargetId.HasValue))
        {
            throw new UserFriendlyException("教师资料只能默认私有或共享给指定教师。");
        }

        var ids = rules.Select(x => x.TargetId!.Value).Where(x => x != item.OwnerTeacherId).Distinct().ToList();
        var teachers = await GetShareTeacherQueryAsync();
        if (await AsyncExecuter.CountAsync(teachers.Where(x => ids.Contains(x.Id))) != ids.Count)
        {
            throw new UserFriendlyException("请选择有效的共享教师。");
        }

        return rules.Any(x => x.TargetId == item.OwnerTeacherId) ? rules :
            [.. rules, new KnowledgeAccessPolicyRule(KnowledgePermissionScopeType.User, item.OwnerTeacherId)];
    }

    private async Task<KnowledgeItem> GetOwnedItemAsync(Guid id)
    {
        var item = await _itemRepository.GetAsync(id);
        await EnsurePersonalKnowledgeOwnerAsync(item);
        EnsureTeachingOwner(item.OwnerUserId ?? item.OwnerTeacherId);
        return item;
    }

    public virtual async Task<ListResultDto<KnowledgeShareTeacherDto>> SearchShareTeachersAsync(string? keyword)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.Items);
        var query = await GetShareTeacherQueryAsync();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var term = keyword.Trim();
            query = query.Where(x => x.UserName.Contains(term) || (x.Name != null && x.Name.Contains(term)));
        }

        var users = await AsyncExecuter.ToListAsync(query.OrderBy(x => x.UserName).Take(20));
        return new ListResultDto<KnowledgeShareTeacherDto>(users.Select(x => new KnowledgeShareTeacherDto
        {
            Id = x.Id,
            UserName = x.UserName,
            DisplayName = string.IsNullOrWhiteSpace(x.Name) ? x.UserName : x.Name
        }).ToList());
    }

    [UnitOfWork]
    public virtual async Task SetSharedTeachersAsync(Guid id, ShareKnowledgeWithTeachersDto input)
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.PermissionScopes);
        var item = await GetOwnedItemAsync(id);
        if (!item.OwnerTeacherId.HasValue)
        {
            throw new AbpAuthorizationException("历史资料必须先核对归属，不能自动转为教师资料。");
        }

        var ids = input.TeacherIds.Distinct().Where(x => x != item.OwnerTeacherId).ToList();
        if (ids.Count > 100 || ids.Any(x => x == Guid.Empty))
        {
            throw new UserFriendlyException("请选择有效的共享教师。");
        }

        var teachers = await GetShareTeacherQueryAsync();
        if (await AsyncExecuter.CountAsync(teachers.Where(x => ids.Contains(x.Id))) != ids.Count)
        {
            throw new UserFriendlyException("共享对象中包含无效或已停用的教师。");
        }

        ids.Insert(0, item.OwnerTeacherId.Value);
        await ReplacePermissionScopesAsync(item, ids.Select(x =>
            new KnowledgeAccessPolicyRule(KnowledgePermissionScopeType.User, x)).ToList());
        await QueuePermissionChangedReindexAsync(item);
    }

    private async Task<System.Linq.IQueryable<IdentityUser>> GetShareTeacherQueryAsync()
    {
        var roles = LazyServiceProvider.LazyGetRequiredService<IRepository<IdentityRole, Guid>>();
        var users = LazyServiceProvider.LazyGetRequiredService<IRepository<IdentityUser, Guid>>();
        var roleQuery = await roles.GetQueryableAsync();
        var teacherIds = await AsyncExecuter.ToListAsync(roleQuery.Where(x => x.Name == "teacher").Select(x => x.Id));
        return (await users.GetQueryableAsync()).Where(x => x.IsActive && x.Id != CurrentUser.Id &&
            x.Roles.Any(r => teacherIds.Contains(r.RoleId)));
    }
}
