using System;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.OpenIddict.ExtensionGrantTypes;
using Volo.Abp.Security.Claims;
using Volo.Abp.Uow;
using Volo.Abp.Users;
using Xunit;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace KnowPulse.WeChat;

public class WeChatTokenAndSessionTests
{
    [Fact]
    public void Internal_Authentication_Should_Exclude_One_Time_Proof_Parameters_From_General_Audit()
    {
        typeof(WeChatBindingAuthenticationService).GetCustomAttributes(typeof(DisableAuditingAttribute), inherit: true).Length.ShouldBe(1);
    }

    [Fact]
    public void Anonymous_Binding_Exchange_Should_Read_Only_The_Explicit_Json_Body()
    {
        var method = typeof(WeChatBindingController).GetMethod(nameof(WeChatBindingController.ExchangeAsync))!;
        method.GetParameters().Single().GetCustomAttributes(typeof(FromBodyAttribute), inherit: true).Length.ShouldBe(1);
    }

    private static WeChatBindingAuthenticationService Authentication() => Substitute.For<WeChatBindingAuthenticationService>(
        Substitute.For<IWeChatAccountBindingRepository>(), Substitute.For<IRepository<IdentityUser, Guid>>(),
        Substitute.For<IWeChatLoginVerifier>(), Substitute.For<IDataFilter>(), Options.Create(new WeChatLoginOptions()));

    [Fact]
    public async Task Grant_Should_Use_Only_The_Verified_Account_And_Server_Claims_With_A_Short_Api_Only_Token()
    {
        var bindingId = Guid.NewGuid(); var user = new IdentityUser(Guid.NewGuid(), "verified-user", "private@example.invalid");
        var auth = Authentication();
        auth.VerifyLoginAsync(bindingId, "KnowPulse_WeChat_main", "SYNTHETIC-WX-CODE").Returns(new WeChatAuthenticatedAccount(bindingId, user.Id, null));
        var users = Substitute.For<IRepository<IdentityUser, Guid>>(); users.FindAsync(user.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(user);
        var factory = Substitute.For<IUserClaimsPrincipalFactory<IdentityUser>>();
        factory.CreateAsync(user).Returns(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(AbpClaimTypes.UserId, user.Id.ToString()), new Claim(AbpClaimTypes.UserName, user.UserName), new Claim(AbpClaimTypes.Role, "stored-role"),
            new Claim("email", "private@example.invalid"), new Claim("AspNet.Identity.SecurityStamp", "PRIVATE-STAMP")], "Verified")));
        var tenant = Substitute.For<ICurrentTenant>(); tenant.Id.Returns((Guid?)null);
        using var services = new ServiceCollection().AddSingleton(auth).AddSingleton(users).AddSingleton(factory).AddSingleton(tenant).AddSingleton<WeChatRequestLimiter>().BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var request = new OpenIddictRequest { ClientId = "KnowPulse_WeChat_main", Code = "SYNTHETIC-WX-CODE", Scope = "KnowPulse", Username = "untrusted-name" };
        request.SetParameter("binding_id", bindingId.ToString()); request.SetParameter("user_id", Guid.NewGuid().ToString()); request.SetParameter("role", "admin");
        request.ToString().ShouldNotContain("SYNTHETIC-WX-CODE");
        var result = (await new WeChatTokenExtensionGrant().HandleAsync(new ExtensionGrantContext(http, request))).ShouldBeOfType<Microsoft.AspNetCore.Mvc.SignInResult>();
        result.AuthenticationScheme.ShouldBe(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        result.Principal.FindFirst(OpenIddictConstants.Claims.Subject)!.Value.ShouldBe(user.Id.ToString());
        result.Principal.FindFirst(WeChatLoginValues.BindingClaim)!.Value.ShouldBe(bindingId.ToString());
        result.Principal.FindAll(AbpClaimTypes.Role).Select(x => x.Value).ShouldBe(new[] { "stored-role" });
        result.Principal.Claims.ShouldNotContain(x => x.Type == "email" || x.Value == "PRIVATE-STAMP" || x.Value == "untrusted-name");
        result.Principal.GetScopes().ShouldBe(new[] { "KnowPulse" }); result.Principal.GetResources().ShouldBe(new[] { "KnowPulse" });
        result.Principal.GetAccessTokenLifetime().ShouldBe(TimeSpan.FromMinutes(5));
        result.Principal.FindFirst(WeChatLoginValues.BindingClaim)!.GetDestinations().ShouldContain(OpenIddictConstants.Destinations.AccessToken);
    }

    [Theory]
    [InlineData("missing-binding")]
    [InlineData("missing-code")]
    [InlineData("scope")]
    [InlineData("offline")]
    [InlineData("verification")]
    [InlineData("tenant")]
    public async Task Grant_Should_Reject_Missing_Proof_Extra_Scopes_And_A_Mismatched_Request_Tenant(string invalid)
    {
        var bindingId = Guid.NewGuid(); var auth = Authentication(); var userId = Guid.NewGuid();
        auth.VerifyLoginAsync(bindingId, "KnowPulse_WeChat_main", "code").Returns(new WeChatAuthenticatedAccount(bindingId, userId, invalid == "tenant" ? Guid.NewGuid() : null));
        if (invalid == "verification") auth.VerifyLoginAsync(bindingId, "KnowPulse_WeChat_main", "code").Returns(Task.FromException<WeChatAuthenticatedAccount>(new BusinessException(WeChatLoginErrorCodes.InvalidBinding)));
        var tenant = Substitute.For<ICurrentTenant>(); tenant.Id.Returns((Guid?)null);
        using var services = new ServiceCollection().AddSingleton(auth).AddSingleton(tenant).AddSingleton<WeChatRequestLimiter>().BuildServiceProvider();
        var request = new OpenIddictRequest { ClientId = "KnowPulse_WeChat_main", Code = invalid == "missing-code" ? null : "code",
            Scope = invalid == "scope" ? "other" : invalid == "offline" ? "KnowPulse offline_access" : "KnowPulse" };
        if (invalid != "missing-binding") request.SetParameter("binding_id", bindingId.ToString());
        var result = (await new WeChatTokenExtensionGrant().HandleAsync(new ExtensionGrantContext(new DefaultHttpContext { RequestServices = services }, request))).ShouldBeOfType<ForbidResult>();
        result.Properties!.Items[OpenIddictServerAspNetCoreConstants.Properties.Error].ShouldBe(OpenIddictConstants.Errors.InvalidGrant);
        result.Properties.Items.Values.ShouldNotContain(x => x != null && x.Contains(userId.ToString()));
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("revoked-token")]
    [InlineData("wrong-subject")]
    [InlineData("revoked-binding")]
    [InlineData("missing-token")]
    [InlineData("wrong-tenant")]
    [InlineData("duplicate-binding")]
    public async Task Session_Gate_Should_Recheck_Token_Revocation_And_Account_Binding_Before_The_Business_Request(string state)
    {
        var bindingId = Guid.NewGuid(); var userId = Guid.NewGuid(); var auth = Authentication();
        auth.ValidateSessionAsync(bindingId, userId, null).Returns(state != "revoked-binding");
        var user = Substitute.For<ICurrentUser>(); user.IsAuthenticated.Returns(true); user.Id.Returns(userId); user.TenantId.Returns((Guid?)null);
        var tenant = Substitute.For<ICurrentTenant>(); tenant.Id.Returns(state == "wrong-tenant" ? Guid.NewGuid() : null);
        var unit = Substitute.For<IUnitOfWork>(); var units = Substitute.For<IUnitOfWorkManager>(); var disposed = false;
        units.Begin(Arg.Any<AbpUnitOfWorkOptions>(), true).Returns(unit); unit.When(x => x.Dispose()).Do(_ => disposed = true);
        var token = new object(); var tokens = Substitute.For<IOpenIddictTokenManager>();
        tokens.FindByIdAsync("entry", Arg.Any<CancellationToken>()).Returns(new ValueTask<object?>(token));
        tokens.HasStatusAsync(token, OpenIddictConstants.Statuses.Valid, Arg.Any<CancellationToken>()).Returns(new ValueTask<bool>(state != "revoked-token"));
        tokens.GetSubjectAsync(token, Arg.Any<CancellationToken>()).Returns(new ValueTask<string?>(state == "wrong-subject" ? Guid.NewGuid().ToString() : userId.ToString()));
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(WeChatLoginValues.BindingClaim, bindingId.ToString())], "TestBearer")) };
        if (state != "missing-token") http.User.SetTokenId("entry");
        if (state == "duplicate-binding") ((ClaimsIdentity)http.User.Identity!).AddClaim(new Claim(WeChatLoginValues.BindingClaim, Guid.NewGuid().ToString()));
        var called = false;
        var middleware = new WeChatBindingSessionMiddleware(_ => { disposed.ShouldBeTrue(); called = true; return Task.CompletedTask; });
        await middleware.InvokeAsync(http, auth, user, tenant, units, tokens);
        called.ShouldBe(state == "valid");
        if (state != "valid") { http.Response.StatusCode.ShouldBe(401); http.Response.Headers.CacheControl.ToString().ShouldBe("no-store, private"); }
    }

    [Fact]
    public async Task Session_Gate_Should_Not_Add_Binding_Requirements_To_Existing_Web_Authentication()
    {
        var called = false; var tokens = Substitute.For<IOpenIddictTokenManager>();
        var middleware = new WeChatBindingSessionMiddleware(_ => { called = true; return Task.CompletedTask; });
        await middleware.InvokeAsync(new DefaultHttpContext(), Authentication(), Substitute.For<ICurrentUser>(), Substitute.For<ICurrentTenant>(), Substitute.For<IUnitOfWorkManager>(), tokens);
        called.ShouldBeTrue();
        await tokens.DidNotReceiveWithAnyArgs().FindByIdAsync(default!);
    }

    [Fact]
    public void Credential_Requests_Should_Have_A_Bounded_Per_Address_Rate_Without_Waiting()
    {
        using var limiter = new WeChatRequestLimiter(); var http = new DefaultHttpContext(); http.Connection.RemoteIpAddress = IPAddress.Loopback;
        for (var i = 0; i < 20; i++) limiter.TryAcquire(http).ShouldBeTrue();
        limiter.TryAcquire(http).ShouldBeFalse(); http.Response.Headers.RetryAfter.ToString().ShouldBe("60");
        http.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.2"); limiter.TryAcquire(http).ShouldBeTrue();
    }
}
