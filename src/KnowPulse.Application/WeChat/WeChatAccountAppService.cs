using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace KnowPulse.WeChat;

[Authorize]
public class WeChatAccountAppService : KnowPulseAppService, IWeChatAccountAppService
{
    private readonly IWeChatAccountBindingRepository _bindings;
    private readonly IRepository<IdentityUser, Guid> _users;
    private readonly WeChatLoginOptions _options;
    private readonly IdentityUserManager _userManager;

    public WeChatAccountAppService(IWeChatAccountBindingRepository bindings, IRepository<IdentityUser, Guid> users, IOptions<WeChatLoginOptions> options, IdentityUserManager userManager)
    {
        _bindings = bindings; _users = users; _options = options.Value; _userManager = userManager;
    }

    public virtual async Task<ListResultDto<WeChatApplicationDto>> GetApplicationsAsync()
    {
        await AccountAsync();
        return new(_options.Applications.Where(x => _options.IsEnabledApplication(x.Key, x.Value)).OrderBy(x => x.Key)
            .Select(x => new WeChatApplicationDto { AppKey = x.Key, AppId = x.Value.AppId, DisplayName = x.Value.DisplayName }).ToList());
    }

    public virtual async Task<PagedResultDto<WeChatAccountBindingDto>> GetMyBindingsAsync(PagedResultRequestDto input)
    {
        var account = await AccountAsync();
        var tenantId = CurrentTenant.Id;
        var query = (await _bindings.GetQueryableAsync()).Where(x => x.TenantId == tenantId && x.AccountUserId == account.Id);
        var count = await AsyncExecuter.CountAsync(query);
        var bindings = await AsyncExecuter.ToListAsync(query.OrderByDescending(x => !x.RevokedAtUtc.HasValue)
            .ThenByDescending(x => x.IssuedAtUtc).ThenByDescending(x => x.Id).Skip(input.SkipCount).Take(input.MaxResultCount));
        return new(count, bindings.Select(x => Map(x, account)).ToList());
    }

    [UnitOfWork]
    public virtual async Task<WeChatBindingCodeDto> IssueBindingCodeAsync(IssueWeChatBindingCodeDto input)
    {
        if (!CurrentUser.IsAuthenticated || !CurrentUser.Id.HasValue || CurrentUser.Id != input.ExpectedAccountUserId ||
            CurrentTenant.Id != input.ExpectedTenantId) throw new AbpAuthorizationException();
        var application = _options.GetApplication(input.AppKey);
        if (!input.ConfirmOwnWeChatAndAccount) throw new BusinessException(WeChatLoginErrorCodes.InvalidBinding);
        var verifiedStamp = await ConfirmPasswordAsync(input.Password);
        using var unit = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var account = await AccountAsync();
        if (WeChatLoginValues.Hash(account.SecurityStamp) != verifiedStamp) throw new BusinessException(WeChatLoginErrorCodes.InvalidBinding);
        await _users.UpdateManyAsync([account], autoSave: true);
        var tenantId = CurrentTenant.Id;
        var query = await _bindings.GetQueryableAsync();
        var existing = await AsyncExecuter.ToListAsync(query.Where(x => x.TenantId == tenantId && x.AccountUserId == account.Id &&
            x.AppId == application.AppId && !x.RevokedAtUtc.HasValue));
        if (existing.Any(x => x.BoundAtUtc.HasValue)) throw new BusinessException(WeChatLoginErrorCodes.AlreadyBound);
        var now = DateTime.UtcNow;
        foreach (var old in existing) old.Revoke(now);
        await _bindings.UpdateManyAsync(existing, autoSave: true);
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var binding = new WeChatAccountBinding(GuidGenerator.Create(), tenantId, account.Id, input.AppKey, application.AppId,
            application.ClientId, WeChatLoginValues.Hash(secret), WeChatLoginValues.Hash(account.SecurityStamp), now);
        await _bindings.InsertAsync(binding, autoSave: true);
        await unit.CompleteAsync();
        return new() { AppKey = binding.AppKey, AppId = binding.AppId, BindingCode = $"{binding.Id:N}.{secret}", ExpiresAtUtc = binding.ExpiresAtUtc };
    }

    [UnitOfWork]
    public virtual async Task<WeChatAccountBindingDto> RevokeMyBindingAsync(Guid id)
    {
        var account = await AccountAsync(forUpdate: true);
        var binding = await _bindings.FindAsync(id);
        if (binding == null || binding.AccountUserId != account.Id || binding.TenantId != CurrentTenant.Id) throw new AbpAuthorizationException();
        if (binding.Revoke(DateTime.UtcNow)) await _bindings.UpdateAsync(binding, autoSave: true);
        return Map(binding, account);
    }

    private async Task<IdentityUser> AccountAsync(bool forUpdate = false)
    {
        if (!CurrentUser.IsAuthenticated || !CurrentUser.Id.HasValue) throw new AbpAuthorizationException();
        var account = await _users.FindAsync(CurrentUser.Id.Value);
        if (account == null || !account.IsActive || account.TenantId != CurrentTenant.Id || string.IsNullOrWhiteSpace(account.SecurityStamp)) throw new AbpAuthorizationException();
        if (forUpdate) await _users.UpdateManyAsync([account], autoSave: true);
        return account;
    }

    private async Task<string> ConfirmPasswordAsync(string password)
    {
        string? verifiedStamp = null;
        // Failed password attempts must commit even when the enclosing binding request is rejected.
        using (var unit = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            var account = await AccountAsync();
            if (!string.IsNullOrWhiteSpace(password) && password.Length <= 256 && !await _userManager.IsLockedOutAsync(account))
            {
                if (await _userManager.CheckPasswordAsync(account, password))
                {
                    if ((await _userManager.ResetAccessFailedCountAsync(account)).Succeeded)
                        verifiedStamp = WeChatLoginValues.Hash(account.SecurityStamp);
                }
                else await _userManager.AccessFailedAsync(account);
            }
            await unit.CompleteAsync();
        }
        return verifiedStamp ?? throw new BusinessException(WeChatLoginErrorCodes.InvalidBinding);
    }

    private static WeChatAccountBindingDto Map(WeChatAccountBinding binding, IdentityUser account) => new()
    {
        Id = binding.Id, AppKey = binding.AppKey, AppId = binding.AppId, IssuedAtUtc = binding.IssuedAtUtc, ExpiresAtUtc = binding.ExpiresAtUtc,
        BoundAtUtc = binding.BoundAtUtc, RevokedAtUtc = binding.RevokedAtUtc,
        RequiresRebinding = binding.AccountSecurityStampHash != WeChatLoginValues.Hash(account.SecurityStamp)
    };
}
