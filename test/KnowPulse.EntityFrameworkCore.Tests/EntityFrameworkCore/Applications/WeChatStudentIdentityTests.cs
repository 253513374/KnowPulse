using System;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.Teaching;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace KnowPulse.EntityFrameworkCore.Applications;

[Collection(KnowPulseTestConsts.CollectionDefinitionName)]
public class WeChatStudentIdentityTests : KnowPulseEntityFrameworkCoreTestBase
{
    [Fact]
    public async Task Should_Map_Verified_WeChat_Login_To_One_Stable_Student_Without_Default_Teacher_Role()
    {
        var exchange = Substitute.For<IWeChatCodeExchangeClient>();
        var openId = "student-" + Guid.NewGuid().ToString("N");
        exchange.GetOpenIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(openId);
        var users = GetRequiredService<IdentityUserManager>();
        var service = new WeChatStudentAuthenticator(exchange,
            Options.Create(new WeChatMiniProgramOptions { AppId = "test-identity-app" }),
            users, GetRequiredService<ICurrentTenant>(), GetRequiredService<IGuidGenerator>());
        var first = await WithUnitOfWorkAsync(() => service.AuthenticateAsync("first-code"));
        var second = await WithUnitOfWorkAsync(() => service.AuthenticateAsync("second-code"));
        first.Id.ShouldBe(second.Id);
        first.UserName.ShouldStartWith("wx_");
        first.UserName.ShouldNotContain(openId);
        await WithUnitOfWorkAsync(async () =>
        {
            (await users.GetRolesAsync(await users.GetByIdAsync(first.Id))).ShouldBe(["student"]);
            var login = await users.FindByLoginAsync("WeChatMini:test-identity-app", openId);
            login!.Id.ShouldBe(first.Id);
        });
    }
}
