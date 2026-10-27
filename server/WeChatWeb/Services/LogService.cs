using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace WeChatWeb.Services;

/// <summary>
/// 追加式 JSONL 日志。生产环境可替换为 Serilog/EF。
/// 每个日志文件带锁 + 体积上限（超限轮转，保留最近文件）。
/// </summary>
public sealed class LogService
{
    private readonly IWebHostEnvironment _env;
    private readonly string _dir;
    private readonly ConcurrentDictionary<string, object> _locks = new();

    /// <summary>
    /// 按日志名称分级上限：requests 是高频轮询端点产生的（已过滤掉 admin/online 等），
    /// 但仍比 auth 日志量大 → 2 MB 就够；其余（auth / files 等）保持 5 MB。
    /// </summary>
    private long MaxBytesFor(string name) =>
        name == "requests" ? 2L * 1024 * 1024 : 5L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
    };

    public LogService(IWebHostEnvironment env)
    {
        _env = env;
        _dir = Path.Combine(env.ContentRootPath, "logs");
        Directory.CreateDirectory(_dir);
    }

    public string Dir => _dir;

    /// <summary>写失败只报一次（按文件计），避免每次请求都往控制台灌一条。</summary>
    private readonly ConcurrentDictionary<string, bool> _warned = new();

    /// <summary>
    /// 追加一行日志。
    ///
    /// ★ 两个必须守住的点：
    /// 1) 旁路设施绝不许抛出去：调用方（RequestLoggerMiddleware）是在响应体发出**之后**
    ///    才写日志的，此时任何异常都会让 Kestrel 中断连接 —— 客户端表现为
    ///    net::ERR_INCOMPLETE_CHUNKED_ENCODING 但状态行已经是 200，极难定位。
    /// 2) 必须显式 FileShare.ReadWrite：管理端日志页会并发读（Tail / ReadLines），
    ///    默认共享模式下读句柄会把写拒绝掉，抛 UnauthorizedAccessException。
    /// </summary>
    public void Write(string name, object record)
    {
        var lockObj = _locks.GetOrAdd(name, _ => new object());
        lock (lockObj)
        {
            try
            {
                var path = Path.Combine(_dir, $"{name}.jsonl");
                if (System.IO.File.Exists(path) && new FileInfo(path).Length > MaxBytesFor(name))
                {
                    // 轮转：保留最近一份 .1
                    var archive = path + ".1";
                    if (System.IO.File.Exists(archive)) System.IO.File.Delete(archive);
                    System.IO.File.Move(path, archive);
                }
                var bytes = Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(record, JsonOpts) + Environment.NewLine);
                using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                fs.Write(bytes, 0, bytes.Length);
            }
            catch (Exception ex)
            {
                if (_warned.TryAdd(name, true))
                {
                    Console.Error.WriteLine($"[log] 写入 {name}.jsonl 失败（后续不再重复）：{ex.GetType().Name}: {ex.Message}");
                }
            }
        }
    }

    public List<string> Tail(string name, int lines)
    {
        var path = Path.Combine(_dir, $"{name}.jsonl");
        if (!System.IO.File.Exists(path)) return new();
        try
        {
            // 同样用 ReadWrite 共享：读的时候不能把正在追加的写挡在外面
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs, Encoding.UTF8);
            var all = new List<string>();
            string? line;
            while ((line = sr.ReadLine()) != null) all.Add(line);
            return all.TakeLast(lines).ToList();
        }
        catch
        {
            return new(); // 读不出来就当没有，日志页不能因此报错
        }
    }
}
