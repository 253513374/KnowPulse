using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using KnowPulse.Teaching;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.Teaching;

public class WeChatCodeExchangeClientTests
{
    [Fact]
    public async Task Should_Return_Only_Verified_OpenId_And_Reject_Provider_Errors()
    {
        var handler = new ExchangeHandler();
        var client = new TestClient(new HttpClient(handler));
        (await client.GetOpenIdAsync("wx-code")).ShouldBe("verified-student");
        handler.LastUri!.Host.ShouldBe("api.weixin.qq.com");
        handler.LastUri.Query.ShouldContain("js_code=wx-code");
        handler.Body = "{\"errcode\":40029,\"errmsg\":\"provider-private-detail\"}";
        var exception = await Should.ThrowAsync<UserFriendlyException>(() => client.GetOpenIdAsync("used-code"));
        exception.Message.ShouldNotContain("provider-private-detail");
        await Should.ThrowAsync<UserFriendlyException>(() => client.GetOpenIdAsync(""));
    }

    private sealed class TestClient(HttpClient client) : WeChatCodeExchangeClient(Options.Create(new WeChatMiniProgramOptions
    {
        AppId = "test-app", AppSecret = "test-secret"
    }))
    {
        protected override HttpClient Client => client;
    }

    private sealed class ExchangeHandler : HttpMessageHandler
    {
        public string Body { get; set; } = "{\"openid\":\"verified-student\",\"session_key\":\"never-return-this\"}";
        public Uri? LastUri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body) });
        }
    }
}
