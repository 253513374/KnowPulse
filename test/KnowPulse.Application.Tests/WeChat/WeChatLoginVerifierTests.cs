using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace KnowPulse.WeChat;

public class WeChatLoginVerifierTests
{
    private static WeChatLoginOptions Configuration()
    {
        var options = new WeChatLoginOptions(); options.Applications.Add("main", new()
        {
            Enabled = true, AppId = "wx0123456789abcdef", AppSecret = "SYNTHETIC-SECRET", ClientId = "KnowPulse_WeChat_main", DisplayName = "Synthetic"
        }); return options;
    }

    [Fact]
    public async Task Verifier_Should_Use_The_Fixed_Https_Endpoint_And_Return_Only_An_App_Scoped_Subject_Hash()
    {
        var handler = new Handler("{\"openid\":\"SYNTHETIC-OPENID\",\"session_key\":\"SYNTHETIC-SESSION-KEY\",\"unionid\":\"SYNTHETIC-UNIONID\"}");
        using var client = new HttpClient(handler);
        var result = await new WeChatLoginVerifier(client, Options.Create(Configuration())).VerifyAsync("main", "one+time&code");
        handler.Request!.Scheme.ShouldBe("https"); handler.Request.Host.ShouldBe("api.weixin.qq.com"); handler.Request.AbsolutePath.ShouldBe("/sns/jscode2session");
        handler.Request.Query.ShouldContain("js_code=one%2Btime%26code");
        result.AppId.ShouldBe("wx0123456789abcdef"); result.SubjectHash.ShouldBe(WeChatLoginValues.Hash(result.AppId + "\nSYNTHETIC-OPENID"));
        result.ToString().ShouldNotContain("SYNTHETIC-SESSION-KEY"); result.ToString().ShouldNotContain("SYNTHETIC-OPENID"); result.ToString().ShouldNotContain("SYNTHETIC-SECRET");
    }

    [Theory]
    [InlineData("{\"errcode\":40029,\"errmsg\":\"SYNTHETIC-SECRET\"}", WeChatLoginErrorCodes.InvalidCode)]
    [InlineData("{\"errcode\":40163}", WeChatLoginErrorCodes.InvalidCode)]
    [InlineData("{\"errcode\":-1}", WeChatLoginErrorCodes.Unavailable)]
    [InlineData("{\"errcode\":\"bad\"}", WeChatLoginErrorCodes.Unavailable)]
    [InlineData("{\"session_key\":\"SYNTHETIC-SECRET\"}", WeChatLoginErrorCodes.Unavailable)]
    [InlineData("not-json-SYNTHETIC-SECRET", WeChatLoginErrorCodes.Unavailable)]
    [InlineData("[]", WeChatLoginErrorCodes.Unavailable)]
    public async Task Verifier_Should_Return_Controlled_Failures_Without_Vendor_Payloads(string json, string code)
    {
        using var client = new HttpClient(new Handler(json));
        var error = await Should.ThrowAsync<BusinessException>(() => new WeChatLoginVerifier(client, Options.Create(Configuration())).VerifyAsync("main", "code"));
        error.Code.ShouldBe(code); error.InnerException.ShouldBeNull(); error.ToString().ShouldNotContain("SYNTHETIC-SECRET");
    }

    [Fact]
    public async Task Verifier_Should_Reject_Unconfigured_And_Invalid_Codes_Before_Making_A_Request()
    {
        var handler = new Handler("{}"); using var client = new HttpClient(handler); var config = Configuration(); var verifier = new WeChatLoginVerifier(client, Options.Create(config));
        foreach (var code in new[] { "", "a\nb", new string('a', 513) })
            (await Should.ThrowAsync<BusinessException>(() => verifier.VerifyAsync("main", code))).Code.ShouldBe(WeChatLoginErrorCodes.InvalidCode);
        config.Applications["main"].Enabled = false;
        (await Should.ThrowAsync<BusinessException>(() => verifier.VerifyAsync("main", "code"))).Code.ShouldBe(WeChatLoginErrorCodes.NotConfigured);
        handler.Calls.ShouldBe(0);
    }

    [Theory]
    [InlineData("large")]
    [InlineData("http")]
    [InlineData("transport")]
    public async Task Verifier_Should_Bound_Responses_And_Suppress_Transport_Details(string failure)
    {
        var handler = new Handler(failure == "large" ? new string('a', 8193) : "SYNTHETIC-SECRET")
            { Status = failure == "http" ? HttpStatusCode.Redirect : HttpStatusCode.OK, ThrowTransport = failure == "transport" };
        using var client = new HttpClient(handler);
        var error = await Should.ThrowAsync<BusinessException>(() => new WeChatLoginVerifier(client, Options.Create(Configuration())).VerifyAsync("main", "code"));
        error.Code.ShouldBe(WeChatLoginErrorCodes.Unavailable); error.InnerException.ShouldBeNull(); error.ToString().ShouldNotContain("SYNTHETIC-SECRET");
    }

    [Fact]
    public async Task Verifier_Should_Time_Out_A_Stalled_Body_After_Headers_Without_Leaking_Transport_Details()
    {
        var body = new StalledStream();
        var handler = new Handler("") { BodyStream = body };
        using var client = new HttpClient(handler);
        var verifier = new WeChatLoginVerifier(client, Options.Create(Configuration()));
        var error = await Should.ThrowAsync<BusinessException>(() => verifier.VerifyAsync("main", "SYNTHETIC-SECRET").WaitAsync(TimeSpan.FromSeconds(12)));
        error.Code.ShouldBe(WeChatLoginErrorCodes.Unavailable);
        error.InnerException.ShouldBeNull();
        error.ToString().ShouldNotContain("SYNTHETIC-SECRET");
        body.DeadlineCanceled.ShouldBeTrue();
    }

    private sealed class StalledStream : MemoryStream
    {
        public bool DeadlineCanceled;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { DeadlineCanceled = true; throw; }
            return 0;
        }
    }

    private sealed class Handler(string body) : HttpMessageHandler
    {
        public Uri? Request; public int Calls; public HttpStatusCode Status = HttpStatusCode.OK; public bool ThrowTransport; public Stream? BodyStream;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; Request = request.RequestUri;
            if (ThrowTransport) throw new HttpRequestException("SYNTHETIC-SECRET");
            return Task.FromResult(new HttpResponseMessage(Status) { Content = BodyStream == null ? new StringContent(body, Encoding.UTF8, "application/json") : new StreamContent(BodyStream) });
        }
    }
}
