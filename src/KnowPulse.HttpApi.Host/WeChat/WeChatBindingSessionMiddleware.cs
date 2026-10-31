using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using OpenIddict.Abstractions;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Volo.Abp.Users;

namespace KnowPulse.WeChat;

public class WeChatBindingSessionMiddleware
{
    private readonly RequestDelegate _next;
    public WeChatBindingSessionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, WeChatBindingAuthenticationService authentication, ICurrentUser user,
        ICurrentTenant tenant, IUnitOfWorkManager units, IOpenIddictTokenManager tokens)
    {
        var claims = context.User.FindAll(WeChatLoginValues.BindingClaim).ToArray();
        if (claims.Length == 0) { await _next(context); return; }
        var valid = false;
        if (claims.Length == 1 && Guid.TryParse(claims[0].Value, out var bindingId) && user.IsAuthenticated && user.Id.HasValue && user.TenantId == tenant.Id)
        {
            using (var unit = units.Begin(requiresNew: true, isTransactional: false))
            {
                var tokenId = context.User.GetTokenId();
                var token = string.IsNullOrWhiteSpace(tokenId) ? null : await tokens.FindByIdAsync(tokenId);
                valid = token != null && await tokens.HasStatusAsync(token, OpenIddictConstants.Statuses.Valid) &&
                    await tokens.GetSubjectAsync(token) == user.Id.Value.ToString() &&
                    await authentication.ValidateSessionAsync(bindingId, user.Id.Value, tenant.Id);
                await unit.CompleteAsync();
            }
        }
        if (!valid)
        {
            context.Response.Headers.CacheControl = "no-store, private";
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        await _next(context);
    }
}
