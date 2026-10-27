using System.Text.Json;

namespace WeChatWeb.Services;

/// <summary>
/// 登录页背景配置（data/login-bg.json）：
/// ① 三种模式：波纹（程序生成，登录页前端画「蓝色动波纹」，暗色=浅蓝）/ 纯色 / 图片；
/// ② 图片模式：管理端上传（PNG/JPG，≤1MB，文件名白名单，存 data/login-bg/），登录页 &lt;img&gt; 同源渲染；
/// ③ 公共只读端点 /api/auth/login-theme 与 /api/auth/login-bg/{name}（无鉴权，图片白名单+路径穿越双保险）；
/// ④ 管理端（环回+密钥）可读写配置、上传图片、删除图片。
/// </summary>
public sealed class LoginBackgroundStore
{
    /// <summary>登录页背景：mode=ripple(波纹) / color(纯色) / image(图片)。</summary>
    public sealed class Config
    {
        public string Mode { get; set; } = "ripple";
        /// <summary>波纹主色（hex，登录页「动波纹」用它；暗色模式自动转浅蓝描线）。</summary>
        public string RippleColor { get; set; } = "#2f6fed";
        /// <summary>纯色模式背景（hex）。</summary>
        public string Color { get; set; } = "#2f6fed";
        /// <summary>图片模式背景文件名（login-bg/ 下）；空 = 波纹。</summary>
        public string Image { get; set; } = "";
    }

    private static readonly System.Text.RegularExpressions.Regex SafeName =
        new(@"^[A-Za-z0-9_\-]{1,64}\.(png|jpg|jpeg|webp)$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private const long MaxBytes = 1 * 1024 * 1024;
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] JpgSignature = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] WebpSignature = { 0x52, 0x49, 0x46, 0x46 };

    private readonly object _lock = new();
    private readonly LogService _log;
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public LoginBackgroundStore (LogService log) => _log = log;

    public string Dir => Path.Combine(Directory.GetCurrentDirectory(), "data", "login-bg");
    private string ConfigPath => Path.Combine(Directory.GetCurrentDirectory(), "data", "login-bg.json");

    public Config Get ()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    if (SensitiveFile.TryReadAllText(ConfigPath, out var raw))
                    {
                        var c = JsonSerializer.Deserialize<Config>(raw);
                        if (c != null)
                        {
                            // 图片模式但文件已被删 → 回退波纹，避免登录页加载坏图
                            if (c.Mode == "image" && !System.IO.File.Exists(Path.Combine(Dir, c.Image)))
                                c.Mode = "ripple";
                            return c;
                        }
                    }
                }
            }
            catch { /* 只读/坏数据 → 默认波纹 */ }
            return Default();
        }
    }

    public static Config Default () => new() { Mode = "ripple" };

    public void Save (Config c)
    {
        if (c == null) return;
        c.Mode = c.Mode is "ripple" or "color" or "image" ? c.Mode : "ripple";
        c.RippleColor = SanitizeHex(c.RippleColor, "#2f6fed");
        c.Color = SanitizeHex(c.Color, "#2f6fed");
        if (c.Mode != "image" || !System.IO.File.Exists(Path.Combine(Dir, c.Image ?? "")))
            c.Image = "";
        if (c.Mode == "image" && string.IsNullOrWhiteSpace(c.Image))
            c.Mode = "ripple";

        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(c, Opts));
                SensitiveFile.RestrictToOwner(ConfigPath);
            }
            catch { /* 只读环境：仅内存生效 */ }
        }
        _log.Write("auth", new { ts = DateTime.UtcNow, result = "login-bg-updated", mode = c.Mode });
    }

    public sealed record BgItem (string Name, long SizeBytes, DateTime ModifiedAt);

    public (int Total, List<BgItem> Items) List ()
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Dir);
            var items = Directory.EnumerateFiles(Dir)
                .OrderBy(f => Path.GetFileName(f), System.StringComparer.OrdinalIgnoreCase)
                .Select(f => new BgItem(Path.GetFileName(f), new System.IO.FileInfo(f).Length, System.IO.File.GetLastWriteTimeUtc(f)))
                .ToList();
            return (items.Count, items);
        }
    }

    /// <summary>读一张背景图片字节（白名单+路径穿越双保险）。失败时 Bytes=null、Err 给出原因。</summary>
    public (byte[]? Bytes, string? ContentType, string? Err) Read (string? name)
    {
        if (!SafeName.IsMatch(name ?? "")) return (null, null, "文件名不合法");
        lock (_lock)
        {
            var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(Dir, name!));
            if (!full.StartsWith(System.IO.Path.GetFullPath(Dir), System.StringComparison.Ordinal))
                return (null, null, "路径不合法");
            if (!System.IO.File.Exists(full))
                return (null, null, "文件不存在");
            return (System.IO.File.ReadAllBytes(full), MimeTypeOf(name!), null);
        }
    }

    public string? Add (string? name, byte[] data)
    {
        if (string.IsNullOrWhiteSpace(name)) return "文件名不能为空";
        name = System.IO.Path.GetFileName(name).Trim();
        if (!SafeName.IsMatch(name))
            return "文件名只能含字母/数字/下划线/中划线，且以 .png/.jpg/.jpeg/.webp 结尾（≤64 字符）";
        if (data == null || data.Length == 0) return "文件为空";
        if (data.Length > MaxBytes) return "登录背景图不能超过 1MB";
        if (!HasImageMagic(data, System.IO.Path.GetExtension(name)))
            return "只接受真实的 PNG / JPEG / WebP 图片";

        lock (_lock)
        {
            Directory.CreateDirectory(Dir);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(Dir, name), data);
        }
        _log.Write("auth", new { ts = DateTime.UtcNow, result = "login-bg-added", name });
        return null;
    }

    public bool Delete (string? name)
    {
        if (!SafeName.IsMatch(name ?? "")) return false;
        lock (_lock)
        {
            var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(Dir, name!));
            if (!full.StartsWith(System.IO.Path.GetFullPath(Dir), System.StringComparison.Ordinal)) return false;
            if (!System.IO.File.Exists(full)) return false;
            System.IO.File.Delete(full);
        }
        _log.Write("auth", new { ts = DateTime.UtcNow, result = "login-bg-deleted", name });
        return true;
    }

    private static string MimeTypeOf (string name) => System.IO.Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        _ => "application/octet-stream",
    };

    private static bool HasImageMagic (byte[] data, string ext)
    {
        var e = ext.ToLowerInvariant();
        if (e == ".png" && data.Length >= 8 && data.AsSpan(0, 8).SequenceEqual(PngSignature)) return true;
        if (e is ".jpg" or ".jpeg" && data.Length >= 3 && data.AsSpan(0, 3).SequenceEqual(JpgSignature)) return true;
        if (e == ".webp" && data.Length >= 12 && data.AsSpan(0, 4).SequenceEqual(WebpSignature)
            && System.Text.Encoding.ASCII.GetString(data, 8, 4) == "WEBP")
            return true;
        return false;
    }

    private static string SanitizeHex (string? v, string fallback)
    {
        if (string.IsNullOrWhiteSpace(v)) return fallback;
        v = v.Trim();
        if (v.Length < 7 || !System.Text.RegularExpressions.Regex.IsMatch(v, "^#[0-9A-Fa-f]{6}$"))
            return fallback;
        return "#" + v.Substring(1, 6).ToUpperInvariant();
    }
}
