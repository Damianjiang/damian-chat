using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using Microsoft.AspNetCore.Connections;

namespace WeChatWeb.Middleware;

/// <summary>
/// 同一端口同时接受 TLS 与明文 HTTP 的连接中间件（首字节嗅探）。
///
/// ★ 要解决的线上故障：
///   TLS 模式下主端口只做 TLS 监听。用户手输 / 收藏夹里残留 <c>http://域名:30011</c> 时，
///   浏览器把明文请求发给 TLS 端口 → 服务端等 ClientHello 等不到，直接断连 →
///   浏览器只显示 <c>net::ERR_EMPTY_RESPONSE</c>（连一个"请用 https"的提示都没有）。
///   一个端口不可能既做 TLS 又做明文 HTTP，所以这里在 TLS 中间件**之前**先看第一个字节：
///     • 0x16（TLS Handshake）→ 原样交回 Kestrel 的 HTTPS 中间件，正常握手；
///     • 其它（G…/P…/H… 等明文请求）→ 直接回 307 到 https://同主机:同端口/原路径。
///   这样主端口对两种协议都"有响应"，明文访问被强制升级，不会再出现空响应。
///
/// 安全边界：只读取首包用于判断，TLS 分支一个字节都不消费（AdvanceTo(start) 后交给下游），
/// 明文分支只回一条固定 307、不解析 body、不接触任何业务/鉴权逻辑。
/// </summary>
public static class PlainHttpRedirectOnTls
{
    /// <summary>TLS Handshake 记录类型（ClientHello 的首字节）。</summary>
    private const byte TlsHandshake = 0x16;

    /// <summary>最多读取这么多字节用于解析请求行 + Host 头（正常浏览器首包远小于此）。</summary>
    private const int MaxProbeBytes = 8192;

    public static Func<ConnectionDelegate, ConnectionDelegate> Create (int tlsPort)
        => next => connection => InvokeAsync(connection, next, tlsPort);

    private static async Task InvokeAsync (
        ConnectionContext connection,
        ConnectionDelegate next,
        int tlsPort)
    {
        var input = connection.Transport.Input;
        ReadResult result;
        try
        {
            result = await input.ReadAsync(connection.ConnectionClosed);
        }
        catch
        {
            return; // 连接已被对端关闭：交给 Kestrel 统一回收
        }

        var buffer = result.Buffer;
        if (buffer.Length == 0)
        {
            if (result.IsCompleted) return;
            input.AdvanceTo(buffer.Start, buffer.Start);
            await next(connection);
            return;
        }

        var firstByte = buffer.FirstSpan.Length > 0 ? buffer.FirstSpan[0] : (byte)0;
        if (firstByte == TlsHandshake)
        {
            /* 关键：把「已检查到哪」退回起点、不消费任何字节。
             * PipeReader 的约束：
             *   ① 一次 ReadAsync 之后必须 AdvanceTo 才能再次读（否则下游报 Reading is already in progress）；
             *   ② examined 只能前进不能后退（推到 End 再让 SslStream 后退会抛 InvalidOperationException）。
             * 所以这里 AdvanceTo(start, start)：结束本次读、字节原样留给下游 TLS 握手。 */
            input.AdvanceTo(buffer.Start, buffer.Start);
            await next(connection);
            return;
        }

        // 明文：尽量把请求行 + Host 读全（最多 8KB / 首包结束），拿不到就按本机端口兜底
        for (var i = 0; i < 4 && !result.IsCompleted && buffer.Length < MaxProbeBytes; i++)
        {
            var text = BufferToString(buffer);
            if (text.Contains("\r\n\r\n", StringComparison.Ordinal)) break;
            input.AdvanceTo(buffer.Start, buffer.End);
            try
            {
                result = await input.ReadAsync(connection.ConnectionClosed);
            }
            catch
            {
                return;
            }
            buffer = result.Buffer;
        }

        var raw = BufferToString(buffer);
        var (host, path) = ParseRequestHead(raw);
        input.AdvanceTo(buffer.End); // 明文分支直接结束，不再交给 HTTP 栈

        if (string.IsNullOrWhiteSpace(host))
        {
            // 连 Host 都解析不出来（扫描器/畸形请求）：静默关闭，不提供任何指纹
            connection.Abort(new ConnectionAbortedException("plain HTTP on TLS port"));
            return;
        }

        var hostPart = host.Trim();
        // Host 没带端口时补上当前 TLS 端口（443 不写端口）
        if (!HasPort(hostPart) && tlsPort != 443) hostPart += ":" + tlsPort;

        var location = $"https://{hostPart}{(string.IsNullOrEmpty(path) ? "/" : path)}";
        var response = "HTTP/1.1 307 Temporary Redirect\r\n"
            + "Location: " + location + "\r\n"
            + "Cache-Control: no-store\r\n"
            + "Content-Length: 0\r\n"
            + "Connection: close\r\n"
            + "\r\n";

        try
        {
            await connection.Transport.Output.WriteAsync(Encoding.ASCII.GetBytes(response));
            await connection.Transport.Output.FlushAsync();
        }
        catch
        {
            // 对端已断：忽略
        }
    }

    /// <summary>ReadOnlySequence → ASCII 文本（不依赖可能产生歧义的 ToArray 扩展）。</summary>
    private static string BufferToString (in ReadOnlySequence<byte> sequence)
    {
        if (sequence.IsSingleSegment) return Encoding.ASCII.GetString(sequence.FirstSpan);
        var array = new byte[sequence.Length];
        sequence.CopyTo(array);
        return Encoding.ASCII.GetString(array);
    }

    /// <summary>解析请求行取 path（含 query），再取 Host 头。</summary>
    private static (string? Host, string Path) ParseRequestHead (string raw)
    {
        if (string.IsNullOrEmpty(raw)) return (null, "/");

        var lines = raw.Split("\r\n", StringSplitOptions.None);
        var path = "/";
        if (lines.Length > 0)
        {
            var parts = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[1].StartsWith('/')) path = parts[1];
        }

        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
                return (line.Substring(5).Trim(), path);
            if (line.Length == 0) break; // 头部结束
        }
        return (null, path);
    }

    /// <summary>Host 是否已带端口（IPv6 字面量 [::1] 视作未带端口）。</summary>
    private static bool HasPort (string host)
    {
        if (host.StartsWith('['))
        {
            var close = host.IndexOf(']');
            return close >= 0 && close + 1 < host.Length && host[close + 1] == ':';
        }
        return host.Count(c => c == ':') == 1;
    }
}
