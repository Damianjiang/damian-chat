using System.Collections.Concurrent;
using WeChatWeb.Services;

namespace WeChatWeb.Middleware;

/// <summary>
/// 全站流量防火墙（挂在中间件管线最前，先于一切路由/静态/蜜罐/限流）：
/// 防"黑客刷流量"——多 IP 代理池、下载型 DDoS、慢连接灌水。
///
/// 三个维度（按 IP 固定 1 分钟窗口），阈值全部读 SecuritySettings（后台可改、即时生效）：
///  ① 请求速率 FwRequestsPerMinute：1 分钟某 IP 累计请求超限 → 429
///  ② 并发连接 FwMaxConcurrent：同时未结束请求超限 → 429
///  ③ 下行带宽 FwBytesPerMinute：1 分钟某 IP 累计响应字节超限 → 429
///  自动封禁：1 分钟被 429 次数达到 FwBanThreshold → 封禁 FwBanMinutes 分钟
///
/// 关键设计：
/// ・正常用户（多标签/刷新/朋友圈/文件下载）远达不到 1000 req/min、20 并发、500MB/min，不误伤
/// ・环回（127.0.0.1 / ::1，本机 GUI 管理 API）完全豁免
/// ・被限响应统一 429 + Retry-After + 通用 JSON，服务端记 firewall 日志留痕
/// ・桶超 10000 才低频清理过期项，避免无界内存增长
/// </summary>
public sealed class TrafficFirewallMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SecuritySettings _sec;
    private readonly LogService _log;

    // 每 IP：1 分钟窗口请求计数 / 下行字节 / 窗口起点
    private readonly ConcurrentDictionary<string, (int Reqs, long Bytes, DateTime WindowStart)> _hits = new();
    // 每 IP：当前并发未结束请求数（进入 +1，结束 -1）
    private readonly ConcurrentDictionary<string, int> _concurrent = new();
    // 每 IP：1 分钟窗口内累计 429 次数（用于自动封禁）
    private readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> _denyHits = new();
    // 封禁到期的 UTC 时间（ip → 封到什么时候）
    private readonly ConcurrentDictionary<string, DateTime> _bannedUntil = new();
    // 每 IP 最近一次活跃时间（用于并发桶清理，不能清掉还有活跃连接的 IP）
    private readonly ConcurrentDictionary<string, DateTime> _lastActive = new();

    public TrafficFirewallMiddleware (RequestDelegate next, SecuritySettings sec, LogService log)
    {
        _next = next;
        _sec = sec;
        _log = log;
    }

    /// <summary>返回 null 表示环回豁免；否则返回 IP 字符串作为字典键。</summary>
    private static string? IpKey (HttpContext context)
    {
        var remote = context.Connection.RemoteIpAddress;
        // 环回豁免：本机 GUI 管理 API + 本机测试不走防火墙（只挡外部刷量）
        if (remote != null && System.Net.IPAddress.IsLoopback(remote))
            return null;
        var ip = remote?.ToString() ?? "unknown";
        if (ip is "::1" or "0:0:0:0:0:0:0:1" or "unknown")
            return null;
        return ip;
    }

    private bool Banned (string ip, DateTime now, out DateTime until)
    {
        until = default;
        if (_bannedUntil.TryGetValue(ip, out var u))
        {
            if (now >= u) { _bannedUntil.TryRemove(ip, out _); return false; }
            until = u;
            return true;
        }
        return false;
    }

    private async Task RejectAsync (HttpContext context, int retryAfter, string reason, string ip)
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers["Retry-After"] = Math.Max(1, retryAfter).ToString();
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync("{\"error\":\"访问过于频繁，请稍后再试\"}");
        try
        {
            _log.Write("firewall", new { ts = DateTime.UtcNow, ip, reason });
        }
        catch { /* 日志失败不影响防火墙本身 */ }
    }

    public async Task InvokeAsync (HttpContext context)
    {
        // ★ 不再对蜜罐路径完全豁免（上一版的 full-exempt 有个缺口：
        // 攻击者开 500 个并发 /.env，每个被 tarpit 拖 30s → 占满 Kestrel 500 连接 → 真实用户全断。
        // 改为蜜罐路径也走下方并发/速率/带宽限制：
        //  - 少量扫诱饵（5~50 个 .env/.git/admin/actuator）远低于 1000 req/min + 20 并发 → 正常进蜜罐
        //  - 大量刷蜜罐（>1000 req/min 或 >20 并发）→ 429 + 自动封禁（防 tarpit 变 DoS 放大器）
        // 蜜罐中间件在防火墙下游，能收到的路径仍然获得完整假数据 + tarpit + 金丝雀留痕。
        var key = IpKey(context);
        if (key == null)
        {
            await _next(context);
            return;
        }

        var now = DateTime.UtcNow;
        _lastActive.AddOrUpdate(key, now, (_, _old) => now);

        // ① 自动封禁优先
        if (Banned(key, now, out var banUntil))
        {
            var bRetry = (int)Math.Ceiling((banUntil - now).TotalSeconds);
            await RejectAsync(context, bRetry, "auto-banned", key);
            return;
        }

        // ② 并发上限
        var concurrentNow = _concurrent.AddOrUpdate(key, _ => 1, (_, v) => v + 1);
        var overConcurrent = concurrentNow > Math.Max(2, _sec.FwMaxConcurrent);

        // ③ 请求速率 + 带宽（1 分钟窗口）
        var entry = _hits.AddOrUpdate(key,
            _ => (1, 0L, now),
            (_, old) => now - old.WindowStart >= TimeSpan.FromMinutes(1)
                ? (1, 0L, now)
                : (old.Reqs + 1, old.Bytes, old.WindowStart));

        bool overRate = entry.Reqs > Math.Max(100, _sec.FwRequestsPerMinute);
        bool overBandwidth = entry.Bytes > Math.Max(1L * 1024 * 1024, _sec.FwBytesPerMinute);

        var retry = (int)Math.Ceiling((entry.WindowStart + TimeSpan.FromMinutes(1) - now).TotalSeconds);

        if (overConcurrent || overRate || overBandwidth)
        {
            // 归还并发计数（本请求不执行）
            _concurrent.AddOrUpdate(key, _ => 0, (_, v) => Math.Max(0, v - 1));

            var reason = overConcurrent ? "max-concurrent" : overBandwidth ? "bandwidth" : "rate-limit";
            await RejectAsync(context, Math.Max(1, retry), reason, key);

            // 记录 429 次数，达到阈值触发自动封禁
            var dEntry = _denyHits.AddOrUpdate(key,
                _ => (1, now),
                (_, old) => now - old.WindowStart >= TimeSpan.FromMinutes(1)
                    ? (1, now)
                    : (old.Count + 1, old.WindowStart));
            if (dEntry.Count >= Math.Max(1, _sec.FwBanThreshold))
            {
                var banEnd = now.AddMinutes(Math.Max(1, _sec.FwBanMinutes));
                _bannedUntil.AddOrUpdate(key, banEnd, (_, oldU) => oldU > banEnd ? oldU : banEnd);
                _denyHits.TryRemove(key, out _);
                _log.Write("firewall", new { ts = DateTime.UtcNow, ip = key, reason = "auto-ban", minutes = _sec.FwBanMinutes });
            }

            Cleanup(now);
            return;
        }

        // 通过 → 执行下游，把响应下行字节累加进带宽窗口
        var origBody = context.Response.Body;
        var counter = new ByteCounter(origBody);
        context.Response.Body = counter;
        try
        {
            await _next(context);
        }
        finally
        {
            context.Response.Body = origBody;
            if (counter.Count > 0)
            {
                var writtenBytes = counter.Count;
                _hits.AddOrUpdate(key,
                    _ => (0, writtenBytes, now),
                    (_, old) => now - old.WindowStart >= TimeSpan.FromMinutes(1)
                        ? (0, writtenBytes, now)
                        : (old.Reqs, old.Bytes + writtenBytes, old.WindowStart));
            }
        }

        // 归还并发计数
        _concurrent.AddOrUpdate(key, _ => 0, (_, v) => Math.Max(0, v - 1));

        Cleanup(now);
    }

    /// <summary>低频清理（任一桶超 10000 才触发）：清窗口超 5 分钟的、并发桶对应 IP 已 5 分钟未活跃的。</summary>
    private void Cleanup (DateTime now)
    {
        if (_hits.Count <= 10_000 && _concurrent.Count <= 10_000 && _denyHits.Count <= 10_000)
            return;

        var stale = TimeSpan.FromMinutes(5);
        foreach (var kv in _hits.Where(kv => now - kv.Value.WindowStart > stale).ToList())
            _hits.TryRemove(kv.Key, out _);

        foreach (var kv in _concurrent.Where(kv =>
            kv.Value <= 0
            || (_lastActive.TryGetValue(kv.Key, out var la) && now - la > stale)).ToList())
            _concurrent.TryRemove(kv.Key, out _);

        foreach (var kv in _denyHits.Where(kv => now - kv.Value.WindowStart > stale).ToList())
            _denyHits.TryRemove(kv.Key, out _);

        foreach (var kv in _bannedUntil.Where(kv => now >= kv.Value).ToList())
            _bannedUntil.TryRemove(kv.Key, out _);

        foreach (var kv in _lastActive.Where(kv => now - kv.Value > stale).ToList())
            _lastActive.TryRemove(kv.Key, out _);
    }

    /// <summary>统计下行写入字节数的响应流包装（线程安全）。</summary>
    private sealed class ByteCounter : Stream
    {
        private readonly Stream _inner;
        private long _count;
        private readonly object _gate = new();

        public long Count { get { lock (_gate) return _count; } }

        public ByteCounter (Stream inner) { _inner = inner; }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read (byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek (long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength (long value) => throw new NotSupportedException();
        public override void Flush () => _inner.Flush();

        public override void Write (byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
            lock (_gate) { _count += count; }
        }

        public override Task WriteAsync (byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            lock (_gate) { _count += count; }
            return _inner.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync (ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            lock (_gate) { _count += buffer.Length; }
            return _inner.WriteAsync(buffer, cancellationToken);
        }
    }
}
