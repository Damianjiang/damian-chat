using System.Collections.Concurrent;
using System.Security.Claims;
using WeChatWeb.Services;

namespace WeChatWeb.Middleware;

/// <summary>
/// 全站 API 限流：对所有 /api/*（/api/auth 由 AuthRateLimitMiddleware 按更严阈值单独管）
/// 按「IP + 用户」双维度固定窗口计数（API-SEC-005/006）。
/// ・纯 IP 维度会被「轮换 IP / 走代理池」绕过，同一账号仍可无限量打接口；
/// ・已认证请求额外按 JWT 的 sub 记一个桶，攻击者即使用 N 个 IP 登录同一账号也被限。
/// 阈值读 SecuritySettings.ApiPerMinute（后台可改、即时生效），超限返回 429 + Retry-After。
/// </summary>
public sealed class ApiRateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SecuritySettings _sec;

    private readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> _ipHits = new();
    private readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> _userHits = new();

    public ApiRateLimitMiddleware (RequestDelegate next, SecuritySettings sec)
    {
        _next = next;
        _sec = sec;
    }

    private static (int Count, DateTime WindowStart) Bump (
        ConcurrentDictionary<string, (int Count, DateTime WindowStart)> map, string key, DateTime now)
        => map.AddOrUpdate(key,
            _ => (1, now),
            (_, old) => now - old.WindowStart >= TimeSpan.FromMinutes(1)
                ? (1, now)
                : (old.Count + 1, old.WindowStart));

    private bool OverLimit ((int Count, DateTime WindowStart) entry, out int retry)
    {
        retry = (int)Math.Ceiling((entry.WindowStart + TimeSpan.FromMinutes(1) - DateTime.UtcNow).TotalSeconds);
        if (retry < 1) retry = 1;
        return entry.Count > Math.Max(10, _sec.ApiPerMinute);
    }

    public async Task InvokeAsync (HttpContext context)
    {
        var path = context.Request.Path;
        if (path.StartsWithSegments("/api") && !path.StartsWithSegments("/api/auth"))
        {
            var now = DateTime.UtcNow;

            // ① IP 维度（所有 /api 请求）
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var ipEntry = Bump(_ipHits, ip, now);

            // ② 用户维度（仅已认证请求：能解析出 sub 才记桶）
            var sub = context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var userEntry = string.IsNullOrEmpty(sub)
                ? default
                : Bump(_userHits, "u:" + sub, now);

            int retry = 0;
            bool blockedIp = OverLimit(ipEntry, out retry);
            bool blockedUser = false;
            if (userEntry != default)
            {
                blockedUser = OverLimit(userEntry, out var userRetry);
                if (blockedUser) retry = Math.Max(retry, userRetry);
            }

            if (blockedIp || blockedUser)
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers["Retry-After"] = retry.ToString();
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync("{\"error\":\"请求过于频繁，请稍后再试\"}");
                return;
            }

            // 低频清理：两个桶各自超 5000 才清过期项（5 分钟窗口外的），避免无界增长
            if (_ipHits.Count > 5000)
                foreach (var kv in _ipHits.Where(kv => now - kv.Value.WindowStart > TimeSpan.FromMinutes(5)).ToList())
                    _ipHits.TryRemove(kv.Key, out _);
            if (_userHits.Count > 5000)
                foreach (var kv in _userHits.Where(kv => now - kv.Value.WindowStart > TimeSpan.FromMinutes(5)).ToList())
                    _userHits.TryRemove(kv.Key, out _);
        }

        await _next(context);
    }
}
