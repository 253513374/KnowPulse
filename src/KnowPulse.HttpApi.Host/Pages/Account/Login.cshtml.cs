using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Volo.Abp.Account.Web;
using Volo.Abp.Account.Web.Pages.Account;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.OpenIddict;

namespace KnowPulse.Pages.Account;

[ExposeServices(typeof(BrandLoginModel))]
public class BrandLoginModel(
    IAuthenticationSchemeProvider schemeProvider,
    IOptions<AbpAccountOptions> accountOptions,
    IOptions<IdentityOptions> identityOptions,
    IdentityDynamicClaimsPrincipalContributorCache identityDynamicClaimsPrincipalContributorCache,
    AbpOpenIddictRequestHelper openIddictRequestHelper,
    IWebHostEnvironment webHostEnvironment)
    : OpenIddictSupportedLoginModel(schemeProvider, accountOptions, identityOptions,
        identityDynamicClaimsPrincipalContributorCache, openIddictRequestHelper, webHostEnvironment), ITransientDependency
{
    public override async Task<IActionResult> OnPostAsync(string action)
    {
        var result = await base.OnPostAsync(action);
        if (!string.Equals(action, "Login", StringComparison.Ordinal) ||
            !string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.Ordinal))
        {
            return result;
        }

        var redirectUrl = result switch
        {
            LocalRedirectResult redirect when !redirect.PreserveMethod && Url.IsLocalUrl(redirect.Url)
                => Url.Content(redirect.Url),
            RedirectResult redirect when !redirect.PreserveMethod => Url.Content(redirect.Url),
            RedirectToPageResult redirect when !redirect.PreserveMethod
                => Url.Page(redirect.PageName, redirect.PageHandler, redirect.RouteValues,
                    redirect.Protocol, redirect.Host, redirect.Fragment),
            _ => null
        };

        if (redirectUrl == null)
        {
            return result;
        }

        // Return the framework's validated destination so fetch never follows the OIDC redirect itself.
        Response.Headers.CacheControl = "no-store";
        return new JsonResult(new { redirectUrl });
    }
}
