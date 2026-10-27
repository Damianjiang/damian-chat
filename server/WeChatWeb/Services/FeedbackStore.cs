using System.Text.Json;

namespace WeChatWeb.Services;

/// <summary>
/// 用户问题反馈（data/feedback.jsonl，追加式 JSONL 持久化）：
/// 客户端「设置 → 问题反馈」提交后写入；管理端（环回 + 密钥）读取并复制。
/// 每条记录带时间戳、提交用户、正文与客户端快照（版本 / UA / 设备）。
/// 追加写 + 全量读，读用 FileShare.Read 不挡写（与 LogService 同约定）。
/// </summary>
public sealed class FeedbackStore
{
    private readonly object _lock = new();
    private readonly string _path;

    /// <summary>正文上限，与前端输入框 maxlength 一致（超限拒绝，不截断）。</summary>
    public const int MaxBody = 2000;

    /// <summary>
    /// 客户端快照（版本 / UA / 设备 / 最近日志）上限：超限**截断**而非拒收。
    /// 此前 client 字段全裸、无长度上限 —— 攻击者可发超大 client 灌爆 feedback.jsonl
    /// （与 /api/reports 修过的「日志资源耗尽」同一类 bug）。正常前端快照 ≤ 数十 KB，
    /// 64KB 足够，再大一律截断。
    /// </summary>
    public const int MaxClient = 65536;

    /// <summary>单文件上限（超限轮转保留最近一份 .1，与 LogService 约定一致），防无界增长耗尽磁盘。</summary>
    public const long MaxBytes = 5L * 1024 * 1024;

    public FeedbackStore()
    {
        _path = Path.Combine(Directory.GetCurrentDirectory(), "data", "feedback.jsonl");
    }

    public sealed record FeedbackItem(string Id, DateTime CreatedAt, string? User, string? Nickname, string Body, string? Client);

    /// <summary>追加一条反馈。返回 null = 成功；否则为可读错误文案。</summary>
    public string? Add(string? user, string? nickname, string? body, string? client)
    {
        var cleanBody = Sanitize(body);
        if (cleanBody.Length == 0) return "反馈内容不能为空";
        if (cleanBody.Length > MaxBody) return "反馈内容过长";
        // 客户端快照去 HTML + 截断到硬上限：防超大 payload 灌爆 JSONL（资源耗尽型 DoS）。
        var clientClean = Truncate(Sanitize(client), MaxClient);

        var id = "fb-" + Guid.NewGuid().ToString("N")[..12];
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                RotateIfLarge(_path);
                var rec = new
                {
                    ts = DateTime.UtcNow,
                    id,
                    user,
                    nickname,
                    body = cleanBody,
                    client = clientClean,
                };
                using var fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
                using var sw = new StreamWriter(fs);
                sw.Write(JsonSerializer.Serialize(rec));
                sw.Write(Environment.NewLine);
            }
            catch
            {
                // 写盘失败（只读环境 / 磁盘忙）必须如实上报，不能假装成功让前端误以为已提交
                return "写入失败，请稍后再试";
            }
        }
        return null;
    }

    /// <summary>超长截断（保留前 N 个字符）；短于上限原样返回。</summary>
    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max];

    /// <summary>
    /// 文件超过 <see cref="MaxBytes"/> 时轮转：当前文件移成 .1（覆盖旧 .1），下次追加写到新文件。
    /// 与 LogService 的轮转约定一致，防止 feedback.jsonl 长期无界增长耗尽磁盘。
    /// </summary>
    private static void RotateIfLarge(string path)
    {
        if (!System.IO.File.Exists(path)) return;
        if (new FileInfo(path).Length <= MaxBytes) return;
        var archive = path + ".1";
        if (System.IO.File.Exists(archive)) System.IO.File.Delete(archive);
        System.IO.File.Move(path, archive);
    }

    /// <summary>全量读取（按时间倒序，最新的在前），供管理端查看/复制。轮转后的归档 .1 一并读入（更旧的放后面）。</summary>
    public List<FeedbackItem> List()
    {
        lock (_lock)
        {
            var items = new List<FeedbackItem>();
            foreach (var p in FeedbackFiles())
                items.AddRange(ReadAll(p));
            return items.OrderByDescending(x => x.CreatedAt).ToList();
        }
    }

    /// <summary>
    /// 按 id 删除一条反馈（当前文件 + 归档 .1 都检索，把**其余原始行**整体重写回当前文件、清空归档）。
    /// 供管理端清理已处理的反馈。保留行按**原字节**写回（不重序列化 → 时间戳/字段零漂移）。
    /// 与 Add/List 共用同一把锁；写盘用 FileShare.Read（与本类读约定一致，不挡管理端并发读）。
    /// 返回 null = 成功；否则为可读错误文案。
    /// </summary>
    public string? Delete(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "id 不能为空";
        var needle = JsonIdOf(id);
        lock (_lock)
        {
            try
            {
                var kept = new List<string>();
                var matched = false;
                foreach (var p in FeedbackFiles())
                {
                    foreach (var line in File.ReadLines(p))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        if (line.Contains(needle)) { matched = true; continue; } // 命中 → 丢弃该行
                        kept.Add(line);
                    }
                }
                if (!matched)
                    return "未找到（可能已被删除或更早归档）";

                // 保留行重写回当前文件（原字节），归档 .1 清空（其行已并入当前文件）
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                using var fs = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.Read);
                using var sw = new StreamWriter(fs);
                foreach (var line in kept)
                {
                    sw.Write(line);
                    sw.Write(Environment.NewLine);
                }
                var archive = _path + ".1";
                if (System.IO.File.Exists(archive)) System.IO.File.Delete(archive);
                return null;
            }
            catch
            {
                return "删除失败，请稍后再试";
            }
        }
    }

    /// <summary>构造「精确匹配某条 id」的 JSONL 片段，避免误删 id 是别的 id 前缀/子串的行。</summary>
    private static string JsonIdOf(string id)
    {
        // 写入字段顺序固定为 ts, id, user, ... → 用 "\"id\":\"<id>\"" 子串精确命中
        var jsonEscaped = JsonSerializer.Serialize(id).Trim('"'); // id 是简单字符串，转义无副作用
        return "\"id\":\"" + jsonEscaped + "\"";
    }

    /// <summary>当前文件 + 归档 .1（若存在）；归档更早，排在后面。</summary>
    private IEnumerable<string> FeedbackFiles()
    {
        if (File.Exists(_path)) yield return _path;
        var archive = _path + ".1";
        if (File.Exists(archive)) yield return archive;
    }

    private static List<FeedbackItem> ReadAll(string path)
    {
        var list = new List<FeedbackItem>();
        if (!File.Exists(path)) return list;
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var o = JsonSerializer.Deserialize<Rec>(line);
                if (o == null) continue;
                var created = DateTime.TryParse(o.ts, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : DateTime.MinValue;
                list.Add(new FeedbackItem(
                    o.id ?? "",
                    created,
                    o.user,
                    o.nickname,
                    o.body ?? "",
                    o.client));
            }
        }
        catch { /* 坏数据忽略：返回能读到的部分 */ }
        return list;
    }

    /// <summary>JSONL 行 DTO（与写入字段一一对应；DateTime 序列化为 ISO 字符串）。</summary>
    private sealed class Rec
    {
        public string? ts { get; set; }
        public string? id { get; set; }
        public string? user { get; set; }
        public string? nickname { get; set; }
        public string? body { get; set; }
        public string? client { get; set; }
    }

    /// <summary>
    /// 打散用户输入里的 HTML/控制字符（存储型 XSS + 日志/数据投毒纵深防御）：
    /// 直接复用 Validate.StripHtml（删尖括号 + 去控制字符，保留换行/制表符），
    /// 确保持久化与复制出来的反馈永远不含可注入标记。
    /// </summary>
    private static string Sanitize(string? s) =>
        Validate.StripHtml(s ?? "").Trim();
}
