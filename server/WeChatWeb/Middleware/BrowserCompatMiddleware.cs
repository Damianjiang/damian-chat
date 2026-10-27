using System.Text.RegularExpressions;

namespace WeChatWeb.Middleware;

/// <summary>
/// 最低浏览器兼容中间件：Chrome/Edge 114、Firefox 115、Safari 16、Opera 100，IE 全拦。
/// 放在 UseRouting 之前。不合规返回纯 HTML 降级页，文案与前端 index.html 一致。
/// </summary>
public sealed class BrowserCompatMiddleware
{
    private readonly RequestDelegate _next;

    public BrowserCompatMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var ua = context.Request.Headers.UserAgent.ToString();

        // 无 UA 或非浏览器（脚本/爬虫）放行，只拦浏览器
        if (TryParseBrowser(ua, out var name, out var version))
        {
            var min = name switch
            {
                "IE" => int.MaxValue,
                "Chrome" or "Edge" => 114,
                "Firefox" => 115,
                "Safari" => 16,
                "Opera" => 100,
                _ => 0,
            };

            if (version < min)
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync(UnsupportedHtml);
                return;
            }
        }

        await _next(context);
    }

    private static bool TryParseBrowser(string ua, out string name, out int version)
    {
        name = "";
        version = 0;
        if (string.IsNullOrWhiteSpace(ua)) return false;

        static int First(Match m, int group) => m.Success ? int.TryParse(m.Groups[group].Value, out var v) ? v : 0 : 0;

        var m = Regex.Match(ua, @"Trident|MSIE");
        if (m.Success) { name = "IE"; version = 0; return true; }

        m = Regex.Match(ua, @"Edg(?:e)?/(\d+)");
        if (m.Success) { name = "Edge"; version = First(m, 1); return true; }

        m = Regex.Match(ua, @"OPR/(\d+)");
        if (m.Success) { name = "Opera"; version = First(m, 1); return true; }

        m = Regex.Match(ua, @"(Chrome)/(\d+)");
        if (m.Success) { name = "Chrome"; version = First(m, 2); return true; }

        m = Regex.Match(ua, @"(Firefox)/(\d+)");
        if (m.Success) { name = "Firefox"; version = First(m, 2); return true; }

        m = Regex.Match(ua, @"Version/(\d+).*Safari|Safari/\d+");
        if (m.Success && !ua.Contains("Chrome", StringComparison.OrdinalIgnoreCase))
        {
            name = "Safari";
            version = Regex.IsMatch(ua, @"Version/(\d+)") ? int.TryParse(Regex.Match(ua, @"Version/(\d+)").Groups[1].Value, out var sv) ? sv : 0 : 0;
            return true;
        }

        return false;
    }

    private const string UnsupportedHtml = """
        <!DOCTYPE html>
        <html lang="zh-CN">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <title>浏览器版本过低</title>
          <style>
            body { font-family: system-ui, sans-serif; background: #f5f5f5; color: #181818; margin: 0; }
            .wx-box { max-width: 420px; margin: 80px auto; padding: 28px 24px; background: #fff; border-radius: 12px; text-align: center; }
            .wx-box h1 { font-size: 18px; margin: 0 0 12px; }
            .wx-box p { font-size: 14px; color: #888888; margin: 0 0 20px; }
            .wx-box a { display: block; margin: 10px auto; width: 220px; padding: 10px; font-size: 14px; color: #07c160; background: #f5f5f5; border-radius: 8px; text-decoration: none; }
          </style>
        </head>
        <body>
          <div class="wx-box">
            <h1>您当前浏览器版本过低，不支持您继续使用此web app</h1>
            <p>请升级浏览器后重试</p>
            <a href="https://www.google.com/chrome/" target="_blank" rel="noopener">下载 Google Chrome</a>
            <a href="https://www.microsoft.com/edge" target="_blank" rel="noopener">下载 Microsoft Edge</a>
            <a href="https://www.mozilla.org/firefox/" target="_blank" rel="noopener">下载 Mozilla Firefox</a>
            <a href="https://www.apple.com/safari/" target="_blank" rel="noopener">下载 Safari</a>
          </div>
        </body>
        </html>
        """;
}
