using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace WeChatWeb.Services;

/// <summary>
/// 电源/冻结看门狗（IHostedService）：针对「VM/云游戏/远程桌面在空闲时被宿主暂停（pause）或
/// 断网」这一类**应用层无法根治、但能被服务端自证**的问题。
///
/// 背景（实测结论）：前端「后台超时、切回前台恢复」的根因**不是** GUI 窗口前后台本身
/// （最小化时环回+局域网 /health 全 200、42-68ms，与前台无差别），而是「切到后台」这一刻
/// 宿主/VM 进入空闲 → 宿主对空闲虚拟机做暂停/降频/虚拟网卡断流 → 外部浏览器「超时」（链路层
/// 被吞，而非服务 500/拒绝）。VM 内部的 Windows 睡眠已设为「永不」，故根因在宿主侧。
///
/// 服务端能做的两件克制的事（都不碰聊天数据、不影响真实请求）：
/// ① **自环回保活**：每 <see cref="KeepAliveIntervalSec"/> 秒打一次本机 /health，
///    给整个 VM 维持一点持续的「活动」信号，降低宿主把它判成空闲而暂停的概率。
///    /health 已被 RequestLoggerMiddleware 的 SkippedPaths 跳过 → 不污染请求日志；
///    失败（如当前确实没网）静默跳过，绝不影响主流程。
/// ② **冻结侦测**：每 15s 采样墙钟 + 单调时钟。正常相邻两次采样间隔应≈15s；
///    若偏差 > 阈值（说明中间 VM 被挂起后又被恢复、时钟发生跳变），把「冻结窗口」
///    写进 logs/power.jsonl（轮转 5MB）。下次再「后台超时」，读这里即可一锤定音：
///    是「进程被宿主冻了 N 秒」，而不是应用崩了。
///
/// 环境变量（可选覆盖，默认值对无外网/内网 VM 友好）：
/// ・WX_KEEPALIVE_ENABLED   默认 true
/// ・WX_KEEPALIVE_INTERVAL  秒，默认 60（下限 10）
/// ・WX_FREEZE_DETECTOR     默认 true
/// ・WX_FREEZE_THRESHOLD    秒，默认 30（相邻采样间隔偏差超此值即记一次冻结）
/// </summary>
public sealed class PowerWatchdogService : IHostedService, IDisposable
{
    private readonly LogService _log;
    private readonly int _keepAliveIntervalSec;
    private readonly bool _keepAliveEnabled;
    private readonly bool _freezeDetectorEnabled;
    private readonly int _freezeThresholdSec;

    private CancellationTokenSource _cts = new();
    private Task? _keepAliveTask;
    private Task? _freezeTask;

    // 冻结侦测基准：墙钟与单调时钟
    private DateTime _lastUtc = DateTime.UtcNow;
    private long _lastMonoMs = Environment.TickCount64;
    private readonly object _gate = new();

    /// <summary>power.jsonl 轮转上限（与 LogService 约定一致，5MB 保留最近一份 .1）。</summary>
    private const long PowerLogMaxBytes = 5L * 1024 * 1024;

    public PowerWatchdogService(LogService log)
    {
        _log = log;
        _keepAliveEnabled = BoolEnv("WX_KEEPALIVE_ENABLED", true);
        _keepAliveIntervalSec = IntEnv("WX_KEEPALIVE_INTERVAL", 60, 10, 3600);
        _freezeDetectorEnabled = BoolEnv("WX_FREEZE_DETECTOR", true);
        _freezeThresholdSec = IntEnv("WX_FREEZE_THRESHOLD", 30, 5, 86400);
    }

    private static bool BoolEnv(string key, bool def)
    {
        var v = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(v)) return def;
        return bool.TryParse(v, out var b) ? b : def;
    }

    private static int IntEnv(string key, int def, int min, int max)
    {
        var v = Environment.GetEnvironmentVariable(key);
        return int.TryParse(v, out var n) ? Math.Clamp(n, min, max) : def;
    }

    private static string PowerLogPath =>
        Path.Combine(Directory.GetCurrentDirectory(), "logs", "power.jsonl");

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _lastUtc = DateTime.UtcNow;
        _lastMonoMs = Environment.TickCount64;

        if (_keepAliveEnabled)
            _keepAliveTask = Task.Run(() => KeepAliveLoopAsync(_cts.Token));
        if (_freezeDetectorEnabled)
            _freezeTask = Task.Run(() => FreezeDetectorLoopAsync(_cts.Token));

        _log.Write("power", new
        {
            ts = DateTime.UtcNow,
            evt = "watchdog-start",
            keepAlive = _keepAliveEnabled,
            keepAliveIntervalSec = _keepAliveIntervalSec,
            freezeDetector = _freezeDetectorEnabled,
            freezeThresholdSec = _freezeThresholdSec,
            note = "后台超时/前台恢复诊断：若宿主把 VM 空闲暂停，见本文件 freeze 事件；根因在宿主侧，非应用崩溃。",
        });
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try { _cts.Cancel(); } catch { /* 已取消 */ }
        var list = new List<Task>();
        if (_keepAliveTask != null) list.Add(_keepAliveTask);
        if (_freezeTask != null) list.Add(_freezeTask);
        if (list.Count > 0)
        {
            try { await Task.WhenAll(list); } catch { /* 收尾不阻断 */ }
        }
        _log.Write("power", new { ts = DateTime.UtcNow, evt = "watchdog-stop" });
    }

    /// <summary>自环回保活：每 N 秒打一次本机 /health（不依赖外网、不污染请求日志）。</summary>
    private async Task KeepAliveLoopAsync(CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(_keepAliveIntervalSec), ct); }
            catch (OperationCanceledException) { break; }

            // 保活目标：优先取 WX_KEEPALIVE_URL，默认本机环回 /health（一定通、不受外网影响）
            var url = Environment.GetEnvironmentVariable("WX_KEEPALIVE_URL")
                      ?? "http://127.0.0.1:30011/health";
            try
            {
                var resp = await http.GetAsync(url, HttpCompletionOption.ResponseContentRead, ct);
                resp.Dispose();
            }
            catch
            {
                // 保活失败（当前确实没网/服务未监听）属正常：静默跳过，不影响主流程，
                // 下一轮重试。绝不把异常抛出去（看门狗挂了不能连累服务）。
            }
        }
    }

    /// <summary>
    /// 冻结侦测：每 15s 采样。墙钟偏差或单调时钟偏差远超采样间隔 → 记一次 freeze 事件
    /// （窗口 = 偏差 - 采样间隔，近似「被挂起的时长」）。
    /// </summary>
    private async Task FreezeDetectorLoopAsync(CancellationToken ct)
    {
        const int sampleSec = 15;
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(sampleSec), ct); }
            catch (OperationCanceledException) { break; }

            var nowUtc = DateTime.UtcNow;
            var nowMono = Environment.TickCount64;
            lock (_gate)
            {
                var wallGapSec = (nowUtc - _lastUtc).TotalSeconds;         // 墙钟经过的秒
                var monoGapSec = (nowMono - _lastMonoMs) / 1000.0;         // 单调时钟经过的秒
                _lastUtc = nowUtc;
                _lastMonoMs = nowMono;

                // 正常应≈15s；偏差 = 实际 - 采样间隔。> 阈值即认为中间发生了挂起/时钟跳变。
                var wallFreeze = wallGapSec - sampleSec;
                var monoFreeze = monoGapSec - sampleSec;
                var freeze = Math.Max(wallFreeze, monoFreeze);
                if (freeze >= _freezeThresholdSec)
                {
                    var ev = new
                    {
                        ts = nowUtc,
                        evt = "freeze",
                        // 估算的被挂起时长（秒）：墙钟跳 与 单调时钟停 取大者更接近真实冻结
                        frozenSec = (int)freeze,
                        wallGapSec = (int)wallGapSec,
                        monoGapSec = (int)monoGapSec,
                        note = "VM/宿主疑似把本进程挂起后恢复（时钟跳变）。外部访问在此期间表现为「超时」，非应用崩溃。",
                    };
                    WritePowerLog(ev);
                }
            }
        }
    }

    private void WritePowerLog(object record)
    {
        try
        {
            var path = PowerLogPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (System.IO.File.Exists(path) && new FileInfo(path).Length > PowerLogMaxBytes)
            {
                var archive = path + ".1";
                if (System.IO.File.Exists(archive)) System.IO.File.Delete(archive);
                System.IO.File.Move(path, archive);
            }
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record) + Environment.NewLine);
            lock (_gate)
            {
                using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                fs.Write(bytes, 0, bytes.Length);
            }
        }
        catch { /* 看门狗写日志失败也不能影响服务 */ }
    }

    public void Dispose() => _cts.Dispose();
}
