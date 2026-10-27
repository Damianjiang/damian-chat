using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace WeChatWeb.Services;

/// <summary>
/// 滑块背景图库（data/slider-bg/）：
/// ① 首次启动程序化生成 20 张风格各异的 300x150 背景图（山峦/波浪/星空/极光/几何/日落）；
/// ② 管理端可分页查看、上传（PNG 魔数 + IHDR 尺寸校验 + 文件名白名单，≤512KB）、删除；
/// ③ 滑块挑战随机取一张，把保密槽位光栅化叠加后再下发 —— 真实照片纹理让
///    「找最亮区域」式的简单 CV 定位失效，防护比纯渐变更强，观感也更好。
/// </summary>
public sealed class SliderBackgroundStore
{
    private static readonly Regex SafeName = new(@"^[A-Za-z0-9_\-]{1,64}\.png$", RegexOptions.Compiled);
    private const long MaxBytes = 512 * 1024;

    private readonly object _lock = new();
    private readonly LogService _log;

    public SliderBackgroundStore (LogService log) => _log = log;

    public string Dir => Path.Combine(Directory.GetCurrentDirectory(), "data", "slider-bg");

    /// <summary>首次启动：目录为空则生成 20 张内置背景。</summary>
    public void EnsureSeeded ()
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(Dir);
                if (Directory.EnumerateFiles(Dir, "*.png").Any()) return;
                for (var i = 1; i <= 20; i++)
                {
                    var png = GenerateBuiltIn(i);
                    File.WriteAllBytes(Path.Combine(Dir, $"bg-{i:00}.png"), png);
                }
                _log.Write("auth", new { ts = DateTime.UtcNow, result = "slider-bg-seeded", count = 20 });
            }
        }
        catch
        {
            // 只读环境：滑块退回程序化渐变，不影响验证功能
        }
    }

    /* ---------------- 管理端 API ---------------- */

    public sealed record BgItem (string Name, long SizeBytes, DateTime ModifiedAt);

    /// <summary>分页列表（按文件名排序）。页号从 1 开始。</summary>
    public (int Total, int Pages, List<BgItem> Items) List (int page, int pageSize)
    {
        lock (_lock)
        {
            var all = EnumerateSorted();
            var pages = Math.Max(1, (int)Math.Ceiling(all.Count / (double)pageSize));
            page = Math.Clamp(page, 1, pages);
            var items = all.Skip((page - 1) * pageSize).Take(pageSize)
                .Select(f => new BgItem(Path.GetFileName(f), new FileInfo(f).Length, File.GetLastWriteTimeUtc(f)))
                .ToList();
            return (all.Count, pages, items);
        }
    }

    /// <summary>读取一张背景的 PNG 字节（名字白名单校验，防路径穿越）。</summary>
    public byte[]? Read (string? name)
    {
        if (!SafeName.IsMatch(name ?? "")) return null;
        lock (_lock)
        {
            var path = Path.Combine(Dir, name!);
            var full = Path.GetFullPath(path);
            if (!full.StartsWith(Path.GetFullPath(Dir), StringComparison.Ordinal)) return null;   // 双保险
            return File.Exists(full) ? File.ReadAllBytes(full) : null;
        }
    }

    /// <summary>上传一张背景。校验失败返回错误原因，成功返回 null。</summary>
    public string? Add (string? name, byte[] data)
    {
        if (string.IsNullOrWhiteSpace(name)) return "文件名不能为空";
        name = Path.GetFileName(name).Trim();
        if (!SafeName.IsMatch(name)) return "文件名只能包含字母/数字/下划线/中划线，且以 .png 结尾（≤64 字符）";
        if (data == null || data.Length == 0) return "文件为空";
        if (data.Length > MaxBytes) return "背景图不能超过 512KB";
        if (data.Length < 57 || !data.AsSpan(0, 8).SequenceEqual(PngSignature)) return "只接受 PNG 图片";
        // 内容校验：必须是本服务可解码的 PNG，且正好 300x150（与滑块组件像素级对齐）
        if (!PngDecoder.TryDecode(data, out var w, out var h, out _)) return "PNG 无法解析（需要 8-bit RGB/RGBA、无隔行）";
        if (w != 300 || h != 150) return $"背景图必须为 300x150 像素（当前 {w}x{h}）";

        lock (_lock)
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllBytes(Path.Combine(Dir, name), data);
        }
        _log.Write("auth", new { ts = DateTime.UtcNow, result = "slider-bg-added", name });
        return null;
    }

    public bool Delete (string? name)
    {
        if (!SafeName.IsMatch(name ?? "")) return false;
        lock (_lock)
        {
            var full = Path.GetFullPath(Path.Combine(Dir, name!));
            if (!full.StartsWith(Path.GetFullPath(Dir), StringComparison.Ordinal)) return false;
            if (!File.Exists(full)) return false;
            File.Delete(full);
        }
        _log.Write("auth", new { ts = DateTime.UtcNow, result = "slider-bg-deleted", name });
        return true;
    }

    /// <summary>随机取一张背景的 PNG 字节；图库为空返回 null（滑块退回程序化渐变）。</summary>
    public byte[]? PickRandom ()
    {
        try
        {
            lock (_lock)
            {
                var files = Directory.EnumerateFiles(Dir, "*.png").ToList();
                if (files.Count == 0) return null;
                var path = files[RandomNumberGenerator.GetInt32(files.Count)];
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length > MaxBytes) return null;
                return bytes;
            }
        }
        catch
        {
            return null;
        }
    }

    private List<string> EnumerateSorted ()
    {
        Directory.CreateDirectory(Dir);
        return Directory.EnumerateFiles(Dir, "*.png").OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /* ---------------- 内置图生成（20 张，5 族 × 4 配色） ---------------- */

    private static byte[] GenerateBuiltIn (int style)
    {
        var canvas = new Raster(300, 150);
        var family = (style - 1) % 5;      // 0山峦 1波浪 2星空 3极光 4几何
        var scheme = (style - 1) / 5;      // 4 套配色

        // 每套配色：顶/底渐变 + 主色（9 个分量）
        var (tr, tg, tb, br, bg, bb, mr, mg, mb) = scheme switch
        {
            0 => ((byte)168, (byte)213, (byte)186, (byte)120, (byte)176, (byte)150, (byte) 74, (byte)144, (byte)110),   // 青绿
            1 => ((byte)255, (byte)205, (byte)150, (byte)236, (byte)150, (byte)100, (byte)200, (byte)110, (byte) 70),   // 暖橙
            2 => ((byte) 90, (byte)110, (byte)180, (byte) 55, (byte) 65, (byte)140, (byte) 60, (byte) 70, (byte)150),   // 蓝紫
            _ => ((byte)120, (byte)180, (byte)200, (byte) 80, (byte)140, (byte)165, (byte) 40, (byte)120, (byte)150),   // 蓝青
        };
        canvas.VerticalGradient((tr, tg, tb), (br, bg, bb));
        var main = (r: mr, g: mg, b: mb);

        switch (family)
        {
            case 0:
            {
                // 山峦：3 层起伏地平线（越近颜色越深）
                for (var layer = 0; layer < 3; layer++)
                {
                    var phase = layer * 2.1 + scheme;
                    var amp = 14 + layer * 9;
                    var baseY = 78 + layer * 24;
                    var shade = Scale(main, 0.75 + layer * 0.12);
                    for (var x = 0; x < 300; x++)
                    {
                        var y = baseY
                            + Math.Sin(x * 0.021 + phase) * amp
                            + Math.Sin(x * 0.052 + phase * 2.3) * amp * 0.45;
                        canvas.FillRect(x, y, 1, 150 - y, shade.r, shade.g, shade.b, 0.85);
                    }
                }
                break;
            }
            case 1:
            {
                // 波浪：横向流动的色带
                for (var band = 0; band < 4; band++)
                {
                    var phase = band * 1.4 + scheme * 0.7;
                    var midY = 36 + band * 26;
                    var shade = Scale(main, 0.7 + band * 0.15);
                    for (var x = 0; x < 300; x++)
                    {
                        var c = midY + Math.Sin(x * 0.03 + phase) * 10 + Math.Sin(x * 0.011 - phase) * 6;
                        canvas.FillRect(x, c - 5, 1, 10, shade.r, shade.g, shade.b, 0.5);
                    }
                }
                break;
            }
            case 2:
            {
                // 星空：深底 + 星点 + 弦月
                canvas.VerticalGradient((30, 34, 68), (10, 12, 30));
                for (var i = 0; i < 90; i++)
                    canvas.FillCircle(
                        RandomNumberGenerator.GetInt32(300), RandomNumberGenerator.GetInt32(150),
                        RandomNumberGenerator.GetInt32(1, 3) * 0.6, 255, 255, 255, 0.35 + RandomNumberGenerator.GetInt32(60) / 100.0);
                canvas.FillCircle(236, 38, 20, 255, 244, 214, 0.95);
                canvas.FillCircle(226, 32, 18, 30, 34, 68, 1.0);   // 月牙遮挡
                break;
            }
            case 3:
            {
                // 极光：斜向柔色带
                for (var band = 0; band < 3; band++)
                {
                    var shade = Scale(main, 0.8 + band * 0.3);
                    var phase = band * 2.0 + scheme;
                    for (var x = 0; x < 300; x++)
                    {
                        var c = 40 + band * 30 + Math.Sin(x * 0.017 + phase) * 22;
                        canvas.SoftBlob(x, c, 26, shade.r, shade.g, shade.b, 0.05);
                    }
                }
                for (var i = 0; i < 26; i++)
                    canvas.FillCircle(RandomNumberGenerator.GetInt32(300), RandomNumberGenerator.GetInt32(150), 0.7, 255, 255, 255, 0.4);
                break;
            }
            default:
            {
                // 几何：同心圆 + 圆点阵
                var cx = 80 + scheme * 40;
                for (var r = 14; r <= 70; r += 14)
                    canvas.FillCircle(cx, 75, r, main.r, main.g, main.b, 0.10);
                for (var gy = 16; gy < 150; gy += 22)
                    for (var gx = 190; gx < 300; gx += 22)
                        canvas.FillCircle(gx, gy, 2.4, main.r, main.g, main.b, 0.35);
                canvas.FillRoundRect(40, 40, 60, 60, 10, main.r, main.g, main.b, 0.22);
                break;
            }
        }

        // 轻噪点统一质感
        canvas.Noise(160, 255, 255, 255, 0.045);
        return canvas.ToPng();
    }

    private static (byte r, byte g, byte b) Scale ((byte r, byte g, byte b) c, double k) =>
        ((byte)Math.Clamp(c.r * k, 0, 255), (byte)Math.Clamp(c.g * k, 0, 255), (byte)Math.Clamp(c.b * k, 0, 255));
}
