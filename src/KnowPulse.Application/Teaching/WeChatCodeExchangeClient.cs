using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace KnowPulse.Teaching;

public interface IWeChatCodeExchangeClient
{
    Task<string> GetOpenIdAsync(string code, CancellationToken cancellationToken = default);
}

public class WeChatCodeExchangeClient(IOptions<WeChatMiniProgramOptions> options)
    : IWeChatCodeExchangeClient, ITransientDependency
{
    // 微信密钥位于查询参数中，专用客户端不注册请求地址日志。
    private static readonly HttpClient SharedClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    }) { Timeout = TimeSpan.FromSeconds(15) };

    protected virtual HttpClient Client => SharedClient;

    public async Task<string> GetOpenIdAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 256)
            throw new UserFriendlyException("微信登录凭证无效，请重新登录。");
        var config = options.Value;
        if (string.IsNullOrWhiteSpace(config.AppId) || string.IsNullOrWhiteSpace(config.AppSecret))
            throw new UserFriendlyException("微信登录尚未配置，请联系平台维护人员。");

        var client = Client;
        var uri = "https://api.weixin.qq.com/sns/jscode2session?appid=" + Uri.EscapeDataString(config.AppId) +
                  "&secret=" + Uri.EscapeDataString(config.AppSecret) + "&js_code=" + Uri.EscapeDataString(code) +
                  "&grant_type=authorization_code";
        using var response = await client.GetAsync(uri, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new UserFriendlyException("微信登录暂时不可用，请稍后重试。");
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var data = json.RootElement;
        if ((data.TryGetProperty("errcode", out var error) && error.GetInt32() != 0) ||
            !data.TryGetProperty("openid", out var openId) || string.IsNullOrWhiteSpace(openId.GetString()) || openId.GetString()!.Length > 128)
            throw new UserFriendlyException("微信登录凭证已失效，请重新登录。");
        return openId.GetString()!;
    }
}
