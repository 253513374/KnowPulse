using System;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace KnowPulse.WeChat;

public sealed class WeChatRequestLimiter : IDisposable
{
    // shortcut: per-instance IP limit; add trusted-proxy and distributed limits before multiple public hosts.
    private readonly PartitionedRateLimiter<string> _limiter = PartitionedRateLimiter.Create<string, string>(key =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));

    public bool TryAcquire(HttpContext context)
    {
        using var lease = _limiter.AttemptAcquire(context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        if (lease.IsAcquired) return true;
        context.Response.Headers.RetryAfter = "60";
        return false;
    }

    public void Dispose() => _limiter.Dispose();
}
