using System.Threading.Tasks;
using KnowPulse.Localization;
using Volo.Abp.Application.Services;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;

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
}