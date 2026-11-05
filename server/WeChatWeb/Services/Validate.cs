using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using WeChatWeb.Data;

namespace WeChatWeb.Services;

/// <summary>
/// 服务端输入校验（所有写接口都必须过一遍）。
/// 客户端校验只是体验，真正的边界在服务端：长度上限、类型白名单、文件名净化、
/// 拒绝路径穿越与控制字符、超限直接拒绝（而不是截断），避免脏数据/注入/内存爆炸。
/// </summary>
public static class Validate
{
    /// <summary>各类文本的字节上限（超出即 400 拒绝，不截断）。</summary>
    public const int MaxNickname = 20 * 3;         // 中文 UTF-8 约 3 字节/字
    public const int MaxSignature = 60 * 3;
    public const int MaxRegion = 30 * 3;
    public const int MaxWxId = 20;
    public const int MaxMessageText = 2000;        // 单条文本消息上限 2000 字（中英文同计数，与前端输入框 maxlength 一致）
    public const int MaxFileName = 120;
    public const int MaxGroupName = 30 * 3;
    public const int MaxGroupNotice = 200 * 3;
    public const int MaxMomentText = 1000 * 3;
    public const int MaxComment = 300 * 3;
    /// <summary>引用 JSON 原始串的上限（防超大 payload DoS）。真实引用经 NormalizeQuote 归一化后远小于此。</summary>
    public const int MaxSignatureField = 2048;
    public const int MaxSticker = 16;
    public const int MaxImagesPerMoment = 9;
    public const int MaxVoiceSeconds = 600;

    /// <summary>
    /// 应用真正使用的隐私键白名单（users/me 更新 Privacy 字典时只接受这些键，
    /// 挡掉任意键注入污染 store.json 持久化数据）。
    /// 前端 SettingsPane 写 searchable/strangerMoments/signatureVisible；
    /// 服务端 PublicProfile.Allow 读 phone/wxId。
    /// </summary>
    public static readonly System.Collections.Generic.HashSet<string> KnownPrivacyKeys =
        new(StringComparer.Ordinal)
        {
            "searchable", "strangerMoments", "signatureVisible", "phone", "wxId",
            "searchByPhone", "searchByWxId",
        };

    private static readonly Regex WxIdPattern = new(@"^[a-z][a-z0-9_-]{5,19}$", RegexOptions.Compiled);

    /// <summary>大面号是否合法（自定义大面号用）：小写字母开头，6-20 位小写字母/数字/下划线/连字符。</summary>
    public static bool IsWxId (string? s) => !string.IsNullOrWhiteSpace(s) && WxIdPattern.IsMatch(s);

    // 账号：11 位手机号 / 3-32 位字母数字下划线 / 2-16 位中文
    private static readonly Regex AccountPattern = new(@"^(?:1[3-9]\d{9}|[A-Za-z0-9_]{3,32}|[\u4e00-\u9fa5]{2,16})$", RegexOptions.Compiled);
    private static readonly Regex SafeFileName = new(@"^[^\x00-\x1f<>:""|?*\\/]+$", RegexOptions.Compiled);

    /// <summary>允许的消息类型白名单（防止伪造 type 触发未处理的分支）。poll = 群投票卡片（content 为 pollId）。
    /// forward = 合并转发「聊天记录」卡片（ForwardJson 携带快照）。</summary>
    public static readonly string[] MessageTypes = { "text", "image", "voice", "file", "sticker", "system", "pat", "poll", "forward" };

    /// <summary>合并转发 JSON 上限（64KB，约可装 300 条纯文本；前端硬上限 50 条先卡）。</summary>
    public const int MaxForwardJson = 64 * 1024;
    /// <summary>单条合并转发的消息条数上限（QQ 同款量级）。</summary>
    public const int MaxForwardCount = 50;

    private static readonly Regex ForwardFileIdsRegex = new("^[a-f0-9]{32}$", RegexOptions.Compiled);

    /// <summary>
    /// 解析并消毒合并转发的 ForwardJson：
    ///   { v:1, msgs:[{ s:发送人名, t:毫秒时间, k:类型, c:内容, f:文件名, fs:大小, i:fileId, e:表情 }] }
    /// 全部字符串过 StripHtml/长度封顶，类型白名单，fileId 必须是合法 32 位 hex；
    /// 超过 MaxForwardCount 条直接拒绝（前端分批）。返回规范化后的紧凑 JSON；非法返回 null。
    /// </summary>
    public static string? ParseForwardJson (string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > MaxForwardJson) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty("msgs", out var msgsEl) || msgsEl.ValueKind != JsonValueKind.Array) return null;
            var list = new List<object>();
            var count = 0;
            foreach (var m in msgsEl.EnumerateArray())
            {
                if (++count > MaxForwardCount) return null;
                if (m.ValueKind != JsonValueKind.Object) return null;

                string Str (string prop, int max)
                {
                    if (!m.TryGetProperty(prop, out var p) || p.ValueKind != JsonValueKind.String) return "";
                    var v = StripHtml(p.GetString()) ?? "";
                    return v.Length > max ? v[..max] : v;
                }

                var kind = Str("k", 20);
                if (!MessageTypes.Contains(kind) || kind == "system" || kind == "poll") return null;
                var sender = Str("s", MaxNickname);
                var content = Str("c", MaxMessageText);
                var fileName = Str("f", 120);
                var sticker = Str("e", MaxSticker);
                var imageId = Str("i", 64);
                if (imageId.Length > 0 && !IsFileId(imageId)) return null;
                long fileSize = 0;
                if (m.TryGetProperty("fs", out var fsEl) && fsEl.ValueKind == JsonValueKind.Number && fsEl.TryGetInt64(out var fsv) && fsv > 0)
                    fileSize = Math.Min(fsv, 1L << 30);
                long timeMs = 0;
                if (m.TryGetProperty("t", out var tEl) && tEl.ValueKind == JsonValueKind.Number && tEl.TryGetInt64(out var tv) && tv > 0)
                    timeMs = Math.Min(tv, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

                var entry = new Dictionary<string, object?>();
                if (sender.Length > 0) entry["s"] = sender;
                if (timeMs > 0) entry["t"] = timeMs;
                entry["k"] = kind;
                if (content.Length > 0) entry["c"] = content;
                if (kind == "file" && fileName.Length > 0) entry["f"] = fileName;
                if (kind == "file" && fileSize > 0) entry["fs"] = fileSize;
                // 媒体类（图片/文件/语音）都携带 fileId：接收方的下载鉴权走消息上的引用链
                if ((kind == "image" || kind == "file" || kind == "voice") && imageId.Length > 0) entry["i"] = imageId;
                if (kind == "voice") entry["fs"] = Math.Clamp(fileSize, 1, 600);
                if (kind == "sticker" && sticker.Length > 0) entry["e"] = sticker;
                if (kind == "forward") entry["c"] = "[聊天记录]";
                list.Add(entry);
            }
            return System.Text.Json.JsonSerializer.Serialize(new { v = 1, msgs = list });
        }
        catch { return null; }
    }

    public static bool IsAccount (string? s) => !string.IsNullOrWhiteSpace(s) && AccountPattern.IsMatch(s);

/// <summary>
    /// 打散用户输入里的 HTML/标签结构（存储型 XSS 纵深防御 + 日志/数据投毒）：
    /// 直接删除尖括号，任何 &lt;tag ...&gt; / &lt;img onerror&gt; / &lt;script&gt; 结构都被破坏成惰性文本；
    /// 同时去掉其它控制字符（保留换行/制表符）。
    /// 说明：前端渲染一律走 {{ }} 文本插值（无 v-html/innerHTML），当前展示本身安全；
    /// 但在服务端入口就把原始标记剥离，确保「持久化到 store.json / 审计日志 / 转发」的数据
    /// 永远不含可注入的标记 —— 不依赖未来某处是否记得用插值渲染。
    /// </summary>
    public static string StripHtml (string? s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c == '<' || c == '>') continue;                        // 打散任何标签
            else if (char.IsControl(c) && c != '\n' && c != '\t') continue;
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 打散 HTML/标签结构 + 去控制字符 + 裁剪到上限（用于展示类字段：昵称/签名/地区等）。
    /// 先 StripHtml 破坏任何 &lt;tag&gt;/&lt;img onerror&gt; 结构，再剥控制字符与 Trim。
    /// 超限或为空返回 null = 拒绝。所有经 Clean 持久化的用户字段都不含可注入标记。
    /// </summary>
    /// <summary>
    /// 引用消息的解析结果（服务端归一化后只保留这几个白名单字符串字段，
    /// 丢弃任何嵌套/未知字段 —— 杜绝投毒注入与结构污染）。
    /// </summary>
    public sealed record QuoteInfo(string SenderName, string Text, string SenderId, string OrigType)
    {
        /// <summary>渲染用的 JSON（只含 senderName/text，与前端 MessageBubble 解析契约一致）。</summary>
        public string ToJson() =>
            JsonSerializer.Serialize(new { senderName = SenderName, text = Text });
    }

    /// <summary>
    /// 引用消息的 JSON 归一化 + 字段白名单（前端发 { senderName, text, senderId, origType }）。
    /// 只保留这几个字符串字段并各自剥离 HTML/截断，丢弃任何嵌套/未知字段（防投毒注入、防结构污染），
    /// 非法 JSON 返回 null（按"不引用"降级处理，而非 400 拒绝 —— 保持向后兼容）。
    /// senderId/origType 仅用于服务端业务校验（不可引用自己、群聊只能引用文字），不随渲染 JSON 落库。
    /// </summary>
    public static QuoteInfo? ParseQuote (string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            string senderName = doc.RootElement.TryGetProperty("senderName", out var sn)
                ? StripHtml(sn.GetString() ?? "") ?? "" : "";
            string text = doc.RootElement.TryGetProperty("text", out var tx)
                ? StripHtml(tx.GetString() ?? "") ?? "" : "";
            string senderId = doc.RootElement.TryGetProperty("senderId", out var si)
                ? StripHtml(si.GetString() ?? "") ?? "" : "";
            string origType = doc.RootElement.TryGetProperty("origType", out var ot)
                ? (ot.ValueKind == JsonValueKind.String ? ot.GetString() ?? "" : "") : "";
            // 长度上限：昵称/签名/短字段各自封顶，防超长引用塞进消息里
            if (senderName.Length > MaxNickname) senderName = senderName[..MaxNickname];
            if (text.Length > MaxSignature) text = text[..MaxSignature];
            if (senderId.Length > 40) senderId = "";
            if (origType.Length > 20) origType = "";
            return new QuoteInfo(senderName, text, senderId, origType);
        }
        catch { return null; }
    }

    /// <summary>
    /// 消息正文专用：尖括号转义为 &amp;lt; / &amp;gt; 而不是删除。
    /// 持久化数据从此不含任何原始标签结构（注入面归零），前端渲染时反向解码，
    /// 用户输入什么对方就看到什么。控制字符同样剥离（保留换行/制表符）。
    /// </summary>
    public static string EscapeMessage (string? s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c == '&') sb.Append("&amp;");
            else if (c == '<') sb.Append("&lt;");
            else if (c == '>') sb.Append("&gt;");
            else if (char.IsControl(c) && c != '\n' && c != '\t') { /* 丢控制字符 */ }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    public static string? Clean (string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var stripped = new string(StripHtml(s)
            .Where(c => !char.IsControl(c) || c == '\n' || c == '\t').ToArray()).Trim();
        if (stripped.Length == 0) return null;
        return stripped.Length <= max ? stripped : null; // 超限返回 null = 拒绝
    }

    /// <summary>
    /// 净化上传文件名：只保留基名，去掉目录分隔符、路径穿越、控制字符和危险字符，
    /// 返回 null 表示非法（拒绝）。下载时也永远用服务端存储的 id，不回显用户路径。
    /// </summary>
    /// <summary>
    /// 净化上传文件名：只保留基名，去掉目录分隔符、路径穿越、控制字符和危险字符，
    /// 返回 null 表示非法（拒绝）。下载时永远用服务端存储的 id，不回显用户路径。
    /// 策略（云盘模式）：任何扩展名都允许上传（.exe / .zip / .script / 视频…），
    /// 安全不靠「拦扩展名」，而靠 ① 路径穿越在这里被挡掉 ② 下载侧 Content-Disposition:
    /// attachment + X-Content-Type-Options: nosniff（绝不内联渲染）③ 文件只存不被执行。
    /// </summary>
    public static string? CleanFileName (string? fileName, int max = MaxFileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        var name = Path.GetFileName(fileName.Replace('\\', '/')); // 去掉任何目录部分（防路径穿越）
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..") return null;
        if (name.Length > max) return null;
        if (!SafeFileName.IsMatch(name)) return null;   // 拒绝控制字符与 < > : " | ? * / 等危险字符
        return name;                                      // 扩展名任意放行
    }

    /// <summary>图片/视频消息必须带 imageId，且必须是 32 位十六进制 id（服务端生成）。</summary>
    public static bool IsFileId (string? id) =>
        !string.IsNullOrEmpty(id) && id.Length == 32 && id.All(c => char.IsAsciiHexDigitLower(c) || char.IsAsciiDigit(c));

    /// <summary>
    /// 头像引用校验（改为「上传模型」）：只接受 ① 空（清除头像）② 32 位 hex fileId（服务端 /files/upload 存的图片）。
    /// 彻底不再接受 http/https/data URL —— 外部头像会让每个渲染 &lt;img&gt; 都向外站发请求
    /// （探测该用户是否在线 / 泄露源 IP / cookie 侧信道），且外站图无法做服务端杀毒。
    /// 头像必须走 /files/upload（kind=avatar，魔数 + 杀毒 + 归属）存成 fileId，渲染走带鉴权 blob，
    /// 与其它图片/文件完全一致。兼容性：历史数据里的 32 位 hex fileId 直接继续可用。
    /// </summary>
    public static bool IsAvatarUrl (string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return true; // 空 = 清除头像，合法
        return IsFileId(s.Trim());                    // 只接受 32 位 hex fileId（服务端生成，不可枚举）
    }
}