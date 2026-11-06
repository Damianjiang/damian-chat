using System.Collections.Concurrent;

namespace WeChatWeb.Data;

/// <summary>
/// 通用「按身份 + 动作」限流器（单例，跨请求共享计数）。
///
/// 为什么需要它：
///   IP 维度限流（ApiRateLimit / 流量防火墙）只能管「每个 IP 不超过阈值」，
///   攻击者换 IP 池、或拿到一个合法 JWT 后直接并发请求，IP 桶一点都拦不住。
///   MessageFloodLimiter 只保护了「发消息」一条链路，加好友 / 改资料 / 踢设备 /
///   举报反馈 / 拍一拍 这些同样会产生持久化副作用或骚扰的写接口此前完全裸奔。
///
/// 两个原语：
///   • <see cref="IsOverLimit"/> —— 固定窗口计数（scope + user，window 秒内最多 limit 次）。
///   • <see cref="TryCooldown"/>  —— 冷却（key 在 cooldown 内只允许一次），用于「A 对 B 拍一拍」这种
///     需要按「双方关系」而不是按「单方身份」限速的场景。
///
/// 两者都是**先记账再判定**：超限的那次也计入，避免攻击者在阈值边缘反复试探。
/// 计数表低频清理，防止无界增长。
/// </summary>
public sealed class ActionRateLimiter
{
    /// <summary>"scope:user" → (窗口内次数, 窗口起点)。</summary>
    private readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> _windows = new();

    /// <summary>"scope:key" → 冷却到期时刻。</summary>
    private readonly ConcurrentDictionary<string, DateTime> _cooldowns = new();

    /// <summary>
    /// 固定窗口计数：window 秒内同一个 (scope, user) 最多 limit 次。
    /// 返回 true = 本次已超限（调用方应回 429 并带上 Retry-After）。
    /// </summary>
    /// <param name="limit">窗口内允许的最大次数（&lt;1 视为 1）。</param>
    public bool IsOverLimit (string scope, string user, int limit, TimeSpan window, out int retryAfterSec)
    {
        retryAfterSec = 0;
        if (string.IsNullOrEmpty(scope) || string.IsNullOrEmpty(user)) return false;
        if (limit < 1) limit = 1;
        if (window <= TimeSpan.Zero) window = TimeSpan.FromMinutes(1);

        var now = DateTime.UtcNow;
        var key = scope + ":" + user;
        var entry = _windows.AddOrUpdate(key,
            _ => (1, now),
            (_, old) => now - old.WindowStart >= window
                ? (1, now)
                : (old.Count + 1, old.WindowStart));

        Prune(now);

        if (entry.Count <= limit) return false;
        retryAfterSec = Math.Max(1,
            (int)Math.Ceiling((entry.WindowStart + window - now).TotalSeconds));
        return true;
    }

    /// <summary>
    /// 冷却：同一个 (scope, key) 在 cooldown 内只放行一次。
    /// 返回 true = 本次被拒（还在冷却里），retryAfterSec 是剩余秒数。
    /// </summary>
    public bool IsInCooldown (string scope, string key, TimeSpan cooldown, out int retryAfterSec)
    {
        retryAfterSec = 0;
        if (string.IsNullOrEmpty(scope) || string.IsNullOrEmpty(key)) return false;
        if (cooldown <= TimeSpan.Zero) return false;

        var now = DateTime.UtcNow;
        var full = scope + ":" + key;

        // 原子：只有「没有记录」或「记录已过期」才能写入新到期时间
        while (true)
        {
            if (_cooldowns.TryGetValue(full, out var until))
            {
                if (until > now)
                {
                    retryAfterSec = Math.Max(1, (int)Math.Ceiling((until - now).TotalSeconds));
                    return true;
                }
                // 已过期：CAS 换成新的到期时间（并发时只有一路成功）
                if (_cooldowns.TryUpdate(full, now + cooldown, until)) return false;
                continue;
            }
            if (_cooldowns.TryAdd(full, now + cooldown)) return false;
        }
    }

    /// <summary>低频清理：表超过阈值时剔除已过期条目，避免字典无界增长。</summary>
    private void Prune (DateTime now)
    {
        if (_windows.Count > 5000)
            foreach (var kv in _windows)
                if (now - kv.Value.WindowStart > TimeSpan.FromMinutes(10))
                    _windows.TryRemove(kv.Key, out _);

        if (_cooldowns.Count > 5000)
            foreach (var kv in _cooldowns)
                if (kv.Value <= now)
                    _cooldowns.TryRemove(kv.Key, out _);
    }
}
