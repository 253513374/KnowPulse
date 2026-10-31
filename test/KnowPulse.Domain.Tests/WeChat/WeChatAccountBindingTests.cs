using System;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.WeChat;

public class WeChatAccountBindingTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc);
    private static WeChatAccountBinding Binding(Guid? tenant = null, Guid? user = null, string appId = "wx0123456789abcdef") =>
        new(Guid.NewGuid(), tenant, user ?? Guid.NewGuid(), "main", appId, "KnowPulse_WeChat_main", WeChatLoginValues.Hash("secret"), WeChatLoginValues.Hash("stamp"), Now);

    [Fact]
    public void Binding_Should_Expire_At_The_Half_Open_Deadline_Without_Creating_A_Credential()
    {
        var binding = Binding(); binding.ExpiresAtUtc.ShouldBe(Now.AddMinutes(5)); binding.IsActive.ShouldBeFalse();
        Should.Throw<BusinessException>(() => binding.Bind(WeChatLoginValues.Hash("wechat"), binding.ExpiresAtUtc)).Code.ShouldBe(WeChatLoginErrorCodes.InvalidBinding);
        binding.BoundAtUtc.ShouldBeNull(); binding.ActiveAccountKey.ShouldBeNull(); binding.ActiveSubjectKey.ShouldBeNull();
    }

    [Fact]
    public void Binding_Should_Recover_Only_The_Same_Verified_Subject_And_Preserve_History_When_Revoked()
    {
        var binding = Binding(); var subject = WeChatLoginValues.Hash("wechat");
        binding.Bind(subject, Now.AddSeconds(1)).ShouldBeTrue(); binding.IsActive.ShouldBeTrue();
        var accountKey = binding.ActiveAccountKey; var subjectKey = binding.ActiveSubjectKey;
        binding.Bind(subject, Now.AddHours(1)).ShouldBeFalse();
        binding.ActiveAccountKey.ShouldBe(accountKey); binding.ActiveSubjectKey.ShouldBe(subjectKey);
        Should.Throw<BusinessException>(() => binding.Bind(WeChatLoginValues.Hash("another"), Now.AddHours(1)));
        binding.Revoke(Now.AddHours(2)).ShouldBeTrue(); binding.Revoke(Now.AddHours(3)).ShouldBeFalse();
        binding.RevokedAtUtc.ShouldBe(Now.AddHours(2)); binding.BoundAtUtc.ShouldBe(Now.AddSeconds(1)); binding.SubjectHash.ShouldBe(subject);
        binding.ActiveAccountKey.ShouldBeNull(); binding.ActiveSubjectKey.ShouldBeNull(); binding.IsActive.ShouldBeFalse();
        Should.Throw<BusinessException>(() => binding.Bind(subject, Now.AddHours(3)));
    }

    [Fact]
    public void Active_Keys_Should_Scope_The_Account_And_WeChat_Subject_To_The_Tenant_And_App()
    {
        var user = Guid.NewGuid(); var subject = WeChatLoginValues.Hash("wechat");
        var first = Binding(user: user); var same = Binding(user: user); var tenant = Binding(Guid.NewGuid(), user); var app = Binding(user: user, appId: "wxfedcba9876543210");
        foreach (var binding in new[] { first, same, tenant, app }) binding.Bind(subject, Now.AddSeconds(1));
        same.ActiveAccountKey.ShouldBe(first.ActiveAccountKey); same.ActiveSubjectKey.ShouldBe(first.ActiveSubjectKey);
        tenant.ActiveAccountKey.ShouldNotBe(first.ActiveAccountKey); tenant.ActiveSubjectKey.ShouldNotBe(first.ActiveSubjectKey);
        app.ActiveAccountKey.ShouldNotBe(first.ActiveAccountKey); app.ActiveSubjectKey.ShouldNotBe(first.ActiveSubjectKey);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Binding_Should_Reject_Invalid_Hashes(string hash)
    {
        Should.Throw<BusinessException>(() => new WeChatAccountBinding(Guid.NewGuid(), null, Guid.NewGuid(), "main", "wx0123456789abcdef", "KnowPulse_WeChat_main", hash, WeChatLoginValues.Hash("stamp"), Now));
        Should.Throw<BusinessException>(() => Binding().Bind(hash, Now.AddSeconds(1)));
    }

    [Fact]
    public void Configuration_Should_Disable_Incomplete_Legacy_Client_Or_Ambiguous_Applications()
    {
        var options = new WeChatLoginOptions();
        var app = new WeChatApplicationOptions { Enabled = true, AppId = "wx0123456789abcdef", AppSecret = "synthetic-secret", ClientId = "KnowPulse_WeChat_main", DisplayName = "Synthetic" };
        options.Applications.Add("main", app); options.GetApplication("main").ShouldBeSameAs(app);
        app.ClientId = "KnowPulse_Blazor"; Should.Throw<BusinessException>(() => options.GetApplication("main"));
        app.ClientId = "KnowPulse_WeChat_main";
        options.Applications.Add("other", new() { Enabled = true, AppId = app.AppId, AppSecret = "synthetic-other", ClientId = "KnowPulse_WeChat_other", DisplayName = "Other" });
        Should.Throw<BusinessException>(() => options.GetApplication("main"));
        options.Applications["other"].AppId = "wxfedcba9876543210"; options.Applications["other"].ClientId = app.ClientId;
        Should.Throw<BusinessException>(() => options.GetApplication("main"));
        options.Applications["other"].Enabled = false; options.GetApplication("main").ShouldBeSameAs(app);
        app.AppSecret = ""; Should.Throw<BusinessException>(() => options.GetApplication("main"));
    }
}
