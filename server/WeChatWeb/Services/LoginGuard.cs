namespace WeChatWeb.Services;

/// <summary>
/// 登录防护：按 IP 的失败限速 + 按账号的失败锁定（防暴力破解 / 防机器人撞库）。
/// 纯内存实现，重启清零。
/// </summary>
public sealed class LoginGuard
{
    private sealed class AccountState
    {
        public int Fails;
        public DateTime WindowStart = DateTime.UtcNow;
        public DateTime LockedUntil;
    }

    private readonly Dictionary<string, Queue<DateTime>> _ipFails = new();
    private readonly Dictionary<string, AccountState> _accountFails = new();
    private readonly object _lock = new();

    /// <summary>返回 null 表示放行，否则返回拒绝原因（HTTP 429/403 用）。</summary>
    public string? Check (string ip, string account, SecuritySettings sec)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            CleanupLocked(now);

            if (!string.IsNullOrEmpty(ip) && _ipFails.TryGetValue(ip, out var q))
            {
                while (q.Count > 0 && q.Peek() < now.AddMinutes(-1)) q.Dequeue();
                if (q.Count >= sec.AuthPerMinute * 2)
                    return "失败次数过多，请 5 分钟后再试";
            }

            if (!string.IsNullOrEmpty(account) && _accountFails.TryGetValue(account, out var st))
            {
                if (st.LockedUntil > now)
                {
                    var min = (int)Math.Ceiling((st.LockedUntil - now).TotalMinutes);
                    return $"账号已锁定，请 {min} 分钟后再试";
                }
            }
            return null;
        }
    }

    public void RecordFailure (string ip, string account, SecuritySettings sec)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(ip))
            {
                if (!_ipFails.TryGetValue(ip, out var q)) _ipFails[ip] = q = new Queue<DateTime>();
                q.Enqueue(now);
            }
            if (!string.IsNullOrEmpty(account))
            {
                if (!_accountFails.TryGetValue(account, out var st))
                    _accountFails[account] = st = new AccountState();
                if (now - st.WindowStart > TimeSpan.FromMinutes(15))
                {
                    st.WindowStart = now;
                    st.Fails = 0;
                }
                st.Fails++;
                if (st.Fails >= sec.MaxLoginAttempts)
                {
                    st.LockedUntil = now.AddMinutes(sec.LockoutMinutes);
                    st.Fails = 0;
                }
            }
        }
    }

    public void ResetAccount (string account)
    {
        if (string.IsNullOrEmpty(account)) return;
        lock (_lock) _accountFails.Remove(account);
    }

    private void CleanupLocked (DateTime now)
    {
        if (_ipFails.Count < 1000 && _accountFails.Count < 1000) return;
        foreach (var kv in _ipFails.Where(kv => kv.Value.Count == 0 || kv.Value.Peek() < now.AddMinutes(-5)).ToList())
            _ipFails.Remove(kv.Key);
        foreach (var kv in _accountFails.Where(kv => kv.Value.LockedUntil < now && now - kv.Value.WindowStart > TimeSpan.FromMinutes(30)).ToList())
            _accountFails.Remove(kv.Key);
    }
}
