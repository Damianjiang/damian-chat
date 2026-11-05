using System.Security.Cryptography;
using System.Text;

namespace WeChatWeb.Services;

/// <summary>pass token 消费结果：Ok=通过并消费；NotReady=延时门未到（token 保留）；Invalid=无效/过期/重放。</summary>
public enum SliderPassResult
{
    Ok,
    NotReady,
    Invalid,
}

/// <summary>
/// 滑块人机验证挑战。
///
/// 流程（服务端全程可控、可重放检测）：
///   1. Issue()   —— 前端点「登录/注册」时调用，签发挑战 key（一次性、2 分钟过期、绑定调用方 IP）。
///   2. Complete()—— 用户完成拼图交互后调用，提交「真实落点 + 完整拖动轨迹样本」，
///      消费 key，换取 pass token（HMAC 签名、90 秒、绑定 IP、签发后 ≥MinHold 秒才可消费）。
///   3. ConsumePass()—— 真正的登录/注册接口校验并消费 pass token（一次性，重放直接失败）。
///
/// 安全设计（针对「公开到互联网」的对抗场景）：
///   ① 背景图是服务端光栅化 PNG —— 缺口坐标只存在于像素里，绝不在 JSON 明文下发。
///   ② 落点校验用「真实松手位置」：此前前端回传的是缺口位置本身，服务端拿目标和目标比
///      恒为真（等于没校验）—— 现在回传真实松手位移，与保密 SlotX 比对。
///   ③ 完整轨迹校验：真人拖动必产生「起点≈0 → 多次移动 → 终点=落点」的样本序列，
///      且单步位移/速度曲线符合人手特征；moveCount/dragMs 这类自报整数已废弃（可伪造）。
///   ④ 延时门：pass token 签发后 ≥MinHold 秒才可消费 —— 脚本拿到 token 立刻打登录接口
///      会被拒；前端在此期间展示「安全检测中 / 已超过 NN% 用户」状态卡。
///   ⑤ 按 IP 失败锁定：15 分钟内试错 5 次即锁 15 分钟；每 IP 同时最多 5 个未过期挑战
///      防囤积；每 IP 10 分钟最多签发 15 个 pass token 防打码农场批量出票。
///   ⑥ 挑战 key 与 pass token 均一次性、绑定 IP、HMAC 签名（常量时间比较）。
/// </summary>
public sealed class SliderCaptchaService
{
    /// <summary>
    /// 拖动轨迹样本：X = 滑块位移（px，0=起点），T = 距按下毫秒数。由前端真实指针事件采样。
    /// </summary>
    public sealed record SliderSample (int X, long T);

    /// <summary>
    /// 挑战。SlotX/SlotY 是「服务端权威缺口位置」：由服务端随机选定并直接画进背景图。
    /// Complete() 校验前端回传的「真实落点」|dropX-SlotX| 是否在容差内。
    /// </summary>
    private sealed record Challenge (string Ip, DateTime ExpiresAt, DateTime IssuedAt, int SlotX, int SlotY);

    /// <summary>已通过滑块交互的凭证：nonce -> (IP, 过期时间, 签发时刻)。</summary>
    private sealed record PassGrant (string Ip, DateTime ExpiresAt, DateTime GrantedAt);

    private readonly Dictionary<string, Challenge> _challenges = new();
    private readonly Dictionary<string, PassGrant> _passes = new();
    private readonly Dictionary<string, Queue<DateTime>> _verifyFails = new();
    private readonly Dictionary<string, Queue<DateTime>> _passTimes = new();
    private readonly object _lock = new();
    private readonly LogService _log;
    private readonly SliderBackgroundStore _backgrounds;

    // 挑战有效期与 pass token 有效期
    private static readonly TimeSpan ChallengeTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PassTtl = TimeSpan.FromSeconds(90);

    // 防爆破：失败锁定窗口 / 次数 / 单 IP 未完成挑战上限
    private static readonly TimeSpan FailWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan Lockout = TimeSpan.FromMinutes(15);
    private const int MaxFails = 5;
    private const int MaxOpenChallengesPerIp = 5;

    // pass token 签发限速：真人登录频率远用不到；打码农场批量出票在此断粮
    private static readonly TimeSpan PassRateWindow = TimeSpan.FromMinutes(10);
    private const int MaxPassPerWindow = 15;

    /// <summary>
    /// 延时门：pass token 签发后至少 1.2 秒才可消费。
    /// 前端在滑块通过后展示 1.5–2.2 秒的「安全检测中 / 已超过 NN% 用户」状态卡再提交登录，
    /// 脚本拿到 token 立刻打登录接口 → NotReady 拒绝（token 不消费，真人稍后仍可用）。
    /// </summary>
    private const double MinHoldSeconds = 1.2;

    /// <summary>
    /// 行为真实性门槛。真人拖动 = 「起点≈0 → 多次移动 → 终点=落点」的样本序列：
    /// ① MinDragMillis —— 签发→完成最短时长（挡最傻的秒传）；
    /// ② MinSamples/MaxSamples —— 样本数上下限（脚本一次性 POST 只有 0-1 个样本）；
    /// ③ MinDragMs/MaxDragMs —— 拖动时长区间（<200ms 的"瞬时拖完"人手做不到）；
    /// ④ MaxStepPx / MaxSpeedPxPerMs —— 单步位移与速度上限（瞬移脚本一步几百 px）；
    /// ⑤ MaxBacktrackPx —— 往回退总量上限（真人手抖会小幅回退，不会倒退几十像素）；
    /// ⑥ MinDistinctSteps —— 步长多样性（匀速等距 = 脚本机器签名）。
    /// </summary>
    private const double MinDragMillis = 400;
    private const int MinSamples = 5;
    private const int MaxSamples = 800;
    private const int MinDragMs = 200;
    private const int MaxDragMs = 15000;
    private const int MaxStepPx = 90;
    private const double MaxSpeedPxPerMs = 4.0;
    private const int MaxBacktrackPx = 60;
    private const int MinDistinctSteps = 3;

    /// <summary>
    /// 服务端权威缺口（拼图目标槽位）几何。背景固定 300×150；缺口是 40×40 的圆角拼图凸块。
    /// 前端把「拼块」裁在同一区域，用户水平拖动使其与缺口对齐；Complete() 用 |dragX-SlotX|≤容差
    /// 做权威判定 —— 之前服务端根本不校验落点，脚本伪造 moveCount/dragMs 即可白拿 passToken（已堵）。
    /// 坐标只画进像素 + 存服务端内存，不以文本形式回传，机器人只能做像素级图像识别才可能定位。
    /// </summary>
    private const int SlotSize = 40;         // 拼块边长（= 前端 barWidth）
    // 落点容差：刻意放宽（用户要求"差不多贴近就算过"，真人拖到缺口附近即应通过）。
    // 前端组件本地放行窗口 diffWidth=30 + 前端 CV 定位描边外缘的误差 ≤4px
    //（服务端画描边在边缘上，CV 取最左品红列 = 外缘，比 SlotX 小 2-3px）→ 服务端 34 全覆盖。
    private const int SlotTolerance = 34;
    private const int SlotMinX = 30;         // 缺口左缘最小（避开左侧拼图手柄区）
    private const int SlotMaxX = 300 - SlotSize - 6;   // 左缘最大（不贴右边缘）
    private const int SlotMinY = 24;         // 顶缘最小（避开顶部刷新图标区）
    private const int SlotMaxY = 150 - SlotSize - 22;  // 顶缘最大（避开底部提示条）

    public SliderCaptchaService (LogService log, SliderBackgroundStore backgrounds)
    {
        _log = log;
        _backgrounds = backgrounds;
    }

    /// <summary>
    /// 签发一次滑块挑战，返回挑战 key 与服务端光栅化背景图（含可见目标槽位）。
    /// 关键：保密的缺口横坐标 pinX 只存在于服务端内存与 PNG 像素里，绝不以文本形式回传。
    /// 返回 (null, null) 表示该 IP 已被失败锁定或囤积了过多未完成挑战（防爆破/防脚本）。
    /// </summary>
    /// <summary>
    /// 签发挑战。返回 (key, image)：缺口坐标 slotX/slotY **只存服务端内存、只画进像素**，
    /// 绝不在 JSON 里明文下发（明文下发 = 脚本读走即可伪造落点，等价于没修）。
    /// 前端用 CV 在图里定位那块高饱和「目标色块」来裁拼块，拖到其上对齐，回传落点，服务端校验。
    /// </summary>
    public (string? Key, string? Image) Issue (string ip)
    {
        ip ??= "";
        lock (_lock)
        {
            CleanupLocked();

            if (IsLockedOutLocked(ip))
                return (null, null);

            // 囤积上限：未过期挑战达到上限就不再发（脚本刷挑战、离线慢慢分析的路径被封死）
            var open = 0;
            foreach (var c in _challenges.Values)
                if (c.Ip == ip && c.ExpiresAt > DateTime.UtcNow) open++;
            if (open >= MaxOpenChallengesPerIp)
            {
                _log.Write("auth", new { ts = DateTime.UtcNow, result = "slider-challenge-cap", ip });
                return (null, null);
            }

            var key = RandomToken(24);

            // 服务端权威缺口：随机选定，画成一块「高饱和目标色 + 白描边」，坐标只存内存 + 像素。
            // 前端 CV 定位该色块裁拼块；Complete() 校验落点 |dragX-SlotX|≤容差，堵住白拿 passToken。
            var slotX = RandomNumberGenerator.GetInt32(SlotMinX, SlotMaxX + 1);
            var slotY = RandomNumberGenerator.GetInt32(SlotMinY, SlotMaxY + 1);

            // 一次性挑战：绑定调用方 IP、2 分钟过期。IssuedAt 用于行为门槛（拦秒传脚本）。
            _challenges[key] = new Challenge(ip, DateTime.UtcNow.Add(ChallengeTtl), DateTime.UtcNow, slotX, slotY);
            return (key, BuildBackground(slotX, slotY));
        }
    }

    /// <summary>
    /// 消费挑战并签发一次性 pass token。
    /// 校验链：一次性（取出即作废）+ 未过期 + 绑定调用方 IP + 未失败锁定 +
    /// 轨迹真实性（起点/样本数/时长/单步位移/速度/回退量/步长多样性）+
    /// 权威落点（真实松手位置与保密缺口坐标比对）+ 每 IP 签发限速。
    /// 返回 (token, null) 或 (null, 拒绝原因)；拒绝会记一次失败（累计触发锁定）。
    /// Reason 仅用于给用户挑一条说得通的提示（如 drop-miss → 「没对准缺口」），不回传内部细节。
    /// </summary>
    public (string? Token, string? Reason) Complete (string? key, string ip, int? dragX, List<SliderSample>? trajectory)
    {
        if (string.IsNullOrWhiteSpace(key)) return (null, "invalid");

        ip ??= "";
        var now = DateTime.UtcNow;
        lock (_lock)
        {
            if (IsLockedOutLocked(ip)) return (null, "locked");

            if (!_challenges.TryGetValue(key, out var c)) return (null, "invalid");
            _challenges.Remove(key); // 一次性：取出即作废
            if (c.ExpiresAt < now) return (null, "invalid");
            if (!string.Equals(c.Ip, ip, StringComparison.Ordinal)) return (null, "invalid");

            var dropX = dragX ?? 0;
            var samples = trajectory ?? new List<SliderSample>();

            // —— 轨迹真实性 ——
            var reject = TrajectoryRejectReason(c, samples, dropX, now);
            if (reject == null)
            {
                // —— 权威落点校验（真实松手位置，不是缺口位置本身）。
                //   盲猜单 IP 5 次即锁 15 分钟；做 CV 定位也得再过轨迹真实性那一关。
                if (Math.Abs(dropX - c.SlotX) > SlotTolerance)
                    reject = "drop-miss";
            }
            if (reject != null)
            {
                RecordFailLocked(ip);
                // 日志（auth.jsonl）会被管理端直接查看/导出：不记 slotX 原值（保密坐标），
                // 不记完整轨迹（体积大）；只记拒绝原因与样本数/时长概要。
                _log.Write("auth", new
                {
                    ts = now,
                    result = "slider-rejected",
                    ip,
                    reason = reject,
                    dragX,
                    samples = samples.Count,
                    dragMs = samples.Count > 0 ? samples[^1].T : 0,
                });
                return (null, reject);
            }

            // —— 每 IP 签发限速 ——
            if (!_passTimes.TryGetValue(ip, out var okQ)) _passTimes[ip] = okQ = new Queue<DateTime>();
            while (okQ.Count > 0 && now - okQ.Peek() > PassRateWindow) okQ.Dequeue();
            if (okQ.Count >= MaxPassPerWindow)
            {
                _log.Write("auth", new { ts = now, result = "slider-pass-rate", ip });
                return (null, "rate");
            }
            okQ.Enqueue(now);

            // 成功：清空该 IP 的失败计数
            _verifyFails.Remove(ip);

            // token = expires.nonce.signature；nonce 由服务端保存，不泄露挑战 key
            var nonce = RandomToken(16);
            var expiresAt = now.Add(PassTtl);
            var expires = new DateTimeOffset(expiresAt).ToUnixTimeSeconds();
            _passes[nonce] = new PassGrant(ip, expiresAt, now);
            CleanupLocked();
            return ($"{expires}.{nonce}.{Sign($"{nonce}|{ip}|{expires}")}", null);
        }
    }

    /// <summary>
    /// 轨迹真实性判定。真人拖动 = 「起点≈0 → 多次移动 → 终点=落点」的样本序列，
    /// 位移/速度符合人手特征；返回 null 表示通过，否则返回拒绝原因（仅入日志，不回前端细节）。
    /// </summary>
    private static string? TrajectoryRejectReason (Challenge c, List<SliderSample> s, int dropX, DateTime now)
    {
        if ((now - c.IssuedAt).TotalMilliseconds < MinDragMillis) return "too-fast-after-issue";
        if (s.Count < MinSamples || s.Count > MaxSamples) return "samples";
        if (s[0].X < -2 || s[0].X > 2) return "start";
        if (Math.Abs(s[^1].X - dropX) > 1) return "end-mismatch";

        var duration = s[^1].T;
        if (duration < MinDragMs || duration > MaxDragMs) return "duration";

        // t 必须单调不减；同毫秒内只允许 2px 内微动（高回报率鼠标一毫秒多事件）
        var backtrack = 0;
        var last = s[0];
        var distinct = new HashSet<int>();
        for (var i = 1; i < s.Count; i++)
        {
            var cur = s[i];
            if (cur.T < last.T) return "time-regress";
            var dx = cur.X - last.X;
            var dt = cur.T - last.T;
            if (Math.Abs(dx) > MaxStepPx) return "teleport";
            if (dt == 0 && Math.Abs(dx) > 2) return "teleport";
            if (dt > 0 && Math.Abs(dx) / dt > MaxSpeedPxPerMs) return "speed";
            if (dx < 0) backtrack -= dx;
            if (i >= 2 && dt > 0) distinct.Add(dx);
            last = cur;
        }
        if (backtrack > MaxBacktrackPx) return "backtrack";
        // 恒速机器签名：样本足够多时步长应有多样性（人手必有抖动）；等距匀速 = 脚本
        if (s.Count >= 8 && distinct.Count < MinDistinctSteps && backtrack == 0) return "machine";
        return null;
    }

    /// <summary>校验并消费 pass token（登录/注册时调用）。延时门未到返回 NotReady（token 保留），无效/重放返回 Invalid。</summary>
    public SliderPassResult ConsumePass (string? token, string ip)
    {
        if (string.IsNullOrWhiteSpace(token)) return SliderPassResult.Invalid;

        var parts = token.Split('.', 3);
        if (parts.Length != 3) return SliderPassResult.Invalid;
        if (!long.TryParse(parts[0], out var expires)) return SliderPassResult.Invalid;
        var now = DateTime.UtcNow;
        if (expires < new DateTimeOffset(now).ToUnixTimeSeconds()) return SliderPassResult.Invalid;

        // 先验签（常量时间比较，防时序攻击）
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(parts[2]),
                Encoding.UTF8.GetBytes(Sign($"{parts[1]}|{ip}|{expires}"))))
            return SliderPassResult.Invalid;

        // 再查服务端记录并消费：延时门 + 一次性 + IP 绑定
        lock (_lock)
        {
            if (!_passes.TryGetValue(parts[1], out var grant)) return SliderPassResult.Invalid;
            // 延时门：签发后 ≥MinHoldSeconds 才可消费。脚本拿 token 秒打登录接口在此被拒；
            // token 不消费，真人稍后带同一 token 重试仍可通过。
            if ((now - grant.GrantedAt).TotalSeconds < MinHoldSeconds) return SliderPassResult.NotReady;
            _passes.Remove(parts[1]); // 一次性：取出即作废
            if (grant.ExpiresAt < now) return SliderPassResult.Invalid;
            if (!string.Equals(grant.Ip, ip ?? "", StringComparison.Ordinal)) return SliderPassResult.Invalid;
        }
        return SliderPassResult.Ok;
    }

    /// <summary>该 IP 是否处于失败锁定状态（供控制器给出明确的 429 提示）。</summary>
    public bool IsLockedOut (string ip)
    {
        lock (_lock)
        {
            CleanupLocked();
            return IsLockedOutLocked(ip ?? "");
        }
    }

    // ---------------- 防爆破 ----------------

    private bool IsLockedOutLocked (string ip)
    {
        if (!_verifyFails.TryGetValue(ip, out var q)) return false;
        var now = DateTime.UtcNow;
        while (q.Count > 0 && now - q.Peek() > FailWindow) q.Dequeue();
        if (q.Count < MaxFails) return false;
        // 锁定到期判定：最早一次失败距现在超过锁定时长即解锁
        if (now - q.Peek() > Lockout)
        {
            _verifyFails.Remove(ip);
            return false;
        }
        return true;
    }

    private void RecordFailLocked (string ip)
    {
        if (!_verifyFails.TryGetValue(ip, out var q)) _verifyFails[ip] = q = new Queue<DateTime>();
        q.Enqueue(DateTime.UtcNow);
        if (q.Count >= MaxFails)
            _log.Write("auth", new { ts = DateTime.UtcNow, result = "slider-lockout", ip, fails = q.Count });
    }

    // ---------------- 内部实现 ----------------

    /// <summary>HMAC-SHA256 签名（进程级随机密钥，重启即失效）。</summary>
    private static readonly byte[] Secret = RandomNumberGenerator.GetBytes(32);

    private static string Sign (string payload)
    {
        using var hmac = new HMACSHA256(Secret);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string RandomToken (int bytes)
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();
    }

    private void CleanupLocked ()
    {
        var now = DateTime.UtcNow;
        foreach (var kv in _challenges.Where(kv => kv.Value.ExpiresAt < now).ToList())
            _challenges.Remove(kv.Key);
        foreach (var kv in _passes.Where(kv => kv.Value.ExpiresAt < now).ToList())
            _passes.Remove(kv.Key);
        foreach (var kv in _verifyFails.Where(kv => kv.Value.Count == 0).ToList())
            _verifyFails.Remove(kv.Key);
        // 失败队列整体过期的 IP 一并回收：否则轮换 IP 各灌 1 次失败即长期驻留（内存缓慢耗尽）
        foreach (var kv in _verifyFails.Where(kv =>
            kv.Value.Count > 0 && (now - kv.Value.Peek()).TotalMinutes > 20).ToList())
            _verifyFails.Remove(kv.Key);
        // 签发限速队列整体过期的 IP 一并回收
        foreach (var kv in _passTimes.Where(kv =>
            kv.Value.Count == 0 || (now - kv.Value.Peek()).TotalMinutes > 11).ToList())
            _passTimes.Remove(kv.Key);
    }

    /// <summary>
    /// 光栅化背景图（PNG，300x150）：优先取管理端图库里的背景（山峦/星空等真实纹理），
    /// 图库为空则退回程序化渐变。
    /// 再在服务端选定位置 (slotX,slotY) 画一个「高饱和目标框」（纯品红描边 + 内层白描边，中间镂空）：
    /// 颜色饱和度远高于图库任意纹理，前端用「品红像素命中统计」即可稳健定位并裁拼块；
    /// 而坐标本身**不在 JSON 明文下发**，机器人只能做 CV 找色块，找错/手抖落点超容差即被拒并锁。
    /// ★ 用空心描边框而非实心品红块：实心块叠在风景上会混成一大坨紫色（看起来像渲染 bug）。
    /// 中间镂空让背景纹理透出，观感自然；CV 定位靠描边竖边的品红命中统计（每竖边 40 高），不依赖实心度。
    /// </summary>
    private string BuildBackground (int slotX, int slotY)
    {
        var canvas = TryLoadStoredBackground();
        if (canvas == null)
            canvas = ProceduralGradient();

        // 目标框：纯品红（R、B 远高于 G，饱和度极大）4px 描边空心 + 内层 2px 白描边增强可读性。
        // 中间镂空 → 背景风景透出，不再出现"紫色实心块"。
        canvas.StrokeRoundRect(slotX, slotY, SlotSize, SlotSize, 8, 4.0, 255, 0, 255, 0.95);
        canvas.StrokeRoundRect(slotX + 3.5, slotY + 3.5, SlotSize - 7, SlotSize - 7, 5, 2.0, 255, 255, 255, 0.9);

        return "data:image/png;base64," + Convert.ToBase64String(canvas.ToPng());
    }

    /// <summary>从图库随机取背景并解码为画布；失败/尺寸不符返回 null。</summary>
    private Raster? TryLoadStoredBackground ()
    {
        var png = _backgrounds.PickRandom();
        if (png == null) return null;
        if (!PngDecoder.TryDecode(png, out var w, out var h, out var rgba)) return null;
        if (w != 300 || h != 150) return null;   // 必须与滑块组件像素级对齐
        return new Raster(w, h, rgba);
    }

    private static Raster ProceduralGradient ()
    {
        static (byte, byte, byte) Pastel (int min, int max) =>
            ((byte)RandomNumberGenerator.GetInt32(min, max),
             (byte)RandomNumberGenerator.GetInt32(min, max),
             (byte)RandomNumberGenerator.GetInt32(min, max));

        var canvas = new Raster(300, 150);
        var top = Pastel(150, 205);
        var bottom = Pastel(120, 175);
        canvas.VerticalGradient(top, bottom);

        // 2-3 个大而淡的色斑：干扰「找最亮区域」式的简单定位，又不影响真人辨认槽位
        var blobs = RandomNumberGenerator.GetInt32(2, 4);
        for (var i = 0; i < blobs; i++)
        {
            var (r, g, b) = Pastel(170, 230);
            canvas.SoftBlob(
                RandomNumberGenerator.GetInt32(30, 270),
                RandomNumberGenerator.GetInt32(20, 130),
                RandomNumberGenerator.GetInt32(45, 85),
                r, g, b, 0.10);
        }

        canvas.Noise(220, 255, 255, 255, 0.05);
        return canvas;
    }
}
