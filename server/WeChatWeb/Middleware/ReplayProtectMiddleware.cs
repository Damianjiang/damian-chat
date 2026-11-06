using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace WeChatWeb.Middleware;

/// <summary>
/// 敏感写接口防重放（B 阶段 1：尽量挡住中间人）。
///
/// 抗中间人模型：攻击者能嗅探到一次完整的 POST/PUT/DELETE 请求（含 Authorization、
/// 消息体、Cookie）。若直接原样重放，可重复执行「删消息 / 改资料 / 转发改名」等
/// 写操作。本中间件在响应侧补一道去重闸门：
///
///   ① X-Request-Nonce —— 客户端生成的 16~32 字节随机串（建议 crypto.getRandomValues）。
///      服务端以 (IP, nonce) 为键做短时窗去重：同一 nonce 在窗口内第二次出现即判重放，
///      返回 401 + Retry-After。nonce 用 HMAC-SHA256 摘要后落内存，避免把明文回显给
///      嗅探者；窗口默认 10 分钟，后台 Timer 每分钟清理过期键。
///   ② X-Request-Timestamp —— Unix 秒级时间戳。偏差超过 5 分钟（可被中间人抓包后隔很久
///      才重放）直接拒，作为「旧请求」的第二道判据。
///
/// 兼容性（渐进式，不破坏现有客户端）：
///   · 两个头都缺 → 放行（旧前端尚未接入 nonce，强制会全挂）。
///   · 带 X-Request-Nonce → 强制去重 + 校验；带 X-Request-Timestamp → 强制时间窗校验。
///   · 一旦前端接入，攻击者重放旧包即被 401 挡下。
///
/// 仅作用于 /api/* 的写方法（POST/PUT/DELETE/PATCH）。GET 天然幂等（带签名票据的下载除外，
/// 票据本身 5 分钟 TTL，已在 SecurityHeaders 收紧），不在此处重复拦截，避免误伤 SignalR/轮询。
/// 纯进程内状态（ConcurrentDictionary），无外部依赖；多实例部署时换 Redis 即可横向扩展。
/// </summary>
public sealed class ReplayProtectMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ReplayProtectMiddleware> _log;

    // (IP, nonce) -> 写入时刻。nonce 已 HMAC 摘要，不外泄明文。
    private readonly ConcurrentDictionary<string, DateTime> _seen = new();

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    // 摘要密钥：进程内随机（nonce 去重不要求跨进程一致，够用；与票据密钥同款做法）。
    private static readonly byte[] HmacKey = RandomNumberGenerator.GetBytes(32);

    public ReplayProtectMiddleware(RequestDelegate next, ILogger<ReplayProtectMiddleware> log)
    {
        _next = next;
        _log = log;
        // 每分钟扫一次过期键，避免 _seen 无限增长（攻击者灌随机 nonce 也不能撑爆内存）
        _sweeper = new Timer(_ =>
        {
            var now = DateTime.UtcNow;
            foreach (var kv in _seen)
            {
                var expired = kv.Value;
                DateTime removed;
                if (now - expired > Window) _seen.TryRemove(kv.Key, out removed);
            }
        }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    private readonly Timer _sweeper;

    public async Task InvokeAsync(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? "";
        if (!path.StartsWith("/api"))
        {
            await _next(ctx);
            return;
        }

        bool isWrite = ctx.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase)
                    || ctx.Request.Method.Equals("PUT", StringComparison.OrdinalIgnoreCase)
                    || ctx.Request.Method.Equals("DELETE", StringComparison.OrdinalIgnoreCase)
                    || ctx.Request.Method.Equals("PATCH", StringComparison.OrdinalIgnoreCase);

        string? nonce = ctx.Request.Headers["X-Request-Nonce"].FirstOrDefault();
        string? tsStr = ctx.Request.Headers["X-Request-Timestamp"].FirstOrDefault();

        // 两个头都没带 → 旧客户端，放行（不破坏既有功能）
        if (string.IsNullOrWhiteSpace(nonce) && string.IsNullOrWhiteSpace(tsStr))
        {
            await _next(ctx);
            return;
        }

        // ② 时间窗：带时间戳就做偏差校验
        if (!string.IsNullOrWhiteSpace(tsStr))
        {
            if (!long.TryParse(tsStr, out var tsSec))
                { Reject(ctx, 401, "invalid X-Request-Timestamp"); return; }

            var dt = DateTimeOffset.FromUnixTimeSeconds(tsSec).UtcDateTime;
            if (Math.Abs((dt - DateTime.UtcNow).TotalSeconds) > ClockSkew.TotalSeconds)
            {
                _log.LogWarning("重放拦截：时间戳偏差超限 {Path} {Method}", path, ctx.Request.Method);
                Reject(ctx, 401, "request timestamp out of window");
                return;
            }
        }

        // ① nonce 去重：仅对写方法强制（GET 已放行，避免误伤）
        if (!string.IsNullOrWhiteSpace(nonce) && isWrite)
        {
            var key = MakeKey(ctx, nonce);
            var now = DateTime.UtcNow;
            // TryAdd 原子：重复 nonce 返回 false → 判重放
            if (!_seen.TryAdd(key, now))
            {
                _log.LogWarning("重放拦截：nonce 重复 {Method} {Path}", ctx.Request.Method, path);
                Reject(ctx, 401, "duplicate X-Request-Nonce detected (replay)");
                return;
            }
        }

        await _next(ctx);
    }

    private static string MakeKey(HttpContext ctx, string nonce)
    {
        var ip = (ctx.Connection.RemoteIpAddress ?? System.Net.IPAddress.Any).ToString();
        // 摘要后落键：内存与潜在日志都不保留 nonce 明文；IP 做前缀，跨 IP 的同 nonce 不算重放
        var data = System.Text.Encoding.UTF8.GetBytes($"{ip}|{nonce}");
        using var h = new System.Security.Cryptography.HMACSHA256(HmacKey);
        var digest = h.ComputeHash(data);
        return Convert.ToHexString(digest);
    }

    private static void Reject(HttpContext ctx, int status, string msg)
    {
        if (ctx.Response.HasStarted) return;
        ctx.Response.StatusCode = status;
        ctx.Response.Headers["Retry-After"] = "5";
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
        var safe = msg.Replace("\"", "'");
        _ = ctx.Response.WriteAsync($"{{\"error\":\"{safe}\"}}");
    }
}
