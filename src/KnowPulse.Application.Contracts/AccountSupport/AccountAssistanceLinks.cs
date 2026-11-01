using System;

namespace KnowPulse.AccountSupport;

public static class AccountAssistanceLinks
{
    public static string? GetPasswordRecoveryUrl(string? authority)
        => GetPublicUrl(authority, "Account/ForgotPassword");

    public static string? GetAccountAssistanceUrl(string? clientRoot)
        => GetPublicUrl(clientRoot, "account/assistance");

    private static string? GetPublicUrl(string? rootUrl, string path)
    {
        if (!Uri.TryCreate(rootUrl?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback) ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0) return null;
        var root = new Uri(uri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
        return new Uri(root, path).AbsoluteUri;
    }
}
