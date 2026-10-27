namespace WeChatWeb.Services;

/// <summary>
/// 上传文件的「类型 → Content-Type」映射（云盘模式）。
///
/// 策略（用户要求：什么都可以上传，安全靠下载侧防护）：
/// ・上传时**不做扩展名拦截、不做魔数校验、不跑杀毒** —— 任意文件（.exe/.zip/脚本/视频…）都能存；
/// ・安全全部放在「下载侧」：① 路径穿越在 <see cref="Validate.CleanFileName"/> 挡掉；
///   ② 登录鉴权 + 归属校验（CanReadFile）；③ 非内联类型一律 <c>Content-Disposition: attachment</c>
///   + <c>X-Content-Type-Options: nosniff</c>（浏览器绝不内联渲染）；④ 文件只存储、服务端从不执行。
///
/// 这里只负责按扩展名给出「服务端权威的 Content-Type」，绝不信任客户端声明的 MIME
/// （攻击者可声明 text/html 让内容被浏览器当页面渲染）。未知类型一律 octet-stream（附件下载）。
/// </summary>
public static class FileContent
{
    /// <summary>
    /// 服务端权威的 Content-Type 映射（按扩展名）。落库时使用，下载时直接回传给浏览器。
    /// 未知扩展名 → application/octet-stream（强制按附件下载，绝不内联渲染）。
    /// </summary>
    public static string ContentTypeFor (string ext) => ext.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".ico" => "image/x-icon",
        ".heic" or ".heif" => "image/heic",
        ".svg" => "image/svg+xml",
        ".mp3" => "audio/mpeg",
        ".m4a" => "audio/mp4",
        ".ogg" => "audio/ogg",
        ".wav" => "audio/wav",
        ".amr" => "audio/amr",
        ".webm" => "audio/webm",
        ".mp4" => "video/mp4",
        ".mov" => "video/quicktime",
        ".pdf" => "application/pdf",
        ".txt" => "text/plain; charset=utf-8",
        ".csv" => "text/csv; charset=utf-8",
        ".md" => "text/markdown; charset=utf-8",
        ".json" => "application/json",
        ".xml" => "application/xml",
        ".zip" => "application/zip",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xls" => "application/vnd.ms-excel",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".ppt" => "application/vnd.ms-powerpoint",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        _ => "application/octet-stream",
    };

    /// <summary>
    /// 该类型是否允许浏览器「内联渲染」（图片/音频/PDF/纯文本，聊天里直接预览）。
    /// 其余（含未知类型）一律走 <c>attachment</c> 强制下载 —— 这是云盘模式的核心防护：
    /// 即使某个文件被改名伪装，只要扩展名不在白名单，浏览器就只能「下载」不能「执行/渲染」。
    /// </summary>
    public static bool InlinePreviewable (string ext) => ext.ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".ico" or ".heic" or ".heif"
            or ".mp3" or ".m4a" or ".ogg" or ".wav" or ".amr" or ".webm"
            or ".pdf" or ".txt" => true,
        _ => false,
    };
}
