using System.Security.Cryptography;

namespace WeChatWeb.Services;

/// <summary>
/// 图形验证码（防机器人挑战）。5 分钟过期，验证一次性。
/// 出图为服务端光栅化 PNG：字符以「折线笔画 + 随机旋转/抖动」画进像素。
/// 此前用 SVG 时每个字符都是 &lt;text&gt;A&lt;/text&gt; 这样的文本节点，
/// 机器人 base64 解码即得答案（等于没有验证码）；现在只能上 OCR，且 4 位 32 字符
/// 组合 + 一次性消费 + 接口限流使在线穷举不可行。
/// </summary>
public sealed class CaptchaService
{
    private sealed record Entry (string Code, DateTime ExpiresAt);

    private readonly Dictionary<string, Entry> _entries = new();
    private readonly object _lock = new();

    public (string Id, string DataUrl) Generate ()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<char> buf = stackalloc char[4];
        for (var i = 0; i < 4; i++)
            buf[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
        var code = new string(buf);
        var id = Guid.NewGuid().ToString("N");

        lock (_lock)
        {
            CleanupLocked();
            _entries[id] = new Entry(code, DateTime.UtcNow.AddMinutes(5));
        }
        return (id, "data:image/png;base64," + Convert.ToBase64String(Render(code)));
    }

    /// <summary>一次性验证：取出即删除，失败也作废，防止穷举重放。</summary>
    public bool Verify (string? id, string? code)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(code)) return false;
        lock (_lock)
        {
            if (!_entries.TryGetValue(id, out var e)) return false;
            _entries.Remove(id);
            if (e.ExpiresAt < DateTime.UtcNow) return false;
            return string.Equals(e.Code, code.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    private void CleanupLocked ()
    {
        // 无门槛清理：Generate 频率即登录页打开频率，遍历几百项开销可忽略。
        // 此前仅在 500 条以上才清，长期低频登录会积累大量过期项不释放。
        var now = DateTime.UtcNow;
        foreach (var kv in _entries.Where(kv => kv.Value.ExpiresAt < now).ToList())
            _entries.Remove(kv.Key);
    }

    // ---------------- 光栅渲染 ----------------

    private static readonly (byte r, byte g, byte b)[] Palette =
    {
        (0x07, 0xc1, 0x60), (0x19, 0x76, 0xd2), (0xe5, 0x39, 0x35),
        (0xfb, 0x8c, 0x00), (0x8e, 0x24, 0xaa), (0x00, 0x89, 0x7b),
    };

    private static byte[] Render (string code)
    {
        var canvas = new Raster(132, 46);
        canvas.VerticalGradient((0xf2, 0xf3, 0xf5), (0xe4, 0xe7, 0xeb));

        // 干扰线
        for (var i = 0; i < 3; i++)
        {
            var (r, g, b) = Palette[RandomNumberGenerator.GetInt32(Palette.Length)];
            canvas.Line(
                RandomNumberGenerator.GetInt32(132), RandomNumberGenerator.GetInt32(46),
                RandomNumberGenerator.GetInt32(132), RandomNumberGenerator.GetInt32(46),
                1.2, r, g, b, 0.35);
        }

        // 字符：折线笔画字形，随机旋转/缩放/颜色，逐点抖动
        for (var i = 0; i < code.Length; i++)
        {
            var (r, g, b) = Palette[RandomNumberGenerator.GetInt32(Palette.Length)];
            StrokeFont.Draw(
                canvas,
                code[i],
                x: 6 + i * 30,
                y: 8 + RandomNumberGenerator.GetInt32(-3, 4),
                scale: 1.05 + RandomNumberGenerator.GetInt32(-8, 13) / 100.0,
                rotationDeg: RandomNumberGenerator.GetInt32(-22, 23),
                jitter: 1.4,
                r, g, b,
                alpha: 0.92,
                thick: 2.2);
        }

        // 噪点
        canvas.Noise(30, 0xc9, 0xcc, 0xd1, 0.6);
        return canvas.ToPng();
    }
}
