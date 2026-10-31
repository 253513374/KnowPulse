using System;
using System.Security.Cryptography;
using System.Text;

namespace KnowPulse.WeChat;

public static class WeChatLoginValues
{
    public const string GrantType = "urn:knowpulse:wechat";
    public const string BindingClaim = "knowpulse_wechat_binding";
    public const int BindingCodeMinutes = 5;
    public const int AccessTokenMinutes = 5;
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool IsHash(string? value) => value?.Length == 64 && value.AsSpan().IndexOfAnyExcept("0123456789abcdef") < 0;
}

public static class WeChatLoginErrorCodes
{
    public const string NotConfigured = "KnowPulse:WeChat:NotConfigured";
    public const string InvalidCode = "KnowPulse:WeChat:InvalidCode";
    public const string Unavailable = "KnowPulse:WeChat:Unavailable";
    public const string InvalidBinding = "KnowPulse:WeChat:InvalidBinding";
    public const string AlreadyBound = "KnowPulse:WeChat:AlreadyBound";
}
