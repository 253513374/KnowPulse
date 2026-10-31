using KnowPulse.AccountSupport;
using Shouldly;
using Xunit;

namespace KnowPulse.AccountSupport;

public class AccountAssistanceLinksTests
{
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
