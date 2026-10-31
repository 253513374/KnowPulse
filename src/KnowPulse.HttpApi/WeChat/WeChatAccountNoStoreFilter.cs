using System.Threading.Tasks;
using KnowPulse.WeChat;
using Microsoft.AspNetCore.Mvc.Filters;

namespace KnowPulse.HttpApi.WeChat;

public sealed class WeChatAccountNoStoreFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Controller is IWeChatAccountAppService)
        {
            context.HttpContext.Response.Headers.CacheControl = "private, no-store";
            context.HttpContext.Response.Headers.Pragma = "no-cache";
        }
        await next();
    }
}
