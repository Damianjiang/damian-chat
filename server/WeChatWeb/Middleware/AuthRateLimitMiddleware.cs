using System.Collections.Concurrent;
using WeChatWeb.Services;

namespace WeChatWeb.Middleware;

/// <summary>
/// 认证接口限流（防机器人刷接口）：对 /api/auth/* 按 IP 固定窗口计数。
/// 阈值读 SecuritySettings.AuthPerMinute（后台修改即时生效），超限 429 + Retry-After。
/// </summary>
public sealed class AuthRateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SecuritySettings _sec;
    private readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> _hits = new();

    public AuthRateLimitMiddleware (RequestDelegate next, SecuritySettings sec)
    {
        _next = next;
        _sec = sec;
    }

    public async Task InvokeAsync (HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api/auth"))
        {
            // 无害的公开只读端点豁免限流：登录页每次渲染都要拉「登录背景配置/图片」，
            // 不豁免的话多开标签/自动刷新会吃掉 /api/auth 额度、误伤真正的登录/滑块请求。
            var p = context.Request.Path;
            if (p.StartsWithSegments("/api/auth/login-theme")
                || p.StartsWithSegments("/api/auth/login-bg"))
            {
                await _next(context);
                return;
            }

            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var limit = Math.Max(5, _sec.AuthPerMinute);
            var now = DateTime.UtcNow;

            var entry = _hits.AddOrUpdate(ip,
                _ => (1, now),
                (_, old) => now - old.WindowStart >= TimeSpan.FromMinutes(1)
                    ? (1, now)
                    : (old.Count + 1, old.WindowStart));

            if (entry.Count > limit)
            {
                var retry = (int)Math.Ceiling((entry.WindowStart + TimeSpan.FromMinutes(1) - now).TotalSeconds);
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers["Retry-After"] = Math.Max(1, retry).ToString();
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync("{\"error\":\"请求过于频繁，请稍后再试\"}");
                return;
            }

            if (_hits.Count > 5000)
            {
                foreach (var kv in _hits.Where(kv => now - kv.Value.WindowStart > TimeSpan.FromMinutes(5)).ToList())
                    _hits.TryRemove(kv.Key, out _);
            }
        }

        await _next(context);
    }
}
