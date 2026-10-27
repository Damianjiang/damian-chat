using System.Text;
using WeChatWeb.Services;

namespace WeChatWeb.Middleware;

/// <summary>
/// 蜜罐中间件（诱饵 + tarpit 拖延 + 留痕）。
/// 装配在 <c>UseStaticFiles</c> 之前：命中诱饵路径 → 回**假**响应（让攻击者以为得手）
/// + 逐次加长延迟 + 记 data/honey/events.jsonl；未命中 → 透传下游（真路由 / SPA fallback 不受影响）。
/// 只读、隔离：不写 store.json / files.json，不鉴权，不授予任何真实权限。
/// 真实用户不会访问这些"扫描清单必查"的路径，误伤面为零。
/// </summary>
public sealed class HoneypotMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HoneypotService _honey;

    public HoneypotMiddleware (RequestDelegate next, HoneypotService honey)
    {
        _next = next;
        _honey = honey;
    }

    public async Task InvokeAsync (HttpContext ctx)
    {
        var lure = _honey.MatchLure(ctx.Request.Path.Value);
        if (lure == null)
        {
            await _next(ctx);
            return;
        }

        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var ua = ctx.Request.Headers.UserAgent.ToString();
        var isPost = ctx.Request.Method == "POST";
        var path = ctx.Request.Path.Value ?? "";

        // tarpit 拖延：服务内部用 _hits 自维护「同 IP 首击快、逐次 +2~6s、封顶 MaxDelaySec」。
        // 恒传 firstHit:false —— 首次命中时 _hits 无记录(cur=0)走"标记并返回 0"，第二次起才开始拖。
        var delayMs = _honey.TarpitDelay(ip, false);
        if (delayMs > 0 && !ctx.RequestAborted.IsCancellationRequested)
        {
            try { await Task.Delay(delayMs, ctx.RequestAborted); } catch { /* 客户端已断开，仍留痕 */ }
        }

        var (body, canaries, ctype, status) = ComposeResponse(lure, isPost, ip, path);

        if (!ctx.RequestAborted.IsCancellationRequested)
        {
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = ctype + "; charset=utf-8";
            ctx.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";   // 不向搜索引擎"背书"诱饵
            await ctx.Response.WriteAsync(body);
        }

        // 留痕（事件 + 响应内种下的金丝雀，供 /api/admin/honeypot 研究）
        _honey.LogEvent(ip, ua, ctx.Request.Method, path, lure.Kind, delayMs, canaries,
            isPost ? "post" : "get");
    }

    /// <summary>按诱饵类型 + 方法组装假响应（机密/备份/管理数据全部内嵌金丝雀，外泄即可溯源）。</summary>
    private (string Body, string[] Canaries, string Ctype, int Status) ComposeResponse (
        HoneypotService.Lure lure, bool isPost, string ip, string path)
    {
        switch (lure.Kind)
        {
            case "env":
            {
                var (b, c) = _honey.FakeEnv(ip);
                return (b, c, "application/x-env", 200);
            }

            case "git":
            {
                var (b, c) = _honey.FakeGit(path, ip);
                return (b, c, "text/plain", 200);
            }

            case "backup":
            {
                var (b, c) = _honey.FakeSql(path);
                return (b, c, "application/sql", 200);
            }

            case "actuator":
            {
                var (b, c) = _honey.FakeActuator(path);
                return (b, c, "application/json", 200);
            }

            case "wp":
            case "dotfile":
                // WordPress / 杂项杂扫：回 404（让扫描器判定"不存在"，别再反复打，省资源）
                return ("", Array.Empty<string>(), "text/plain", 404);

            case "admin":
            default:
                // 管理台类：GET 回假登录页（鼓励攻击者 POST）；POST 回"登录成功"（token 是金丝雀）
                if (isPost)
                {
                    var (b, c) = _honey.FakeAdminOk(ip);
                    return (b, c, "application/json", 200);
                }
                return (_honey.FakeAdminLoginPage(PathTitle(path)), Array.Empty<string>(), "text/html", 200);
        }
    }

    /// <summary>按路径段给假登录页一个"像样"的标题（/admin → 管理控制台，/test → 测试系统）。</summary>
    private static string PathTitle(string path)
    {
        var seg = path.Trim('/').Split('/').FirstOrDefault(s => s.Length > 0);
        return string.IsNullOrEmpty(seg) ? "系统" : seg;
    }
}
