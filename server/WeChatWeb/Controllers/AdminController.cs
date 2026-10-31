using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using WeChatWeb.Data;
using WeChatWeb.Services;

namespace WeChatWeb.Controllers;

/// <summary>
/// 管理端接口（供本机 WxAdmin 控制台 / 图形界面调用）。
/// 鉴权：① 只接受来自环回地址（127.0.0.1/::1）的请求；② 校验本机密钥（data/admin.key，
/// 首次启动随机生成）。本机程序自动读取该文件，无需人工输入任何账号密码。
/// </summary>
[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly DataStore _store;
    private readonly LogService _log;
    private readonly IConfiguration _config;
    private readonly SecuritySettings _sec;

    private readonly FileStore _files;
    private readonly SliderBackgroundStore _sliderBgs;
    private readonly WeChatWeb.Services.LoginBackgroundStore _loginBg;
    private readonly FeedbackStore _feedback;
    private readonly HoneypotService _honey;

    public AdminController(DataStore store, LogService log, IConfiguration config, SecuritySettings sec, FileStore files, SliderBackgroundStore sliderBgs, WeChatWeb.Services.LoginBackgroundStore loginBg, FeedbackStore feedback, HoneypotService honey)
    {
        _store = store;
        _files = files;
        _sliderBgs = sliderBgs;
        _loginBg = loginBg;
        _log = log;
        _config = config;
        _sec = sec;
        _feedback = feedback;
        _honey = honey;
    }


    /// <summary>控制台登录：账号 + 密码 → 30 分钟管理令牌（失败计数与锁定复用登录防护策略）。</summary>
    private bool KeyOk()
    {
        var expected = _config["Admin:Key"];
        // 旧的公开默认值已废弃：未显式配置且没生成密钥时一律拒绝
        if (string.IsNullOrWhiteSpace(expected) || expected == "wxadmin-dev-key") return false;

        // 只接受来自本机环回地址的管理请求：局域网/公网上的机器一律拒绝。
        // （服务默认就监听 localhost，这里再挡一层，防止误配成 0.0.0.0 时被外部调用）
        var remote = HttpContext.Connection.RemoteIpAddress;
        if (remote != null && !System.Net.IPAddress.IsLoopback(remote)) return false;

        // 本机程序直接带密钥（密钥文件在本机 data/admin.key，无需人工输入密码）
        // 仅接受 X-Admin-Key 请求头：不把密钥放 ?key= 查询串——查询串会泄漏进
        // 代理/访问日志、浏览器历史、Referer 头，还能被 URL 复制分享造成管理提权。
        var given = Request.Headers["X-Admin-Key"].ToString();
        return FixedEquals(expected, given);
    }

    private static bool FixedEquals(string a, string b)
    {
        var ha = SHA256.HashData(Encoding.UTF8.GetBytes(a ?? ""));
        var hb = SHA256.HashData(Encoding.UTF8.GetBytes(b ?? ""));
        return CryptographicOperations.FixedTimeEquals(ha, hb);
    }

    private IActionResult? NoKey() => KeyOk() ? null : Unauthorized();

    [HttpGet("online")]
    public IActionResult Online()
    {
        if (NoKey() is { } r) return r;
        var users = _store.OnlineUsers();
        return Ok(new
        {
            count = users.Count,
            users = users.Select(u => new
            {
                userId = u.UserId,
                nickname = _store.Users.TryGetValue(u.UserId, out var x) ? x.Nickname : u.UserId,
                lastActive = u.LastActive,
            }),
        });
    }

    [HttpGet("logs")]
    public IActionResult Logs([FromQuery] string type = "requests", [FromQuery] int lines = 100)
    {
        if (NoKey() is { } r) return r;
        if (type is not ("requests" or "auth" or "reports")) return BadRequest(new { error = "type 只能是 requests / auth / reports" });
        return Ok(new { lines = _log.Tail(type, Math.Clamp(lines, 1, 1000)) });
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        if (NoKey() is { } r) return r;
        return Ok(new { ok = true, time = DateTime.UtcNow, memoryMb = GC.GetTotalMemory(false) / 1024 / 1024 });
    }

    /* ---------------- 安全设置 ---------------- */

    /// <summary>读取安全配置（注册密钥、验证码开关、锁定策略）。</summary>
    [HttpGet("security")]
    public IActionResult GetSecurity()
    {
        if (NoKey() is { } r) return r;
        return Ok(new
        {
            _sec.RegisterKey,
            _sec.EnableCaptcha,
            _sec.EnableSlider,
            _sec.MaxLoginAttempts,
            _sec.LockoutMinutes,
            _sec.AuthPerMinute,
            _sec.ApiPerMinute,
            _sec.FwRequestsPerMinute,
            _sec.FwMaxConcurrent,
            _sec.FwBytesPerMinute,
            _sec.FwBanThreshold,
            _sec.FwBanMinutes,
            _sec.FloodMessagesPerMinute,
            file = SecuritySettings.FilePath,
        });
    }

    public record SecurityUpdate(string? RegisterKey, bool? EnableCaptcha, bool? EnableSlider, int? MaxLoginAttempts, int? LockoutMinutes, int? AuthPerMinute, int? ApiPerMinute, int? FwRequestsPerMinute, int? FwMaxConcurrent, long? FwBytesPerMinute, int? FwBanThreshold, int? FwBanMinutes, int? FloodMessagesPerMinute);

    /// <summary>更新安全配置（立即生效并写入 security.json）。</summary>
    [HttpPut("security")]
    public IActionResult PutSecurity([FromBody] SecurityUpdate req)
    {
        if (NoKey() is { } r) return r;
        if (req.RegisterKey != null)
        {
            var key = req.RegisterKey.Trim();
            // 注册密码强度：至少 8 位，避免又被设成弱口令
            if (key.Length > 0 && key.Length < 8)
                return BadRequest(new { error = "注册密码至少 8 位（留空 = 关闭注册）" });
            _sec.RegisterKey = key;
        }
        if (req.EnableCaptcha.HasValue) _sec.EnableCaptcha = req.EnableCaptcha.Value;
        if (req.EnableSlider.HasValue) _sec.EnableSlider = req.EnableSlider.Value;
        if (req.MaxLoginAttempts is > 0 and < 1000) _sec.MaxLoginAttempts = req.MaxLoginAttempts!.Value;
        if (req.LockoutMinutes is > 0 and < 10000) _sec.LockoutMinutes = req.LockoutMinutes!.Value;
        if (req.AuthPerMinute is > 0 and < 100000) _sec.AuthPerMinute = req.AuthPerMinute!.Value;
        if (req.ApiPerMinute is >= 10 and < 1000000) _sec.ApiPerMinute = req.ApiPerMinute!.Value;
        if (req.FwRequestsPerMinute is >= 100 and < 10_000_000) _sec.FwRequestsPerMinute = req.FwRequestsPerMinute!.Value;
        if (req.FwMaxConcurrent is >= 2 and < 100000) _sec.FwMaxConcurrent = req.FwMaxConcurrent!.Value;
        if (req.FwBytesPerMinute is >= 1_048_576 and < 1_073_741_824_000L) _sec.FwBytesPerMinute = req.FwBytesPerMinute!.Value;
        if (req.FwBanThreshold is >= 1 and < 100000) _sec.FwBanThreshold = req.FwBanThreshold!.Value;
        if (req.FwBanMinutes is >= 1 and < 10080) _sec.FwBanMinutes = req.FwBanMinutes!.Value;
        // 消息洪泛上限（身份维度）：0/负 = 关闭此检查（钳到 1）；上限 100000 防误填
        if (req.FloodMessagesPerMinute is not null) _sec.FloodMessagesPerMinute = Math.Clamp(req.FloodMessagesPerMinute.Value, 1, 100_000);
        _sec.Save();
        _log.Write("auth", new { ts = DateTime.UtcNow, result = "admin-security-updated", file = SecuritySettings.FilePath });
        return GetSecurity();
    }

    /* ---------------- 文件与群限制 ---------------- */

    /// <summary>读取文件存储与群限制配置（GUI 安全页展示/编辑）。</summary>
    [HttpGet("limits")]
    public IActionResult GetLimits()
    {
        if (NoKey() is { } r) return r;
        var f = _files.Config;
        var g = _store.GroupLimits;
        return Ok(new
        {
            maxFileMb = f.MaxFileBytes / 1024 / 1024,
            uploadSpeedMb = f.MaxUploadBytesPerSecond / 1024 / 1024,
            keepDays = f.KeepDays,
            maxGroupMembers = g.MaxMembers,
            maxGroupAdmins = g.MaxAdmins,
            maxSelectOnCreate = g.MaxSelectOnCreate,
        });
    }

    public record LimitsUpdate(long? MaxFileMb, long? UploadSpeedMb, int? KeepDays, int? MaxGroupMembers, int? MaxGroupAdmins, int? MaxSelectOnCreate);

    /// <summary>更新限制配置（即时生效并落盘）。</summary>
    [HttpPut("limits")]
    public IActionResult PutLimits([FromBody] LimitsUpdate req)
    {
        if (NoKey() is { } r) return r;
        _files.UpdateConfig(req.MaxFileMb * 1024 * 1024, req.UploadSpeedMb * 1024 * 1024, req.KeepDays);
        _store.GroupLimits.Apply(req.MaxGroupMembers, req.MaxGroupAdmins, req.MaxSelectOnCreate);
        _log.Write("auth", new { ts = DateTime.UtcNow, result = "admin-limits-updated" });
        return GetLimits();
    }

    /// <summary>立即清理过期文件（手动触发，默认保留 5 天）。</summary>
    [HttpPost("files/cleanup")]
    public IActionResult CleanupFiles()
    {
        if (NoKey() is { } r) return r;
        var removed = _files.CleanupExpired();
        _log.Write("auth", new { ts = DateTime.UtcNow, result = "admin-files-cleanup", removed });
        return Ok(new { removed, keepDays = _files.Config.KeepDays });
    }

    /// <summary>
    /// 立即清理数据：手动触发「7 天数据清理」逻辑（删除超出保留期的聊天记录），
    /// 相当于把后台 7 天一次的自动清理立即执行一次。
    /// </summary>
    [HttpPost("data/cleanup")]
    public IActionResult CleanupData()
    {
        if (NoKey() is { } r) return r;
        var removedMessages = _store.PruneOldMessages(DataStore.RetentionMessages);
        _log.Write("auth", new { ts = DateTime.UtcNow, result = "admin-data-cleanup", removedMessages });
        return Ok(new { removedMessages, keepDays = (int)DataStore.RetentionMessages.TotalDays });
    }

    /* ---------------- 滑块背景图库 ---------------- */

    /// <summary>滑块背景图分页列表（供管理端 GUI 查看/管理）。</summary>
    [HttpGet("slider/backgrounds")]
    public IActionResult SliderBgList([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        if (NoKey() is { } r) return r;
        var (total, pages, items) = _sliderBgs.List(Math.Max(1, page), Math.Clamp(pageSize, 1, 50));
        return Ok(new
        {
            total,
            pages,
            page = Math.Clamp(page, 1, pages),
            pageSize,
            items = items.Select(i => new { name = i.Name, sizeKb = (int)(i.SizeBytes / 1024), modifiedAt = i.ModifiedAt }),
        });
    }

    /// <summary>预览一张滑块背景（本机管理端带 key 拉取，浏览器 img 标签场景不存在 —— 管理端是 WinForms）。</summary>
    [HttpGet("slider/backgrounds/{name}/preview")]
    public IActionResult SliderBgPreview(string name)
    {
        if (NoKey() is { } r) return r;
        var bytes = _sliderBgs.Read(name);
        if (bytes == null) return NotFound();
        return File(bytes, "image/png");
    }

    /// <summary>上传滑块背景（PNG、300x150、≤512KB；魔数+尺寸校验）。</summary>
    [HttpPost("slider/backgrounds")]
    [Consumes("multipart/form-data")]
    public IActionResult SliderBgUpload(IFormFile file)
    {
        if (NoKey() is { } r) return r;
        if (file == null || file.Length == 0) return BadRequest(new { error = "文件为空" });
        // 先检查声明大小再读入内存：避免超大文件被整份缓冲（此前 CopyTo 先于校验）
        if (file.Length > 512 * 1024) return BadRequest(new { error = "背景图不能超过 512KB" });
        using var ms = new MemoryStream();
        file.CopyTo(ms);
        var err = _sliderBgs.Add(file.FileName, ms.ToArray());
        if (err != null) return BadRequest(new { error = err });
        _log.Write("auth", new { ts = DateTime.UtcNow, result = "admin-slider-upload", name = file.FileName, sizeKb = file.Length / 1024 });
        return Ok(new { ok = true });
    }

    /// <summary>删除滑块背景（名字白名单校验，防路径穿越）。</summary>
    [HttpDelete("slider/backgrounds/{name}")]
    public IActionResult SliderBgDelete(string name)
    {
        if (NoKey() is { } r) return r;
        return _sliderBgs.Delete(name) ? Ok(new { ok = true }) : NotFound();
    }

    /* ---------------- 登录页背景（GUI 设置；登录页公开读取 /api/auth/login-theme + /api/auth/login-bg/{name}） ---------------- */

    public record LoginBgConfig(string? Mode, string? RippleColor, string? Color, string? Image);

    /// <summary>读登录背景配置（GUI 展示/编辑）。</summary>
    [HttpGet("login-background")]
    public IActionResult GetLoginBackground()
    {
        if (NoKey() is { } r) return r;
        var c = _loginBg.Get();
        return Ok(new { c.Mode, c.RippleColor, c.Color, c.Image });
    }

    /// <summary>保存登录背景配置（立即生效：登录页下次渲染即按新模式/颜色/图片）。</summary>
    [HttpPut("login-background")]
    public IActionResult PutLoginBackground([FromBody] LoginBgConfig req)
    {
        if (NoKey() is { } r) return r;
        var c = _loginBg.Get();
        if (!string.IsNullOrWhiteSpace(req.Mode)) c.Mode = req.Mode!;
        if (req.RippleColor != null) c.RippleColor = req.RippleColor;
        if (req.Color != null) c.Color = req.Color;
        if (req.Image != null) c.Image = Path.GetFileName(req.Image).Trim();
        _loginBg.Save(c);
        return GetLoginBackground();
    }

    /// <summary>登录背景图片分页列表。</summary>
    [HttpGet("login-background/images")]
    public IActionResult LoginBgImages()
    {
        if (NoKey() is { } r) return r;
        var (total, items) = _loginBg.List();
        return Ok(new
        {
            total,
            items = items.Select(i => new { name = i.Name, sizeKb = (int)(i.SizeBytes / 1024), modifiedAt = i.ModifiedAt }).ToList(),
        });
    }

    /// <summary>上传登录背景图（PNG/JPG/WEBP，≤1MB，魔数+白名单）。</summary>
    [HttpPost("login-background/images")]
    [Consumes("multipart/form-data")]
    public IActionResult LoginBgImageUpload(IFormFile file)
    {
        if (NoKey() is { } r) return r;
        if (file == null || file.Length == 0) return BadRequest(new { error = "文件为空" });
        if (file.Length > 1024 * 1024) return BadRequest(new { error = "登录背景图不能超过 1MB" });
        using var ms = new MemoryStream();
        file.CopyTo(ms);
        var err = _loginBg.Add(file.FileName, ms.ToArray());
        if (err != null) return BadRequest(new { error = err });
        return Ok(new { ok = true, name = Path.GetFileName(file.FileName) });
    }

    /// <summary>删除一张登录背景图。</summary>
    [HttpDelete("login-background/images/{name}")]
    public IActionResult LoginBgImageDelete(string name)
    {
        if (NoKey() is { } r) return r;
        return _loginBg.Delete(name) ? Ok(new { ok = true }) : NotFound();
    }

    /* ---------------- 用户反馈（客户端「设置 → 问题反馈」提交；管理端查看/复制） ---------------- */

    /// <summary>
    /// 读取全部用户反馈（按时间倒序，最新的在前）。每条含时间 / 用户 / 昵称 / 正文 / 客户端快照。
    /// 管理端（环回 + 密钥）展示并支持一键复制到剪贴板。
    /// </summary>
    [HttpGet("feedback")]
    public IActionResult FeedbackList([FromQuery] int limit = 200)
    {
        if (NoKey() is { } r) return r;
        var all = _feedback.List();
        var items = all
            .Take(Math.Clamp(limit, 1, 1000))
            .Select(f => new
            {
                f.Id,
                ts = f.CreatedAt > DateTime.MinValue ? f.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "",
                f.User,
                f.Nickname,
                f.Body,
                f.Client,
            })
            .ToList();
        return Ok(new { count = items.Count, items });
    }

    /// <summary>
    /// 删除一条用户反馈（管理端清理已处理项）。按 id 从当前文件 + 归档里删掉并重写剩余条目。
    /// 环回 + 密钥；id 走路径参数。返回 { ok, error? }。
    /// </summary>
    [HttpDelete("feedback/{id}")]
    public IActionResult FeedbackDelete(string id)
    {
        if (NoKey() is { } r) return r;
        var err = _feedback.Delete(id);
        return Ok(new { ok = err == null, error = err });
    }

    /* ---------------- 蜜罐（研究用：读攻击者被诱饵套走的事件 + 金丝雀；环回 + 密钥） ---------------- */

    /// <summary>蜜罐事件 + 金丝雀（管理端/GUI 查看"谁、何时、被哪种诱饵套走、种了哪些金丝雀"）。</summary>
    [HttpGet("honeypot")]
    public IActionResult Honeypot([FromQuery] int limit = 100)
    {
        if (NoKey() is { } r) return r;
        return Ok(new
        {
            enabled = _honey.Config.Enabled,
            tarpit = _honey.Config.Tarpit,
            events = _honey.Events(limit),
            canaries = _honey.Canaries(),
        });
    }

    /// <summary>蜜罐热开关（enabled / tarpit），即时生效，无需重启。</summary>
    [HttpPut("honeypot")]
    public IActionResult SetHoneypot([FromQuery] bool? enabled, [FromQuery] bool? tarpit)
    {
        if (NoKey() is { } r) return r;
        if (enabled.HasValue) _honey.Config.Enabled = enabled.Value;
        if (tarpit.HasValue) _honey.Config.Tarpit = tarpit.Value;
        _honey.Config.Save();
        _log.Write("auth", new { ts = DateTime.UtcNow, result = "honeypot-config", enabled = _honey.Config.Enabled, tarpit = _honey.Config.Tarpit });
        return Ok(new { ok = true, enabled = _honey.Config.Enabled, tarpit = _honey.Config.Tarpit });
    }


    /* ---------------- 用户管理 ---------------- */

    public record CreateUser(string? Phone, string? Nickname, string? Password);

    /// <summary>创建用户（干净项目的第一个账号从这里来）。密码留空则随机生成并返回一次。</summary>
    [HttpPost("users")]
    public IActionResult CreateUserAction([FromBody] CreateUser req)
    {
        if (NoKey() is { } r) return r;

        var account = (req.Phone ?? "").Trim();
        var nickname = Validate.Clean(req.Nickname, Validate.MaxNickname);
        if (!Validate.IsAccount(account)) return BadRequest(new { error = "账号需为 11 位手机号、3-32 位字母/数字/下划线，或 2-16 位中文" });
        var pwd = string.IsNullOrWhiteSpace(req.Password) ? RandomPassword() : req.Password!.Trim();
        if (PasswordPolicy.Validate(pwd) is { } pwErr) return BadRequest(new { error = pwErr });

        WeChatWeb.Models.User user;
        lock (_store)
        {
            if (_store.Users.Values.Any(u => u.Phone == account)) return Conflict(new { error = "该账号已存在" });
            user = new WeChatWeb.Models.User
            {
                Id = "u-" + Guid.NewGuid().ToString("N")[..12],
                Phone = account,
                Password = PasswordHasher.Hash(pwd),
                Nickname = nickname ?? account,
                WxId = "wx" + Guid.NewGuid().ToString("N")[..8],
            };
            _store.Users[user.Id] = user;
            _store.Contacts[user.Id] = new List<WeChatWeb.Models.Contact>();
        }
        // 立即落盘放到锁外：全库序列化不进全局锁（GUI 重启后用户列表不为空）
        _store.SaveNow();
        _log.Write("auth", new { ts = DateTime.UtcNow, result = "admin-create-user", account });
        return Ok(new { user.Id, user.Phone, user.Nickname, user.WxId, password = pwd, note = "密码只返回这一次" });
    }

    private static string RandomPassword ()
    {
        // BE-SEC-037：初始密码是「交给用户持有的凭证」，必须用密码学随机源（RandomNumberGenerator），
        // 不可用 System.Random（LCG，可预测）。字母表已剔除 0/O/1/I/l 降低歧义。
        const string alphabet = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var chars = new char[10];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        return new string(chars);
    }

    [HttpGet("users")]
    public IActionResult Users()
    {
        if (NoKey() is { } r) return r;
        lock (_store)
        {
            var list = _store.Users.Values.Select(u => new
            {
                u.Id,
                account = u.Phone,
                u.Nickname,
                u.Banned,
                u.IsBot,
                u.CreatedAt,
                contacts = _store.Contacts.TryGetValue(u.Id, out var c) ? c.Count : 0,
            }).OrderByDescending(x => x.CreatedAt).ToList();
            return Ok(new { count = list.Count, users = list });
        }
    }

    public record BanUpdate(bool Banned);

    /// <summary>管理员重置用户密码（忘记密码找回、封禁处置；写入即对登录生效）。</summary>
    [HttpPut("users/{id}/password")]
    public IActionResult ResetPassword(string id, [FromBody] PasswordReset req)
    {
        if (NoKey() is { } r) return r;
        var pwd = (req.Password ?? "").Trim();
        // 与注册/改密同一套密码策略（此前只要 6 位，弱于用户自设口令的标准）
        if (PasswordPolicy.Validate(pwd) is { } pwErr) return BadRequest(new { error = pwErr });
        lock (_store)
        {
            if (!_store.Users.TryGetValue(id, out var u)) return NotFound();
            u.Password = PasswordHasher.Hash(pwd);
            foreach (var kv in _store.RefreshTokens.Where(kv => kv.Value.UserId == id).ToList())
                _store.RefreshTokens.Remove(kv.Key);
        }
        _store.SaveNow();   // 密码重置必须立即落盘，落盘在锁外（重启后不能回滚）
        _log.Write("auth", new { ts = DateTime.UtcNow, result = "admin-reset-password", user = id });
        return Ok(new { ok = true });
    }

    public record PasswordReset(string? Password);

    /// <summary>停用/恢复账号：停用后登录与续期均被拒绝，且**实时连接被强制断开**（WS 通道旧 token 也失效）。</summary>
    [HttpPut("users/{id}/banned")]
    public IActionResult SetBanned(string id, [FromBody] BanUpdate req)
    {
        if (NoKey() is { } r) return r;
        int kicked;
        lock (_store)
        {
            if (!_store.Users.TryGetValue(id, out var u)) return NotFound();
            u.Banned = req.Banned;
            if (req.Banned)
            {
                foreach (var kv in _store.RefreshTokens.Where(kv => kv.Value.UserId == id).ToList())
                    _store.RefreshTokens.Remove(kv.Key);
            }
        }
        // 踢线在锁外做（连接中止是 IO 类操作）：封号即断开其全部实时通道，
        // 自动重连会被 ChatHub.OnConnectedAsync 的封禁检查拒绝
        kicked = req.Banned ? _store.KickConnections(id) : 0;
        _store.SaveNow();   // 封禁状态必须立即落盘，落盘在锁外（重启后不能失效）
        _log.Write("auth", new { ts = DateTime.UtcNow, result = req.Banned ? "admin-ban" : "admin-unban", user = id, kicked });
        return Ok(new { ok = true, banned = req.Banned });
    }

    /// <summary>
    /// 删除用户并级联清理其全部关联数据（联系人 / 私聊会话 / 消息 / 群 / 朋友圈 /
    /// 好友申请 / 收藏 / 刷新令牌 / 在线连接 / 上传文件），立即落盘。写审计日志。
    /// </summary>
    [HttpDelete("users/{id}")]
    public IActionResult DeleteUserAction(string id)
    {
        if (NoKey() is { } r) return r;
        WeChatWeb.Models.UserDeleteSummary summary;
        lock (_store)
        {
            if (!_store.Users.ContainsKey(id)) return NotFound();
            // RemoveUser 内部持锁做级联清理并 MarkDirty（Monitor 可重入，本线程已持有锁）
            summary = _store.RemoveUser(id);
        }
        // 全量落盘在锁外（RemoveUser 只标脏兜底）
        _store.SaveNow();
        _log.Write("auth", new
        {
            ts = DateTime.UtcNow,
            result = "admin-delete-user",
            user = id,
            contacts = summary.RemovedContacts,
            conversations = summary.RemovedConversations,
            groupMemberships = summary.RemovedGroupMemberships,
            groups = summary.RemovedGroups,
            moments = summary.RemovedMoments,
            friendRequests = summary.RemovedFriendRequests,
            favorites = summary.RemovedFavorites,
            refreshTokens = summary.RemovedRefreshTokens,
            files = summary.RemovedFiles,
        });
        return Ok(new
        {
            ok = true,
            user = id,
            removed = new
            {
                contacts = summary.RemovedContacts,
                conversations = summary.RemovedConversations,
                groupMemberships = summary.RemovedGroupMemberships,
                groups = summary.RemovedGroups,
                moments = summary.RemovedMoments,
                friendRequests = summary.RemovedFriendRequests,
                favorites = summary.RemovedFavorites,
                files = summary.RemovedFiles,
            },
        });
    }
}
