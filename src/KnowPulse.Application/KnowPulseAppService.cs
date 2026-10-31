using System;
using System.Threading.Tasks;
using KnowPulse.KnowledgeBase;
using KnowPulse.Localization;
using Volo.Abp.Application.Services;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace KnowPulse;

/* Inherit your application services from this class.
 */
public abstract class KnowPulseAppService : ApplicationService
{
    protected KnowPulseAppService()
    {
        LocalizationResource = typeof(KnowPulseResource);
    }

    protected async Task CheckMvpPermissionAsync(string permissionName)
    {
        if (!CurrentUser.IsAuthenticated)
        {
            throw new AbpAuthorizationException("Authentication is required for this operation.");
        }

        var permissionChecker = LazyServiceProvider.LazyGetRequiredService<IPermissionChecker>();
        if (!await permissionChecker.IsGrantedAsync(permissionName))
        {
            throw new AbpAuthorizationException($"Permission is required: {permissionName}");
        }
    }

    protected Task<bool> IsMvpPermissionGrantedAsync(string permissionName)
    {
        if (!CurrentUser.IsAuthenticated)
        {
            return Task.FromResult(false);
        }

        return LazyServiceProvider.LazyGetRequiredService<IPermissionChecker>().IsGrantedAsync(permissionName);
    }

    protected async Task EnsurePersonalKnowledgeOwnerAsync(KnowledgeItem item)
    {
        if (!item.LibraryId.HasValue && !item.OwnerUserId.HasValue)
        {
            return;
        }

        if (!CurrentUser.IsAuthenticated || !item.LibraryId.HasValue ||
            !item.OwnerUserId.HasValue || item.OwnerUserId != CurrentUser.Id ||
            item.TenantId != CurrentTenant.Id)
        {
            throw new AbpAuthorizationException("Personal knowledge is only accessible to its owner.");
        }

        var libraries = LazyServiceProvider.LazyGetRequiredService<IRepository<KnowledgeLibrary, Guid>>();
        var library = await libraries.FindAsync(item.LibraryId.Value);
        if (library == null || library.IsDeleted || library.OwnerUserId != item.OwnerUserId || library.TenantId != item.TenantId)
        {
            throw new AbpAuthorizationException("The personal knowledge library is unavailable.");
        }
    }

    protected async Task CheckKnowledgeItemPermissionAsync(KnowledgeItem item, string organizationalPermission)
    {
        await CheckMvpPermissionAsync(item.LibraryId.HasValue || item.OwnerUserId.HasValue
            ? KnowPulse.Permissions.KnowPulsePermissions.KnowledgeBase.PersonalLibrary
            : organizationalPermission);
        await EnsurePersonalKnowledgeOwnerAsync(item);
    }
}
