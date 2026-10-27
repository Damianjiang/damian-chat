using System.Text.Json;

namespace WeChatWeb.Services;

/// <summary>
/// 敏感词过滤（单例）。
///
/// 词库文件：data/sensitive.json，结构 <c>{ "words": ["词1", "词2", ...] }</c>。
/// 文件不存在时用一份克制的默认词库初始化并写出（便于后续维护者直接编辑文件扩充）。
///
/// 行为：
/// ① <see cref="Filter"/> 命中则将对应子串替换为等长 <c>*</c> 掩码（掩码而非拒收，体验接近微信）；
/// ② <see cref="Contains"/> 是否含敏感词；
/// ③ 大小写不敏感；线程安全；支持 <see cref="Reload"/> 热加载（管理端未来可用）。
/// </summary>
public sealed class SensitiveWordService
{
    /// <summary>词库文件路径（默认 data/sensitive.json，与 FileStore 的 data 目录保持一致）。</summary>
    private readonly string _file;

    private readonly object _gate = new();
    /// <summary>预小写词条（原词, 小写词），长词优先 —— 避免 Contains/Filter 每次调用重复 ToLower。</summary>
    private List<(string Word, string Lower)> _words = new();

    public SensitiveWordService()
        : this(Path.Combine(Directory.GetCurrentDirectory(), "data", "sensitive.json"))
    {
    }

    /// <summary>测试/指定路径用构造函数。</summary>
    public SensitiveWordService(string file)
    {
        _file = file;
        Reload();
    }

    /// <summary>
    /// 默认词库（克制、得体）：仅放内容安全分类下的常见违规「类别词」做示例占位，
    /// 不含任何明示的污言秽语或仇恨词。真实部署请把业务所需的敏感词填进 data/sensitive.json。
    /// </summary>
    private static readonly string[] DefaultWords =
    {
        "赌博", "诈骗", "色情", "违禁品", "代开发票", "办证",
    };

    /// <summary>当前生效的敏感词（只读快照，供管理端查看）。</summary>
    public IReadOnlyList<string> Words
    {
        get
        {
            lock (_gate)
                return _words.Select(w => w.Word).ToList();
        }
    }

    /// <summary>热加载：重新读取 data/sensitive.json；文件缺失或损坏时退回默认词库。</summary>
    public void Reload()
    {
        HashSet<string> loaded;
        try
        {
            if (File.Exists(_file))
            {
                // 自愈读：读不到（历史遗留空 DACL）时先修 ACL，失败则退回默认词库并告警
                if (!SensitiveFile.TryReadAllText(_file, out var json))
                {
                    Console.Error.WriteLine(
                        $"[敏感词] 警告：无法读取 {_file}，本次使用内置默认词库。" +
                        "请修复该文件权限后重启。");
                    loaded = new HashSet<string>(DefaultWords, StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                var doc = JsonSerializer.Deserialize<SensitiveWordFile>(json);
                var list = doc?.Words ?? new List<string>();
                loaded = new HashSet<string>(
                    list.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w.Trim()),
                    StringComparer.OrdinalIgnoreCase);
                }
            }
            else
            {
                loaded = new HashSet<string>(DefaultWords, StringComparer.OrdinalIgnoreCase);
                Save(loaded); // 写出默认词库，便于后续编辑
            }
        }
        catch
        {
            // 词库损坏或目录只读：退回默认词库（仅内存生效）
            loaded = new HashSet<string>(DefaultWords, StringComparer.OrdinalIgnoreCase);
        }

        // 长词优先，避免「色」「情」先被单独命中而漏掉「色情」整体；同时预小写
        lock (_gate)
            _words = loaded
                .OrderByDescending(w => w.Length)
                .Select(w => (Word: w, Lower: w.ToLowerInvariant()))
                .ToList();
    }

    /// <summary>是否含敏感词（大小写不敏感）。</summary>
    public bool Contains(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        List<(string Word, string Lower)> words;
        lock (_gate) words = _words;
        if (words.Count == 0) return false;

        var lower = text.ToLowerInvariant();
        foreach (var w in words)
        {
            if (w.Word.Length == 0) continue;
            if (lower.Contains(w.Lower, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 过滤并返回掩码后的文本：命中子串替换为等长 <c>*</c>。
    /// 入参为 null/空直接原样返回；词库为空则原样返回。
    /// ★ 性能：只 ToLowerInvariant 一次 + 单次扫描（MaskWord 接收预小写化的 lowerInput），
    ///   不再每个词都全串复制一遍（旧实现词库 N 词 → N 次全串 ToLower，刷屏时纯浪费）。
    /// </summary>
    public string? Filter(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        List<(string Word, string Lower)> words;
        lock (_gate) words = _words;
        if (words.Count == 0) return text;

        var lowerInput = text.ToLowerInvariant();
        var result = text;
        foreach (var w in words)
        {
            if (w.Word.Length == 0) continue;
            result = MaskWord(result, w.Word, w.Lower, lowerInput);
        }
        return result;
    }

    /// <summary>
    /// 从 input 中大小写不敏感地把 word 的所有出现替换为等长 *。
    /// lowerInput 是「原始文本」的预小写化（与 input/result 逐位等长对齐）；
    /// result 是被逐词替换后的串（掩码等长，故位置不变）。
    /// 若某位置已被前一个词掩成 * 则跳过，避免重叠词重复命中。
    /// </summary>
    private static string MaskWord(string result, string word, string lowerWord, string lowerInput)
    {
        var sb = new System.Text.StringBuilder(result.Length);
        var i = 0;
        while (i < result.Length)
        {
            if (result[i] == '*')
            {
                sb.Append(result[i]);
                i++;
            }
            else if (i + lowerWord.Length <= lowerInput.Length
                     && lowerInput.AsSpan(i, lowerWord.Length).SequenceEqual(lowerWord.AsSpan()))
            {
                sb.Append('*', word.Length);
                i += word.Length;
            }
            else
            {
                sb.Append(result[i]);
                i++;
            }
        }
        return sb.ToString();
    }

    private void Save(IEnumerable<string> words)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var doc = new SensitiveWordFile(words.ToList());
            File.WriteAllText(_file, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 只读环境：仅内存生效 */ }
    }

    /// <summary>词库文件结构。</summary>
    private sealed record SensitiveWordFile(List<string>? Words);
}
