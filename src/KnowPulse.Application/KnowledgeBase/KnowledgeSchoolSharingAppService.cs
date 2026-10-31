using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.KnowledgeIndexing;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace KnowPulse.KnowledgeBase;

/// <summary>
/// 共享是独立的指定版本授权，不修改个人条目访问策略或当前发布版本。
/// 每次新使用以关系型事实重新检查学校、教师身份、版本及用途。
/// </summary>
[Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
public class KnowledgeSchoolSharingAppService : KnowPulseAppService, IKnowledgeSchoolSharingAppService
{
    private readonly IRepository<SchoolKnowledgeShare, Guid> _shares;
    private readonly IRepository<KnowledgeItem, Guid> _items;
    private readonly IRepository<KnowledgeVersion, Guid> _versions;
    private readonly IRepository<KnowledgeLibrary, Guid> _libraries;
    private readonly IRepository<School, Guid> _schools;
    private readonly IRepository<SchoolMembership, Guid> _memberships;
    private readonly IRepository<IdentityUser, Guid> _users;
    private readonly IRepository<KnowledgeAttachment, Guid> _attachments;
    private readonly IRepository<KnowledgeIndexJob, Guid> _jobs;
    private readonly IRepository<KnowledgeChunk, Guid> _chunks;
    private readonly IBlobContainer<KnowledgeAttachmentBlobContainer> _attachmentContainer;
    private readonly IUnitOfWorkManager _uowManager;

    public KnowledgeSchoolSharingAppService(IRepository<SchoolKnowledgeShare, Guid> shares,
        IRepository<KnowledgeItem, Guid> items, IRepository<KnowledgeVersion, Guid> versions,
        IRepository<KnowledgeLibrary, Guid> libraries, IRepository<School, Guid> schools,
        IRepository<SchoolMembership, Guid> memberships, IRepository<IdentityUser, Guid> users,
        IRepository<KnowledgeAttachment, Guid> attachments, IRepository<KnowledgeIndexJob, Guid> jobs,
        IRepository<KnowledgeChunk, Guid> chunks,
        IBlobContainer<KnowledgeAttachmentBlobContainer> attachmentContainer, IUnitOfWorkManager uowManager)
    {
        _shares = shares;
        _items = items;
        _versions = versions;
        _libraries = libraries;
        _schools = schools;
        _memberships = memberships;
        _users = users;
        _attachments = attachments;
        _jobs = jobs;
        _chunks = chunks;
        _attachmentContainer = attachmentContainer;
        _uowManager = uowManager;
    }

    public virtual async Task<SchoolKnowledgeShareDto> ShareVersionAsync(CreateSchoolKnowledgeShareDto input)
    {
        await CheckPersonalPermissionAsync();
        SchoolKnowledgeShare.EnsureValidUses(input.Uses);
        if ((input.Uses & SchoolKnowledgeShareUses.Export) != 0)
            throw new BusinessException(SchoolKnowledgeSharingErrorCodes.InvalidUses);
        if (input.IdempotencyKey == Guid.Empty || input.KnowledgeItemId == Guid.Empty ||
            input.KnowledgeVersionId == Guid.Empty || input.SchoolId == Guid.Empty)
            throw new BusinessException(EducationErrorCodes.InvalidIdentifier);

        try
        {
            using var uow = _uowManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true);
            var repeated = await _shares.FindAsync(input.IdempotencyKey);
            if (repeated != null)
            {
                EnsureMatchingRequest(repeated, input);
                await uow.CompleteAsync();
                return Map(repeated);
            }

            var item = await _items.GetAsync(input.KnowledgeItemId);
            await EnsurePersonalKnowledgeOwnerAsync(item);
            if (!item.LibraryId.HasValue || !item.OwnerUserId.HasValue || item.IsDeleted || !item.IsActive)
                throw new AbpAuthorizationException("An available personal item owned by this account is required.");
            var version = await _versions.GetAsync(input.KnowledgeVersionId);
            if (version.IsDeleted || version.TenantId != item.TenantId || version.KnowledgeItemId != item.Id ||
                version.Status != KnowledgeVersionStatus.Published)
                throw new BusinessException(SchoolKnowledgeSharingErrorCodes.PreparedVersionRequired);
            var school = await GetTeacherSchoolAsync(input.SchoolId);

            var shares = await _shares.GetQueryableAsync();
            var existing = await AsyncExecuter.FirstOrDefaultAsync(shares.Where(x => x.TenantId == item.TenantId &&
                x.KnowledgeItemId == item.Id && x.KnowledgeVersionId == version.Id && x.SchoolId == school.Id &&
                !x.IsDeleted && !x.IsRevoked));
            if (existing != null)
            {
                EnsureMatchingRequest(existing, input);
                await uow.CompleteAsync();
                return Map(existing);
            }

            // 条目摘要可随私有草稿变化，版本没有摘要字段。只使用作者明确确认的共享摘要，默认空。
            var share = new SchoolKnowledgeShare(input.IdempotencyKey, item.TenantId, item.OwnerUserId.Value,
                item.LibraryId.Value, item.Id, version.Id, school.Id, version.Title, input.SharedSummary, input.Uses, DateTime.UtcNow,
                await LazyServiceProvider.LazyGetRequiredService<EducationMaterialScope>().GetShareBindingIdsAsync(item, version, school.Id));

            // 与版本切换及学校成员变更争用已有并发令牌，唯一有效授权由数据库过滤唯一索引兜底。
            school.AdvanceConfigurationRevision();
            await _schools.UpdateAsync(school, autoSave: true);
            await _items.UpdateManyAsync([item], autoSave: true);
            // 资料关联确认/结束写入同一版本屏障，冻结名单不能来自并发变更中的半份关系。
            await _versions.UpdateManyAsync([version], autoSave: true);
            await _shares.InsertAsync(share, autoSave: true);
            await EnsureRetrievalIndexAsync(share);
            await uow.CompleteAsync();
            return Map(share);
        }
        catch (Exception exception) when (exception is not AbpAuthorizationException &&
            exception is not BusinessException && exception is not OperationCanceledException)
        {
            // 失败事务已释放。只将另一请求已经提交的完全相同授权视为成功，其他存储失败继续抛出。
            using var recovery = _uowManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = false }, requiresNew: true);
            var shares = await _shares.GetQueryableAsync();
            var winner = await AsyncExecuter.FirstOrDefaultAsync(shares.Where(x => x.TenantId == CurrentTenant.Id &&
                x.OwnerUserId == CurrentUser.Id && x.Id == input.IdempotencyKey));
            winner ??= await AsyncExecuter.FirstOrDefaultAsync((await GetAvailableSharesQueryAsync(SchoolKnowledgeShareUses.Read)).Where(x =>
                x.OwnerUserId == CurrentUser.Id && x.KnowledgeItemId == input.KnowledgeItemId &&
                x.KnowledgeVersionId == input.KnowledgeVersionId && x.SchoolId == input.SchoolId));
            if (winner == null) throw;
            EnsureMatchingRequest(winner, input);
            await recovery.CompleteAsync();
            return Map(winner);
        }
    }

    public virtual async Task<PagedResultDto<SchoolKnowledgeShareDto>> GetMySharesAsync(GetSchoolKnowledgeShareListInput input)
    {
        await CheckPersonalPermissionAsync();
        var query = (await _shares.GetQueryableAsync()).Where(x => !x.IsDeleted && x.TenantId == CurrentTenant.Id &&
            x.OwnerUserId == CurrentUser.Id);
        return await PageAsync(query, input);
    }

    public virtual async Task<PagedResultDto<SchoolKnowledgeShareDto>> GetSchoolSharesAsync(Guid schoolId, GetSchoolKnowledgeShareListInput input)
    {
        await CheckPersonalPermissionAsync();
        var query = await GetAvailableSharesQueryAsync(SchoolKnowledgeShareUses.Read, schoolId);
        return await PageAsync(query, input);
    }

    public virtual async Task<SchoolKnowledgeShareDto> GetAsync(Guid shareId)
    {
        await CheckPersonalPermissionAsync();
        var share = await _shares.FindAsync(shareId);
        if (share != null && !share.IsDeleted && share.TenantId == CurrentTenant.Id && share.OwnerUserId == CurrentUser.Id)
            return Map(share);
        return Map(await GetReadableShareAsync(shareId, SchoolKnowledgeShareUses.Read));
    }

    public virtual async Task<SchoolKnowledgeShareDetailDto> GetDetailAsync(Guid shareId)
    {
        await CheckPersonalPermissionAsync();
        var share = await GetReadableShareAsync(shareId, SchoolKnowledgeShareUses.Read);
        var version = await _versions.GetAsync(share.KnowledgeVersionId);
        var jobs = await _jobs.GetQueryableAsync();
        var latestJob = await AsyncExecuter.FirstOrDefaultAsync(jobs.Where(x => !x.IsDeleted && x.TenantId == share.TenantId &&
            x.KnowledgeItemId == share.KnowledgeItemId && x.KnowledgeVersionId == share.KnowledgeVersionId)
            .OrderByDescending(x => x.CreationTime).ThenByDescending(x => x.Id));
        var attachments = await _attachments.GetQueryableAsync();
        var rows = await AsyncExecuter.ToListAsync(attachments.Where(x => !x.IsDeleted && x.TenantId == share.TenantId &&
            x.KnowledgeVersionId == share.KnowledgeVersionId).OrderBy(x => x.FileName).ThenBy(x => x.Id));
        return new SchoolKnowledgeShareDetailDto
        {
            Share = Map(share), VersionNumber = version.VersionNumber, ContentText = version.ContentText,
            IndexStatus = latestJob?.Status,
            Attachments = rows.Select(x => new KnowledgeAttachmentDto
            {
                Id = x.Id, KnowledgeVersionId = x.KnowledgeVersionId, FileName = x.FileName,
                ContentType = x.ContentType, Size = x.Size
            }).ToList()
        };
    }

    public virtual async Task<IRemoteStreamContent> DownloadAttachmentAsync(Guid shareId, Guid attachmentId)
    {
        await CheckPersonalPermissionAsync();
        var share = await GetReadableShareAsync(shareId, SchoolKnowledgeShareUses.Download);
        var attachment = await _attachments.GetAsync(attachmentId);
        if (attachment.IsDeleted || attachment.TenantId != share.TenantId || attachment.KnowledgeVersionId != share.KnowledgeVersionId)
            throw new AbpAuthorizationException("The attachment does not belong to the shared version.");
        var stream = await _attachmentContainer.GetAsync(attachment.BlobName);
        return new RemoteStreamContent(stream, attachment.FileName, attachment.ContentType, attachment.Size, disposeStream: true);
    }

    public virtual async Task<SchoolKnowledgeShareDto> RevokeAsync(Guid shareId)
    {
        await CheckPersonalPermissionAsync();
        using var uow = _uowManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true);
        var share = await _shares.GetAsync(shareId);
        if (share.IsDeleted || share.TenantId != CurrentTenant.Id || share.OwnerUserId != CurrentUser.Id)
            throw new AbpAuthorizationException("Only the author can revoke this school share.");
        if (share.IsRevoked)
        {
            var existing = Map(share);
            await uow.CompleteAsync();
            return existing;
        }
        // 离校或来源停用不妨碍作者收回授权。
        var version = await _versions.FindAsync(share.KnowledgeVersionId);
        if (version != null) await _versions.UpdateManyAsync([version], autoSave: true);
        share.Revoke(DateTime.UtcNow);
        await _shares.UpdateAsync(share, autoSave: true);
        var result = Map(share);
        await uow.CompleteAsync();
        return result;
    }

    private async Task CheckPersonalPermissionAsync()
    {
        await CheckMvpPermissionAsync(KnowPulsePermissions.KnowledgeBase.PersonalLibrary);
        await CheckAuthenticatedAccountAsync();
    }

    private async Task CheckAuthenticatedAccountAsync()
    {
        if (!CurrentUser.IsAuthenticated || !CurrentUser.Id.HasValue || CurrentUser.Id == Guid.Empty)
            throw new AbpAuthorizationException("An identified account is required.");
        var account = await _users.FindAsync(CurrentUser.Id.Value);
        if (account == null || account.IsDeleted || !account.IsActive || account.TenantId != CurrentTenant.Id)
            throw new AbpAuthorizationException("An active account in the current tenant is required.");
    }

    private async Task<School> GetTeacherSchoolAsync(Guid schoolId)
    {
        var school = await _schools.FindAsync(schoolId);
        if (school == null || school.IsDeleted || school.TenantId != CurrentTenant.Id)
            throw new AbpAuthorizationException("The school is unavailable.");
        var now = DateTime.UtcNow;
        var memberships = await _memberships.GetQueryableAsync();
        if (!await AsyncExecuter.AnyAsync(memberships.Where(x => !x.IsDeleted && x.TenantId == school.TenantId &&
            x.SchoolId == school.Id && x.UserId == CurrentUser.Id && x.Role == SchoolMemberRole.Teacher &&
            x.EffectiveFromUtc <= now && (!x.EffectiveToUtc.HasValue || now < x.EffectiveToUtc.Value))))
            throw new AbpAuthorizationException("An effective teacher membership in this school is required.");
        return school;
    }

    // 仅程序集内部调用，不产生 HTTP 端点；RAG/出题调用者分别校验自己的功能权限。
    internal async Task<IQueryable<SchoolKnowledgeShare>> GetAvailableSharesQueryAsync(
        SchoolKnowledgeShareUses requiredUses, Guid? schoolId = null)
    {
        await CheckAuthenticatedAccountAsync();
        SchoolKnowledgeShare.EnsureValidUses(requiredUses | SchoolKnowledgeShareUses.Read);
        if (requiredUses == 0 || (requiredUses & SchoolKnowledgeShareUses.Export) != 0)
            throw new BusinessException(SchoolKnowledgeSharingErrorCodes.InvalidUses);
        if (schoolId.HasValue) await GetTeacherSchoolAsync(schoolId.Value);
        return await AvailableSharesQueryAsync(requiredUses | SchoolKnowledgeShareUses.Read, CurrentUser.Id!.Value, schoolId);
    }

    // Caller first verifies an exact current student assignment; this is not a general student library query.
    internal async Task<IQueryable<SchoolKnowledgeShare>> GetStudentMaterialSharesQueryAsync(Guid teacherId, Guid schoolId, bool download)
    {
        await CheckAuthenticatedAccountAsync();
        return await AvailableSharesQueryAsync(SchoolKnowledgeShareUses.Read | SchoolKnowledgeShareUses.AssignToStudents |
            (download ? SchoolKnowledgeShareUses.Download : 0), teacherId, schoolId);
    }

    private async Task<IQueryable<SchoolKnowledgeShare>> AvailableSharesQueryAsync(SchoolKnowledgeShareUses uses, Guid readerUserId, Guid? schoolId)
    {
        var tenantId = CurrentTenant.Id;
        var userId = readerUserId;
        var now = DateTime.UtcNow;
        var items = await _items.GetQueryableAsync();
        var versions = await _versions.GetQueryableAsync();
        var libraries = await _libraries.GetQueryableAsync();
        var schools = await _schools.GetQueryableAsync();
        var users = await _users.GetQueryableAsync();
        var memberships = (await _memberships.GetQueryableAsync()).Where(x => !x.IsDeleted && x.TenantId == tenantId &&
            x.Role == SchoolMemberRole.Teacher && x.EffectiveFromUtc <= now &&
            (!x.EffectiveToUtc.HasValue || now < x.EffectiveToUtc.Value));
        return (await _shares.GetQueryableAsync()).Where(share => !share.IsDeleted && !share.IsRevoked && share.TenantId == tenantId &&
            (share.Uses & uses) == uses && (!schoolId.HasValue || share.SchoolId == schoolId) &&
            items.Any(item => !item.IsDeleted && item.IsActive && item.TenantId == tenantId && item.Id == share.KnowledgeItemId &&
                item.OwnerUserId == share.OwnerUserId && item.LibraryId == share.LibraryId) &&
            versions.Any(version => !version.IsDeleted && version.TenantId == tenantId && version.Id == share.KnowledgeVersionId &&
                version.KnowledgeItemId == share.KnowledgeItemId && version.Status == KnowledgeVersionStatus.Published) &&
            libraries.Any(library => !library.IsDeleted && library.TenantId == tenantId && library.Id == share.LibraryId &&
                library.OwnerUserId == share.OwnerUserId) &&
            schools.Any(school => !school.IsDeleted && school.TenantId == tenantId && school.Id == share.SchoolId) &&
            users.Any(account => !account.IsDeleted && account.IsActive && account.TenantId == tenantId && account.Id == share.OwnerUserId) &&
            users.Any(account => !account.IsDeleted && account.IsActive && account.TenantId == tenantId && account.Id == userId) &&
            memberships.Any(membership => membership.SchoolId == share.SchoolId && membership.UserId == share.OwnerUserId) &&
            memberships.Any(membership => membership.SchoolId == share.SchoolId && membership.UserId == userId));
    }

    private async Task<SchoolKnowledgeShare> GetReadableShareAsync(Guid shareId, SchoolKnowledgeShareUses use)
    {
        var share = await AsyncExecuter.FirstOrDefaultAsync((await GetAvailableSharesQueryAsync(use)).Where(x => x.Id == shareId));
        return share ?? throw new AbpAuthorizationException("This shared version or requested use is unavailable.");
    }

    private async Task EnsureRetrievalIndexAsync(SchoolKnowledgeShare share)
    {
        if (!share.Allows(SchoolKnowledgeShareUses.Retrieve)) return;
        var jobs = (await _jobs.GetQueryableAsync()).Where(x => !x.IsDeleted && x.TenantId == share.TenantId &&
            x.KnowledgeItemId == share.KnowledgeItemId && x.KnowledgeVersionId == share.KnowledgeVersionId);
        if (await AsyncExecuter.AnyAsync(jobs.Where(x => x.Status == KnowledgeIndexJobStatus.Pending ||
            x.Status == KnowledgeIndexJobStatus.Processing))) return;
        var chunks = await _chunks.GetQueryableAsync();
        if (await AsyncExecuter.AnyAsync(jobs.Where(job => job.Status == KnowledgeIndexJobStatus.Indexed &&
            chunks.Any(chunk => !chunk.IsDeleted && chunk.IsActive && chunk.IsPublishedSnapshot && chunk.TenantId == share.TenantId &&
                chunk.KnowledgeItemId == share.KnowledgeItemId && chunk.KnowledgeVersionId == share.KnowledgeVersionId &&
                chunk.KnowledgeIndexJobId == job.Id)))) return;

        await _jobs.InsertAsync(new KnowledgeIndexJob(GuidGenerator.Create(), share.TenantId, share.KnowledgeItemId,
            share.KnowledgeVersionId, KnowledgeIndexJobTriggerType.ManualRebuild, requestedByUserId: CurrentUser.Id), autoSave: true);
    }

    private async Task<PagedResultDto<SchoolKnowledgeShareDto>> PageAsync(IQueryable<SchoolKnowledgeShare> query, GetSchoolKnowledgeShareListInput input)
    {
        if (input.KnowledgeItemId.HasValue) query = query.Where(x => x.KnowledgeItemId == input.KnowledgeItemId.Value);
        if (input.SchoolId.HasValue) query = query.Where(x => x.SchoolId == input.SchoolId.Value);
        if (input.IsRevoked.HasValue) query = query.Where(x => x.IsRevoked == input.IsRevoked.Value);
        if (!string.IsNullOrWhiteSpace(input.Keyword))
        {
            var keyword = input.Keyword.Trim();
            query = query.Where(x => x.Title.Contains(keyword) || (x.Summary != null && x.Summary.Contains(keyword)));
        }
        var count = await AsyncExecuter.CountAsync(query);
        var rows = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => x.SharedAtUtc).ThenBy(x => x.Id)
            .Skip(input.SkipCount).Take(input.MaxResultCount));
        return new PagedResultDto<SchoolKnowledgeShareDto>(count, rows.Select(Map).ToList());
    }

    private void EnsureMatchingRequest(SchoolKnowledgeShare share, CreateSchoolKnowledgeShareDto input)
    {
        if (share.IsDeleted || share.TenantId != CurrentTenant.Id || share.OwnerUserId != CurrentUser.Id)
            throw new AbpAuthorizationException("This sharing operation is unavailable to this account.");
        if (share.KnowledgeItemId != input.KnowledgeItemId || share.KnowledgeVersionId != input.KnowledgeVersionId ||
            share.SchoolId != input.SchoolId || share.Uses != input.Uses || share.Summary != input.SharedSummary)
            throw new BusinessException(SchoolKnowledgeSharingErrorCodes.ConflictingShare);
    }

    private static SchoolKnowledgeShareDto Map(SchoolKnowledgeShare x) => new()
    {
        Id = x.Id, OwnerUserId = x.OwnerUserId, KnowledgeItemId = x.KnowledgeItemId,
        KnowledgeVersionId = x.KnowledgeVersionId, SchoolId = x.SchoolId, Title = x.Title, Summary = x.Summary,
        CurriculumBindingIds = x.GetCurriculumBindingIds().ToList(),
        Uses = x.Uses, IsRevoked = x.IsRevoked, SharedAtUtc = x.SharedAtUtc, RevokedAtUtc = x.RevokedAtUtc
    };
}
