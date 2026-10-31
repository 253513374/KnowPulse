using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.OpenIddict.ExtensionGrantTypes;
using Volo.Abp.Security.Claims;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace KnowPulse.WeChat;

public class WeChatTokenExtensionGrant : ITokenExtensionGrant
{
    public string Name => WeChatLoginValues.GrantType;

    public async Task<IActionResult> HandleAsync(ExtensionGrantContext context)
    {
        var services = context.HttpContext.RequestServices;
        if (!services.GetRequiredService<WeChatRequestLimiter>().TryAcquire(context.HttpContext)) return new StatusCodeResult(429);
        var request = context.Request;
        if (!Guid.TryParse((string?)request.GetParameter("binding_id"), out var bindingId) || string.IsNullOrWhiteSpace(request.Code) ||
            string.IsNullOrWhiteSpace(request.ClientId) || !request.GetScopes().SequenceEqual(new[] { "KnowPulse" })) return Denied();
        try
        {
            var verified = await services.GetRequiredService<WeChatBindingAuthenticationService>().VerifyLoginAsync(bindingId, request.ClientId, request.Code);
            var tenant = services.GetRequiredService<ICurrentTenant>();
            if (tenant.Id != verified.TenantId) return Denied();
            var user = await services.GetRequiredService<IRepository<IdentityUser, Guid>>().FindAsync(verified.AccountUserId);
            if (user == null || !user.IsActive || user.TenantId != verified.TenantId) return Denied();
            var principal = await services.GetRequiredService<IUserClaimsPrincipalFactory<IdentityUser>>().CreateAsync(user);
            var allowed = new HashSet<string>
            {
                OpenIddictConstants.Claims.Subject, AbpClaimTypes.UserId, AbpClaimTypes.UserName, AbpClaimTypes.Name,
                AbpClaimTypes.SurName, AbpClaimTypes.Role, AbpClaimTypes.TenantId
            };
            foreach (var identity in principal.Identities)
                foreach (var claim in identity.Claims.ToList()) if (!allowed.Contains(claim.Type)) identity.RemoveClaim(claim);
            principal.SetClaim(OpenIddictConstants.Claims.Subject, user.Id.ToString());
            principal.SetClaim(AbpClaimTypes.UserId, user.Id.ToString());
            principal.SetClaim(AbpClaimTypes.TenantId, user.TenantId?.ToString());
            principal.SetClaim(WeChatLoginValues.BindingClaim, verified.BindingId.ToString());
            principal.SetScopes("KnowPulse"); principal.SetResources("KnowPulse");
            principal.SetAccessTokenLifetime(TimeSpan.FromMinutes(WeChatLoginValues.AccessTokenMinutes));
            foreach (var claim in principal.Claims)
                if (allowed.Contains(claim.Type) || claim.Type == WeChatLoginValues.BindingClaim) claim.SetDestinations(OpenIddictConstants.Destinations.AccessToken);
            return new Microsoft.AspNetCore.Mvc.SignInResult(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, principal);
        }
        catch (BusinessException) { return Denied(); }
    }

    private static ForbidResult Denied() => new(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
        new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "WeChat authentication could not be completed."
        }));
}
