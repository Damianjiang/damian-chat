using System.Collections.Concurrent;
using WeChatWeb.Services;

namespace WeChatWeb.Data;

/// <summary>
/// 用户身份（JWT sub）维度的消息洪泛上限 —— 单例（跨请求共享计数器）。
///
/// 为什么必须是单例：
/// ASP.NET Core 默认控制器是 transient（每个 HTTP 请求新建一个 MessagesController 实例），
/// 若把计数器做成控制器实例字段，每次请求都是全新空字典，计数永远积累不起来、检查形同虚设。
/// 所以计数器放这里（DI 单例），控制器每次注入同一个实例，共享同一份并发计数。
///
/// 防的是哪类攻击：
/// ① 攻击者偷到一个合法 JWT（泄露 / 重放 / 绕过人机验证后签出），高频往真实会话灌消息；
/// ② 轮换 IP 池重放 —— IP 维度限流器（ApiRateLimit / 流量防火墙）每个 IP 都卡阈值下、合计却爆量，
///    身份维度按 JWT sub 计数，**换多少个 IP 都躲不掉**。
///
/// 与 IP 防火墙互补：IP 管"多 IP 刷"，这里管"同一身份发太多"。
/// 超限 → 返回 true（调用方回 429 + 记 flood 日志留痕，指向是哪个 user 在滥用）。
/// </summary>
public sealed class MessageFloodLimiter
{
    /// <summary>sub → (1 分钟窗口内发送次数, 窗口起点)。</summary>
    private readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> _byUser = new();

    /// <summary>
    /// 记录一次消息发送并判定是否超限。
    /// <paramref name="limit"/> 为 1 分钟允许的最大条数（>=1）。返回 true = 本次已超限（调用方应 429）。
    /// </summary>
    public bool IsOverLimit (string user, int limit, out int retryAfterSec)
    {
        retryAfterSec = 0;
        if (string.IsNullOrEmpty(user)) return false;
        limit = Math.Max(1, limit);
        var now = DateTime.UtcNow;

        var entry = _byUser.AddOrUpdate(user,
            _ => (1, now),
            (_, old) => now - old.WindowStart >= TimeSpan.FromMinutes(1)
                ? (1, now)
                : (old.Count + 1, old.WindowStart));

        if (entry.Count > limit)
        {
            retryAfterSec = Math.Max(1,
                (int)Math.Ceiling((entry.WindowStart + TimeSpan.FromMinutes(1) - now).TotalSeconds));
            return true;
        }

        // 低频清理（避免无界增长）：超 5000 个子账号时清 5 分钟外的过期窗口
        if (_byUser.Count > 5000)
            foreach (var kv in _byUser.Where(kv => now - kv.Value.WindowStart > TimeSpan.FromMinutes(5)).ToList())
                _byUser.TryRemove(kv.Key, out _);

        return false;
    }
}
