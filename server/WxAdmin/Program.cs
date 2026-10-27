using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using WxAdmin;
using WxAdmin.Shared;

var root = Paths.ServerDir;
var cfg = WxConfig.Load();

Console.WriteLine("大面聊天 · 服务管理控制台");
Console.WriteLine("数据目录：" + root);
Console.WriteLine();

while (true)
{
    PrintMenu();
    var choice = Console.ReadLine()?.Trim();
    switch (choice)
    {
        case "1":
            StartServer(cfg);
            break;
        case "2":
            StopServer();
            break;
        case "3":
            StopServer();
            Console.WriteLine("按回车继续重启…");
            Console.ReadLine();
            StartServer(cfg);
            break;
        case "4":
            ChangePort(cfg);
            break;
        case "5":
            ShowLogs("auth");
            break;
        case "6":
            ShowLogs("requests");
            break;
        case "7":
            ShowOnline(cfg);
            break;
        case "8":
            ShowStatus(cfg);
            break;
        case "9":
            SecurityMenu(cfg);
            break;
        case "10":
            UsersMenu(cfg);
            break;
        case "0":
            Console.WriteLine("已退出。");
            return;
        default:
            Console.WriteLine("无效选项，请输入菜单编号。");
            break;
    }
    Console.WriteLine();
    Console.WriteLine(new string('─', 46));
}

static void PrintMenu()
{
    Console.WriteLine("┌───────────────────────────────────────────┐");
    Console.WriteLine("  [1] 启动服务        [7] 查看在线用户      ");
    Console.WriteLine("  [2] 停止服务        [8] 服务状态          ");
    Console.WriteLine("  [3] 重启服务        [9] 安全设置          ");
    Console.WriteLine("  [4] 修改端口        [10] 用户管理         ");
    Console.WriteLine("  [5] 查看登录日志    [0] 退出              ");
    Console.WriteLine("  [6] 查看请求日志                          ");
    Console.WriteLine("└───────────────────────────────────────────┘");
    Console.Write("请选择：");
}

static void StartServer(WxConfig cfg)
{
    var dll = Paths.WebDll;
    if (!File.Exists(dll))
    {
        Console.WriteLine("未找到已编译的服务，先执行 dotnet build …");
        var build = Process.Start(new ProcessStartInfo("dotnet", $"build {Paths.WebProject} -v q") { RedirectStandardOutput = true })!;
        build.WaitForExit();
        if (build.ExitCode != 0 || !File.Exists(dll))
        {
            Console.WriteLine("编译失败，无法启动。");
            return;
        }
    }
    if (IsPortInUse(cfg.Port))
    {
        Console.WriteLine($"端口 {cfg.Port} 被占用，请停止占用方或先修改端口。");
        return;
    }
    var cert = Path.Combine(Paths.WebProject, "Data", "certs", "selfsigned.pfx");
    var tls = File.Exists(cert);
    var urlsArg = tls ? $"http://127.0.0.1:{cfg.Port + 1}" : $"http://0.0.0.0:{cfg.Port}";
    Console.WriteLine(tls
        ? $"启动服务 https://0.0.0.0:{cfg.Port} + https://0.0.0.0:443（管理环回 http://127.0.0.1:{cfg.Port + 1}）"
        : $"启动服务 http://0.0.0.0:{cfg.Port}");
    var psi = new ProcessStartInfo("dotnet", $"exec \"{dll}\" --urls {urlsArg}")
    {
        WorkingDirectory = Paths.WebProject,
        UseShellExecute = false,
        CreateNoWindow = true,
    };
    // DEP-SEC HTTPS：证书存在即注入；TLS 端口被占由服务端逐端口跳过
    if (tls)
    {
        psi.EnvironmentVariables["WX_HTTPS_CERT"] = cert;
        psi.EnvironmentVariables["WX_HTTPS_CERT_PASSWORD"] = "wxchat2026";
        psi.EnvironmentVariables["WX_HTTPS_PORT"] = cfg.Port == 443 ? "443" : $"{cfg.Port},443";
    }
    var proc = Process.Start(psi)!;
    File.WriteAllText(Paths.PidFile, proc.Id.ToString());
    Thread.Sleep(1200);
    if (proc.HasExited)
    {
        Console.WriteLine("服务启动失败，请查看 server/WeChatWeb 输出。");
        return;
    }
    Console.WriteLine($"服务已启动：{(tls ? "https" : "http")}://0.0.0.0:{cfg.Port}（PID {proc.Id}）");
}

static void StopServer()
{
    if (!File.Exists(Paths.PidFile))
    {
        Console.WriteLine("没有运行中的服务（未找到 PID 文件）。");
        return;
    }
    if (!int.TryParse(File.ReadAllText(Paths.PidFile).Trim(), out var pid))
    {
        File.Delete(Paths.PidFile);
        Console.WriteLine("PID 文件损坏，已清除。");
        return;
    }
    try
    {
        using var p = Process.GetProcessById(pid);
        p.Kill(entireProcessTree: true);
        Console.WriteLine($"服务已停止（PID {pid}）。");
    }
    catch (ArgumentException)
    {
        Console.WriteLine($"服务未在运行（PID {pid} 不存在）。");
    }
    File.Delete(Paths.PidFile);
}

static void ChangePort(WxConfig cfg)
{
    Console.WriteLine($"当前端口：{cfg.Port}");
    Console.Write("输入新端口（1-65535）：");
    if (!int.TryParse(Console.ReadLine(), out var port) || port is < 1 or > 65535)
    {
        Console.WriteLine("端口不合法，未修改。");
        return;
    }
    if (IsPortInUse(port))
    {
        Console.WriteLine($"端口 {port} 被占用，未修改。");
        return;
    }
    cfg.Port = port;
    WxConfig.Save(cfg);
    Console.WriteLine($"已保存端口 {port}。重启服务后生效。");
}

static void ShowLogs(string kind)
{
    var path = Path.Combine(Paths.LogDir, kind + ".jsonl");
    if (!File.Exists(path))
    {
        Console.WriteLine($"{kind} 日志文件还不存在（服务产生日志后可查看）。");
        return;
    }
    Console.Write("显示条数（默认 30）：");
    int n = int.TryParse(Console.ReadLine(), out var x) ? x : 30;
    var lines = File.ReadLines(path).TakeLast(Math.Max(1, Math.Min(n, 500))).ToList();
    Console.WriteLine($"── 最近 {lines.Count} 条 ──");
    foreach (var raw in lines)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var o = doc.RootElement;
            var ts = o.TryGetProperty("ts", out var t) ? DateTime.Parse(t.GetString()!).ToLocalTime().ToString("MM-dd HH:mm:ss") : "";
            var parts = new List<string>();
            foreach (var p in o.EnumerateObject())
            {
                if (p.Name == "ts") continue;
                var val = p.Value.GetString() ?? p.Value.GetRawText();
                if (val.Length > 40) val = val[..37] + "...";
                parts.Add($"{p.Name}={val}");
            }
            Console.WriteLine($"[{ts}]  {string.Join("  ", parts)}");
        }
        catch
        {
            Console.WriteLine(raw);
        }
    }
}

static void ShowOnline(WxConfig cfg)
{
    try
    {
        using var http = new HttpClient();
        using var req = Build(cfg, HttpMethod.Get, "online");
        if (req == null) { Console.WriteLine("未登录管理接口。"); return; }
        var resp = http.SendAsync(req).Result.Content.ReadAsStringAsync().Result;
        using var doc = JsonDocument.Parse(resp);
        var count = doc.RootElement.GetProperty("count").GetInt32();
        Console.WriteLine($"在线用户：{count} 人");
        foreach (var u in doc.RootElement.GetProperty("users").EnumerateArray())
        {
            var name = u.GetProperty("nickname").GetString();
            var last = DateTime.Parse(u.GetProperty("lastActive").GetString()!).ToLocalTime();
            Console.WriteLine($"  · {name}（最后活跃 {last:HH:mm:ss}）");
        }
    }
    catch
    {
        Console.WriteLine("无法读取在线用户（服务未启动或连接失败）。");
    }
}

static void ShowStatus(WxConfig cfg)
{
    var running = File.Exists(Paths.PidFile) && IsProcessAlive();
    Console.WriteLine($"配置端口：{cfg.Port}");
    Console.WriteLine(running ? "服务：运行中" : "服务：未运行");
    if (IsPortInUse(cfg.Port))
        Console.WriteLine($"端口 {cfg.Port}：已监听");
    else
        Console.WriteLine($"端口 {cfg.Port}：空闲");
    Console.WriteLine($"服务地址：http://localhost:{cfg.Port}（API + SignalR /hubs/chat）");
    Console.WriteLine($"Swagger：http://localhost:{cfg.Port}/swagger");
}

/* ================= 安全设置 ================= */

static string SecurityFilePath() => Path.Combine(Paths.WebProject, "security.json");

static SecurityCfg LoadSecurity(WxConfig cfg)
{
    // 服务运行中：从管理 API 读（权威）；否则读 security.json
    if (IsPortInUse(cfg.Port))
    {
        try
        {
            using var http = new HttpClient();
            using var req = Build(cfg, HttpMethod.Get, "security");
            if (req == null) throw new InvalidOperationException("未登录管理接口");
            var json = http.SendAsync(req).Result.Content.ReadAsStringAsync().Result;
            var s = JsonSerializer.Deserialize<SecurityCfg>(json);
            if (s != null) return s;
        }
        catch { /* 落回文件 */ }
    }
    if (File.Exists(SecurityFilePath()))
    {
        try
        {
            var s = JsonSerializer.Deserialize<SecurityCfg>(File.ReadAllText(SecurityFilePath()));
            if (s != null) return s;
        }
        catch { }
    }
    return new SecurityCfg();
}

static void SaveSecurity(WxConfig cfg, SecurityCfg s)
{
    var payload = JsonSerializer.Serialize(s);
    if (IsPortInUse(cfg.Port))
    {
        try
        {
            using var http = new HttpClient();
            using var req = Build(cfg, HttpMethod.Put, "security", payload);
            if (req == null) throw new InvalidOperationException("未登录管理接口");
            var resp = http.SendAsync(req).Result;
            if (resp.IsSuccessStatusCode)
            {
                Console.WriteLine("已通过管理 API 保存并即时生效。");
                return;
            }
            Console.WriteLine($"管理 API 保存失败（{(int)resp.StatusCode}），改为写入文件。");
        }
        catch { Console.WriteLine("服务不可达，改为写入文件（重启后生效）。"); }
    }
    File.WriteAllText(SecurityFilePath(), JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"已写入 {SecurityFilePath()}");
}

static void SecurityMenu(WxConfig cfg)
{
    while (true)
    {
        var s = LoadSecurity(cfg);
        Console.WriteLine();
        Console.WriteLine("── 安全设置 ─────────────────────────────");
        Console.WriteLine($"  注册密码：{(string.IsNullOrEmpty(s.RegisterKey) ? "（空 = 注册已关闭）" : s.RegisterKey)}");
        Console.WriteLine($"  图形验证码：{(s.EnableCaptcha ? "开" : "关")}   登录限流：{s.AuthPerMinute} 次/分/IP");
        Console.WriteLine($"  全站 API 限流：{s.ApiPerMinute} 次/分/IP   登录失败锁定：{s.MaxLoginAttempts} 次 / {s.LockoutMinutes} 分钟");
        Console.WriteLine("─────────────────────────────────────────");
        Console.WriteLine("  [1] 修改注册密码   [4] 锁定时长(分钟)");
        Console.WriteLine("  [2] 验证码 开/关   [5] 登录限流次数");
        Console.WriteLine("  [3] 锁定次数       [6] 全站API限流");
        Console.WriteLine("  [0] 返回");
        Console.Write("请选择：");
        var c = Console.ReadLine()?.Trim();
        switch (c)
        {
            case "1":
                Console.Write("输入新注册密码（至少 8 位，留空则关闭注册）：");
                var regPwd = Console.ReadLine()?.Trim() ?? "";
                if (regPwd.Length > 0 && regPwd.Length < 8)
                {
                    Console.WriteLine("  注册密码至少 8 位（已放弃修改）。");
                }
                else
                {
                    s.RegisterKey = regPwd;
                    SaveSecurity(cfg, s);
                    Console.WriteLine(regPwd.Length == 0 ? "  已关闭注册。" : "  注册密码已更新。");
                }
                break;
            case "2":
                s.EnableCaptcha = !s.EnableCaptcha;
                SaveSecurity(cfg, s);
                break;
            case "3":
                Console.Write("登录失败几次后锁定（1-999）：");
                if (int.TryParse(Console.ReadLine(), out var n) && n is > 0 and < 1000) { s.MaxLoginAttempts = n; SaveSecurity(cfg, s); }
                else Console.WriteLine("输入不合法。");
                break;
            case "4":
                Console.Write("锁定多少分钟（1-999）：");
                if (int.TryParse(Console.ReadLine(), out var m) && m is > 0 and < 1000) { s.LockoutMinutes = m; SaveSecurity(cfg, s); }
                else Console.WriteLine("输入不合法。");
                break;
            case "5":
                Console.Write("每分钟允许的登录认证次数（5-9999）：");
                if (int.TryParse(Console.ReadLine(), out var r) && r is > 4 and < 10000) { s.AuthPerMinute = r; SaveSecurity(cfg, s); }
                else Console.WriteLine("输入不合法。");
                break;
            case "6":
                Console.Write("每分钟允许的全站 API 请求次数（10-999999）：");
                if (int.TryParse(Console.ReadLine(), out var ap) && ap is >= 10 and < 1000000) { s.ApiPerMinute = ap; SaveSecurity(cfg, s); }
                else Console.WriteLine("输入不合法。");
                break;
            case "0":
                return;
            default:
                Console.WriteLine("无效选项。");
                break;
        }
    }
}

/* ================= 用户管理 ================= */

static void UsersMenu(WxConfig cfg)
{
    if (!IsPortInUse(cfg.Port))
    {
        Console.WriteLine("用户在内存中，需先启动服务（菜单 1）再管理。");
        return;
    }
    using var http = new HttpClient();

    while (true)
    {
        try
        {
            using var listReq = Build(cfg, HttpMethod.Get, "users");
            if (listReq == null) throw new InvalidOperationException("未登录管理接口");
            var json = http.SendAsync(listReq).Result.Content.ReadAsStringAsync().Result;
            using var doc = JsonDocument.Parse(json);
            var users = doc.RootElement.GetProperty("users").EnumerateArray().ToList();
            Console.WriteLine();
            Console.WriteLine($"── 用户列表（{users.Count}）──────────────────");
            for (var i = 0; i < users.Count; i++)
            {
                var u = users[i];
                var account = u.GetProperty("account").GetString();
                var nick = u.GetProperty("nickname").GetString();
                var banned = u.GetProperty("banned").GetBoolean();
                var bot = u.GetProperty("isBot").GetBoolean();
                var flag = banned ? "已停用" : bot ? "机器人" : "正常";
                Console.WriteLine($"  [{i + 1,2}] {account,-16} {nick,-10} {flag}");
            }
            Console.WriteLine("─────────────────────────────────────────");
            Console.WriteLine("  输入序号 = 停用/恢复，[d] 序号 = 删除，[n] 新建用户，[r] 刷新，[0] 返回");
            Console.Write("请选择：");
            var c = Console.ReadLine()?.Trim();
            if (c == "0") return;
            if (c is "n" or "N")
            {
                CreateUser(cfg);
                continue;
            }
            if (c is "r" or null or "") continue;

            var delMatch = System.Text.RegularExpressions.Regex.Match(c, @"^[dD]\s*(\d+)$");
            if (delMatch.Success && int.TryParse(delMatch.Groups[1].Value, out var dIdx) && dIdx >= 1 && dIdx <= users.Count)
            {
                var delTarget = users[dIdx - 1];
                var delId = delTarget.GetProperty("id").GetString();
                var delNick = delTarget.GetProperty("nickname").GetString();
                Console.WriteLine($"  将删除用户 {delTarget.GetProperty("account").GetString()}（{delNick}），级联清理其联系人/会话/群/朋友圈/文件。");
                Console.Write("  输入「删除」确认：");
                if (Console.ReadLine()?.Trim() == "删除")
                {
                    using var delReq = Build(cfg, HttpMethod.Delete, $"users/{delId}");
                    if (delReq == null) return;
                    var delResp = http.SendAsync(delReq).Result;
                    if (delResp.IsSuccessStatusCode)
                        Console.WriteLine("  已删除用户并级联清理相关数据。");
                    else
                        Console.WriteLine($"  删除失败：{(int)delResp.StatusCode}");
                }
                else
                    Console.WriteLine("  已取消删除。");
                continue;
            }

            if (!int.TryParse(c, out var idx) || idx < 1 || idx > users.Count)
            {
                Console.WriteLine("无效输入。");
                continue;
            }
            var target = users[idx - 1];
            var id = target.GetProperty("id").GetString();
            var bannedNow = target.GetProperty("banned").GetBoolean();
            var payload = JsonSerializer.Serialize(new { banned = !bannedNow });
            using var banReq = Build(cfg, HttpMethod.Put, $"users/{id}/banned", payload);
            if (banReq == null) throw new InvalidOperationException("未登录管理接口");
            var resp = http.SendAsync(banReq).Result;
            Console.WriteLine(resp.IsSuccessStatusCode
                ? (!bannedNow ? "已停用该账号（登录与续期即刻拒绝）。" : "已恢复该账号。")
                : $"操作失败：{(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"请求失败：{ex.Message}");
            return;
        }
    }
}

/* ================= 管理接口请求构造 ================= */

/// <summary>
/// 构造管理接口请求：本机程序直接用服务端生成的密钥（data/admin.key），不需要输入账号密码。
/// 服务未启动过（还没有密钥文件）时返回 null。
/// </summary>
static HttpRequestMessage? Build(WxConfig cfg, HttpMethod method, string path, string? json = null)
{
    var key = AdminKeyFile.Read();
    if (string.IsNullOrEmpty(key))
    {
        Console.WriteLine("  尚未找到管理密钥：请先用菜单 1 启动一次服务（密钥生成在 WeChatWeb/data/admin.key）。");
        return null;
    }
    var req = new HttpRequestMessage(method, $"http://localhost:{cfg.Port}/api/admin/{path}");
    req.Headers.Add("X-Admin-Key", key);
    if (json != null) req.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
    return req;
}

/// <summary>新建用户：干净项目没有种子账号，第一个账号从这里建。密码留空 = 随机生成。</summary>
static void CreateUser(WxConfig cfg)
{
    Console.Write("  账号（11 位手机号、3-32 位字母/数字/下划线或 2-16 位中文）：");
    var phone = Console.ReadLine()?.Trim() ?? "";
    Console.Write("  昵称（可留空）：");
    var nick = Console.ReadLine()?.Trim() ?? "";
    Console.Write("  密码（8-32 位，留空则随机生成）：");
    var pwd = Console.ReadLine()?.Trim() ?? "";

    var payload = JsonSerializer.Serialize(new { phone, nickname = nick, password = pwd });
    using var req = Build(cfg, HttpMethod.Post, "users", payload);
    if (req == null) return;
    using var http = new HttpClient();
    var resp = http.SendAsync(req).Result;
    var body = resp.Content.ReadAsStringAsync().Result;
    if (!resp.IsSuccessStatusCode)
    {
        Console.WriteLine("  创建失败：" + body);
        return;
    }
    using var doc = JsonDocument.Parse(body);
    Console.WriteLine();
    Console.WriteLine("  用户已创建（密码只显示这一次）：");
    Console.WriteLine($"    账号：{doc.RootElement.GetProperty("phone").GetString()}");
    Console.WriteLine($"    密码：{doc.RootElement.GetProperty("password").GetString()}");
    Console.WriteLine();
}

static bool IsProcessAlive()
{
    if (!File.Exists(Paths.PidFile)) return false;
    if (!int.TryParse(File.ReadAllText(Paths.PidFile).Trim(), out var pid)) return false;
    try
    {
        using var p = Process.GetProcessById(pid);
        return true;
    }
    catch (ArgumentException)
    {
        return false;
    }
}

static bool IsPortInUse(int port)
{
    // 用系统真实监听表判断；「127.0.0.1 试绑定」对 0.0.0.0 监听有假阴性（Windows 允许并存绑定）。
    foreach (var ep in System.Net.NetworkInformation.IPGlobalProperties
                 .GetIPGlobalProperties().GetActiveTcpListeners())
    {
        if (ep.Port == port) return true;
    }
    return false;
}

sealed class SecurityCfg
{
    public string RegisterKey { get; set; } = "";   // 空 = 关闭注册，管理员在控制台设置注册密码
    public bool EnableCaptcha { get; set; } = true;
    public int MaxLoginAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 10;
    public int AuthPerMinute { get; set; } = 20;
    public int ApiPerMinute { get; set; } = 300;
}
