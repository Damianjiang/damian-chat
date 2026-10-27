using System.Diagnostics;
using System.Security.Claims;
using WeChatWeb.Services;

namespace WeChatWeb.Middleware;

/// <summary>
/// 请求日志：JSONL 追加到 logs/requests.jsonl（含耗时、状态、用户）。
/// </summary>
public sealed class RequestLoggerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly LogService _log;

    public RequestLoggerMiddleware(RequestDelegate next, LogService log)
    {
        _next = next;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // 只记录 API 与静态资源，跳过 swagger
        if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // ★ 高频轮询端点不入请求日志：GUI 管理端每隔几秒就 GET /api/admin/online
        //   刷新在线用户数；本地 IP 一直刷 → requests.jsonl 无限膨胀。
        //   这些端点没有"审计价值"（只读计数），跳过即可。
        var path = context.Request.Path.Value ?? "";
        if (IsHighFrequency(path))
        {
            await _next(context);
            return;
        }

        var sw = Stopwatch.StartNew();
        await _next(context);
        sw.Stop();

        // ★ 这段代码跑在响应体已经发出之后。此处抛出的任何异常都无法再改状态码，
        //   Kestrel 只能中断连接 → 客户端看到 net::ERR_INCOMPLETE_CHUNKED_ENCODING
        //   但状态行是 200（曾经的日志写盘失败就是这样把所有 /api 响应打断的）。
        //   记日志是旁路功能，这里必须兜住，绝不能让请求本身失败。
        try
        {
            _log.Write("requests", new
            {
                ts = DateTime.UtcNow,
                method = context.Request.Method,
                path = context.Request.Path.Value,
                status = context.Response.StatusCode,
                ms = sw.ElapsedMilliseconds,
                ip = context.Connection.RemoteIpAddress?.ToString(),
                // JWT 中间件默认把 sub 映射为 NameIdentifier：FindFirst("sub") 恒为 null，
                // 日志里的 user 字段此前一直是空的（与 controllers 的 GetUserId 同一取法）
                user = context.User?.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier),
            });
        }
        catch
        {
            // 日志写不进去就放弃，请求已经成功返回了
        }
    }

    /// <summary>
    /// 高频轮询/健康检查端点不记入请求日志（避免 GUI 在线用户轮询把日志刷爆）。
    /// 维护：新增此类端点时把前缀追加进集合。
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<string> SkippedPaths = new()
    {
        "/api/admin/online",
        "/health",
        "/api/health",
    };

    private static bool IsHighFrequency(string path)
    {
        // 用 StartsWith 防路径尾随斜杠变体；集合小，O(n) 可接受
        foreach (var p in SkippedPaths)
        {
            if (path.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
