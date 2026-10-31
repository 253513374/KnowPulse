using System.Threading.Tasks;
using KnowPulse.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Auditing;

namespace KnowPulse.WeChat;

[AllowAnonymous]
[DisableAuditing]
[Route("api/account/wechat")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class WeChatBindingController : KnowPulseController
{
    private readonly IWeChatBindingExchange _exchange;
    private readonly WeChatRequestLimiter _limiter;

    public WeChatBindingController(IWeChatBindingExchange exchange, WeChatRequestLimiter limiter)
    {
        _exchange = exchange; _limiter = limiter;
    }

    [HttpPost("exchange")]
    [IgnoreAntiforgeryToken]
    public async Task<ActionResult<WeChatBindingReceiptDto>> ExchangeAsync([FromBody] ExchangeWeChatBindingCodeDto input)
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.Pragma = "no-cache";
        if (!_limiter.TryAcquire(HttpContext)) return StatusCode(429);
        return await _exchange.ExchangeBindingCodeAsync(input);
    }
}
