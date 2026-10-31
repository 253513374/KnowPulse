using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace KnowPulse.WeChat;

public sealed record WeChatAuthenticatedAccount(Guid BindingId, Guid AccountUserId, Guid? TenantId);

[RemoteService(false)]
[DisableAuditing]
public class WeChatBindingAuthenticationService : KnowPulseAppService, IWeChatBindingExchange
{
    private readonly IWeChatAccountBindingRepository _bindings;
    private readonly IRepository<IdentityUser, Guid> _users;
    private readonly IWeChatLoginVerifier _verifier;
    private readonly IDataFilter _filter;
    private readonly WeChatLoginOptions _options;

    public WeChatBindingAuthenticationService(IWeChatAccountBindingRepository bindings, IRepository<IdentityUser, Guid> users,
        IWeChatLoginVerifier verifier, IDataFilter filter, IOptions<WeChatLoginOptions> options)
    {
        _bindings = bindings; _users = users; _verifier = verifier; _filter = filter; _options = options.Value;
    }

    [UnitOfWork]
    public virtual async Task<WeChatBindingReceiptDto> ExchangeBindingCodeAsync(ExchangeWeChatBindingCodeDto input)
    {
        if (string.IsNullOrWhiteSpace(input.BindingCode)) throw Invalid();
        var parts = input.BindingCode.Split('.');
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out var id) || parts[1].Length != 43 ||
            parts[1].AsSpan().IndexOfAnyExcept("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_") >= 0) throw Invalid();
        var binding = await BindingAsync(id);
        if (binding == null || binding.AppKey != input.AppKey || binding.RevokedAtUtc.HasValue ||
            !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(binding.CodeSecretHash), Encoding.ASCII.GetBytes(WeChatLoginValues.Hash(parts[1]))) ||
            (!binding.BoundAtUtc.HasValue && DateTime.UtcNow >= binding.ExpiresAtUtc)) throw Invalid();
        Application(binding);
        using var tenant = CurrentTenant.Change(binding.TenantId);
        var account = await ValidAccountAsync(binding);
        var verified = await _verifier.VerifyAsync(binding.AppKey, input.Code);
        if (verified.AppId != binding.AppId) throw Invalid();
        if (!binding.BoundAtUtc.HasValue) await _users.UpdateManyAsync([account], autoSave: true);
        if (binding.Bind(verified.SubjectHash, DateTime.UtcNow))
        {
            var query = await _bindings.GetQueryableAsync();
            if (await AsyncExecuter.AnyAsync(query.Where(x => x.Id != binding.Id &&
                (x.ActiveAccountKey == binding.ActiveAccountKey || x.ActiveSubjectKey == binding.ActiveSubjectKey))))
                throw new BusinessException(WeChatLoginErrorCodes.AlreadyBound);
            await _bindings.SaveBoundAsync(binding);
        }
        return new() { BindingId = binding.Id, AccountUserId = binding.AccountUserId, TenantId = binding.TenantId, BoundAtUtc = binding.BoundAtUtc!.Value };
    }

    [UnitOfWork]
    public virtual async Task<WeChatAuthenticatedAccount> VerifyLoginAsync(Guid bindingId, string clientId, string code)
    {
        var binding = await BindingAsync(bindingId);
        if (binding == null || !binding.IsActive || binding.ClientId != clientId) throw Invalid();
        Application(binding);
        using var tenant = CurrentTenant.Change(binding.TenantId);
        await ValidAccountAsync(binding);
        var verified = await _verifier.VerifyAsync(binding.AppKey, code);
        if (verified.AppId != binding.AppId || verified.SubjectHash != binding.SubjectHash) throw Invalid();
        return new(binding.Id, binding.AccountUserId, binding.TenantId);
    }

    [UnitOfWork]
    public virtual async Task<bool> ValidateSessionAsync(Guid bindingId, Guid userId, Guid? tenantId)
    {
        var binding = await BindingAsync(bindingId);
        if (binding == null || !binding.IsActive || binding.AccountUserId != userId || binding.TenantId != tenantId) return false;
        try
        {
            Application(binding);
            using var tenant = CurrentTenant.Change(binding.TenantId);
            await ValidAccountAsync(binding);
            return true;
        }
        catch (BusinessException) { return false; }
    }

    private void Application(WeChatAccountBinding binding)
    {
        var application = _options.GetApplication(binding.AppKey);
        if (application.AppId != binding.AppId || application.ClientId != binding.ClientId) throw Invalid();
    }

    private async Task<WeChatAccountBinding?> BindingAsync(Guid id)
    {
        using (_filter.Disable<IMultiTenant>()) return await _bindings.FindAsync(id);
    }

    private async Task<IdentityUser> ValidAccountAsync(WeChatAccountBinding binding)
    {
        var account = await _users.FindAsync(binding.AccountUserId);
        if (account == null || account.TenantId != binding.TenantId || !account.IsActive ||
            (account.LockoutEnabled && account.LockoutEnd.HasValue && account.LockoutEnd.Value > DateTimeOffset.UtcNow) || string.IsNullOrWhiteSpace(account.SecurityStamp) ||
            binding.AccountSecurityStampHash != WeChatLoginValues.Hash(account.SecurityStamp)) throw Invalid();
        return account;
    }

    private static BusinessException Invalid() => new(WeChatLoginErrorCodes.InvalidBinding);
}
