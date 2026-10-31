using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp;

namespace KnowPulse.WeChat;

public class WeChatLoginVerifier : IWeChatLoginVerifier
{
    // A direct client has no HTTP logging handlers; the WeChat query contains credentials.
    private static readonly HttpClient SharedClient = new(new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        { Timeout = TimeSpan.FromSeconds(10) };
    private readonly HttpClient _client;
    private readonly WeChatLoginOptions _options;

    public WeChatLoginVerifier(IOptions<WeChatLoginOptions> options) : this(SharedClient, options) { }

    internal WeChatLoginVerifier(HttpClient client, IOptions<WeChatLoginOptions> options)
    {
        _client = client; _options = options.Value;
    }

    public async Task<WeChatVerifiedIdentity> VerifyAsync(string appKey, string code, CancellationToken cancellationToken = default)
    {
        var app = _options.GetApplication(appKey);
        if (string.IsNullOrWhiteSpace(code) || code.Length > 512 || code.AsSpan().IndexOfAny('\r', '\n') >= 0)
            throw new BusinessException(WeChatLoginErrorCodes.InvalidCode);
        var uri = "https://api.weixin.qq.com/sns/jscode2session?appid=" + Uri.EscapeDataString(app.AppId) +
            "&secret=" + Uri.EscapeDataString(app.AppSecret) + "&js_code=" + Uri.EscapeDataString(code) + "&grant_type=authorization_code";
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            using var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode) throw Unavailable();
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            var bytes = new byte[8193]; var length = 0;
            while (length < bytes.Length)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(length), deadline.Token);
                if (read == 0) break;
                length += read;
            }
            if (length > 8192) throw Unavailable();
            using var json = JsonDocument.Parse(bytes.AsMemory(0, length), new() { MaxDepth = 8 });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw Unavailable();
            if (root.TryGetProperty("errcode", out var error))
            {
                if (error.ValueKind != JsonValueKind.Number || !error.TryGetInt32(out var errorCode)) throw Unavailable();
                if (errorCode == -1) throw Unavailable();
                if (errorCode != 0) throw new BusinessException(WeChatLoginErrorCodes.InvalidCode);
            }
            if (!root.TryGetProperty("openid", out var value) || value.ValueKind != JsonValueKind.String) throw Unavailable();
            var subject = value.GetString();
            if (string.IsNullOrWhiteSpace(subject) || subject.Length < 10 || subject.Length > 128 ||
                subject.AsSpan().IndexOfAnyExcept("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_") >= 0) throw Unavailable();
            return new(app.AppId, WeChatLoginValues.Hash(app.AppId + "\n" + subject));
        }
        catch (HttpRequestException) { throw Unavailable(); }
        catch (IOException) { throw Unavailable(); }
        catch (JsonException) { throw Unavailable(); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw Unavailable(); }
    }

    private static BusinessException Unavailable() => new(WeChatLoginErrorCodes.Unavailable);
}
