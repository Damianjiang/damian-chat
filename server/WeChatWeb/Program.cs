using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using WeChatWeb.Data;
using WeChatWeb.Hubs;
using WeChatWeb.Middleware;
using WeChatWeb.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
// 说明：上传体积的硬上限由 Kestrel 的 MaxRequestBodySize（已在下方 ConfigureKestrel 设为 1GB）
// 统一控制；.NET 8 没有独立的多部分表单 30MB 限制项，无需额外配置。
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

/*
 * DEP-SEC HTTPS（opt-in）：设置 WX_HTTPS_CERT（PFX 证书路径）即启用加密传输。
 * 明文 HTTP 下登录凭据 / JWT / 聊天全文在局域网内可被任一端抓包，是 Windows
 * 服务器"容易被攻击"的第一入口。启用后额外监听 https://{WX_HTTPS_BIND}:{WX_HTTPS_PORT}
 * （默认 0.0.0.0:5001），并对明文请求做 HTTPS 重定向 + HSTS。
 * 未设置 WX_HTTPS_CERT 时完全维持现状（不 brick 现有内网双击启动用法）。
 */
var httpsCert = Environment.GetEnvironmentVariable("WX_HTTPS_CERT");
var httpsCertPass = Environment.GetEnvironmentVariable("WX_HTTPS_CERT_PASSWORD");
// WX_HTTPS_PORT 支持逗号分隔的多个 TLS 端口（如 "30010,443"），默认 443
var httpsPorts = (Environment.GetEnvironmentVariable("WX_HTTPS_PORT") ?? "443")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(s => int.TryParse(s, out var p) ? p : -1)
    .Where(p => p > 0)
    .Distinct()
    .ToArray();
if (httpsPorts.Length == 0) httpsPorts = new[] { 443 };
var httpsEnabled = !string.IsNullOrWhiteSpace(httpsCert) && File.Exists(httpsCert);

var httpsBound = false; // ConfigureKestrel 回调里置位；跳转中间件按请求时读取（回调在 host 构建时已执行）
builder.WebHost.ConfigureKestrel(options =>
{
    // 单文件上传上限 1GB（与 FileStorageConfig.MaxFileBytes 默认值一致）；
    // 请求体按 1GB + 64MB 缓冲放开（容纳 multipart 边界与表单字段开销），普通小请求不受影响。
    options.Limits.MaxRequestBodySize = 1L * 1024 * 1024 * 1024 + 64L * 1024 * 1024;
    // 允许同步 IO：流量防火墙（ByteCounter 包装 Response.Body 统计下行字节）与静态文件同步写、
    // Kestrel 响应收尾的同步 Flush，都会撞上默认 AllowSynchronousIO=false 抛
    // 「Synchronous operations are disallowed」—— 环回 IP 被防火墙豁免（不包装 Body）故本机无感，
    // 而外部 IP 全部被包装 → 整站 500/断连，正常用户一个功能都打不开（攻击轮 1 实测）。
    // 带宽统计是防火墙核心能力，必须放行同步路径；框架仍优先走异步，无实质安全/性能损失。
    options.AllowSynchronousIO = true;
    // DoS 加固（.NET 8 可用的 Kestrel 限制项）：
    // ・MaxConcurrentConnections=500：全站并发 TCP 连接上限，超限直接拒绝握手（不挂起、不占内存），
    //   挡住"连接耗尽 / 慢连接灌水"把服务打崩。
    // ・KeepAliveTimeout=2min：空闲连接 2 分钟回收，防 Slowloris 式长连接占位。
    // ・RequestHeadersTimeout=30s：请求头 30s 内必须到齐，防 Slow HTTP 头攻击。
    options.Limits.MaxConcurrentConnections = 500;
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
    if (httpsEnabled)
    {
        /*
         * 每个 TLS 端口独立判断：被其他进程占用（另一实例 / IIS 等）就跳过该端口并告警，
         * 绝不因单个端口绑定失败让整站起不来。
         * 端口检测用系统真实监听表；「试绑定 127.0.0.1」对 0.0.0.0 监听有假阴性（Windows 允许并存绑定）。
         */
        bool PortTaken(int port)
        {
            foreach (var ep in System.Net.NetworkInformation.IPGlobalProperties
                         .GetIPGlobalProperties().GetActiveTcpListeners())
            {
                if (ep.Port == port) return true;
            }
            return false;
        }

        var bind = Environment.GetEnvironmentVariable("WX_HTTPS_BIND");
        foreach (var tlsPort in httpsPorts)
        {
            if (PortTaken(tlsPort))
            {
                Console.Error.WriteLine($"[HTTPS] 端口 {tlsPort} 已被其他进程占用，本次启动跳过该 TLS 端口。");
                continue;
            }
            if (string.IsNullOrWhiteSpace(bind) || bind == "0.0.0.0" || bind == "*")
                options.ListenAnyIP(tlsPort, listen => listen.UseHttps(httpsCert!, httpsCertPass));
            else
                options.Listen(System.Net.IPAddress.Parse(bind), tlsPort, listen => listen.UseHttps(httpsCert!, httpsCertPass));
            httpsBound = true;
        }

        if (httpsBound)
        {
            /*
             * 显式 Listen 会让 Kestrel 覆盖并丢弃 --urls（warn "Overriding address(es)"），
             * 明文端口整个消失 → GUI 管理 API 直接拒连。
             * 这里把 --urls / ASPNETCORE_URLS 中的明文端点补绑回来（TLS 模式下 GUI 传的是
             * 管理专用环回口 http://127.0.0.1:{主端口+1}，仅本机可达、不对外暴露）。
             */
            var plainUrls = builder.Configuration["urls"] ?? builder.Configuration["Urls"]
                ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
            if (!string.IsNullOrWhiteSpace(plainUrls))
            {
                foreach (var u in plainUrls.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!u.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) continue;
                    var hostPort = u.Substring("http://".Length).Split('/')[0];
                    var idx = hostPort.LastIndexOf(':');
                    var host = idx >= 0 ? hostPort[..idx] : hostPort;
                    if (!int.TryParse(idx >= 0 ? hostPort[(idx + 1)..] : "", out var pport) || httpsPorts.Contains(pport)) continue;
                    if (PortTaken(pport))
                    {
                        Console.Error.WriteLine($"[HTTP] 明文端口 {pport} 已被其他进程占用，跳过（GUI 管理 API 将不可用）。");
                        continue;
                    }
                    if (host is "0.0.0.0" or "*" or "" or "[::]")
                        options.ListenAnyIP(pport);
                    else
                        options.Listen(System.Net.IPAddress.Parse(host.Trim('[', ']')), pport);
                }
            }
        }
    }
});

/*
 * DEP-SEC 监听地址兜底：未显式配置 urls / ASPNETCORE_URLS 且未启用 HTTPS 时，
 * 强制只绑 127.0.0.1，防"忘了配"时 Kestrel 落到 0.0.0.0 把整套服务
 * （登录/上传/实时通道，普通用户 API 并无 loopback 限制）暴露到局域网/公网。
 * 显式传 --urls 或环境变量才允许覆盖。
 */
var hasExplicitUrls = args.Any(a => a.StartsWith("--urls", StringComparison.OrdinalIgnoreCase))
    || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS"))
    || !string.IsNullOrWhiteSpace(builder.Configuration["urls"])
    || !string.IsNullOrWhiteSpace(builder.Configuration["Urls"]);
if (!hasExplicitUrls && !httpsEnabled)
{
    builder.WebHost.UseUrls("http://127.0.0.1:5000");
}

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    // 前端已由后端同源托管（wwwroot），正常访问不需要跨域。
    // 仅放行本机其它端口（vite dev 5173 等）便于开发；不再 AllowAnyOrigin。
    var origins = new List<string> { "https://localhost:5173", "http://localhost:5173", "http://127.0.0.1:5173" };
    var extra = Environment.GetEnvironmentVariable("WX_EXTRA_ORIGINS");
    if (!string.IsNullOrWhiteSpace(extra))
        origins.AddRange(extra.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    policy.WithOrigins(origins.ToArray()).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));

builder.Services.AddSignalR();

builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<DataStore>();
// 消息洪泛上限（按 JWT sub 身份计数，单例跨请求共享）：防偷 token / 绕过人机验证 / 轮换 IP 池重放
builder.Services.AddSingleton<MessageFloodLimiter>();
builder.Services.AddSingleton<WeChatWeb.Services.FileStore>();
builder.Services.AddHostedService<WeChatWeb.Services.FileCleanupService>();
// 服务端仅存 1 天聊天记录：定时清理过期消息（更早的由浏览器 IndexedDB 缓存）
builder.Services.AddHostedService<WeChatWeb.Services.MessageRetentionService>();
// 电源/冻结看门狗：VM/云游戏后台空闲被宿主暂停时——① 周期自环回保活降低被判空闲概率；② 时钟跳变侦测把「冻结窗口」写 logs/power.jsonl 留证（后台超时时直接取证）
builder.Services.AddHostedService<WeChatWeb.Services.PowerWatchdogService>();
builder.Services.AddSingleton<LogService>();

// 安全：敏感词过滤（从 data/sensitive.json 加载，缺失则用默认词库写出；支持 Reload 热加载）
builder.Services.AddSingleton<SensitiveWordService>();
// 滑块背景图库（首次启动自动生成 20 张内置背景；管理端可上传/删除/分页管理）
builder.Services.AddSingleton<SliderBackgroundStore>();
builder.Services.AddSingleton<WeChatWeb.Services.LoginBackgroundStore>();
// 用户问题反馈（data/feedback.jsonl 追加式持久化；客户端设置页提交，管理端读取/复制）
builder.Services.AddSingleton<WeChatWeb.Services.FeedbackStore>();
// 蜜罐（隔离式 Web 欺骗防御：诱饵路径 + tarpit 拖延 + 金丝雀留痕，不碰真系统数据；data/honey/）
builder.Services.AddSingleton<WeChatWeb.Services.HoneypotService>();


// 安全：注册密钥/验证码/锁定策略（security.json 持久化，WxAdmin 可改）、图形验证码、登录防护
var securitySettings = SecuritySettings.Load();
builder.Services.AddSingleton(_ => securitySettings);
builder.Services.AddSingleton<CaptchaService>();
builder.Services.AddSingleton<SliderCaptchaService>();
builder.Services.AddSingleton<LoginGuard>();
builder.Services.AddSingleton<EmailCodeService>();

// 限流：滑块挑战接口按 IP 限制，防止挑战被批量刷取（登录/接口另有 AuthRateLimit/ApiRateLimit 中间件）
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("captcha", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

// JWT 密钥：配置为空/过短/占位符时自动生成并持久化到 data/jwt.secret（见 JwtKeyProvider）
var jwtSecret = JwtKeyProvider.GetKey(builder.Configuration);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "wechat-web",
            ValidateAudience = true,
            ValidAudience = "wechat-web",
            ValidateLifetime = true,
            // BE-SEC-042：显式限定允许的签名算法白名单（仅 HS256），
            // 阻止 alg=none / 算法混淆（攻击者以公开 HS256 密钥伪造 RS256 声明）等攻击。
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
            IssuerSigningKey = new SymmetricSecurityKey(jwtSecret),
            ClockSkew = TimeSpan.FromMinutes(1),
        };

        // SignalR（WebSocket / SSE）无法自定义请求头，前端把 JWT 放在 access_token 查询参数里。
        // 按官方做法在这里把查询参数换算成 Bearer 令牌，否则 /hubs/* 会一直 401。
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// DEP-SEC 启动即递归收紧敏感目录树（Windows icacls /t、Unix chmod 700）：
// ・data/：jwt.secret、admin.key、store.json（含全用户 PBKDF2 密码哈希）、files/*（聊天上传）、slider-bg
// ・logs/：请求/认证/举报日志
// 收紧后「子项继承」已打开，之后任何新落盘文件自动继承「仅当前账户」，
// 本机低权限账户（共享电脑/被入侵的服务账户）再也无法直接读走密钥与密码哈希。
SensitiveFile.RestrictSensitiveDirs(
    Path.Combine(Directory.GetCurrentDirectory(), "data"),
    Path.Combine(app.Environment.ContentRootPath, "logs"));

// 滑块背景图库：目录为空时生成 20 张内置背景（data/slider-bg/）
app.Services.GetRequiredService<SliderBackgroundStore>().EnsureSeeded();

// 启动后置安全日志：注册密钥若为自动生成，明确提示管理员去哪里查看
if (securitySettings.RegisterKeyAutoGenerated)
    app.Services.GetRequiredService<LogService>().Write("auth", new
    {
        ts = DateTime.UtcNow,
        result = "register-key-auto-generated",
        hint = "注册密钥已自动生成，查看：管理端-安全设置 或 security.json",
        file = SecuritySettings.FilePath,
    });

// 管理密钥：appsettings 未显式配置时自动生成随机 32 字节并持久化（见 AdminKeyProvider）。
// 密钥本身只存在于服务端；GUI/WxAdmin 从 data/admin.key 读取，管理 API 恒定时间比较。
var adminKey = AdminKeyProvider.GetKey(builder.Configuration);
builder.Configuration["Admin:Key"] = adminKey;


// 浏览器支持改为应用内手动确认页（web/src/components/BrowserGate.vue + index.html 纯 HTML 兜底），
// 不做 UA 自动拦截；BrowserCompatMiddleware 保留在代码里但不再注册，需要时可重新启用。
// app.UseMiddleware<BrowserCompatMiddleware>();

app.UseCors();
// 流量防火墙：挂在最前，先于一切路由/静态/蜜罐/限流。
// 防"黑客刷流量"（多 IP 代理池、下载型 DDoS、慢连接灌水）；正常用户远达不到阈值，不误伤。
// 环回（127.0.0.1 / ::1，本机 GUI 管理 API）完全豁免。
app.UseMiddleware<TrafficFirewallMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();

/*
 * 全局安全异常兜底（BE-SEC / API-SEC）：
 * 任何未捕获异常 → 统一返回 500 + 通用 JSON，绝不把堆栈/SQL/文件路径/中间件名等
 * 内部细节回显给客户端（防止信息泄露被攻击者用于下一步利用）。真实异常在服务端记日志。
 * 放在安全头之后，保证 500 响应也带上 nosniff / CSP 等头。
 */
var logService = app.Services.GetRequiredService<WeChatWeb.Services.LogService>();
app.Use(async (ctx, next) =>
{
    try
    {
        await next(ctx);
    }
    catch (Exception ex)
    {
        try
        {
            logService.Write("error", new
            {
                ts = DateTime.UtcNow,
                path = ctx.Request.Path.Value ?? "",
                method = ctx.Request.Method,
                exception = ex.GetType().Name,
                message = ex.Message,
            });
        }
        catch { /* 日志失败不能影响兜底响应 */ }
        if (ctx.Response.HasStarted) throw; // 响应已开始（如 WebSocket 已升级）无法改写
        ctx.Response.Clear();
        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        await ctx.Response.WriteAsync("{\"error\":\"服务器内部错误，请稍后重试\"}");
    }
});

// DEP-SEC HTTPS：启用证书（WX_HTTPS_CERT）时，把外部明文 HTTP 请求 307 到 HTTPS（保留方法，登录 POST 不降级为 GET）。
// 环回豁免：GUI 管理 API 的 HttpClient 与本机调试走 127.0.0.1 明文口——若一并跳转，
// GUI 的 HttpClient 过不了自签名证书校验，管理功能会整个挂掉。
// HSTS 头已由 SecurityHeadersMiddleware 统一下发，此处不重复挂。
// 未启用证书时这段是 no-op，维持现有内网明文行为。
if (httpsEnabled)
{
    app.Use(async (ctx, next) =>
    {
        var remote = ctx.Connection.RemoteIpAddress;
        var loopback = remote != null && System.Net.IPAddress.IsLoopback(remote);
        // httpsBound=false（HTTPS 端口被占降级为纯 HTTP）时不能跳转，否则外部请求会被 307 到不属于我们的 443 上
        if (!loopback && ctx.Request.Scheme == "http" && httpsBound)
        {
            var portSuffix = httpsPorts.Contains(443) ? "" : $":{httpsPorts[0]}";
            ctx.Response.StatusCode = StatusCodes.Status307TemporaryRedirect;
            ctx.Response.Headers.Location =
                $"https://{ctx.Request.Host.Host}{portSuffix}{ctx.Request.PathBase}{ctx.Request.Path}{ctx.Request.QueryString}";
            return;
        }
        await next();
    });
}

// 限流：必须在 UseRouting 之前、且 UseRateLimiter 必须真正挂上，
// 否则 [EnableRateLimiting("captcha")] 之类的特性是死代码（曾导致验证码接口完全不限流）。
app.UseRateLimiter();

app.UseMiddleware<AuthRateLimitMiddleware>();
app.UseMiddleware<ApiRateLimitMiddleware>();
app.UseAuthentication();
// 封号/删号即时失效：已认证的每个请求（含 /hubs）按 sub 反查 DataStore，
// 用户不存在或已封禁即 401（BE-SEC-047 / BIZ-SEC-023）
app.UseMiddleware<UserInvalidationMiddleware>();
app.UseAuthorization();
app.UseMiddleware<WeChatWeb.Middleware.RequestLoggerMiddleware>();

// 前端静态资源：npm run build 已把 SPA 输出到 wwwroot，由后端直接托管，运行时无需 Node。
// 开发调试仍可用 npm run dev（5173，代理到 5000）。
// 缓存策略：assets/ 下是内容哈希文件名 → 一年 immutable 长缓存（刷新不再全量重拉几 MB bundle）；
// index.html / robots.txt / policy 等入口文件 → no-cache，每次校验保证发布后立即可见。
// 蜜罐：拦「扫描清单必查」的诱饵路径（.env/.git/backup//admin//actuator…），回假响应 + 拖延 + 留痕。
// 必须挂在静态/SPA 之前，否则 .env/.git 等会被静态托管或 SPA fallback 接走（蜜罐失效）。
app.UseMiddleware<WeChatWeb.Middleware.HoneypotMiddleware>();

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var path = ctx.Context.Request.Path;
        var immutable = path.StartsWithSegments("/assets");
        ctx.Context.Response.Headers.CacheControl = immutable
            ? "public, max-age=31536000, immutable"
            : "no-cache";
    }
});

// Swagger 只在开发环境暴露：生产/演示环境下 SwaggerUI 会泄露全部接口结构
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat").RequireAuthorization();

// LOG-SEC-008：匿名健康检查端点（供负载均衡 / 监控探针 / uptime 使用）。
// 只暴露「服务存活 + 当前 UTC 时间」，刻意不回显内存/框架/数据库等内部细节（避免 API-SEC-003 过度数据暴露）。
// 详细状态仍走 /api/admin/health（需 loopback + 管理密钥）。
app.MapGet("/health", () => Results.Ok(new { ok = true, ts = DateTime.UtcNow }));

// SPA 兜底：非 /api、/hubs 的路径一律交给前端路由（/chat、/contacts 等前端路由直接访问也不会 404）
app.MapFallback(async ctx =>
{
    if (ctx.Request.Path.StartsWithSegments("/api")
        || ctx.Request.Path.StartsWithSegments("/hubs")
        || ctx.Request.Path.StartsWithSegments("/swagger")
        || ctx.Request.Path.StartsWithSegments("/files"))
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    var index = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html");
    if (File.Exists(index))
    {
        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.SendFileAsync(index);
    }
    else
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        await ctx.Response.WriteAsync("前端尚未编译：请在 web 目录执行 npm run build（或用 npm run dev 开发）。");
    }
});

/*
 * DEP-SEC 启动自检：若监听地址含「非回环」（0.0.0.0 / * / 具体公网或局域网 IP / 主机名）
 * 又没启用 HTTPS，打印一条醒目告警。普通用户 API 没有 loopback 限制，这种组合等于把
 * 登录/上传/实时通道直接暴露，是"Windows 服务器被外部攻击"最典型的误配。
 * （强制回环的默认路径 hasExplicitUrls==false 不会触发此告警。）
 */
{
    string? effective = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
    if (string.IsNullOrWhiteSpace(effective))
    {
        var urlArg = args.FirstOrDefault(a => a.StartsWith("--urls=", StringComparison.OrdinalIgnoreCase));
        if (urlArg != null) effective = urlArg.Substring("--urls=".Length);
    }
    if (string.IsNullOrWhiteSpace(effective))
        effective = builder.Configuration["urls"];

    // 默认已强制 127.0.0.1 时无需告警；仅当「显式指定了监听地址」且「未启用 HTTPS」时检测外部绑定
    if (hasExplicitUrls && !httpsEnabled && !string.IsNullOrWhiteSpace(effective))
    {
        bool external = false;
        foreach (var u0 in effective!.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var host = u0.Trim()
                .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
                .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
                .Split('/')[0].Split(':')[0]
                .Trim('[', ']');
            // 127.x / localhost / ::1 = 回环（安全）；其余（0.0.0.0 / * / 内网 / 公网 / 主机名）= 外部
            bool isLoopback = host == "*" ? false
                : host.StartsWith("127.", StringComparison.Ordinal)
                || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || host == "::1";
            if (host is "0.0.0.0" or "*" or "::") { external = true; break; }
            if (!isLoopback && host.Length > 0) { external = true; break; }
        }
        if (external)
        {
            Console.Error.WriteLine(
                "============================================================\n" +
                " [DEP-SEC 警告] 服务将监听外部地址 " + effective + " 且未启用 HTTPS！\n" +
                "  普通用户 API 没有环回限制，等同于把登录/上传/实时通道暴露到网络。\n" +
                "  建议：内网用 127.0.0.1；需对外则设 WX_HTTPS_CERT 启用加密传输。\n" +
                "============================================================");
        }
    }
}

app.Run();
