using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Volo.Abp;

namespace KnowPulse.WeChat;

public class WeChatLoginOptions
{
    public const string SectionName = "WeChatLogin";
    public Dictionary<string, WeChatApplicationOptions> Applications { get; set; } = new(System.StringComparer.Ordinal);

    public WeChatApplicationOptions GetApplication(string appKey)
    {
        if (string.IsNullOrWhiteSpace(appKey) || !Applications.TryGetValue(appKey, out var application) || !IsEnabledApplication(appKey, application))
            throw new BusinessException(WeChatLoginErrorCodes.NotConfigured);
        return application;
    }

    public bool IsEnabledApplication(string key, WeChatApplicationOptions application) => IsConfigured(key, application) &&
        Applications.Count(x => IsConfigured(x.Key, x.Value) && x.Value.AppId == application.AppId) == 1 &&
        Applications.Count(x => IsConfigured(x.Key, x.Value) && x.Value.ClientId == application.ClientId) == 1;

    private static bool IsConfigured(string key, WeChatApplicationOptions application) => application.Enabled &&
        Regex.IsMatch(key, "^[A-Za-z0-9-]{1,64}$") && Regex.IsMatch(application.AppId ?? string.Empty, "^wx[0-9a-fA-F]{16}$") &&
        !string.IsNullOrWhiteSpace(application.AppSecret) && application.AppSecret.Length <= 256 &&
        Regex.IsMatch(application.ClientId ?? string.Empty, "^KnowPulse_WeChat_[A-Za-z0-9_-]{1,100}$") &&
        !string.IsNullOrWhiteSpace(application.DisplayName) && application.DisplayName.Length <= 128;
}

public class WeChatApplicationOptions
{
    public bool Enabled { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
}
