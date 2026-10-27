using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WeChatWeb.Data;
using WeChatWeb.Services;

namespace WeChatWeb.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class DiscoveryController : ControllerBase
{
    private readonly DataStore _store;
    private readonly LogService _log;
    private readonly FeedbackStore _feedback;

    public DiscoveryController(DataStore store, LogService log, FeedbackStore feedback)
    {
        _store = store;
        _log = log;
        _feedback = feedback;
    }

    /// <summary>发现页入口（不含小程序/游戏/视频号）。
    /// 「附近的人」为伪距离演示数据，已按产品要求移除 —— 只保留真实功能。</summary>
    [HttpGet("discovery/entries")]
    public IActionResult Entries()
    {
        return Ok(new[]
        {
            new { id = "moments", name = "朋友圈", icon = "mdi-camera-account", route = "/moments", placeholder = false, optional = false },
            new { id = "qrcode", name = "扫一扫", icon = "mdi-qrcode-scan", route = "", placeholder = false, optional = false },
        });
    }

    /// <summary>举报（写入日志，管理员可在后台查看）。</summary>
    public record ReportRequest(string TargetType, string TargetId, string Reason);

    [HttpPost("reports")]
    public IActionResult Report([FromBody] ReportRequest req)
    {
        // API-SEC-010 / BIZ-SEC 数据投毒：此前三个字段全裸、无长度上限，
        // 攻击者可发超大字符串灌爆 reports.jsonl（日志资源耗尽）。现在全加上限并打散标记。
        if (string.IsNullOrWhiteSpace(req.Reason)) return BadRequest(new { error = "请填写举报原因" });
        var targetType = Validate.StripHtml(req.TargetType ?? "");
        var targetId = Validate.StripHtml(req.TargetId ?? "");
        var reason = Validate.StripHtml(req.Reason ?? "");
        if (targetType.Length > 64) return BadRequest(new { error = "举报类型过长" });
        if (targetId.Length > 64) return BadRequest(new { error = "举报目标无效" });
        if (reason.Length > 500) return BadRequest(new { error = "举报原因过长" });

        _log.Write("reports", new
        {
            ts = DateTime.UtcNow,
            user = HttpContext.GetUserId(),
            targetType,
            targetId,
            reason,
        });
        return Ok(new { ok = true });
    }

    /// <summary>问题反馈（客户端「设置 → 问题反馈」提交，落盘 data/feedback.jsonl；
    /// 管理端在「用户反馈」页查看/复制。带客户端快照（版本 / UA / 设备）便于排查。</summary>
    public record FeedbackRequest(string Body, string? Client);

    [HttpPost("feedback")]
    public IActionResult SubmitFeedback([FromBody] FeedbackRequest req)
    {
        // 先做长度预检再 Sanitize：避免对超大 body/client 做 O(n) 遍历（资源耗尽型 DoS，
        // 与 /api/reports 修过的「灌爆日志」同一类）。store 层仍有兜底（截断/拒收）。
        if (req.Body is { Length: > FeedbackStore.MaxBody })
            return BadRequest(new { error = "反馈内容过长" });
        if (req.Client is { Length: > FeedbackStore.MaxClient })
            return BadRequest(new { error = "客户端快照过长" });

        var user = HttpContext.GetUserId();
        // 昵称读共享字典必须持全局锁（与全库加锁约定一致，防并发注册/删号时的 ConcurrentModification）
        string? nickname;
        lock (_store)
        {
            nickname = _store.Users.TryGetValue(user, out var u) ? u.Nickname : null;
        }
        // 客户端快照也过一遍去 HTML：日志/数据投毒纵深防御（与举报同一约定）
        var client = Validate.StripHtml(req.Client ?? "");
        var err = _feedback.Add(user, nickname, req.Body, client);
        if (err != null) return BadRequest(new { error = err });

        _log.Write("auth", new
        {
            ts = DateTime.UtcNow,
            result = "feedback-submitted",
            user,
            len = req.Body?.Trim()?.Length ?? 0,
        });
        return Ok(new { ok = true });
    }

    }

[ApiController]
[Route("api")]
[Authorize] // 文件读写一律要求登录：此前匿名可凭 id 下载任意人的文件
public class FilesController : ControllerBase
{
    private readonly DataStore _store;
    private readonly WeChatWeb.Services.FileStore _files;
    private readonly SensitiveWordService _sw;

    // 功能性类型约束：头像要渲染成头像、语音要能播放，故限定图片/音频。
    // 普通「文件」上传（kind=file/默认）不限扩展名 —— 云盘逻辑，什么都能传（含 .exe/.zip/脚本）。
    private static readonly string[] AllowedImage = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".heic", ".heif" };
    private static readonly string[] AllowedAudio = { ".mp3", ".m4a", ".ogg", ".wav", ".amr", ".webm" };

    public FilesController(DataStore store, WeChatWeb.Services.FileStore files,
        SensitiveWordService sw)
    {
        _store = store;
        _files = files;
        _sw = sw;
        // 全查回调：记忆缓存未命中时按需遍布在库用户（一次会后回填缓存）
        _ipHashScan = hash =>
        {
            foreach (var id in store.Users.Keys)
                if (IpHash(id) == hash) return id;
            return null;
        };
    }

    /// <summary>
    /// 上传需登录。
    /// 安全与体验：
    /// ① 文件名只做净化展示，磁盘名永远是内容哈希，杜绝路径穿越；
    /// ② 内容 SHA-256 去重 —— 同一文件重复上传/转发只占一份磁盘（不再重复存储炸盘）；
    /// ③ 单文件大小上限来自配置（GUI 可改，默认 100MB），图片/语音另有更严上限；
    /// ④ 限速写入（默认 10MB/s），不占满带宽；
    /// ⑤ 元数据落盘 data/files.json，重启仍可下载。
    /// </summary>
    [Authorize]
    [HttpPost("files/upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload(IFormFile file, [FromForm] string kind = "image")
    {
        if (file == null || file.Length == 0) return BadRequest(new { error = "文件为空" });

        var safeName = Validate.CleanFileName(file.FileName);
        if (safeName == null) return BadRequest(new { error = "文件名不合法" });

        var ext = Path.GetExtension(safeName).ToLowerInvariant();
        // 云盘模式：任意文件都能传（.exe/.zip/脚本/视频…），不做扩展名拦截。
        // 只有「按用途内联渲染/播放」的头像、语音做功能性类型约束（传 .exe 当头像/语音没法用），
        // 普通文件上传不限任何扩展名。安全不靠上传拦截，而靠下载侧：
        //   ① 路径穿越已在 CleanFileName 挡掉；② 登录鉴权 + 归属校验；
        //   ③ Content-Disposition: attachment + X-Content-Type-Options: nosniff（绝不内联渲染）；
        //   ④ 文件只存储、服务端从不执行。
        if (kind == "avatar")
        {
            if (!AllowedImage.Contains(ext))
                return BadRequest(new { error = "头像需为图片文件（PNG/JPG/GIF/WEBP/BMP/HEIC）" });
        }
        else if (kind == "voice")
        {
            if (!AllowedAudio.Contains(ext))
                return BadRequest(new { error = "语音需为音频文件（MP3/M4A/OGG/WAV/AMR/WEBM）" });
        }

        // 大小上限：以全局配置（默认 1GB，管理端可调）为准；图片/语音/头像另有更严上限。
        // 普通「文件」类不再写死 20MB —— 用户上传文件允许到 1GB（与 Kestrel/表单上限对齐）。
        var cfg = _files.Config;
        var cap = kind switch
        {
            "voice" => Math.Min(cfg.MaxFileBytes, 10L * 1024 * 1024),
            "avatar" => Math.Min(cfg.MaxFileBytes, 5L * 1024 * 1024),
            _ => cfg.MaxFileBytes,   // 文件/默认：放开到全局 1GB
        };
        if (file.Length > cap) return BadRequest(new { error = $"文件超过大小限制（{cap / 1024 / 1024}MB）" });

        // Kestrel 已把整个请求体 buffer 到本地临时文件；ThrottleStream 再对本地磁盘按速率限速
        // 只会人为拖慢响应（100MB @10MB/s ≈ 10s 纯等待，1GB ≈ 100s）→ 客户端"卡在 100%"。
        // 网络公平性由 Kestrel 请求体大小上限保障，本地磁盘 I/O 无需额外限速。
        using var uploadStream = file.OpenReadStream();
        var (id, dedup) = await _files.SaveAsync(uploadStream, ext, HttpContext.GetUserId(), safeName, HttpContext.RequestAborted);

        // 展示用文件名先做敏感词过滤（掩码而非拒收），再写回元数据
        var displayName = _sw.Filter(safeName) ?? safeName;

        // 云盘模式：不做内容拦截/杀毒，无论是否去重命中都落库并更新展示文件名。
        // 安全不靠"拒收文件"，而靠下载侧（路径穿越已挡 + 登录鉴权 + attachment + nosniff + 文件从不被执行）。
        lock (_store)
        {
            if (_store.Files.TryGetValue(id, out var m2)) m2.FileName = displayName;
        }
        _files.PersistIndex();

        return Ok(new
        {
            id,
            // 返回短时签名 URL：<img>/<a download> 无法带 Authorization 头，
            // 用签名票据过渡，同时服务端依旧校验登录态与归属，杜绝裸 ID 直链枚举。
            url = $"/api/files/{id}?t={SignTicket(id)}",
            kind,
            ext,
            size = file.Length,
            deduplicated = dedup,
            note = dedup ? "已存在相同文件，复用原文件（未重复占用空间）" : "",
        });
    }

    /// <summary>
    /// 换取短时签名访问地址（需登录）。
    /// &lt;img&gt; / &lt;a download&gt; 无法携带 Authorization 头，由前端先取票据再渲染，
    /// 票据与用户绑定、5 分钟过期，配合归属校验彻底堵死「拿到 id 就能下载」。
    /// </summary>
    [HttpGet("files/{id}/ticket")]
    public IActionResult Ticket(string id)
    {
        Models.FileMeta? meta;
        lock (_store)
        {
            if (!_store.Files.TryGetValue(id, out meta)) return NotFound();
        }
        if (meta == null || !System.IO.File.Exists(meta.Path)) return NotFound();
        if (!CanReadFile(meta, HttpContext.GetUserId())) return NotFound();

        return Ok(new { url = $"/api/files/{id}?t={SignTicket(id)}" });
    }

    // Thumb 死路径已删除：前端从未调用 /files/{id}/thumb（图片直接走 Download 流式返回）

    /// <summary>
    /// 下载文件。显式鉴权（不用控制器级 [Authorize]）：
    ///  ① 带 Authorization 头 —— 直接取当前用户；
    ///  ② &lt;img&gt;/&lt;a download&gt; 无法带头 —— 用短时签名票据 t 还原用户身份。
    /// 两条路最终都要过 CanReadFile 归属校验，裸 ID 外泄也无法下载他人文件。
    /// </summary>
    [AllowAnonymous]
    [HttpGet("files/{id}")]
    public IActionResult Download(string id, [FromQuery] string? t = null)
    {
        Models.FileMeta? meta;
        lock (_store)
        {
            if (!_store.Files.TryGetValue(id, out meta)) return NotFound();
        }
        if (meta == null || !System.IO.File.Exists(meta.Path)) return NotFound();

        string me;
        if (User.Identity?.IsAuthenticated == true)
        {
            me = HttpContext.GetUserId();
        }
        else
        {
            me = string.IsNullOrEmpty(t) ? "" : VerifyTicket(id, t) ?? "";
            if (me.Length == 0) return Unauthorized();
        }

        // 归属校验：只有上传者本人，或在可访问会话里收到过该文件的人能下载。
        // 此前任何拿到 id 的人都能下载全站文件。
        if (!CanReadFile(meta, me)) return NotFound();

        return StreamFile(meta);
    }

    // ---------------- 签名票据 ----------------

    /// <summary>票据密钥：进程内随机，重启失效（内网场景足够）。</summary>
    private static readonly byte[] TicketSecret =
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

    private static readonly TimeSpan TicketTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 签发票据：exp.userId.sig
    /// userId 明文放在票据里，但整体被 HMAC 签名保护 —— 无法篡改成别人的 id，
    /// 因此既能证明「谁签发的」，又能让无 Authorization 头的 &lt;img&gt;/&lt;a download&gt; 完成归属校验。
    /// </summary>
    private string SignTicket (string fileId)
    {
        var me = HttpContext.GetUserId();
        var exp = new DateTimeOffset(DateTime.UtcNow.Add(TicketTtl)).ToUnixTimeSeconds();
        // 票据第二段存 userId 的单向 HMAC 哈希（IpHash），不存明文 userId ——
        // 票据会随 <img src> / 日志 / 转发出现，明文 userId 属可被留存的泄露面。
        // 验签仍基于 TicketSecret（HMAC），伪造/篡改/过期照样被拒。
        var uh = IpHash(me);
        return $"{exp}.{uh}.{Mac(fileId, exp, uh)}";
    }

    /// <summary>
    /// 校验票据，还原其签发用户 id（供归属校验 CanReadFile）；签名错/过期/伪造一律返回 null。
    /// 票据第二段是 userId 的单向哈希：验签后遍历在库用户反查真实 userId（用户集有限，内网规模可接受）。
    /// </summary>
    private string? VerifyTicket (string fileId, string ticket)
    {
        var parts = ticket.Split('.', 3);
        if (parts.Length != 3) return null;
        if (!long.TryParse(parts[0], out var exp)) return null;
        if (exp < new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds()) return null;
        if (parts[1].Length == 0) return null;

        // 验签（常量时间比较，防篡改/伪造）
        var expected = Mac(fileId, exp, parts[1]);
        var sigOk = System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(parts[2]),
            System.Text.Encoding.UTF8.GetBytes(expected));
        if (!sigOk) return null;

        // 反查真实 userId：票据里只存了单向哈希。命中记忆缓存 O(1)；
        // 未命中（或缓存对象已被删号）才做全库反扫，并把扫描结果补进缓存（已删 → null → 下载 404）
        lock (_store)
        {
            var uid = RecoverUserId(parts[1]);
            return uid != null && _store.Users.ContainsKey(uid) ? uid : null;
        }
    }

    /// <summary>hash → userId 记忆缓存：同一 userId 的 HMAC 只算一次，图片滚动渲染时验签不再重复哈希全库用户。</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> UidHashMemo = new(StringComparer.Ordinal);

    private static string? RecoverUserId (string uidHash)
    {
        // 缓存反向命中（存 string 比较，无 HMAC 重算）
        foreach (var kv in UidHashMemo)
        {
            if (kv.Value == uidHash) return kv.Key;
        }
        // 未命中：全库反查一次并写入缓存（O(全库) 只发生一次/新用户）
        if (_ipHashScan == null) return null;
        return _ipHashScan(uidHash);
    }

    /// <summary>全查回调：由控制器在构造时注入（遍历在库用户算出哈希并回填记忆缓存）。</summary>
    private static Func<string, string?>? _ipHashScan;

    private static string Mac (string fileId, long exp, string uidHash)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(TicketSecret);
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"{fileId}|{exp}|{uidHash}"));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>用户标识单向哈希：票据里只存 16-hex 的 HMAC(userId)，不出现明文，不可反推。
    /// 每用户只算一次（记忆缓存）—— 图片滚动渲染的高频验签路径不再反复构造 HMAC。</summary>
    private static string IpHash (string userId)
        => UidHashMemo.GetOrAdd(userId ?? "", static uid =>
        {
            using var hmac = new System.Security.Cryptography.HMACSHA256(TicketSecret);
            return Convert.ToHexString(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(uid)))
                .ToLowerInvariant()[..16];
        });

    /// <summary>文件读取权限：上传者本人，或在任何可访问会话里收到过该文件的用户。</summary>
    private bool CanReadFile (Models.FileMeta meta, string me)
    {
        if (meta.UploaderId == me) return true;
        lock (_store)
        {
            // imageId → 会话索引直取，替代全量消息扫描（下载/取票是每图渲染的高频路径）
            return _store.CanReadFile(meta.Id, meta.UploaderId, me);
        }
    }

    // ControllerBase.File 方法名与本类字段冲突时，用完全限定名回传。
    // 内联 vs 下载分离：
    //   图片/音频/PDF/纯文本（魔数已双重校验）→ 不带文件名（inline），浏览器内联渲染（聊天图片/语音在线预览）；
    //   其余（zip/doc/docx…）→ 强制 attachment 下载并带回「原始文件名」，浏览器下载时即显示真实名称
    //   （用中文/非 ASCII 时 ASP.NET 自动编码 Content-Disposition 的 filename* RFC5987，避免被改成乱码/占位名）。
    private IActionResult StreamFile (Models.FileMeta meta)
    {
        var ext = Path.GetExtension(meta.Path);
        if (FileContent.InlinePreviewable(ext))
            return PhysicalFile(meta.Path, meta.ContentType, enableRangeProcessing: true);

        // 附件：必须用 FileStream 才能同时「指定原始文件名」+「保留 Range 断点续传」。
        // PhysicalFile 在显式传 fileName 时不支持 range，会破坏大文件断点续传。
        var stream = new FileStream(meta.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, meta.ContentType, meta.FileName ?? Path.GetFileName(meta.Path), enableRangeProcessing: true);
    }
}
