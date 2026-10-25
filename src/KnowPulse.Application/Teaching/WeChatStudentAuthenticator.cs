using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Identity;
using IdentityUser = Volo.Abp.Identity.IdentityUser;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace KnowPulse.Teaching;

public class WeChatStudentAuthenticator(
    IWeChatCodeExchangeClient codeExchange,
    IOptions<WeChatMiniProgramOptions> options,
    IdentityUserManager userManager,
    ICurrentTenant currentTenant,
    IGuidGenerator guidGenerator) : ITransientDependency
{
    [UnitOfWork]
    public virtual async Task<IdentityUser> AuthenticateAsync(string code, CancellationToken cancellationToken = default)
    {
        var openId = await codeExchange.GetOpenIdAsync(code, cancellationToken);
        var provider = "WeChatMini:" + options.Value.AppId;
        var user = await userManager.FindByLoginAsync(provider, openId);
        if (user == null)
        {
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(provider + ":" + openId))).ToLowerInvariant()[..24];
            user = new IdentityUser(guidGenerator.Create(), "wx_" + key, "wx_" + key + "@students.knowpulse.invalid", currentTenant.Id) { Name = "学生" };
            (await userManager.CreateAsync(user)).CheckErrors();
            (await userManager.AddToRoleAsync(user, "student")).CheckErrors();
            (await userManager.AddLoginAsync(user, new Microsoft.AspNetCore.Identity.UserLoginInfo(provider, openId, "微信小程序"))).CheckErrors();
        }

        if (!user.IsActive || await userManager.IsLockedOutAsync(user) || !await userManager.IsInRoleAsync(user, "student"))
            throw new UserFriendlyException("当前学生账号不可用，请联系平台维护人员。");
        return user;
    }
}
