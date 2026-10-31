using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using KnowPulse.Education;
using KnowPulse.KnowledgeBase;
using KnowPulse.WeChat;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.Users;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class WeChatAccountAppServiceTests : KnowPulseEntityFrameworkCoreTestBase
{
    private readonly IWeChatAccountAppService _accounts;
    private readonly WeChatBindingAuthenticationService _authentication;
    private readonly IWeChatAccountBindingRepository _bindings;
    private readonly IRepository<IdentityUser, Guid> _users;
    private readonly TestWeChatLoginVerifier _verifier;
    private readonly WeChatLoginOptions _options;
    private readonly ICurrentPrincipalAccessor _principals;

    public WeChatAccountAppServiceTests()
    {
        _accounts = GetRequiredService<IWeChatAccountAppService>(); _authentication = GetRequiredService<WeChatBindingAuthenticationService>();
        _bindings = GetRequiredService<IWeChatAccountBindingRepository>(); _users = GetRequiredService<IRepository<IdentityUser, Guid>>();
        _verifier = GetRequiredService<TestWeChatLoginVerifier>(); _options = GetRequiredService<IOptions<WeChatLoginOptions>>().Value;
        _principals = GetRequiredService<ICurrentPrincipalAccessor>();
        _options.Applications["main"] = new() { Enabled = true, AppId = _verifier.AppId, AppSecret = "synthetic-not-a-real-secret", ClientId = "KnowPulse_WeChat_main", DisplayName = "合成应用" };
    }

    [Fact]
    public async Task Binding_Should_Use_The_Verified_Web_Account_And_Fresh_WeChat_Proof_Without_Creating_Accounts_Roles_Or_Libraries()
    {
        var owner = await UserAsync(); WeChatBindingCodeDto code;
        using (Actor(owner)) code = await IssueAsync();
        var userCount = await WithUnitOfWorkAsync(() => _users.CountAsync());
        var libraryCount = await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<KnowledgeLibrary, Guid>>().CountAsync());
        using var anonymous = Anonymous();
        var receipt = await _authentication.ExchangeBindingCodeAsync(Exchange(code));
        var replay = await _authentication.ExchangeBindingCodeAsync(Exchange(code)); replay.BindingId.ShouldBe(receipt.BindingId);
        var identity = await _authentication.VerifyLoginAsync(receipt.BindingId, "KnowPulse_WeChat_main", "fresh-login-code");
        identity.AccountUserId.ShouldBe(owner.Id); identity.TenantId.ShouldBeNull();
        (await _authentication.ValidateSessionAsync(identity.BindingId, owner.Id, null)).ShouldBeTrue();
        (await _authentication.ValidateSessionAsync(identity.BindingId, Guid.NewGuid(), null)).ShouldBeFalse();
        (await _authentication.ValidateSessionAsync(identity.BindingId, owner.Id, Guid.NewGuid())).ShouldBeFalse();
        (await WithUnitOfWorkAsync(() => _users.CountAsync())).ShouldBe(userCount);
        (await WithUnitOfWorkAsync(() => GetRequiredService<IRepository<KnowledgeLibrary, Guid>>().CountAsync())).ShouldBe(libraryCount);
        using (Actor(owner))
        {
            (await GetRequiredService<IEducationSchoolAppService>().GetMyWorkspaceAsync()).Workspaces.ShouldBeEmpty();
            var rows = await _accounts.GetMyBindingsAsync(new()); rows.TotalCount.ShouldBe(1); rows.Items.Single().BoundAtUtc.ShouldBe(receipt.BoundAtUtc);
            var json = JsonSerializer.Serialize(rows); json.ShouldNotContain(code.BindingCode); json.ShouldNotContain("Hash"); json.ShouldNotContain("AppSecret");
        }
        var stored = await WithUnitOfWorkAsync(() => _bindings.GetAsync(receipt.BindingId));
        stored.CodeSecretHash.ShouldBe(WeChatLoginValues.Hash(code.BindingCode.Split('.')[1])); stored.SubjectHash.ShouldBe(_verifier.SubjectHash);
    }

    [Fact]
    public async Task Issuing_A_New_Code_Should_Revoke_Previous_Pending_Codes_And_Reject_Them_Before_Calling_WeChat()
    {
        var owner = await UserAsync(); WeChatBindingCodeDto old; WeChatBindingCodeDto current;
        using (Actor(owner)) { old = await IssueAsync(); current = await IssueAsync(); }
        old.BindingCode.ShouldNotBe(current.BindingCode);
        using var anonymous = Anonymous();
        (await Should.ThrowAsync<BusinessException>(() => _authentication.ExchangeBindingCodeAsync(Exchange(old)))).Code.ShouldBe(WeChatLoginErrorCodes.InvalidBinding);
        _verifier.Calls.ShouldBe(0);
        var receipt = await _authentication.ExchangeBindingCodeAsync(Exchange(current));
        using (Actor(owner)) (await _accounts.GetMyBindingsAsync(new())).Items.Count(x => x.RevokedAtUtc.HasValue).ShouldBe(1);
        (await _authentication.ValidateSessionAsync(receipt.BindingId, owner.Id, null)).ShouldBeTrue();
    }

    [Fact]
    public async Task Revocation_Should_Block_Login_And_Old_Sessions_Keep_History_And_Allow_An_Explicit_New_Binding()
    {
        var owner = await UserAsync(); var receipt = await BoundAsync(owner);
        using (Actor(owner))
        {
            (await Should.ThrowAsync<BusinessException>(() => IssueAsync())).Code.ShouldBe(WeChatLoginErrorCodes.AlreadyBound);
            var revoked = await _accounts.RevokeMyBindingAsync(receipt.BindingId);
            (await _accounts.RevokeMyBindingAsync(receipt.BindingId)).RevokedAtUtc.ShouldBe(revoked.RevokedAtUtc);
            var newCode = await IssueAsync();
            var newReceipt = await _authentication.ExchangeBindingCodeAsync(Exchange(newCode)); newReceipt.BindingId.ShouldNotBe(receipt.BindingId);
            (await _accounts.GetMyBindingsAsync(new())).TotalCount.ShouldBe(2);
        }
        using var anonymous = Anonymous();
        (await _authentication.ValidateSessionAsync(receipt.BindingId, owner.Id, null)).ShouldBeFalse();
        await Should.ThrowAsync<BusinessException>(() => _authentication.VerifyLoginAsync(receipt.BindingId, "KnowPulse_WeChat_main", "fresh-code"));
        var old = await WithUnitOfWorkAsync(() => _bindings.GetAsync(receipt.BindingId)); old.SubjectHash.ShouldNotBeNull(); old.ActiveSubjectKey.ShouldBeNull();
    }

    [Fact]
    public async Task Binding_Should_Not_Allow_A_Second_Account_To_Take_The_Same_WeChat_Identity()
    {
        var owner = await UserAsync(); var other = await UserAsync(); var first = await BoundAsync(owner); WeChatBindingCodeDto code;
        using (Actor(other)) code = await IssueAsync();
        using var anonymous = Anonymous();
        (await Should.ThrowAsync<BusinessException>(() => _authentication.ExchangeBindingCodeAsync(Exchange(code)))).Code.ShouldBe(WeChatLoginErrorCodes.AlreadyBound);
        (await _authentication.ValidateSessionAsync(first.BindingId, owner.Id, null)).ShouldBeTrue();
        (await WithUnitOfWorkAsync(() => _bindings.CountAsync(x => x.BoundAtUtc.HasValue && !x.RevokedAtUtc.HasValue))).ShouldBe(1);
    }

    [Theory]
    [InlineData("secret")]
    [InlineData("appid")]
    [InlineData("subject")]
    [InlineData("client")]
    [InlineData("disabled")]
    public async Task Binding_Should_Reject_Changed_Proof_Application_Or_Client(string change)
    {
        var owner = await UserAsync(); WeChatBindingCodeDto code;
        using (Actor(owner)) code = await IssueAsync();
        using var anonymous = Anonymous();
        var receipt = await _authentication.ExchangeBindingCodeAsync(Exchange(code));
        if (change == "secret") code.BindingCode = code.BindingCode[..^1] + (code.BindingCode[^1] == 'A' ? "B" : "A");
        if (change == "appid") _verifier.AppId = "wxfedcba9876543210";
        if (change == "subject") _verifier.SubjectHash = WeChatLoginValues.Hash("different-wechat");
        if (change == "disabled") _options.Applications["main"].Enabled = false;
        if (change == "secret") await Should.ThrowAsync<BusinessException>(() => _authentication.ExchangeBindingCodeAsync(Exchange(code)));
        else await Should.ThrowAsync<BusinessException>(() => _authentication.VerifyLoginAsync(receipt.BindingId, change == "client" ? "KnowPulse_Blazor" : "KnowPulse_WeChat_main", "fresh-code"));
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("deleted")]
    [InlineData("stamp")]
    [InlineData("lockout")]
    public async Task Account_Changes_Should_Block_Binding_Login_And_All_Previous_Sessions(string change)
    {
        var owner = await UserAsync(); var receipt = await BoundAsync(owner);
        await WithUnitOfWorkAsync(async () =>
        {
            var user = await _users.GetAsync(owner.Id);
            if (change == "inactive") { user.SetIsActive(false); await _users.UpdateAsync(user, true); }
            if (change == "deleted") await _users.DeleteAsync(user, true);
            if (change == "stamp") (await GetRequiredService<IdentityUserManager>().UpdateSecurityStampAsync(user)).Succeeded.ShouldBeTrue();
            if (change == "lockout")
            {
                var manager = GetRequiredService<IdentityUserManager>();
                (await manager.SetLockoutEnabledAsync(user, true)).Succeeded.ShouldBeTrue();
                (await manager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(5))).Succeeded.ShouldBeTrue();
            }
        });
        using var anonymous = Anonymous();
        (await _authentication.ValidateSessionAsync(receipt.BindingId, owner.Id, null)).ShouldBeFalse();
        await Should.ThrowAsync<BusinessException>(() => _authentication.VerifyLoginAsync(receipt.BindingId, "KnowPulse_WeChat_main", "fresh-code"));
    }

    [Fact]
    public async Task Binding_Should_Use_The_Tenant_Confirmed_By_The_Web_Account_And_Restore_The_Request_Tenant_After_Exchange()
    {
        var tenantId = Guid.NewGuid(); IdentityUser owner; WeChatBindingCodeDto code;
        using (GetRequiredService<ICurrentTenant>().Change(tenantId))
        {
            owner = await UserAsync(tenantId); using var actor = Actor(owner); code = await IssueAsync();
        }
        using var anonymous = Anonymous(); var requestTenant = Guid.NewGuid();
        using var changed = GetRequiredService<ICurrentTenant>().Change(requestTenant);
        var receipt = await _authentication.ExchangeBindingCodeAsync(Exchange(code));
        var identity = await _authentication.VerifyLoginAsync(receipt.BindingId, "KnowPulse_WeChat_main", "fresh-code");
        identity.TenantId.ShouldBe(tenantId); identity.AccountUserId.ShouldBe(owner.Id);
        GetRequiredService<ICurrentTenant>().Id.ShouldBe(requestTenant);
    }

    [Fact]
    public async Task Binding_Management_Should_Reject_Peers_Anonymous_And_Unconfirmed_Issues()
    {
        var owner = await UserAsync(); var other = await UserAsync(); var receipt = await BoundAsync(owner);
        using (Actor(other))
        {
            (await _accounts.GetMyBindingsAsync(new())).TotalCount.ShouldBe(0);
            await Should.ThrowAsync<AbpAuthorizationException>(() => _accounts.RevokeMyBindingAsync(receipt.BindingId));
            await Should.ThrowAsync<BusinessException>(() => _accounts.IssueBindingCodeAsync(new() { ExpectedAccountUserId = other.Id, AppKey = "main", Password = "Synthetic1!" }));
        }
        using var anonymous = Anonymous();
        await Should.ThrowAsync<AbpAuthorizationException>(() => _accounts.GetMyBindingsAsync(new()));
        await Should.ThrowAsync<AbpAuthorizationException>(() => _accounts.GetApplicationsAsync());
    }

    [Fact]
    public async Task Code_Issuance_Should_Require_The_Current_Password_And_Commit_A_Failed_Attempt_Without_Creating_A_Binding()
    {
        var owner = await UserAsync(); using var actor = Actor(owner);
        var input = new IssueWeChatBindingCodeDto { ExpectedAccountUserId = owner.Id, AppKey = "main", Password = "Wrong-password1!", ConfirmOwnWeChatAndAccount = true };
        (await Should.ThrowAsync<BusinessException>(() => _accounts.IssueBindingCodeAsync(input))).Code.ShouldBe(WeChatLoginErrorCodes.InvalidBinding);
        (await WithUnitOfWorkAsync(() => _users.GetAsync(owner.Id))).AccessFailedCount.ShouldBe(1);
        (await WithUnitOfWorkAsync(() => _bindings.CountAsync())).ShouldBe(0);
        _verifier.Calls.ShouldBe(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Code_Issuance_Should_Reject_An_Account_Or_Tenant_Switch_Before_Checking_The_Password(bool tenant)
    {
        var owner = await UserAsync(); using var actor = Actor(owner);
        var input = new IssueWeChatBindingCodeDto { ExpectedAccountUserId = tenant ? owner.Id : Guid.NewGuid(), ExpectedTenantId = tenant ? Guid.NewGuid() : null,
            AppKey = "main", Password = "Synthetic1!", ConfirmOwnWeChatAndAccount = true };
        await Should.ThrowAsync<AbpAuthorizationException>(() => _accounts.IssueBindingCodeAsync(input));
        (await WithUnitOfWorkAsync(() => _bindings.CountAsync())).ShouldBe(0);
    }

    private Task<WeChatBindingCodeDto> IssueAsync() => _accounts.IssueBindingCodeAsync(new()
    {
        ExpectedAccountUserId = GetRequiredService<ICurrentUser>().Id ?? Guid.Empty, ExpectedTenantId = GetRequiredService<ICurrentTenant>().Id,
        AppKey = "main", Password = "Synthetic1!", ConfirmOwnWeChatAndAccount = true
    });
    private static ExchangeWeChatBindingCodeDto Exchange(WeChatBindingCodeDto code) => new() { AppKey = code.AppKey, BindingCode = code.BindingCode, Code = "fresh-one-time-code-" + Guid.NewGuid().ToString("N") };
    private async Task<WeChatBindingReceiptDto> BoundAsync(IdentityUser user)
    {
        using var actor = Actor(user); var code = await IssueAsync(); return await _authentication.ExchangeBindingCodeAsync(Exchange(code));
    }
    private Task<IdentityUser> UserAsync(Guid? tenantId = null) => WithUnitOfWorkAsync(async () =>
    {
        var id = Guid.NewGuid(); var user = new IdentityUser(id, $"wx-{id:N}", $"wx-{id:N}@example.invalid", tenantId);
        await _users.InsertAsync(user, true);
        (await GetRequiredService<IdentityUserManager>().AddPasswordAsync(user, "Synthetic1!")).Succeeded.ShouldBeTrue();
        return user;
    });
    private IDisposable Actor(IdentityUser user) => _principals.Change(new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim(AbpClaimTypes.UserId, user.Id.ToString()), new Claim(AbpClaimTypes.UserName, user.UserName)], "Test")));
    private IDisposable Anonymous() => _principals.Change(new ClaimsPrincipal(new ClaimsIdentity()));
}
