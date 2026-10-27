namespace WeChatWeb.Middleware;

/// <summary>
/// 安全响应头：防 MIME 嗅探、防点击劫持、限制引荐来源与敏感能力（摄像头/麦克风仅本源可用）。
/// CSP 留待生产环境按部署域名收紧（开发态 Vite HMR 需要 ws/inline）。
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware (RequestDelegate next) => _next = next;

    public async Task InvokeAsync (HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var h = context.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "SAMEORIGIN";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            h["Permissions-Policy"] = "camera=(self), microphone=(self), geolocation=(), payment=(), usb=()";
            h["Cross-Origin-Opener-Policy"] = "same-origin";
            h["X-Permitted-Cross-Domain-Policies"] = "none";
            // 拒绝搜索引擎收录：所有响应带 X-Robots-Tag；API 与前端 HTML 不缓存（便于重新编译后立即生效）
            h["X-Robots-Tag"] = "noindex, nofollow, noarchive, nosnippet, noimageindex";

            // CSP：即使某处漏了转义，浏览器也会拦下脚本执行（XSS 兜底）。
            // SignalR 的 WebSocket 走同源：现代浏览器把同源 ws/wss 视为 'self'，
            // 不再显式放开 ws:/wss:（那等于允许页面连任意 WebSocket 主机）。
            // connect-src 加 blob:：前端媒体走 URL.createObjectURL 的 objectURL，下载附件会
            // fetch(objectURL) 取 blob —— 'self' 不含 blob: 会被 CSP 拒（控制台 "Refused to
            // connect … violates connect-src 'self'"），导致"下载点击无反应/图片 blob 取不到"。
            // 放开 blob: 是安全的：objectURL 只能是本页自己创建的，不能指向任意外部资源。
            h["Content-Security-Policy"] =
                "default-src 'self'; "
              + "script-src 'self'; "
              + "style-src 'self' 'unsafe-inline'; "
              + "img-src 'self' data: blob:; "
              + "font-src 'self' data:; "
              + "media-src 'self' blob: data:; "
              + "connect-src 'self' blob: data:; "
              + "object-src 'none'; "
              + "frame-ancestors 'self'; "
              + "base-uri 'self'; "
              + "form-action 'self'";

            // 其它纵深防御
            h["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
            h["Cross-Origin-Resource-Policy"] = "same-origin";
            h["X-DNS-Prefetch-Control"] = "off";
            h["Origin-Agent-Cluster"] = "?1";

            var path = context.Request.Path;
            if (path.StartsWithSegments("/api"))
            {
                // 文件下载（含聊天图片/语音在线预览）允许私有缓存：会话滚动时不再反复全量重拉。
                // URL 里带签名票据与过期时间，缓存 key 随票据变化，不会跨用户串权限。
                // 缓存时长必须 ≤ 票据 TTL（5 分钟）：此前 3600s 会让"被移群/删好友后"
                // 浏览器私有缓存仍可渲染该文件 45 分钟（越权残留窗口），收紧到 300 与票据对齐。
                if (path.StartsWithSegments("/api/files"))
                    h["Cache-Control"] = "private, max-age=300";
                else
                    h["Cache-Control"] = "no-store";
            }
            else if (path == "/" || path.StartsWithSegments("/chat")
                || path.StartsWithSegments("/contacts") || path.StartsWithSegments("/discovery")
                || path.StartsWithSegments("/me") || path.StartsWithSegments("/settings")
                || path.StartsWithSegments("/profile") || path.StartsWithSegments("/moments")
                || path.StartsWithSegments("/group") || path.StartsWithSegments("/search")
                || path.StartsWithSegments("/login") || path.StartsWithSegments("/nearby"))
            {
                // SPA 入口页：每次回源校验，/assets/*（带内容哈希）由 StaticFiles 长缓存
                h["Cache-Control"] = "no-cache, no-store, must-revalidate";
            }
            return Task.CompletedTask;
        });
        await _next(context);
    }
}
