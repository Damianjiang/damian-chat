using System.IO.Compression;

namespace WeChatWeb.Services;

/// <summary>
/// 极简纯 C# PNG 编码器（无外部依赖、跨平台）。
/// 验证码出图必须用光栅图而非 SVG：SVG 是文本标记，机器人 base64 解码即可直接
/// 读出缺口坐标 / 验证码字符（等于没有验证码）；PNG 必须做像素级图像识别才能提取，
/// 防护等级与商业验证码一致。
/// </summary>
public static class SimplePng
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>把 RGBA 像素缓冲编码为 PNG 字节（filter 0，zlib 压缩）。</summary>
    public static byte[] Encode (int width, int height, byte[] rgba)
    {
        using var ms = new MemoryStream();
        ms.Write(Signature, 0, Signature.Length);

        // IHDR：宽、高（大端）、位深 8、颜色类型 6 (RGBA)、压缩/滤波/隔行均 0
        var ihdr = new byte[13];
        WriteBe32(ihdr, 0, (uint)width);
        WriteBe32(ihdr, 4, (uint)height);
        ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        WriteChunk(ms, "IHDR", ihdr);

        // 每行前加 filter byte 0（None）
        var stride = width * 4;
        var raw = new byte[(stride + 1) * height];
        for (var y = 0; y < height; y++)
            Buffer.BlockCopy(rgba, y * stride, raw, y * (stride + 1) + 1, stride);
        WriteChunk(ms, "IDAT", ZlibCompress(raw));

        WriteChunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    private static void WriteBe32 (byte[] buf, int offset, uint value)
    {
        buf[offset] = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }

    private static byte[] ZlibCompress (byte[] data)
    {
        using var outMs = new MemoryStream();
        // ZLibStream 输出即 zlib 容器格式（RFC1950），正是 PNG IDAT 要求的格式
        using (var zlib = new ZLibStream(outMs, CompressionLevel.Fastest, leaveOpen: true))
            zlib.Write(data, 0, data.Length);
        return outMs.ToArray();
    }

    private static void WriteChunk (MemoryStream ms, string type, byte[] data)
    {
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        var len = new byte[4];
        WriteBe32(len, 0, (uint)data.Length);
        ms.Write(len, 0, 4);
        ms.Write(typeBytes, 0, typeBytes.Length);
        ms.Write(data, 0, data.Length);
        var crcInput = new byte[typeBytes.Length + data.Length];
        Buffer.BlockCopy(typeBytes, 0, crcInput, 0, typeBytes.Length);
        Buffer.BlockCopy(data, 0, crcInput, typeBytes.Length, data.Length);
        var crc = new byte[4];
        WriteBe32(crc, 0, Crc32(crcInput));
        ms.Write(crc, 0, 4);
    }

    // ---------------- CRC-32 (IEEE 802.3, PNG 规范要求) ----------------

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable ()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    private static uint Crc32 (byte[] data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (var b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}

/// <summary>
/// RGBA 光栅画布：渐变底、圆角矩形（填充/描边）、圆、粗线段、噪点。
/// 所有形状用带透明度的 alpha 混合叠加，输出经 SimplePng.Encode 转 PNG。
/// </summary>
public sealed class Raster
{
    public readonly int W;
    public readonly int H;
    private readonly byte[] _px;

    public Raster (int w, int h)
    {
        W = w;
        H = h;
        _px = new byte[w * h * 4];
    }

    /// <summary>用既有 RGBA 像素构建画布（如：解码上传的背景图后再叠加槽位）。</summary>
    public Raster (int w, int h, byte[] rgba)
    {
        if (rgba.Length != w * h * 4) throw new ArgumentException("RGBA 缓冲长度与画布尺寸不符");
        W = w;
        H = h;
        _px = rgba;
    }

    /// <summary>填充矩形（轴对齐）。</summary>
    public void FillRect (double x, double y, double w, double h, byte r, byte g, byte b, double alpha)
    {
        var x0 = Math.Max(0, (int)Math.Ceiling(x));
        var x1 = Math.Min(W - 1, (int)(x + w) - 1);
        var y0 = Math.Max(0, (int)Math.Ceiling(y));
        var y1 = Math.Min(H - 1, (int)(y + h) - 1);
        for (var py = y0; py <= y1; py++)
            for (var px = x0; px <= x1; px++)
                Blend(px, py, r, g, b, alpha);
    }

    /// <summary>垂直线性渐变（整幅铺底）。</summary>
    public void VerticalGradient ((byte r, byte g, byte b) top, (byte r, byte g, byte b) bottom)
    {
        for (var y = 0; y < H; y++)
        {
            var t = H <= 1 ? 0 : (double)y / (H - 1);
            var r = (byte)(top.r + (bottom.r - top.r) * t);
            var g = (byte)(top.g + (bottom.g - top.g) * t);
            var b = (byte)(top.b + (bottom.b - top.b) * t);
            for (var x = 0; x < W; x++)
                SetOpaque(x, y, r, g, b);
        }
    }

    /// <summary>半透明大色斑（径向衰减），让背景不那么"纯净"以干扰简单的图像定位脚本。</summary>
    public void SoftBlob (double cx, double cy, double radius, byte r, byte g, byte b, double alpha)
    {
        if (alpha <= 0) return;
        var x0 = Math.Max(0, (int)(cx - radius));
        var x1 = Math.Min(W - 1, (int)(cx + radius));
        var y0 = Math.Max(0, (int)(cy - radius));
        var y1 = Math.Min(H - 1, (int)(cy + radius));
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                if (d >= radius) continue;
                var a = alpha * (1 - d / radius);
                Blend(x, y, r, g, b, a);
            }
        }
    }

    /// <summary>填充圆。</summary>
    public void FillCircle (double cx, double cy, double radius, byte r, byte g, byte b, double alpha)
    {
        var x0 = Math.Max(0, (int)(cx - radius - 1));
        var x1 = Math.Min(W - 1, (int)(cx + radius + 1));
        var y0 = Math.Max(0, (int)(cy - radius - 1));
        var y1 = Math.Min(H - 1, (int)(cy + radius + 1));
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var d = Math.Sqrt((x + 0.5 - cx) * (x + 0.5 - cx) + (y + 0.5 - cy) * (y + 0.5 - cy));
                if (d <= radius) Blend(x, y, r, g, b, alpha);
            }
        }
    }

    /// <summary>描边圆（环，|dist - radius| ≤ 厚度/2）。拼图压块凸齿的轮廓用。</summary>
    public void StrokeCircle (double cx, double cy, double radius, double thick, byte r, byte g, byte b, double alpha)
    {
        var half = thick / 2;
        var x0 = Math.Max(0, (int)(cx - radius - half - 1));
        var x1 = Math.Min(W - 1, (int)(cx + radius + half + 1));
        var y0 = Math.Max(0, (int)(cy - radius - half - 1));
        var y1 = Math.Min(H - 1, (int)(cy + radius + half + 1));
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var d = Math.Sqrt((x + 0.5 - cx) * (x + 0.5 - cx) + (y + 0.5 - cy) * (y + 0.5 - cy));
                if (Math.Abs(d - radius) <= half) Blend(x, y, r, g, b, alpha);
            }
        }
    }

    /// <summary>填充圆角矩形（SDF 判定）。</summary>
    public void FillRoundRect (double x, double y, double w, double h, double rad, byte r, byte g, byte b, double alpha)
    {
        SdfRoundRect(x, y, w, h, rad, (px, py, sdf) =>
        {
            if (sdf <= 0) Blend(px, py, r, g, b, alpha);
        });
    }

    /// <summary>描边圆角矩形（|SDF| ≤ 厚度/2）。</summary>
    public void StrokeRoundRect (double x, double y, double w, double h, double rad, double thick, byte r, byte g, byte b, double alpha)
    {
        var half = thick / 2;
        SdfRoundRect(x, y, w, h, rad, (px, py, sdf) =>
        {
            if (Math.Abs(sdf) <= half) Blend(px, py, r, g, b, alpha);
        });
    }

    /// <summary>粗线段（到线段的距离 ≤ 厚度/2）。</summary>
    public void Line (double ax, double ay, double bx, double by, double thick, byte r, byte g, byte b, double alpha)
    {
        var half = thick / 2;
        var px0 = Math.Max(0, (int)Math.Min(ax, bx) - (int)half - 1);
        var px1 = Math.Min(W - 1, (int)Math.Max(ax, bx) + (int)half + 1);
        var py0 = Math.Max(0, (int)Math.Min(ay, by) - (int)half - 1);
        var py1 = Math.Min(H - 1, (int)Math.Max(ay, by) + (int)half + 1);
        for (var y = py0; y <= py1; y++)
        {
            for (var x = px0; x <= px1; x++)
            {
                var d = DistanceToSegment(x + 0.5, y + 0.5, ax, ay, bx, by);
                if (d <= half) Blend(x, y, r, g, b, alpha);
            }
        }
    }

    /// <summary>随机噪点。</summary>
    public void Noise (int count, byte r, byte g, byte b, double alpha)
    {
        for (var i = 0; i < count; i++)
            Blend(
                System.Security.Cryptography.RandomNumberGenerator.GetInt32(W),
                System.Security.Cryptography.RandomNumberGenerator.GetInt32(H),
                r, g, b, alpha);
    }

    public byte[] ToPng () => SimplePng.Encode(W, H, _px);

    // ---------------- 内部 ----------------

    private void SdfRoundRect (double x, double y, double w, double h, double rad, Action<int, int, double> plot)
    {
        var cx = x + w / 2;
        var cy = y + h / 2;
        var hw = w / 2 - rad;
        var hh = h / 2 - rad;
        var x0 = Math.Max(0, (int)x - 1);
        var x1 = Math.Min(W - 1, (int)(x + w) + 1);
        var y0 = Math.Max(0, (int)y - 1);
        var y1 = Math.Min(H - 1, (int)(y + h) + 1);
        for (var py = y0; py <= y1; py++)
        {
            for (var px = x0; px <= x1; px++)
            {
                var dx = Math.Abs(px + 0.5 - cx) - hw;
                var dy = Math.Abs(py + 0.5 - cy) - hh;
                var ox = Math.Max(dx, 0);
                var oy = Math.Max(dy, 0);
                var sdf = Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(dx, dy), 0) - rad;
                plot(px, py, sdf);
            }
        }
    }

    private static double DistanceToSegment (double px, double py, double ax, double ay, double bx, double by)
    {
        var abx = bx - ax;
        var aby = by - ay;
        var len2 = abx * abx + aby * aby;
        double t;
        if (len2 <= 0) t = 0;
        else
        {
            t = ((px - ax) * abx + (py - ay) * aby) / len2;
            t = Math.Clamp(t, 0, 1);
        }
        var dx = px - (ax + abx * t);
        var dy = py - (ay + aby * t);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private void SetOpaque (int x, int y, byte r, byte g, byte b)
    {
        var i = (y * W + x) * 4;
        _px[i] = r; _px[i + 1] = g; _px[i + 2] = b; _px[i + 3] = 255;
    }

    private void Blend (int x, int y, byte r, byte g, byte b, double alpha)
    {
        if (x < 0 || y < 0 || x >= W || y >= H || alpha <= 0) return;
        if (alpha > 1) alpha = 1;
        var i = (y * W + x) * 4;
        var inv = 1 - alpha;
        _px[i] = (byte)(r * alpha + _px[i] * inv);
        _px[i + 1] = (byte)(g * alpha + _px[i + 1] * inv);
        _px[i + 2] = (byte)(b * alpha + _px[i + 2] * inv);
        _px[i + 3] = 255;
    }
}

/// <summary>
/// 折线笔画字形库（验证码用）：每个字符由若干折线段组成，渲染时随机旋转/缩放/抖动。
/// 不使用任何文本元素，机器人无法从数据里直接读出字符，只能上 OCR。
/// </summary>
public static class StrokeFont
{
    // 每个字形：若干条折线，点对为 [x0,y0,x1,y1,...]，坐标系 20 宽 × 26 高，y 向下（整数存储，使用时转 double）
    private static readonly Dictionary<char, int[][]> Glyphs = new()
    {
        ['A'] = new[] { new[] { 2, 26, 10, 0, 18, 26 }, new[] { 6, 17, 14, 17 } },
        ['B'] = new[] { new[] { 2, 0, 2, 26 }, new[] { 2, 0, 12, 0, 17, 2, 17, 10, 12, 12, 2, 12 }, new[] { 2, 12, 13, 12, 18, 14, 18, 24, 13, 26, 2, 26 } },
        ['C'] = new[] { new[] { 17, 4, 13, 1, 7, 1, 2, 5, 2, 21, 7, 25, 13, 25, 17, 22 } },
        ['D'] = new[] { new[] { 2, 0, 2, 26 }, new[] { 2, 0, 10, 0, 17, 5, 17, 21, 10, 26, 2, 26 } },
        ['E'] = new[] { new[] { 17, 1, 2, 1, 2, 25, 17, 25 }, new[] { 2, 13, 13, 13 } },
        ['F'] = new[] { new[] { 17, 1, 2, 1, 2, 25 }, new[] { 2, 13, 12, 13 } },
        ['G'] = new[] { new[] { 17, 4, 13, 1, 7, 1, 2, 5, 2, 21, 7, 25, 13, 25, 17, 21, 17, 14, 9, 14 } },
        ['H'] = new[] { new[] { 2, 0, 2, 26 }, new[] { 18, 0, 18, 26 }, new[] { 2, 13, 18, 13 } },
        ['J'] = new[] { new[] { 15, 0, 15, 22, 12, 25, 6, 25, 3, 22 } },
        ['K'] = new[] { new[] { 2, 0, 2, 26 }, new[] { 17, 0, 2, 15 }, new[] { 7, 10, 18, 26 } },
        ['L'] = new[] { new[] { 2, 0, 2, 25, 17, 25 } },
        ['M'] = new[] { new[] { 2, 25, 2, 1, 10, 15, 18, 1, 18, 25 } },
        ['N'] = new[] { new[] { 2, 25, 2, 1, 18, 25, 18, 1 } },
        ['P'] = new[] { new[] { 2, 25, 2, 1, 13, 1, 17, 3, 17, 10, 13, 12, 2, 12 } },
        ['Q'] = new[] { new[] { 2, 1, 13, 1, 17, 5, 17, 18, 13, 22, 6, 22, 2, 18, 2, 1 }, new[] { 10, 15, 18, 25 } },
        ['R'] = new[] { new[] { 2, 25, 2, 1, 13, 1, 17, 3, 17, 10, 13, 12, 2, 12 }, new[] { 8, 12, 17, 25 } },
        ['S'] = new[] { new[] { 16, 3, 13, 1, 6, 1, 2, 4, 2, 9, 6, 12, 13, 12, 17, 15, 17, 22, 13, 25, 6, 25, 2, 23 } },
        ['T'] = new[] { new[] { 1, 1, 19, 1 }, new[] { 10, 1, 10, 25 } },
        ['U'] = new[] { new[] { 2, 0, 2, 21, 5, 25, 14, 25, 17, 21, 17, 0 } },
        ['V'] = new[] { new[] { 2, 0, 10, 25, 18, 0 } },
        ['W'] = new[] { new[] { 1, 0, 5, 25, 10, 10, 15, 25, 19, 0 } },
        ['X'] = new[] { new[] { 2, 0, 18, 25 }, new[] { 18, 0, 2, 25 } },
        ['Y'] = new[] { new[] { 2, 0, 10, 12 }, new[] { 18, 0, 10, 12, 10, 25 } },
        ['Z'] = new[] { new[] { 2, 1, 18, 1, 2, 25, 18, 25 } },
        ['2'] = new[] { new[] { 3, 4, 6, 1, 14, 1, 17, 4, 17, 8, 2, 25, 17, 25 } },
        ['3'] = new[] { new[] { 3, 4, 6, 1, 13, 1, 17, 4, 17, 9, 13, 12, 8, 12 }, new[] { 13, 12, 17, 15, 17, 22, 13, 25, 5, 25, 2, 22 } },
        ['4'] = new[] { new[] { 14, 25, 14, 1, 2, 17, 18, 17 } },
        ['5'] = new[] { new[] { 17, 1, 3, 1, 2, 12, 12, 12, 17, 15, 17, 22, 13, 25, 4, 25, 2, 23 } },
        ['6'] = new[] { new[] { 16, 3, 12, 1, 6, 1, 2, 6, 2, 20, 6, 25, 13, 25, 17, 21, 17, 17, 13, 13, 6, 13, 2, 17 } },
        ['7'] = new[] { new[] { 1, 1, 19, 1, 8, 25 } },
        ['8'] = new[] { new[] { 7, 1, 13, 1, 17, 4, 17, 9, 13, 12, 7, 12, 3, 9, 3, 4, 7, 1 }, new[] { 7, 12, 14, 12, 18, 16, 18, 22, 14, 25, 6, 25, 2, 22, 2, 16, 6, 12 } },
        ['9'] = new[] { new[] { 3, 23, 6, 25, 13, 25, 17, 20, 17, 6, 13, 1, 6, 1, 2, 5, 2, 9, 6, 13, 13, 13, 17, 9 } },
    };

    /// <summary>在画布上绘制一个字符：平移 x,y → 缩放 → 绕字形中心随机旋转 → 逐点抖动 → 画粗折线。</summary>
    public static void Draw (Raster raster, char ch, double x, double y, double scale,
        double rotationDeg, double jitter, byte r, byte g, byte b, double alpha, double thick)
    {
        if (!Glyphs.TryGetValue(ch, out var polylines)) return;

        var rad = rotationDeg * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        var cx = 10 * scale;   // 字形中心（20x26 盒子）
        var cy = 13 * scale;

        foreach (var line in polylines)
        {
            var pts = new (double X, double Y)[line.Length / 2];
            for (var i = 0; i < pts.Length; i++)
            {
                var px = line[i * 2] * scale + (RandomJitter(jitter));
                var py = line[i * 2 + 1] * scale + (RandomJitter(jitter));
                // 绕中心旋转
                var ox = px - cx;
                var oy = py - cy;
                pts[i] = (x + cx + ox * cos - oy * sin, y + cy + ox * sin + oy * cos);
            }
            for (var i = 0; i + 1 < pts.Length; i++)
                raster.Line(pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y, thick, r, g, b, alpha);
        }
    }

    private static double RandomJitter (double amplitude) =>
        (System.Security.Cryptography.RandomNumberGenerator.GetInt32(-1000, 1001) / 1000.0) * amplitude;
}

/// <summary>
/// 极简 PNG 解码器（受限安全面）：
/// 仅支持 8-bit 深度、无隔行、颜色类型 2 (RGB) / 6 (RGBA)、滤波 0-4。
/// 只用于解码本服务自己生成的、以及管理端上传前校验过的滑块背景图 ——
/// 拒绝一切不合规格的输入（尺寸上限、长度精确校验），不做通用图片解码。
/// </summary>
public static class PngDecoder
{
    private static readonly byte[] Sig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public static bool TryDecode (byte[] png, out int width, out int height, out byte[] rgba)
    {
        width = 0; height = 0; rgba = Array.Empty<byte>();
        try
        {
            if (png.Length < 57 || png.Length > 1024 * 1024) return false;   // ≤1MB
            if (!png.AsSpan(0, 8).SequenceEqual(Sig)) return false;

            // ---- IHDR ----
            if (!ReadChunk(png, 8, out var type1, out var ihdr, out var next)) return false;
            if (type1 != "IHDR" || ihdr.Length != 13) return false;
            width = (ihdr[0] << 24) | (ihdr[1] << 16) | (ihdr[2] << 8) | ihdr[3];
            height = (ihdr[4] << 24) | (ihdr[5] << 16) | (ihdr[6] << 8) | ihdr[7];
            var depth = ihdr[8];
            var colorType = ihdr[9];
            var compression = ihdr[10];
            var filterMethod = ihdr[11];
            var interlace = ihdr[12];
            if (depth != 8 || compression != 0 || filterMethod != 0 || interlace != 0) return false;
            if (colorType is not (2 or 6)) return false;
            if (width is < 100 or > 2000 || height is < 60 or > 1200) return false;   // 合理尺寸带

            var bpp = colorType == 6 ? 4 : 3;

            // ---- 收集 IDAT ----
            var idat = new List<byte[]>();
            var pos = next;
            while (pos < png.Length)
            {
                if (!ReadChunk(png, pos, out var type, out var data, out pos)) return false;
                if (type == "IDAT") idat.Add(data);
                else if (type == "IEND") break;
            }
            if (idat.Count == 0) return false;

            // ---- zlib 解压 ----
            var total = idat.Sum(d => d.Length);
            using var src = new MemoryStream();
            foreach (var d in idat) src.Write(d, 0, d.Length);
            src.Position = 0;
            var stride = width * bpp;
            var expected = (stride + 1) * height;
            var raw = new byte[expected];
            using (var zlib = new ZLibStream(src, CompressionMode.Decompress))
            {
                var read = 0;
                while (read < expected)
                {
                    var n = zlib.Read(raw, read, expected - read);
                    if (n <= 0) return false;      // 数据不足：拒绝
                    read += n;
                }
                if (zlib.Read(new byte[1], 0, 1) > 0) return false;   // 数据超长：拒绝
            }

            // ---- 反滤波 ----
            // raw 每行前有 1 个 filter 字节；defiltered 只含像素数据（stride*height），
            // 长度必须精确等于 w*h*bpp —— 下游 Raster(w,h,rgba) 会严格校验。
            var defiltered = new byte[stride * height];
            for (var y = 0; y < height; y++)
            {
                var filter = raw[y * (stride + 1)];
                if (filter is < 0 or > 4) return false;   // 未知滤波：拒绝
                var rowStart = y * (stride + 1) + 1;
                var outStart = y * stride;
                for (var i = 0; i < stride; i++)
                {
                    var x = raw[rowStart + i];
                    var a = i >= bpp ? defiltered[outStart + i - bpp] : (byte)0;
                    var b = y > 0 ? defiltered[outStart + i - stride] : (byte)0;
                    var c = i >= bpp && y > 0 ? defiltered[outStart + i - bpp - stride] : (byte)0;
                    defiltered[outStart + i] = filter switch
                    {
                        0 => x,
                        1 => (byte)(x + a),
                        2 => (byte)(x + b),
                        3 => (byte)(x + (a + b) / 2),
                        _ => (byte)(x + Paeth(a, b, c)),   // 4: Paeth
                    };
                }
            }

            // ---- RGB → RGBA ----
            if (colorType == 6) { rgba = defiltered; return true; }
            rgba = new byte[width * height * 4];
            for (var i = 0; i < width * height; i++)
            {
                rgba[i * 4] = defiltered[i * 3];
                rgba[i * 4 + 1] = defiltered[i * 3 + 1];
                rgba[i * 4 + 2] = defiltered[i * 3 + 2];
                rgba[i * 4 + 3] = 255;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int Paeth (int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        if (pb <= pc) return b;
        return c;
    }

    private static bool ReadChunk (byte[] png, int offset, out string type, out byte[] data, out int next)
    {
        type = ""; data = Array.Empty<byte>(); next = offset;
        if (offset + 8 > png.Length) return false;
        var len = (png[offset] << 24) | (png[offset + 1] << 16) | (png[offset + 2] << 8) | png[offset + 3];
        if (len < 0 || offset + 12 + (long)len > png.Length) return false;
        type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
        data = png[(offset + 8)..(offset + 8 + len)];
        next = offset + 12 + len;
        return true;
    }
}
