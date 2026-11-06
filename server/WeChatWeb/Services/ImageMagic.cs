namespace WeChatWeb.Services;

/// <summary>
/// 图片魔数（magic bytes）校验：按扩展名验证文件头是否真的是该格式。
///
/// 安全用途：云盘模式「什么都可以上传」，但头像/登录背景这类**会走内联渲染路径**
/// （&lt;img&gt; 或 CSS background，同源可被脚本读走像素）的文件必须挡掉
/// 「.exe 改名 .jpg」——伪装成图片的木马/可执行文件。文件头不是声称的格式即拒收，
/// 不依赖扩展名（扩展名可被改名，魔数改不掉）。
///
/// 支持 AllowedImage 白名单全部格式：PNG / JPEG / WEBP / GIF / BMP / HEIC(HEIF)。
/// HEIC 走 ISO Base Media File（.heic/.heif 实为同一容器）：offset 4 起是 "ftyp" box，
/// major brand 取 "heic"/"heix"/"heif"/"mif1"/"msf1"/"hevc" 任一。
/// </summary>
public static class ImageMagic
{
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] JpgSignature = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] GifSignature = { 0x47, 0x49, 0x46, 0x38 };   // "GIF8"
    private static readonly byte[] WebpRiff = { 0x52, 0x49, 0x46, 0x46 };       // "RIFF"
    private static readonly byte[] WebpBrand = { 0x57, 0x45, 0x42, 0x50 };     // "WEBP" (offset 8)
    private static readonly byte[] BmpSignature = { 0x42, 0x4D };              // "BM"
    private static readonly byte[] FtypBrand = { 0x66, 0x74, 0x79, 0x70 };     // "ftyp" (offset 4)
    private static readonly string[] HeicBrands = { "heic", "heix", "heif", "mif1", "msf1", "hevc" };

    /// <summary>
    /// 验证 data 头部是否为 ext 声称的格式。data 为 null/过短 / ext 未知 → 返回 false（拒收）。
    /// 调用方在落盘前读文件头（SaveAsync 已把整个流读完算 SHA，可在其前读前 32 字节）。
    /// </summary>
    public static bool Match (byte[]? data, string ext)
    {
        if (data == null || data.Length < 2) return false;
        var e = (ext ?? "").ToLowerInvariant();
        return e switch
        {
            ".png" => data.Length >= 8 && data.AsSpan(0, 8).SequenceEqual(PngSignature),
            ".jpg" or ".jpeg" => data.Length >= 3 && data.AsSpan(0, 3).SequenceEqual(JpgSignature),
            ".gif" => data.Length >= 4 && data.AsSpan(0, 4).SequenceEqual(GifSignature),
            ".webp" => data.Length >= 12
                       && data.AsSpan(0, 4).SequenceEqual(WebpRiff)
                       && data.AsSpan(8, 4).SequenceEqual(WebpBrand),
            ".bmp" => data.AsSpan(0, 2).SequenceEqual(BmpSignature),
            ".heic" or ".heif" => IsHeic(data),
            _ => false,   // 非图片扩展名：魔数无从谈起，一律拒收（配合扩展名白名单双保险）
        };
    }

    /// <summary>HEIC/HEIF：ISO BMFF 容器，offset 4 起 "ftyp" + major brand 命中白名单。</summary>
    private static bool IsHeic (byte[] data)
    {
        if (data.Length < 12) return false;
        if (!data.AsSpan(4, 4).SequenceEqual(FtypBrand)) return false;
        var brand = System.Text.Encoding.ASCII.GetString(data, 8, 4);
        return HeicBrands.Contains(brand, StringComparer.OrdinalIgnoreCase);
    }
}
