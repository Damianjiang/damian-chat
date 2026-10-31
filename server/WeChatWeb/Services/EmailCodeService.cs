using System.Collections.Concurrent;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;

namespace WeChatWeb.Services;

/// <summary>
/// 邮箱验证码服务（注册 / 绑定邮箱用）：
///  6 位数字码，10 分钟有效，验证错 5 次作废（防爆破）；
///  同一邮箱 60 秒内只能发一次，同 IP 每小时最多 10 封（防被当短信轰炸跳板）。
///  验证码只存内存（重启即失效，可接受——重新发送即可），绝不落盘。
/// </summary>
public sealed class EmailCodeService
{
    private sealed record Entry(string Code, DateTime ExpiresAt, int Fails);

    private readonly ConcurrentDictionary<string, Entry> _codes = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastSend = new();
    private readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> _ipWindow = new();
    private readonly SecuritySettings _sec;
    private readonly LogService _log;

    public EmailCodeService (SecuritySettings sec, LogService log)
    {
        _sec = sec;
        _log = log;
    }

    private static string Norm (string email) => (email ?? "").Trim().ToLowerInvariant();

    /// <summary>邮箱格式粗校验（不需要真到达，只挡明显乱填）。</summary>
    private static readonly System.Text.RegularExpressions.Regex EmailPattern =
        new(@"^[^@\s]{1,64}@[^@\s]{1,255}\.[A-Za-z]{2,24}$", System.Text.RegularExpressions.RegexOptions.Compiled);
    public static bool ValidEmail (string? email) => !string.IsNullOrWhiteSpace(email) && email.Length <= 254 && EmailPattern.IsMatch(email);

    /// <summary>发送验证码。返回 null=成功，否则为错误文案。</summary>
    public string? Send (string email, string ip, string purpose)
    {
        if (!_sec.SmtpReady) return "管理员尚未配置邮件服务（SMTP），请联系管理员";
        var e = Norm(email);
        if (!ValidEmail(e)) return "邮箱格式不正确";

        if (_lastSend.TryGetValue(e, out var last) && (DateTime.UtcNow - last).TotalSeconds < 60)
            return "发送太频繁，请 1 分钟后再试";
        var w = _ipWindow.AddOrUpdate(ip,
            _ => (1, DateTime.UtcNow),
            (_, old) => (DateTime.UtcNow - old.WindowStart).TotalHours >= 1 ? (1, DateTime.UtcNow) : (old.Count + 1, old.WindowStart));
        if (w.Count > 10) return "该 IP 发送验证码次数已达上限，请 1 小时后再试";

        // 用加密随机源生成 6 位码（Random 可预测，验证码等同于一次性凭据）
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        _codes[e] = new Entry(code, DateTime.UtcNow.AddMinutes(10), 0);
        _lastSend[e] = DateTime.UtcNow;

        try
        {
            using var client = new SmtpClient(_sec.SmtpHost, _sec.SmtpPort)
            {
                EnableSsl = _sec.SmtpSsl,
                Credentials = new NetworkCredential(_sec.SmtpUser, _sec.SmtpPass),
            };
            var from = string.IsNullOrWhiteSpace(_sec.SmtpFrom) ? _sec.SmtpUser : _sec.SmtpFrom;
            var msg = new MailMessage(from, e, "大面聊天 验证码", $"你的验证码是：{code}\n10 分钟内有效。若非本人操作请忽略本邮件。")
            {
                IsBodyHtml = false,
            };
            client.Send(msg);
            _log.Write("auth", new { ts = DateTime.UtcNow, result = "email-code-sent", purpose, ip });
            return null;
        }
        catch (Exception ex)
        {
            _codes.TryRemove(e, out _);
            _log.Write("auth", new { ts = DateTime.UtcNow, result = "email-code-fail", purpose, error = ex.Message, ip });
            return "验证码发送失败，请稍后重试或联系管理员检查邮件服务配置";
        }
    }

    /// <summary>校验并消费验证码（一次性）。null=通过，否则错误文案。</summary>
    public string? Verify (string email, string code)
    {
        var e = Norm(email);
        if (_codes.TryGetValue(e, out var entry))
        {
            if (entry.ExpiresAt < DateTime.UtcNow) { _codes.TryRemove(e, out _); return "验证码已过期，请重新发送"; }
            if (entry.Code != (code ?? "").Trim())
            {
                var nf = entry with { Fails = entry.Fails + 1 };
                if (nf.Fails >= 5) _codes.TryRemove(e, out _); else _codes[e] = nf;
                return "验证码错误";
            }
            _codes.TryRemove(e, out _);
            return null;
        }
        return "请先获取验证码";
    }

    /// <summary>瞬态保养：清过期码/发送节流记录。返回清理条数。</summary>
    public (int codes, int throttles) Prune ()
    {
        var now = DateTime.UtcNow;
        int n = 0;
        foreach (var kv in _codes)
            if (kv.Value.ExpiresAt < now) { _codes.TryRemove(kv.Key, out _); n++; }
        foreach (var kv in _lastSend)
            if ((now - kv.Value).TotalMinutes > 5) _lastSend.TryRemove(kv.Key, out _);
        foreach (var kv in _ipWindow)
            if ((now - kv.Value.WindowStart).TotalHours >= 1) _ipWindow.TryRemove(kv.Key, out _);
        return (n, 0);
    }
}
