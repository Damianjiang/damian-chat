namespace WeChatWeb.Services;

/// <summary>
/// 下载限速流（按「字节/秒」限制吞吐，管理端可调；默认 0 = 不限）。
///
/// 设计：滑动窗口 —— 记录过去 1 秒实际读出的字节数，若超过目标速率就按
/// 「超出量 / 速率」估算等待毫秒并 Sleep，把单次下载的上行占用压到上限以内
/// （一个用户拉 1GB 大文件不会把整站上行拖死）。
///
/// 关键不变量：
/// ・Seek / Length / Position / CanSeek 全部转发给 base stream —— 浏览器 Range
///   断点续传照常可用（大文件中途断开重连不会从头再来）。
/// ・只对读限速，写侧返回 false（下载流是只读的）。
/// ・bytesPerSec &lt;= 0 时整个 Throttle 是 no-op，走最快路径（与 PhysicalFile 等效）。
/// </summary>
public sealed class ThrottleStream : Stream
{
    private readonly Stream _base;
    private readonly long _bytesPerSec;
    private readonly double _windowMs;

    // 滑动窗口历史：(时刻, 该次读出的字节数)
    private readonly Queue<(long At, int Bytes)> _history = new();
    private readonly object _gate = new();

    public ThrottleStream(Stream baseStream, long bytesPerSecond)
    {
        _base = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
        _bytesPerSec = bytesPerSecond;
        _windowMs = 1000.0;   // 1 秒统计窗口
    }

    public override bool CanRead => _base.CanRead;
    public override bool CanSeek => _base.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _base.Length;

    public override long Position
    {
        get => _base.Position;
        set => _base.Position = value;
    }

    public override long Seek(long offset, SeekOrigin origin) => _base.Seek(offset, origin);
    public override void Flush() => _base.Flush();
    public override void SetLength(long value) => _base.SetLength(value);

    // 下载流是只读的：写方法一律拒绝
    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("ThrottleStream 只读");
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Task.FromException(new NotSupportedException("ThrottleStream 只读"));

    public override int Read(byte[] buffer, int offset, int count)
    {
        var n = _base.Read(buffer, offset, count);
        Throttle(n);
        return n;
    }

    // ASP.NET 的 FileStreamResult 走 CopyToAsync → ReadAsync(Memory<byte>)，
    // 必须 override 这一版才能拦住（否则限速被绕过）。
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var n = await _base.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Throttle(n);
        return n;
    }

    // 兼容走 byte[] 的路径（旧 CopyTo 同步 / 某些 WriteFileContents 回退）
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var n = await _base.ReadAsync(new Memory<byte>(buffer, offset, count), cancellationToken).ConfigureAwait(false);
        Throttle(n);
        return n;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _base.Dispose();
        base.Dispose(disposing);
    }

    private void Throttle (int bytesRead)
    {
        if (_bytesPerSec <= 0 || bytesRead <= 0) return;   // 不限速：完全 no-op
        int sleepMs = 0;
        lock (_gate)
        {
            var now = Environment.TickCount64;
            while (_history.Count > 0 && now - _history.Peek().At > _windowMs)
                _history.Dequeue();
            long windowBytes = 0;
            foreach (var h in _history) windowBytes += h.Bytes;
            _history.Enqueue((now, bytesRead));

            // 统计窗口是 1 秒（_windowMs=1000），窗口内允许的总字节 = 速率本身（字节/秒）
            var allowed = _bytesPerSec;
            var over = windowBytes + bytesRead - allowed;
            if (over > 0)
            {
                // 超出量 ÷ 速率 = 需要等待的秒数；夹到 int 上限防 Thread.Sleep 溢出
                var secs = (double)over / _bytesPerSec;
                sleepMs = (int)Math.Min(int.MaxValue, Math.Ceiling(secs * 1000.0));
            }
        }
        if (sleepMs > 0) Thread.Sleep(sleepMs);
    }
}
