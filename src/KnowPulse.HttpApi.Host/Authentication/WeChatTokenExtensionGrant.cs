using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KnowPulse.Teaching;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Volo.Abp;
using Volo.Abp.OpenIddict;
using Volo.Abp.OpenIddict.ExtensionGrantTypes;
using IdentityUser = Volo.Abp.Identity.IdentityUser;
using SignInResult = Microsoft.AspNetCore.Mvc.SignInResult;

namespace KnowPulse.Authentication;

public class WeChatTokenExtensionGrant : ITokenExtensionGrant
{
    public const string GrantType = "wechat";
    public const string ClientId = "KnowPulse_MiniProgram";
    public string Name => GrantType;

    public async Task<IActionResult> HandleAsync(ExtensionGrantContext context)
    {
        if (context.Request.ClientId != ClientId)
            return Denied("当前客户端不能使用微信登录。");
        var code = context.Request.GetParameter("code")?.ToString() ?? string.Empty;
        try
        {
            var services = context.HttpContext.RequestServices;
            var user = await services.GetRequiredService<WeChatStudentAuthenticator>()
                .AuthenticateAsync(code, context.HttpContext.RequestAborted);
            var principal = await services.GetRequiredService<IUserClaimsPrincipalFactory<IdentityUser>>().CreateAsync(user);
            principal.SetScopes("KnowPulse", "profile", "roles");
            principal.SetResources("KnowPulse");
            principal.SetAccessTokenLifetime(TimeSpan.FromHours(2));
            await services.GetRequiredService<AbpOpenIddictClaimsPrincipalManager>().HandleAsync(context.Request, principal);
            return new SignInResult(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, principal);
        }
        catch (UserFriendlyException ex)
        {
            return Denied(ex.Message);
        }
    }

    private static ForbidResult Denied(string message) => new(
        [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme],
        new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = OpenIddictConstants.Errors.InvalidGrant,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = message
        }));
}
