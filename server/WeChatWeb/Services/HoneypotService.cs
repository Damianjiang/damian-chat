using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WeChatWeb.Services;

/// <summary>
/// 蜜罐配置（落盘 data/honey/config.json，可热开关）。
/// 蜜罐是「纯诱饵 + 纯留痕」：绝不读写 store.json / files.json，不授予任何真实权限，
/// 真实用户永远不会访问这些路径（它们只出现在攻击者扫描清单里）。
/// </summary>
public sealed class HoneypotConfig
{
    /// <summary>蜜罐总开关（关闭后所有诱饵路径恢复原行为：SPA fallback / 404）。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>tarpit 拖延开关：对触发过蜜罐的 IP 逐次加长响应延迟（让攻击者"陷进泥潭"）。</summary>
    public bool Tarpit { get; set; } = true;

    /// <summary>单 IP 累计拖延上限（秒），封顶防蜜罐被当成 DoS 放大器烧真资源。</summary>
    public int MaxDelaySec { get; set; } = 30;

    /// <summary>伪造响应体 padding 上限（KB）：拖时间但不放大成带宽攻击。</summary>
    public int MaxPadKb { get; set; } = 64;

    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static string FilePath =>
        Path.Combine(Directory.GetCurrentDirectory(), "data", "honey", "config.json");

    public static HoneypotConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var c = JsonSerializer.Deserialize<HoneypotConfig>(File.ReadAllText(FilePath));
                if (c != null) { c.Normalized(); return c; }
            }
        }
        catch { /* 损坏/只读：用默认 */ }
        var def = new HoneypotConfig();
        def.Save();
        return def;
    }

    public void Save()
    {
        Normalized();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Opts));
            SensitiveFile.RestrictToOwner(FilePath);
        }
        catch { /* 只读环境：仅内存生效 */ }
    }

    private void Normalized()
    {
        MaxDelaySec = Math.Clamp(MaxDelaySec, 0, 300);
        MaxPadKb = Math.Clamp(MaxPadKb, 0, 512);
    }
}

/// <summary>
/// 蜜罐（OWASP WebAppDeception + tarpit + canary/honeytoken 经典组合）：
/// ① 诱饵：扫描器/攻击者 100% 会打的"必查路径"（.env、.git、backup、/admin、/actuator…）
///    返回**看似成功、且与本系统真实凭证格式一致**的响应（假登录成功、假 .env、假管理面板），
///    让攻击者以为得手；
/// ② 拖延：tarpit —— 同一 IP 每命中一次逐次加长响应延迟并 padding，消耗攻击者时间/带宽；
/// ③ 留痕：全部事件写 data/honey/events.jsonl（轮转），响应内嵌**金丝雀**（可溯源，
///    注册在 data/honey/canaries.json）。金丝雀取值**刻意做成与真实凭证同款格式**
///    （64-hex 管理密钥 / base64 JWT 密钥 / glpat- Git 令牌 / 真实格式 PBKDF2 哈希 / 会话 token），
///    攻击者看不出"这是假数据"，把"战利品"外泄后精确比对即可反查是谁、何时、被哪种诱饵套走；
/// ④ 隔离：独立目录、独立命名空间（hx- 前缀仅用于内部注册表键，绝不进入外泄的响应体），
///    绝不触碰真实用户数据与权限；真实用户不会访问这些路径，误伤面为零。
/// </summary>
public sealed class HoneypotService
{
    private readonly object _gate = new();
    private readonly string _dir;
    private readonly string _eventsPath;
    private readonly string _canariesPath;
    private readonly LogService _log;
    private readonly HoneypotConfig _config;

    private readonly Dictionary<string, Canary> _canaries = new();
    private bool _canariesLoaded;

    /// <summary>tarpit 状态：ip → 已命中的累计次数（每次命中递增，延迟随之加长）。</summary>
    private readonly ConcurrentDictionary<string, int> _hits = new();

    public HoneypotService(LogService log)
    {
        _log = log;
        _dir = Path.Combine(Directory.GetCurrentDirectory(), "data", "honey");
        _eventsPath = Path.Combine(_dir, "events.jsonl");
        _canariesPath = Path.Combine(_dir, "canaries.json");
        _config = HoneypotConfig.Load();
        try { Directory.CreateDirectory(_dir); } catch { /* 只读环境：蜜罐降级为纯内存 */ }
    }

    public HoneypotConfig Config => _config;

    public sealed record Lure(string Prefix, string Kind);

    /// <summary>
    /// 诱饵路由表（全部是"真实系统不存在、但扫描清单必有"的前缀；大小写不敏感前缀匹配）。
    /// 注意：绝不包含真实路径（/api/admin/* 是真管理 API，不拦截；.git 等是诱饵）。
    /// </summary>
    /// <summary>
    /// 诱饵路由表（全部是"真实系统不存在、但扫描清单必有"的前缀；大小写不敏感前缀匹配）。
    /// 注意：绝不包含真实路径（/api/admin/* 是真管理 API，不拦截；.git 等是诱饵）。
    /// 匹配逻辑见 MatchLure（三层：① 本表前缀 ② 机密文件子串 ③ 扩展名启发式）。
    /// </summary>
    public static readonly Lure[] Lures =
    {
        // 机密文件类（攻击者最想拿的就是这些）
        new(".env", "env"), new(".env.bak", "env"), new(".env.production", "env"),
        new(".env.local", "env"), new(".git/", "git"), new(".git/config", "git"), new(".git/HEAD", "git"),
        new(".DS_Store", "dotfile"), new("config.json", "env"), new("appsettings.json", "env"), new("config/", "env"),
        // 备份 / 数据泄露类
        new("backup", "backup"), new("site.zip", "backup"), new("db.sql", "backup"),
        new("dump.sql", "backup"), new("database", "backup"), new("data/backup", "backup"),
        // 管理台类（假"已登录"）
        new("admin/", "admin"), new("admin", "admin"), new("wp-login.php", "admin"),
        new("login.php", "admin"), new("manager", "admin"), new("console", "admin"), new("test", "admin"),
        // 框架探测类（.NET 服务被 Spring / WordPress / Java 监控面板特征扫描）
        new("actuator", "actuator"), new("xmlrpc.php", "wp"), new("wp-admin", "wp"),
        new("phpmyadmin", "wp"), new("druid", "actuator"),
        // 补漏：常见扫描清单里的其他"必查"入口（本系统真实路由中绝无出现，零误伤）
        new(".svn/", "git"), new(".svn", "git"), new(".hg/", "git"),
        new(".ssh/", "env"), new("id_rsa", "env"), new("id_dsa", "env"),
        new("web.config", "env"), new("config.php", "env"),
        new(".htpasswd", "env"), new("crossdomain.xml", "dotfile"),
        new("cgi-bin/", "dotfile"), new("phpinfo.php", "dotfile"),
        new("server-status", "dotfile"), new("server-info", "dotfile"),
    };

    /// <summary>
    /// 机密文件「子串」匹配表：路径**任意位置**出现即命中（小写）。
    /// ★ 抓嵌套/编码/反斜杠探测（/var/www/.env、//uploads/.git、www/.env、/config/appsettings.json）——
    ///   这些不是前缀命中、旧版会漏；但扫描器常这么打。这些子串在真实路由（/api、/hubs、
    ///   /files、/assets、SPA）里**绝无**出现，零误伤。
    /// </summary>
    private static readonly (string Sub, string Kind)[] SecretSubs =
    {
        (".env", "env"), (".git", "git"), (".svn", "git"), (".hg", "git"),
        (".DS_Store", "dotfile"), ("id_rsa", "env"), ("id_dsa", "env"), (".ssh", "env"),
        (".htpasswd", "env"), ("web.config", "env"), ("config.php", "env"),
        ("appsettings.json", "env"), ("config.json", "env"),
        ("crossdomain.xml", "dotfile"), ("xmlrpc.php", "wp"), ("wp-login.php", "admin"),
        ("phpinfo.php", "dotfile"), ("server-status", "dotfile"), ("server-info", "dotfile"),
        ("phpmyadmin", "wp"), ("actuator", "actuator"), ("druid", "actuator"),
    };

    /// <summary>扩展名启发式：命中 → 归到 backup（伪造"数据库/备份文件"诱饵，诱导下载）。</summary>
    private static readonly string[] BackupExts =
        { ".sql", ".zip", ".tar.gz", ".bak", ".dmp", ".sqlite", ".dat" };

    /// <summary>
    /// 路径是否命中诱饵（三层，堵死"嵌套/编码/反斜杠"绕过）：
    /// ① 前缀匹配（Lures）② 机密文件子串匹配（SecretSubs，抓深层路径）③ 扩展名启发式（BackupExts）。
    /// 未命中 / 蜜罐关闭返回 null。
    /// </summary>
    public Lure? MatchLure(string? path)
    {
        if (!_config.Enabled || string.IsNullOrEmpty(path)) return null;
        var p = NormalizePath(path);
        if (p.Length == 0) return null;

        // ① 前缀匹配（带 / 结尾的必须整段命中，避免 "adminfoo" 误伤 "admin"）
        foreach (var l in Lures)
        {
            var prefix = l.Prefix.ToLowerInvariant();
            if (p == prefix
                || (p.StartsWith(prefix, StringComparison.Ordinal)
                    && (prefix.EndsWith('/') || p.Length > prefix.Length)))
                return l;
        }
        // ② 机密文件子串匹配（抓嵌套/编码/反斜杠探测路径）
        foreach (var (sub, kind) in SecretSubs)
            if (p.Contains(sub, StringComparison.Ordinal))
                return new Lure(sub, kind);
        // ③ 扩展名启发式（伪造"备份/数据库"诱饵）
        foreach (var ext in BackupExts)
            if (p.EndsWith(ext, StringComparison.Ordinal))
                return new Lure(ext, "backup");
        return null;
    }

    /// <summary>
    /// 路径归一化（挡绕过）：小写 + 反斜杠转正斜杠 + 折叠重复斜杠（//）+ 去首尾斜杠。
    /// 目的：让 <c>/.env</c>、<c>/.env/</c>、<c>/.env//</c>、<c>/.env?</c>、<c>\.env</c>、<c>/.env%00</c>
    /// 等各种变体都能稳定命中子串/前缀表（ASP.NET 的 Request.Path 已解码 URL 编码，这里再做斜杠归一）。
    /// </summary>
    private static string NormalizePath(string raw)
    {
        var p = raw.Replace('\\', '/').ToLowerInvariant();
        while (p.Contains("//", StringComparison.Ordinal)) p = p.Replace("//", "/", StringComparison.Ordinal);
        p = p.TrimStart('/');
        return p;
    }

    /* ---------------- 金丝雀（canary / honeytoken） ---------------- */

    public sealed class Canary
    {
        /// <summary>内部注册表键（hx- + 12 hex）。只存在服务端，绝不进入外泄给攻击者的响应体。</summary>
        public string Id { get; set; } = "";
        /// <summary>类型：db-password / jwt-secret / admin-key / session-token / repo-token …</summary>
        public string Type { get; set; } = "";
        /// <summary>
        /// 唯一可溯源值。**刻意做成与真实凭证同款式**（64-hex / base64 / glpat- / PBKDF2 哈希 / 会话 token），
        /// 攻击者从"战利品"里拿到的就是一串看起来完全正常的秘密；外泄后精确比对可反查来源。
        /// </summary>
        public string Value { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        /// <summary>种在哪个诱饵路径（研究用：哪类诱饵最容易被套走）。</summary>
        public string PlantedAt { get; set; } = "";
    }

    /// <summary>
    /// 注册一枚可溯源金丝雀：传入一个"看起来就是真实秘密"的 value，记录到注册表。
    /// 各 Fake* 方法按下方 New* 生成器产出与真实凭证同格式的 value，再调这里登记。
    /// </summary>
    private string RegisterCanary(string type, string plantedAt, string value)
    {
        var c = new Canary
        {
            Id = "hx-" + Hex(RandomNumberGenerator.GetBytes(6)),
            Type = type,
            Value = value,
            CreatedAt = DateTime.UtcNow,
            PlantedAt = plantedAt,
        };
        lock (_gate)
        {
            EnsureCanariesLoaded();
            _canaries[c.Id] = c;
            // 注册表封顶（防无界增长）：超 2000 枚即只保留最新 1000 枚
            if (_canaries.Count > 2000)
            {
                var keep = _canaries.Values
                    .OrderByDescending(x => x.CreatedAt)
                    .Take(1000)
                    .ToDictionary(x => x.Id, x => x);
                _canaries.Clear();
                foreach (var kv in keep) _canaries[kv.Key] = kv.Value;
            }
            PersistCanaries();
        }
        return value;
    }

    public List<Canary> Canaries()
    {
        lock (_gate)
        {
            EnsureCanariesLoaded();
            return _canaries.Values.OrderByDescending(c => c.CreatedAt).Take(500).ToList();
        }
    }

    private void EnsureCanariesLoaded()
    {
        if (_canariesLoaded) return;
        _canariesLoaded = true;
        try
        {
            if (File.Exists(_canariesPath))
            {
                var list = JsonSerializer.Deserialize<List<Canary>>(File.ReadAllText(_canariesPath));
                if (list != null) foreach (var c in list) if (!string.IsNullOrEmpty(c.Id)) _canaries[c.Id] = c;
            }
        }
        catch { /* 损坏：空注册表 */ }
    }

    private void PersistCanaries()
    {
        try
        {
            Directory.CreateDirectory(_dir);
            var raw = JsonSerializer.Serialize(_canaries.Values.ToList());
            var tmp = _canariesPath + ".tmp";
            File.WriteAllText(tmp, raw);
            File.Move(tmp, _canariesPath, overwrite: true);
            SensitiveFile.RestrictToOwner(_canariesPath);
        }
        catch { /* 只读环境：仅内存 */ }
    }

    /* ---------------- tarpit 拖延 ---------------- */

    /// <summary>
    /// 取该 IP 本次命中应施加的响应延迟（毫秒）。首次命中返回 0（让攻击者"快速得手"、上钩更深），
    /// 之后每次 +2~6s、累计封顶 <see cref="HoneypotConfig.MaxDelaySec"/>。tarpit 关闭或无 IP 时恒 0。
    /// </summary>
    public int TarpitDelay(string? ip, bool firstHit)
    {
        if (!_config.Tarpit || string.IsNullOrEmpty(ip)) return 0;
        _hits.TryGetValue(ip, out var cur);
        if (firstHit || cur == 0)
        {
            _hits[ip] = 1;   // 标记已触发，下次开始拖
            if (_hits.Count > 50_000) _hits.Clear();   // 池上限（蜜罐状态可丢，事件已留痕）
            return 0;
        }
        var add = 2 + RandomNumberGenerator.GetInt32(5);   // 2~6s
        cur = Math.Min(cur + add, _config.MaxDelaySec);
        _hits[ip] = cur;
        if (_hits.Count > 50_000) _hits.Clear();
        return cur * 1000;
    }

    /* ---------------- 事件留痕 ---------------- */

    public sealed record HoneyEvent(DateTime Ts, string Ip, string Ua, string Method, string Path, string Lure, int DelayMs, string[] Canaries, string Action);

    /// <summary>追加一条蜜罐事件（JSONL，超限轮转保留 .1）。</summary>
    public void LogEvent(string ip, string ua, string method, string path, string lure, int delayMs, string[] canaries, string action)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            // 轮转：当前文件超 5MB 时移成 .1（覆盖旧 .1）
            if (File.Exists(_eventsPath) && new FileInfo(_eventsPath).Length > 5L * 1024 * 1024)
            {
                var archive = _eventsPath + ".1";
                if (File.Exists(archive)) File.Delete(archive);
                File.Move(_eventsPath, archive);
            }
            var rec = new { ts = DateTime.UtcNow, ip, ua, method, path, lure, delayMs, canaries, action };
            lock (_gate)
            {
                using var fs = new FileStream(_eventsPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                using var sw = new StreamWriter(fs, Encoding.UTF8);
                sw.Write(JsonSerializer.Serialize(rec));
                sw.Write(Environment.NewLine);
            }
            // 服务端结构化日志再记一份（管理端日志页可查；脱敏：只记 IP/路径/诱饵/金丝雀数，不记响应内容）
            _log.Write("auth", new
            {
                ts = DateTime.UtcNow,
                result = "honeypot-" + action,
                ip,
                path,
                lure,
                delayMs,
                canaryCount = canaries.Length,
            });
        }
        catch { /* 留痕失败不阻断（蜜罐本身也不能打挂真服务） */ }
    }

    /// <summary>读取蜜罐事件（按时间倒序，供管理端 /api/admin/honeypot 研究）。</summary>
    public List<HoneyEvent> Events(int limit)
    {
        var items = new List<HoneyEvent>();
        foreach (var p in new[] { _eventsPath, _eventsPath + ".1" })
        {
            if (!File.Exists(p)) continue;
            try
            {
                foreach (var line in File.ReadLines(p))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var o = JsonSerializer.Deserialize<EventRec>(line);
                    if (o == null) continue;
                    var ts = DateTime.TryParse(o.ts, out var d) ? d : DateTime.MinValue;
                    items.Add(new HoneyEvent(ts, o.ip ?? "", o.ua ?? "", o.method ?? "", o.path ?? "",
                        o.lure ?? "", o.delayMs, o.canaries ?? Array.Empty<string>(), o.action ?? ""));
                }
            }
            catch { /* 坏数据忽略：返回能读到的部分 */ }
        }
        return items.OrderByDescending(x => x.Ts).Take(Math.Clamp(limit, 1, 1000)).ToList();
    }

    private sealed class EventRec
    {
        public string? ts { get; set; }
        public string? ip { get; set; }
        public string? ua { get; set; }
        public string? method { get; set; }
        public string? path { get; set; }
        public string? lure { get; set; }
        public int delayMs { get; set; }
        public string[]? canaries { get; set; }
        public string? action { get; set; }
    }

    private static string Hex(byte[] b) => Convert.ToHexString(b).ToLowerInvariant();

    /* ---------------- 可溯源秘密生成器（取值即真实凭证同款式，外泄即可精确比对） ---------------- */

    /// <summary>64-hex（等同真实 data/admin.key 长度）。</summary>
    public string NewAdminKey(string plantedAt) => RegisterCanary("admin-key", plantedAt, Hex(RandomNumberGenerator.GetBytes(32)));

    /// <summary>base64（等同真实 data/jwt.secret：64 字节→88 字符，尾部常带 =）。</summary>
    public string NewJwtSecret(string plantedAt) => RegisterCanary("jwt-secret", plantedAt, Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)));

    /// <summary>24 位混合口令（真实 .env 里数据库/SMTP 口令的观感）。</summary>
    public string NewAppPassword(string plantedAt)
    {
        const string abc = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*";
        var bytes = RandomNumberGenerator.GetBytes(24);
        var s = new char[24];
        for (var i = 0; i < 24; i++) s[i] = abc[bytes[i] % abc.Length];
        return RegisterCanary("db-password", plantedAt, new string(s));
    }

    /// <summary>GitLab 风格个人访问令牌（glpat- + 20 hex，真实 git 凭证观感）。</summary>
    public string NewGitToken(string plantedAt) => RegisterCanary("repo-token", plantedAt, "glpat-" + Hex(RandomNumberGenerator.GetBytes(10)));

    /// <summary>64-hex 会话/接口令牌（攻击者"登录成功后"最想直接复用的东西）。</summary>
    public string NewSessionToken(string plantedAt) => RegisterCanary("session-token", plantedAt, Hex(RandomNumberGenerator.GetBytes(32)));

    /* ---------------- 伪造内容（全部内嵌可溯源秘密；padding 拖时间） ---------------- */

    /// <summary>
    /// 伪造 .env（攻击者最想拿的"机密"）。取值与本系统真实凭证同款：admin.key 式 64-hex、
    /// jwt.secret 式 base64、.NET 连接串风格——攻击者看不出与真货的差别。
    /// </summary>
    public (string Body, string[] Canaries) FakeEnv(string ip)
    {
        var dbPass = NewAppPassword("/.env");
        var jwt = NewJwtSecret("/.env");
        var admin = NewAdminKey("/.env");
        var smtp = NewAppPassword("/.env");
        var redis = NewAppPassword("/.env");
        var connStr = $"Host=10.0.0.8;Port=5432;Database=wxprod;Username=damian_app;Password={dbPass};Pooling=true;Maximum Pool Size=50";
        var sb = new StringBuilder()
            .Append("# damian-chat 生产环境配置（/opt/damian-chat/config/.env）\n")
            .Append("# 最近一次备份 2026-10-18 03:12 · 内部系统，请勿外传\n")
            .Append("APP__Env=Production\n")
            .Append("ASPNETCORE__Urls=http://0.0.0.0:30011\n")
            .Append("WX_DATA_DIR=/opt/damian-chat/data\n")
            .Append("WX_FILES_DIR=/opt/damian-chat/data/files\n")
            .Append("DB__Connection__String=")
            .Append(connStr)
            .Append("\nDB__EnableRetry=true\n")
            .Append("DB__RetryCount=3\n")
            .Append($"JWT__Secret={jwt}\n")
            .Append("JWT__AccessMinutes=120\n")
            .Append("JWT__RefreshDays=7\n")
            .Append($"ADMIN__Key={admin}\n")
            .Append("REDIS__Connection=10.0.0.11:6379,abortConnect=false\n")
            .Append($"REDIS__Password={redis}\n")
            .Append("SMTP__Host=smtp.mxhichina.net\n")
            .Append("SMTP__Port=465\n")
            .Append($"SMTP__Password={smtp}\n")
            .Append("SIGNALR__Hub=chat\n")
            .Append("LOG__Level=Information\n");
        PadEnv(sb);
        return (sb.ToString(), [dbPass, jwt, admin, smtp, redis]);
    }

    /// <summary>
    /// 伪造 .git 内容（让攻击者以为拿到源码仓库入口；配置里种 GitLab 风格的 repo 凭证金丝雀）。
    /// </summary>
    public (string Body, string[] Canaries) FakeGit(string path, string ip)
    {
        var token = NewGitToken(path);
        var body = path.Contains("/config", StringComparison.OrdinalIgnoreCase)
            ? "[core]\n\trepositoryformatversion = 0\n\tfileformat = git\n\tbare = false\n\tlogallrefupdates = true\n[remote \"origin\"]\n\turl = https://oauth2:" + token + "@git.internal-dev.net/team/damian-chat.git\n\tfetch = +refs/heads/*:refs/remotes/origin/*\n[branch \"main\"]\n\tremote = origin\n\tmerge = refs/heads/main\n"
            : "ref: refs/heads/main\n";
        return (body, [token]);
    }

    /// <summary>伪造"数据库备份"（pg_dump 观感；真实表结构 + 真实格式的管理密钥金丝雀；诱导下载）。</summary>
    public (string Body, string[] Canaries) FakeSql(string path)
    {
        var can = NewAdminKey(path);
        var created = RandomCreatedDate();
        var sb = new StringBuilder();
        sb.Append("--\n-- PostgreSQL database dump\n--\n");
        sb.Append("-- Dumped from database version 15.4\n-- Dumped by pg_dump version 15.4\n--\n\n");
        sb.Append("SET statement_timeout = 0;\nSET client_encoding = 'UTF8';\nSET standard_conforming_strings = on;\n");
        sb.Append("\nCREATE TABLE admin (id text NOT NULL, key text, created timestamptz);\n");
        sb.Append($"INSERT INTO admin VALUES ('u-{Uid()}', '{can}', '{created.Replace('T', ' ')}');\n");
        for (var i = 0; i < 40; i++)
            sb.Append($"INSERT INTO admin VALUES ('u-{Uid()}', '{Hex(RandomNumberGenerator.GetBytes(32))}', '{RandomCreatedDate().Replace('T', ' ')}');\n");
        sb.Append("\n-- Dump completed\n");
        return (sb.ToString(), [can]);
    }

    /// <summary>伪造管理端登录成功 JSON（token 为可溯源 64-hex 会话令牌；攻击者拿去调用会持续被套并留痕）。</summary>
    public (string Body, string[] Canaries) FakeAdminOk(string ip)
    {
        var token = NewSessionToken("/admin/login");
        var body = JsonSerializer.Serialize(new
        {
            ok = true,
            token,
            user = new
            {
                id = "u-" + Uid(),
                nickname = "夜风",
                role = "owner",
                phone = MobilePhone(),
                createdAt = RandomCreatedDate(),
            },
            expiresIn = 7200,
            apiBase = "/api/admin",
        });
        return (body, [token]);
    }

    /// <summary>
    /// 伪造管理端用户导出（攻击者"登录后"最想抓的数据）。每行含：
    /// 真实手机号观感、真实 12-hex user id、真实 10-hex wxid、**本系统真实格式的 PBKDF2 密码哈希**、
    /// 可溯源 64-hex 会话令牌——攻击者一眼看不出与真导出有差别。
    /// </summary>
    public (string Body, string[] Canaries) FakeUserExport(string ip)
    {
        var nick = new[] { "夜风", "KillerQ", "小旋风", "Luffy", "Coco酱", "星辰", "夜雨", "Momo", "大胃王", "Kite" };
        var canaries = new List<string>();
        var sb = new StringBuilder();
        sb.Append("[\n");
        for (var i = 0; i < 8; i++)
        {
            var phone = MobilePhone();
            var hash = RealisticPbkdf2Hash();                 // PBKDF2$100000$<b64salt>$<b64hash>（与本系统完全同款）
            var tok = NewSessionToken("/admin/users");          // 每用户一枚可溯源会话令牌
            canaries.Add(tok);
            sb.Append("  {\"id\":\"u-").Append(Uid())
              .Append("\",\"phone\":\"").Append(phone)
              .Append("\",\"nickname\":\"").Append(nick[i])
              .Append("\",\"wxid\":\"wx").Append(Hex(RandomNumberGenerator.GetBytes(5)))
              .Append("\",\"banned\":false")
              .Append(",\"token\":\"").Append(tok)
              .Append("\",\"passwordHash\":\"").Append(hash)
              .Append("\",\"lastLogin\":\"").Append(RandomCreatedDate().Replace(' ', 'T'))
              .Append("\"},\n");
        }
        sb.Append("]");
        return (sb.ToString(), canaries.ToArray());
    }

    /// <summary>伪造 Spring actuator（.NET 服务被 Spring 扫描器打）：返回假健康/环境，并内嵌可溯源秘密。</summary>
    public (string Body, string[] Canaries) FakeActuator(string path)
    {
        if (path.Contains("env", StringComparison.OrdinalIgnoreCase))
        {
            var jwt = NewJwtSecret("/actuator/env");
            var admin = NewAdminKey("/actuator/env");
            var body = JsonSerializer.Serialize(new
            {
                configProps = new
                {
                    db = new { server = "10.0.0.8:5432", database = "wxprod" },
                    jwt_secret = jwt,
                    admin_key = admin,
                    log_level = "Debug",
                },
            });
            return (body, [jwt, admin]);
        }
        if (path.Contains("health", StringComparison.OrdinalIgnoreCase))
        {
            var dbcan = NewAppPassword("/actuator/health");
            var body = "{\"status\":\"UP\",\"name\":\"damian-chat\",\"version\":\"2.4.1\",\"components\":" +
                       "{\"db\":{\"status\":\"UP\",\"details\":{\"database\":\"wxprod\",\"up\":true,\"password\":\"" + dbcan + "\"}}," +
                       "\"redis\":{\"status\":\"UP\",\"details\":{\"connected\":true}}," +
                       "\"diskSpace\":{\"status\":\"UP\",\"details\":{\"total\":\"512GB\",\"free\":\"203GB\"}}}}";
            return (body, [dbcan]);
        }
        // 根 /actuator：假端点索引
        var body2 = "{\"_links\":{\"self\":{\"href\":\"/actuator\"},\"health\":{\"href\":\"/actuator/health\"},\"env\":{\"href\":\"/actuator/env\"},\"info\":{\"href\":\"/actuator/info\"}}}";
        return (body2, Array.Empty<string>());
    }

    /// <summary>伪造泛管理登录页（HTML）；攻击者 POST 后会拿到"登录成功"（见 FakeAdminOk）。</summary>
    public string FakeAdminLoginPage(string title)
    {
        var t = string.IsNullOrWhiteSpace(title) ? "系统" : title;
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html lang=\"zh-CN\"><head><meta charset=\"utf-8\" />");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        sb.Append("<title>").Append(t).Append(" - 管理控制台</title>\n<style>\n");
        sb.Append("body{font-family:system-ui,-apple-system,'Microsoft YaHei',sans-serif;background:#f5f6fa;color:#181818;margin:0}");
        sb.Append(".box{max-width:360px;margin:90px auto;background:#fff;border-radius:10px;padding:28px;box-shadow:0 2px 14px rgba(0,0,0,.08)}");
        sb.Append("h1{font-size:17px;margin:0 0 16px}label{display:block;font-size:13px;color:#666;margin:10px 0 4px}");
        sb.Append("input{width:100%;box-sizing:border-box;padding:9px;border:1px solid #ddd;border-radius:6px;font-size:14px}");
        sb.Append("input[type=checkbox]{width:auto}");
        sb.Append(".row{display:flex;justify-content:space-between;align-items:center;font-size:12px;color:#666}");
        sb.Append("a{color:#3371f5;text-decoration:none}");
        sb.Append("button{width:100%;margin-top:16px;padding:10px;background:#3371f5;color:#fff;border:0;border-radius:6px;font-size:14px;cursor:pointer}");
        sb.Append(".hint{font-size:11px;color:#999;margin-top:14px;text-align:center}");
        sb.Append("</style></head>\n<body><div class=\"box\">\n");
        sb.Append("<h1>").Append(t).Append(" 管理控制台</h1>\n");
        sb.Append("<form method=\"post\" action=\"/").Append(t).Append("/login\">");
        sb.Append("<label>管理员账号</label><input name=\"username\" autocomplete=\"username\" placeholder=\"请输入账号\" />");
        sb.Append("<label>密码</label><input name=\"password\" type=\"password\" autocomplete=\"current-password\" placeholder=\"请输入密码\" />");
        sb.Append("<div class=\"row\" style=\"margin:8px 0 0\"><label><input type=\"checkbox\" name=\"remember\" /> 记住登录</label>");
        sb.Append("<a href=\"/").Append(t).Append("/forgot\">忘记密码？</a></div>");
        sb.Append("<input type=\"hidden\" name=\"csrf\" value=\"")
          .Append(Hex(RandomNumberGenerator.GetBytes(16))).Append("\" />");
        sb.Append("<button type=\"submit\">登 录</button></form>\n");
        sb.Append("<div class=\"hint\">v2.4.1 · 内部系统 · 请勿外传 · © 2024 大面科技</div>\n</div></body></html>");
        return sb.ToString();
    }

    /// <summary>
    /// padding：按配置上限追加"看起来就是真实部署"的额外配置行，拖慢扫描器解析。
    /// 不放大成带宽攻击（不超过 MaxPadKb），且**不使用任何暴露"这是填充"的字样**（无 pad/hex/占位词）。
    /// </summary>
    private void PadEnv(StringBuilder sb)
    {
        var cap = _config.MaxPadKb * 1024;
        var keys = new[] { "FEATURE", "OPT", "POOL", "RETRY", "CACHE", "LIMIT", "TIMEOUT", "MAXCONN" };
        var i = 0;
        while (sb.Length < cap && i < 5000)
        {
            var k = keys[i % keys.Length];
            i++;
            var r = RandomNumberGenerator.GetInt32(4);
            var val = r == 0 ? "true" : (r == 1 ? "false" : RandomNumberGenerator.GetInt32(1000).ToString());
            sb.Append(k).Append('_').Append(RandomNumberGenerator.GetInt32(999)).Append('_').Append(i)
              .Append('=').Append(val).Append('\n');
        }
    }

    /* ---------------- 假数据辅助生成器（真实观感、无真实 PII、逐值可溯源） ---------------- */

    /// <summary>12-hex user id（等同本系统真实 u-xxxx 观感）。</summary>
    private static string Uid() => Hex(RandomNumberGenerator.GetBytes(6));

    /// <summary>11 位大陆手机号观感。</summary>
    private static string MobilePhone()
    {
        var prefixes = new[] { 13, 14, 15, 16, 17, 18, 19 };
        var p = prefixes[RandomNumberGenerator.GetInt32(prefixes.Length)];
        return p.ToString() + RandomNumberGenerator.GetInt32(100_000_000).ToString("D9");
    }

    /// <summary>
    /// 与本系统 <see cref="PasswordHasher"/> 完全同款的 PBKDF2 哈希串（不做真 10 万迭代 KDF，
    /// 避免高频命中时的 CPU 放大；仅按真实格式生成 salt/hash 字段，观感零差别）。
    /// </summary>
    private static string RealisticPbkdf2Hash() =>
        "PBKDF2$100000$" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)) + "$" +
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>2026 年内的随机 ISO 时刻（带 7 位小数，等同本系统 CreatedAt 观感；避免"2024-01-01"式圆值）。</summary>
    private static string RandomCreatedDate()
    {
        var baseDate = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)
                       .AddDays(RandomNumberGenerator.GetInt32(75));
        baseDate = baseDate.AddHours(RandomNumberGenerator.GetInt32(24))
                       .AddMinutes(RandomNumberGenerator.GetInt32(60))
                       .AddSeconds(RandomNumberGenerator.GetInt32(60));
        var frac = RandomNumberGenerator.GetInt32(1_000_000);
        return baseDate.AddMilliseconds(frac / 1000.0).ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");
    }
}
