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
    /// 按日志名称分级上限：
    /// ・requests：已跳过 admin/文件下载/健康检查，剩余的是用户真实 API 调用，1 MB 足够。
    /// ・auth / reports / error / power / firewall：2 MB 足够（低频、信息密度高）。
    /// 总量控制：单文件 1~2 MB + .1 归档（最多 4 MB/文件），7 个日志文件共 ≤28 MB。
    /// 加上定期清理（LogCleanupService 7 天过期），存储占用有上限、不会无限涨。
    /// </summary>
    private long MaxBytesFor(string name) =>
        name == "requests" ? 1L * 1024 * 1024 : 2L * 1024 * 1024;

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

    /// <summary>
    /// 删除日志目录里超过保留期的文件（按最后修改时间），供 LogCleanupService 定期调用。
    /// 只删 .jsonl/.log/.1 结尾的文件，绝不误删目录或其它资源；返回删除数量。
    /// </summary>
    public int PurgeOlderThan(TimeSpan retention)
    {
        var cutoff = DateTime.Now - retention;
        var removed = 0;
        try
        {
            foreach (var f in new DirectoryInfo(_dir).GetFiles())
            {
                // 只清理日志类文件：*.jsonl、*.log、*.jsonl.1、*.log.1（轮转归档）
                var n = f.Name;
                if (!(n.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)
                      || n.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
                      || n.EndsWith(".jsonl.1", StringComparison.OrdinalIgnoreCase)
                      || n.EndsWith(".log.1", StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (f.LastWriteTime >= cutoff) continue;
                try
                {
                    f.Delete();
                    removed++;
                }
                catch { /* 占用中下次再清，不影响主流程 */ }
            }
        }
        catch { /* 目录不存在等：忽略 */ }
        return removed;
    }
}

/// <summary>
/// 日志定期清理（IHostedService）：启动 1 小时后每 24 小时把超过 7 天的日志文件删掉。
/// 配合 LogService 的单文件容量上限（1~2 MB + .1 归档），让 logs/ 占用始终有上限，
/// 长期挂机也不会把磁盘写满。
/// </summary>
public sealed class LogCleanupService : IHostedService
{
    private const int RetentionDays = 7;
    private readonly LogService _log;
    private CancellationTokenSource _cts = new();
    private Task? _task;

    public LogCleanupService(LogService log) => _log = log;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _task = Task.Run(() => LoopAsync(_cts.Token));
        return Task.CompletedTask;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromHours(1), ct); }
        catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var n = _log.PurgeOlderThan(TimeSpan.FromDays(RetentionDays));
                if (n > 0)
                    Console.WriteLine($"[log] 已清理 {n} 个超过 {RetentionDays} 天的日志文件");
            }
            catch { /* 清理失败不影响服务 */ }
            try { await Task.Delay(TimeSpan.FromHours(24), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        return _task ?? Task.CompletedTask;
    }
}
