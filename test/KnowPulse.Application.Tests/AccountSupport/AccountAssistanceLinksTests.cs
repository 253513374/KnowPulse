using KnowPulse.AccountSupport;
using Shouldly;
using Xunit;

namespace KnowPulse.AccountSupport;

public class AccountAssistanceLinksTests
{
    [Theory]
    [InlineData("https://app.example.invalid/workspace", "https://app.example.invalid/workspace/account/assistance")]
    [InlineData("http://localhost:44307/", "http://localhost:44307/account/assistance")]
    [InlineData(null, null)]
    [InlineData("https://user:secret@app.example.invalid", null)]
    [InlineData("https://app.example.invalid?returnUrl=https://other.example.invalid", null)]
    [InlineData("http://app.example.invalid", null)]
    public void Login_Help_Should_Use_A_Fixed_Public_Client_Route(string? root, string? expected)
        => AccountAssistanceLinks.GetAccountAssistanceUrl(root).ShouldBe(expected);

    [Fact]
    public void Recovery_Should_Use_The_Configured_Authentication_Origin_And_Path_Base_With_Only_Fixed_Routes()
    {
        AccountAssistanceLinks.GetPasswordRecoveryUrl("https://identity.example.invalid/auth").ShouldBe("https://identity.example.invalid/auth/Account/ForgotPassword");
        AccountAssistanceLinks.GetPasswordRecoveryUrl("http://localhost:44305").ShouldBe("http://localhost:44305/Account/ForgotPassword");
    }
    [Theory]
    [InlineData("https://user:secret@identity.example.invalid")]
    [InlineData("https://identity.example.invalid?returnUrl=https://other.example.invalid")]
    [InlineData("http://identity.example.invalid")]
    [InlineData("/Account/Login")]
    public void Recovery_Should_Reject_Credentials_Query_Redirection_Nonlocal_Http_And_Relative_Configuration(string authority)
        => AccountAssistanceLinks.GetPasswordRecoveryUrl(authority).ShouldBeNull();
}
