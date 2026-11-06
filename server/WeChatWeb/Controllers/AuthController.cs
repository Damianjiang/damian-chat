using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using WeChatWeb.Data;
using WeChatWeb.Services;

namespace WeChatWeb.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly DataStore _store;
    private readonly JwtService _jwt;
    private readonly LogService _log;
    private readonly SecuritySettings _sec;
    private readonly CaptchaService _captcha;
    private readonly SliderCaptchaService _slider;
    private readonly LoginGuard _guard;
    private readonly WeChatWeb.Services.LoginBackgroundStore _loginBg;
    private readonly EmailCodeService _emailCode;
    private readonly IHubContext<Hubs.ChatHub> _hub;

    public AuthController (DataStore store, JwtService jwt, LogService log, SecuritySettings sec, CaptchaService captcha, SliderCaptchaService slider, LoginGuard guard, WeChatWeb.Services.LoginBackgroundStore loginBg, EmailCodeService emailCode, IHubContext<Hubs.ChatHub> hub)
    {
        _store = store;
        _jwt = jwt;
        _log = log;
        _sec = sec;
        _captcha = captcha;
        _slider = slider;
        _guard = guard;
        _loginBg = loginBg;
        _emailCode = emailCode;
        _hub = hub;
    }

    /// <summary>
    /// 账号安全提醒（微信「安全提醒」同款）：向该账号当前全部在线连接推送。
    /// 被盗号的第一发现人往往是被挤下线的本人 —— 新设备登录/改密/换绑邮箱都推一条。
    /// </summary>
    private async Task NotifyAccountSecurity (string userId, object payload)
    {
        try
        {
            List<string> conns;
            lock (_store) conns = _store.ConnectionsOf(userId).ToList();
            if (conns.Count > 0)
                await _hub.Clients.Clients(conns).SendAsync("AccountSecurity", payload);
        }
        catch { /* 实时通道不可用不影响登录本身 */ }
    }

    /* ---------------- 登录页主题 / 背景（公开，无鉴权） ---------------- */

    /// <summary>登录页背景配置：波纹色 / 纯色 / 图片文件名。登录页首帧据此渲染「蓝色动波纹」（暗色=浅蓝）。</summary>
    [HttpGet("login-theme")]
    public IActionResult LoginTheme ()
    {
        var c = _loginBg.Get();
        return Ok(new
        {
            mode = c.Mode,
            rippleColor = c.RippleColor,
            color = c.Color,
            image = c.Mode == "image" ? $"/api/auth/login-bg/{c.Image}" : "",
        });
    }

    /// <summary>登录背景图片（仅「图片模式」且管理端上传过才有效；白名单+路径穿越双保险，无鉴权）。</summary>
    [HttpGet("login-bg/{name}")]
    public IActionResult LoginBg (string name)
    {
        var (bytes, contentType, err) = _loginBg.Read(name);
        if (bytes == null) return NotFound(new { error = err ?? "文件不存在" });
        return File(bytes, contentType ?? "application/octet-stream", enableRangeProcessing: true);
    }

    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
    private string Ua => Request.Headers.UserAgent.ToString();

    // ---------------- refresh 令牌 httpOnly cookie（防 XSS 读走 7 天长效凭据） ----------------
    /// <summary>refresh 令牌 cookie 名。HttpOnly：JS 不可读；SameSite=Strict：防跨站携带。Path 收窄到 /api/auth。</summary>
    private const string RefreshCookie = "wx_refresh";

    /// <summary>
    /// 本次请求携带的 refresh 令牌：httpOnly cookie 优先（JS 读不到，须服务端取），退回请求体（过渡期兼容旧客户端）。
    /// 身份判定只信服务端 cookie，不接受客户端自报之外的来源。
    /// </summary>
    private string? PresentedRefresh (string? body) =>
        string.IsNullOrEmpty(Request.Cookies[RefreshCookie]) ? body : Request.Cookies[RefreshCookie];

    /// <summary>下发 refresh 令牌 cookie（7 天，HttpOnly；Secure 按请求协议自适应——内网明文 http 下强制 Secure 会拒收 cookie）。</summary>
    private void SetRefreshCookie (string token)
    {
        Response.Cookies.Append(RefreshCookie, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = HttpContext.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
            MaxAge = TimeSpan.FromDays(7),
        });
    }

    private void ClearRefreshCookie ()
    {
        Response.Cookies.Delete(RefreshCookie, new CookieOptions
        {
            HttpOnly = true,
            Secure = HttpContext.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
        });
    }

    // 与 Validate.AccountPattern 保持一致：11 位手机号 / 3-32 位字母数字下划线 / 2-16 位中文
    private static readonly Regex AccountPattern = new(@"^(?:1[3-9]\d{9}|[A-Za-z0-9_]{3,32}|[\u4e00-\u9fa5]{2,16})$", RegexOptions.Compiled);

    public record LoginRequest (string Account, string Password, string? CaptchaId, string? Captcha, string? PassToken = null);
    public record RegisterRequest (string Account, string Password, string RegisterKey, string? CaptchaId = null, string? Captcha = null, string? PassToken = null, string? Email = null, string? EmailCode = null);
    public record EmailCodeRequest (string Email, string? CaptchaId, string? Captcha);
    public record RefreshRequest (string RefreshToken);

    /// <summary>账号不存在时用于抹平时序的固定哈希（进程启动时算一次）。</summary>
    private static readonly string DummyHash = PasswordHasher.Hash("timing-equalizer");

    /// <summary>图形验证码（防机器人挑战）：登录/注册前获取。</summary>
    [HttpGet("captcha")]
    public IActionResult GetCaptcha ()
    {
        var (id, dataUrl) = _captcha.Generate(Ip);
        return Ok(new { captchaId = id, image = dataUrl });
    }

    /// <summary>登录策略（公开）：是否需要滑块/验证码、注册是否开放与注册模式，供登录页自适应。</summary>
    [HttpGet("policy")]
    public IActionResult Policy () => Ok(new
    {
        slider = _sec.EnableSlider,    // 滑块：登录/注册按钮点击即弹出；管理端可关（安全设置页）
        captcha = _sec.EnableCaptcha,  // 图形验证码：双重验证的第一重
        registerOpen = !string.IsNullOrEmpty(_sec.RegisterKey),
        registerMode = string.IsNullOrEmpty(_sec.RegisterKey) ? "" : _sec.RegisterMode, // direct=账号注册 / email=邮箱注册（空=注册关闭）
        smtpReady = _sec.SmtpReady,    // 邮箱注册需要 SMTP；未配置时前端提示管理员
    });

    /// <summary>发送邮箱验证码（注册 / 后续绑定邮箱共用）。图形验证码挡机器人；发送侧自身节流在 EmailCodeService。</summary>
    [HttpPost("email-code")]
    public IActionResult EmailCode ([FromBody] EmailCodeRequest req)
    {
        // 不看注册开关：已注册用户「绑定邮箱」也走本接口；滥用由验证码+节流兜底
        // 图形验证码（若开启）：挡脚本批量发信
        if (_sec.EnableCaptcha && !_captcha.Verify(req.CaptchaId, req.Captcha, Ip))
            return BadRequest(new { error = "图形验证码错误或已过期，请刷新重试" });
        var err = _emailCode.Send(req.Email, Ip, "register");
        if (err != null)
        {
            // SMTP 故障属服务端错误，不应把用户输入的任意串计入账号防护；只计 IP
            _guard.RecordFailure(Ip, "", _sec);
            return BadRequest(new { error = err });
        }
        return Ok(new { ok = true });
    }

    /// <summary>账号密码 + 图形验证码登录。失败计入 IP 限速与账号锁定。</summary>
    [HttpPost("login")]
    public IActionResult Login ([FromBody] LoginRequest req)
    {
        var account = (req.Account ?? "").Trim();
        var blocked = _guard.Check(Ip, account, _sec);
        if (blocked != null) return StatusCode(StatusCodes.Status429TooManyRequests, new { error = blocked });

        // 双重人机验证 ①：图形验证码（原有逻辑，保留）
        if (_sec.EnableCaptcha && !_captcha.Verify(req.CaptchaId, req.Captcha, Ip))
        {
            // 验证码错 ≠ 密码错：不能把失败记到「账号」上，否则任何人不用密码
            // 就能连环触发对受害者的账号锁定（Register 分支同款修复，见下）
            _guard.RecordFailure(Ip, "", _sec);
            _log.Write("auth", new { ts = DateTime.UtcNow, result = "captcha-fail", account, ip = Ip, ua = Ua });
            return BadRequest(new { error = "图形验证码错误或已过期，请刷新重试" });
        }

        // 双重人机验证 ②：滑块挑战换来的 pass token（一次性、90 秒、绑定 IP、签发 1.2 秒后才可消费）。
        // 管理端可关（EnableSlider）：关闭后不再要求 passToken（图形验证码仍在）。
        var pass = _slider.ConsumePass(req.PassToken, Ip);
        if (pass != SliderPassResult.Ok)
        {
            // 同上：没过滑块说明还没走到「猜密码」那一步，只计 IP 不记账号
            _guard.RecordFailure(Ip, "", _sec);
            _log.Write("auth", new
            {
                ts = DateTime.UtcNow,
                result = pass == SliderPassResult.NotReady ? "slider-not-ready" : "slider-missing",
                account,
                ip = Ip,
                ua = Ua,
            });
            return BadRequest(new
            {
                error = pass == SliderPassResult.NotReady ? "安全检测尚未完成，请稍候重试" : "请先完成滑块验证",
            });
        }

        Models.User? user;
        DateTime pwdChangedSnapshot;
        lock (_store)
        {
            // 登录：注册账号 或 大面号（改过号的用户用大面号也能登录）。
            // 大面号按大小写不敏感匹配（与改号唯一性一致，防同形号行为分裂）
            user = _store.Users.Values.FirstOrDefault(u => u.Phone == account)
                ?? _store.Users.Values.FirstOrDefault(u => string.Equals(u.WxId, account, StringComparison.OrdinalIgnoreCase));
            pwdChangedSnapshot = user?.PasswordChangedAt ?? DateTime.MinValue;
        }

        if (user == null)
        {
            // 假 Verify：账号不存在时也执行一次同成本哈希比较。
            // 否则「账号不存在」明显快于「密码错误」，响应时序可被用来枚举有效账号。
            PasswordHasher.Verify(req.Password ?? "", DummyHash);
        }
        if (user == null || !PasswordHasher.Verify(req.Password ?? "", user.Password))
        {
            _guard.RecordFailure(Ip, account, _sec);
            _log.Write("auth", new { ts = DateTime.UtcNow, result = "fail", account, ip = Ip, ua = Ua });
            return Unauthorized(new { error = "账号或密码错误，请重试" });
        }
        // 封禁检查在密码验证之后：无密码者探测不到账号状态（不做状态 oracle）
        if (user.Banned)
        {
            _log.Write("auth", new { ts = DateTime.UtcNow, result = "banned", account, ip = Ip });
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "该账号已被管理员停用" });
        }

        // 改密 TOCTOU：密码验证期间用户改了密码 → 在途登录不得签发新会话
        bool newDevice;
        lock (_store)
        {
            if (user.PasswordChangedAt != pwdChangedSnapshot)
            {
                // 与密码失败同规写审计（此前该分支静默 401，攻击痕迹凭空消失）
                _log.Write("auth", new { ts = DateTime.UtcNow, result = "fail-stale-password", user = user.Id, account, ip = Ip, ua = Ua });
                return Unauthorized(new { error = "账号或密码错误，请重试" });
            }
            // 盗号检测：本机（同 UA）此前从未登录过 = 新设备。须在 CreateSessionTokens
            // 登记会话之前判定。老账号的第一个会话不算（首次登录没得提醒）。
            var sessions = _store.LoginSessions.TryGetValue(user.Id, out var l) ? l! : new();
            newDevice = sessions.Count > 0 && sessions.All(s => s.Device != Ua);
        }
        _guard.ResetAccount(account);
        _log.Write("auth", new { ts = DateTime.UtcNow, result = "ok", user = user.Id, account, ip = Ip, ua = Ua, newDevice });
        if (newDevice)
            _log.Write("auth", new { ts = DateTime.UtcNow, result = "new-device-login", user = user.Id, ip = Ip, ua = Ua });
        var issued = IssueTokens(user);
        if (newDevice)
        {
            // 安全提醒推给该账号当前在线的其他端（此刻新端尚未建立 WS 连接，正好全是他端）
            _ = NotifyAccountSecurity(user.Id, new
            {
                type = "new-device-login",
                device = Ua,
                ip = Ip,
                time = DateTime.UtcNow,
            });
        }
        return Ok(issued);

        object IssueTokens (Models.User u)
        {
            var (access, refresh, sid) = CreateSessionTokens(u);
            var cacheKey = EnsureCacheKey(u);
            SetRefreshCookie(refresh);   // refresh 落 httpOnly cookie（JS 读不到）
            return new { accessToken = access, expiresIn = 7200, cacheKey, user = PublicUser(u) };
        }
    }

    /// <summary>
    /// 注册：账号 + 密码 + 管理员注册密钥（三者缺一不可）。
    /// 密钥为空表示注册关闭；错误密钥计入 IP/账号防护。
    /// </summary>
    [HttpPost("register")]
    public IActionResult Register ([FromBody] RegisterRequest req)
    {
        // 注册模式（管理端配置）：direct=账号即大面号；email=邮箱+验证码注册，
        // 登录账号=邮箱，大面号随机生成且默认不可被搜索（微信 2017 年后关闭随机
        // wxid 搜索，防批量加人骚扰；用户改成自定义大面号后才可被搜到）。
        var account = (req.Account ?? "").Trim();
        var blocked = _guard.Check(Ip, account, _sec);
        if (blocked != null) return StatusCode(StatusCodes.Status429TooManyRequests, new { error = blocked });

        if (string.IsNullOrEmpty(_sec.RegisterKey))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "注册未开放，请联系管理员获取注册密钥" });

        // 双重人机验证 ①：图形验证码（原有逻辑，保留）
        if (_sec.EnableCaptcha && !_captcha.Verify(req.CaptchaId, req.Captcha, Ip))
        {
            // 人机验证失败只计 IP：把攻击者的失败记到「账号」上会让任何人恶意锁死受害者登录
            _guard.RecordFailure(Ip, "", _sec);
            _log.Write("auth", new { ts = DateTime.UtcNow, result = "captcha-fail", account, ip = Ip, ua = Ua });
            return BadRequest(new { error = "图形验证码错误或已过期，请刷新重试" });
        }

        // 双重人机验证 ②：滑块 pass token（管理端可关 EnableSlider）。NotReady=延时门未到。
        var pass = _slider.ConsumePass(req.PassToken, Ip);
        if (pass != SliderPassResult.Ok)
        {
            _guard.RecordFailure(Ip, "", _sec);
            _log.Write("auth", new
            {
                ts = DateTime.UtcNow,
                result = pass == SliderPassResult.NotReady ? "slider-not-ready" : "slider-missing",
                account,
                ip = Ip,
                ua = Ua,
            });
            return BadRequest(new
            {
                error = pass == SliderPassResult.NotReady ? "安全检测尚未完成，请稍候重试" : "请先完成滑块验证",
            });
        }

        if (!_sec.RegisterKeyMatches(req.RegisterKey))
        {
            _guard.RecordFailure(Ip, "", _sec);   // 注册密钥错误不是密码错误，只计 IP
            _log.Write("auth", new { ts = DateTime.UtcNow, result = "register-key-fail", account, ip = Ip, ua = Ua });
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "注册密钥不正确（管理员可在后台修改）" });
        }

        string email = "";
        if (_sec.RegisterMode == "email")
        {
            // 邮箱模式：登录账号就是邮箱（不再要求手机号/用户名格式），大面号随机生成
            email = (req.Email ?? "").Trim().ToLowerInvariant();
            if (!EmailCodeService.ValidEmail(email))
                return BadRequest(new { error = "请输入正确的邮箱地址" });
            var codeErr = _emailCode.Verify(email, req.EmailCode);
            if (codeErr != null) return BadRequest(new { error = codeErr });
            account = email;
        }
        else if (!AccountPattern.IsMatch(account))
            return BadRequest(new { error = "账号需为 11 位手机号、3-32 位字母/数字/下划线，或 2-16 位中文" });
        if (PasswordPolicy.Validate(req.Password) is { } pwErr)
            return BadRequest(new { error = pwErr });

        Models.User user;
        lock (_store)
        {
            if (_store.Users.Values.Any(u => u.Phone == account))
            {
                _guard.RecordFailure(Ip, "", _sec);   // 重复注册失败只计 IP，防恶意锁号
                return Conflict(new { error = "该账号已被注册" });
            }
            if (email.Length > 0 && _store.Users.Values.Any(u => u.Email == email))
            {
                _guard.RecordFailure(Ip, account, _sec);
                return Conflict(new { error = "该邮箱已被绑定" });
            }
            // 大面号唯一性是全路径不变量：账号名若已被他人用作大面号，拒绝注册
            //（否则按大面号搜索会命中两个人，好友申请可被先注册者劫持）。忽略大小写。
            if (_store.Users.Values.Any(u => string.Equals(u.WxId, account, StringComparison.OrdinalIgnoreCase)))
            {
                _guard.RecordFailure(Ip, "", _sec);
                return Conflict(new { error = "该账号已被用作大面号，请更换账号后重试" });
            }
            user = _store.RegisterUser(account, req.Password);
            if (email.Length > 0) user.Email = email;
            // 邮箱注册：大面号随机生成（WxIdCustom=false → 不可被搜索），昵称给个默认
            if (_sec.RegisterMode == "email")
            {
                user.WxId = _store.GenerateUniqueWxIdLocked();
                user.WxIdCustom = false;
                user.Nickname = "用户" + user.WxId[^4..];
            }
        }

        _guard.ResetAccount(account);
        _log.Write("auth", new { ts = DateTime.UtcNow, result = "register-ok", user = user.Id, account, ip = Ip, ua = Ua });

        var (access, refresh, _) = CreateSessionTokens(user);
        var cacheKey = EnsureCacheKey(user);
        SetRefreshCookie(refresh);   // 注册成功即登录：refresh 落 httpOnly cookie
        return Ok(new { accessToken = access, expiresIn = 7200, cacheKey, user = PublicUser(user) });
    }

    [HttpPost("refresh")]
    public IActionResult Refresh ([FromBody] RefreshRequest? req)
    {
        // 新客户端：refresh 走 httpOnly cookie（浏览器自动携带，JS 读不到）；旧客户端 body 过渡兼容
        var presented = PresentedRefresh(req?.RefreshToken);
        lock (_store)
        {
            // 顺手回收过期 refresh token：此前过期项永不清理，长期运行缓慢积累
            var now = DateTime.UtcNow;
            foreach (var kv in _store.RefreshTokens.Where(kv => kv.Value.ExpiresAt < now).ToList())
                _store.RefreshTokens.Remove(kv.Key);

            if (string.IsNullOrEmpty(presented)
                || !_store.RefreshTokens.TryGetValue(DataStore.HashRefreshToken(presented), out var rt) || rt.ExpiresAt < now)
            { ClearRefreshCookie(); return Unauthorized(new { error = "refresh token 无效" }); }
            if (!_store.Users.TryGetValue(rt.UserId, out var user)) { ClearRefreshCookie(); return Unauthorized(new { error = "用户不存在" }); }
            if (user.Banned) { ClearRefreshCookie(); return Unauthorized(new { error = "账号已停用" }); }

            _store.RefreshTokens.Remove(DataStore.HashRefreshToken(presented));
            // 解析旧 token 所属设备会话：sid 延续到新令牌（踢出/设备列表依赖它）；
            // 旧令牌没有会话（升级前签发）则新建一条，保证"每个 refresh 都有会话可踢"
            var oldSid = _store.FindSessionIdByToken(presented);
            var sid = oldSid ?? Guid.NewGuid().ToString("N");
            var (access, refresh) = _jwt.CreateTokens(user, sid);
            var cacheKey = EnsureCacheKey(user);
            // RefreshTokens / 设备会话一律存哈希：store.json 外泄不再等于全站刷新链泄露
            _store.RefreshTokens[DataStore.HashRefreshToken(refresh)] = new RefreshToken { Value = DataStore.HashRefreshToken(refresh), UserId = user.Id, ExpiresAt = DateTime.UtcNow.AddDays(7) };
            _store.BindSession(user.Id, sid, refresh, Ua, Ip);
            SetRefreshCookie(refresh);   // 轮转后的新 refresh 写回 httpOnly cookie
            return Ok(new { accessToken = access, expiresIn = 7200, cacheKey });
        }
    }

    /// <summary>退出登录：吊销 refresh token（cookie + 旧客户端 body）。</summary>
    [HttpPost("logout")]
    public IActionResult Logout ([FromBody] RefreshRequest? req)
    {
        var presented = PresentedRefresh(req?.RefreshToken);
        var me = User.Identity?.IsAuthenticated == true ? HttpContext.GetUserId() : null;
        lock (_store)
        {
            if (!string.IsNullOrEmpty(presented))
            {
                _store.RefreshTokens.Remove(DataStore.HashRefreshToken(presented));
                // 有鉴权按 sub 清；无鉴权（token 已过期的客户端）反查清，避免僵尸会话残留
                if (me != null) _store.EndSessionByToken(me, presented);
                else if (_store.TryFindSessionByToken(presented, out var zuid, out var zsid)) _store.EndSession(zuid, zsid);
            }
        }
        ClearRefreshCookie();   // 清 httpOnly cookie（+ 旧客户端传了 body 的一并吊销）
        return Ok(new { ok = true });
    }

    /// <summary>
    /// 签发一对令牌并登记「登录设备会话」（设备=UA+IP，永久保存）。
    /// sid 随 access token 下发：被踢设备即使 token 未过期也立即失效（中间件校验）。
    /// </summary>
    /// <summary>
    /// 本机缓存加密密钥（按用户稳定）：首次登录签发 32B 随机数并随用户落盘，此后恒定。
    /// 客户端对 IndexedDB 聊天记录/媒体缓存做 AES-GCM 加密用。锁内调用（登录/注册/刷新路径均已持锁）。
    /// </summary>
    private string EnsureCacheKey (Models.User u)
    {
        if (!string.IsNullOrEmpty(u.CacheKey)) return u.CacheKey;
        u.CacheKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return u.CacheKey;
    }

    private (string access, string refresh, string sid) CreateSessionTokens (Models.User u)
    {
        string access;
        string refresh;
        // sid 先生成并写进 access token（此前漏传，整条"踢出即时失效"链是死代码）
        var sid = Guid.NewGuid().ToString("N");
        lock (_store)
        {
            (access, refresh) = _jwt.CreateTokens(u, sid);
            _store.BindSession(u.Id, sid, refresh, Ua, Ip);
            // 只存哈希（明文只活在签发响应与 httpOnly cookie 里，绝不落盘）
            _store.RefreshTokens[DataStore.HashRefreshToken(refresh)] = new RefreshToken { Value = DataStore.HashRefreshToken(refresh), UserId = u.Id, ExpiresAt = DateTime.UtcNow.AddDays(7) };
        }
        return (access, refresh, sid);
    }

    /// <summary>客户端安全视图：绝不携带密码哈希。WxId 是账号本人的大面号，随登录下发。</summary>
    private static object PublicUser (Models.User u) => new
    {
        u.Id,
        Phone = u.Phone,
        u.Nickname,
        u.Avatar,
        WxId = u.WxId,
        u.WxIdCustom,
        u.WxIdPrompted,
        u.Email,
        u.Signature,
        u.Region,
        u.Gender,
        u.Banned,
        u.CreatedAt,
    };
}
