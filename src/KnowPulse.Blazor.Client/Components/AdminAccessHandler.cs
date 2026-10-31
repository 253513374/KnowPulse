using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Authorization.Permissions;

namespace KnowPulse.Blazor.Client.Components;

public sealed class AdminAccessRequirement : IAuthorizationRequirement { }

public sealed class AdminAccessHandler(IPermissionChecker permissionChecker) : AuthorizationHandler<AdminAccessRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminAccessRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true && await AdminNavigation.CanEnterAsync(permissionChecker))
        {
            context.Succeed(requirement);
        }
    }
}
