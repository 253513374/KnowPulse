using System;
using System.Linq;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;

namespace KnowPulse.KnowledgeBase;

// 旧学校共享仅保留来源授权校验，不再提供创建、读取正文或撤回的公开接口。
[RemoteService(false)]
[Authorize(KnowPulsePermissions.KnowledgeBase.PersonalLibrary)]
public class SchoolKnowledgeSourceAccessGuard : KnowPulseAppService
{
    private IRepository<T, Guid> Repo<T>() where T : class, IEntity<Guid>
        => LazyServiceProvider.LazyGetRequiredService<IRepository<T, Guid>>();

    internal async Task<IQueryable<SchoolKnowledgeShare>> GetAvailableSharesQueryAsync(
        SchoolKnowledgeShareUses requiredUses, Guid? schoolId = null)
    {
        if (!CurrentUser.IsAuthenticated || !CurrentUser.Id.HasValue || CurrentUser.Id == Guid.Empty)
            throw new AbpAuthorizationException("An identified account is required.");
        var account = await Repo<IdentityUser>().FindAsync(CurrentUser.Id.Value);
        if (account == null || account.IsDeleted || !account.IsActive || account.TenantId != CurrentTenant.Id)
            throw new AbpAuthorizationException("An active account in the current tenant is required.");
        var uses = requiredUses | SchoolKnowledgeShareUses.Read;
        SchoolKnowledgeShare.EnsureValidUses(uses);
        if (requiredUses == 0 || (requiredUses & SchoolKnowledgeShareUses.Export) != 0)
            throw new BusinessException(SchoolKnowledgeSharingErrorCodes.InvalidUses);
        if (schoolId.HasValue) await EnsureTeacherSchoolAsync(schoolId.Value);

        var tenantId = CurrentTenant.Id;
        var userId = CurrentUser.Id.Value;
        var now = DateTime.UtcNow;
        var items = await Repo<KnowledgeItem>().GetQueryableAsync();
        var versions = await Repo<KnowledgeVersion>().GetQueryableAsync();
        var libraries = await Repo<KnowledgeLibrary>().GetQueryableAsync();
        var schools = await Repo<School>().GetQueryableAsync();
        var users = await Repo<IdentityUser>().GetQueryableAsync();
        var memberships = (await Repo<SchoolMembership>().GetQueryableAsync()).Where(x => !x.IsDeleted && x.TenantId == tenantId &&
            x.Role == SchoolMemberRole.Teacher && x.EffectiveFromUtc <= now &&
            (!x.EffectiveToUtc.HasValue || now < x.EffectiveToUtc.Value));
        return (await Repo<SchoolKnowledgeShare>().GetQueryableAsync()).Where(share => !share.IsDeleted && !share.IsRevoked && share.TenantId == tenantId &&
            (share.Uses & uses) == uses &&
            (!schoolId.HasValue || share.SchoolId == schoolId) &&
            items.Any(item => !item.IsDeleted && item.IsActive && item.TenantId == tenantId && item.Id == share.KnowledgeItemId &&
                item.OwnerUserId == share.OwnerUserId && item.LibraryId == share.LibraryId) &&
            versions.Any(version => !version.IsDeleted && version.TenantId == tenantId && version.Id == share.KnowledgeVersionId &&
                version.KnowledgeItemId == share.KnowledgeItemId && version.Status == KnowledgeVersionStatus.Published) &&
            libraries.Any(library => !library.IsDeleted && library.TenantId == tenantId && library.Id == share.LibraryId &&
                library.OwnerUserId == share.OwnerUserId) &&
            schools.Any(school => !school.IsDeleted && school.TenantId == tenantId && school.Id == share.SchoolId) &&
            users.Any(owner => !owner.IsDeleted && owner.IsActive && owner.TenantId == tenantId && owner.Id == share.OwnerUserId) &&
            users.Any(reader => !reader.IsDeleted && reader.IsActive && reader.TenantId == tenantId && reader.Id == userId) &&
            memberships.Any(membership => membership.SchoolId == share.SchoolId && membership.UserId == share.OwnerUserId) &&
            memberships.Any(membership => membership.SchoolId == share.SchoolId && membership.UserId == userId));
    }

    private async Task EnsureTeacherSchoolAsync(Guid schoolId)
    {
        var school = await Repo<School>().FindAsync(schoolId);
        if (school == null || school.IsDeleted || school.TenantId != CurrentTenant.Id)
            throw new AbpAuthorizationException("The school is unavailable.");
        var now = DateTime.UtcNow;
        var memberships = await Repo<SchoolMembership>().GetQueryableAsync();
        if (!await AsyncExecuter.AnyAsync(memberships.Where(x => !x.IsDeleted && x.TenantId == school.TenantId &&
            x.SchoolId == school.Id && x.UserId == CurrentUser.Id && x.Role == SchoolMemberRole.Teacher &&
            x.EffectiveFromUtc <= now && (!x.EffectiveToUtc.HasValue || now < x.EffectiveToUtc.Value))))
            throw new AbpAuthorizationException("An effective teacher membership in this school is required.");
    }
}
