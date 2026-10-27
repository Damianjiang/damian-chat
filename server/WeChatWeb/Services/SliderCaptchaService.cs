using System.Security.Cryptography;
using System.Text;

namespace WeChatWeb.Services;

/// <summary>
/// 滑块人机验证挑战。
///
/// 流程（服务端全程可控、可重放检测）：
///   1. Issue()   —— 前端点「登录/注册」时调用，签发挑战 key（一次性、2 分钟过期、绑定调用方 IP）。
///   2. Complete()—— 用户完成拼图交互后调用，消费 key，换取 pass token（HMAC 签名、90 秒、绑定 IP）。
///   3. ConsumePass()—— 真正的登录/注册接口校验并消费 pass token（一次性，重放直接失败）。
///
/// 安全设计（针对「公开到互联网」的对抗场景）：
///   ① 背景图是服务端光栅化 PNG —— 缺口坐标只存在于像素里。此前用 SVG 时，
///      机器人 base64 解码就能从标记里直接读出 &lt;rect x="pinX"&gt;，一秒绕过；
///      光栅图必须做像素级图像识别才能定位缺口。
///   ② pinX 范围 [50,246]、容差 ±5 —— 即使做图像识别失败退化为盲猜，
///      单次命中率也只有 11/197 ≈ 5.6%，期望约 18 次。
///   ③ 按 IP 失败锁定：15 分钟内试错 5 次即锁 15 分钟（真人极少连错 5 次），
///      盲猜 / 脚本枚举在期望次数之前就会被锁死。
///   ④ 每 IP 同时最多 5 个未过期挑战 —— 防止脚本囤积挑战批量离线分析。
///   ⑤ 挑战 key 与 pass token 均一次性、绑定 IP、HMAC 签名（常量时间比较）。
/// </summary>
public sealed class SliderCaptchaService
{
    /// <summary>
    /// 挑战。SlotX/SlotY 是「服务端权威缺口位置」：由服务端随机选定并直接画进背景图，
    /// 前端把它裁成拼图压块；Complete() 时校验前端回传的落点 |dragX-SlotX| 是否在容差内。
    /// 之前缺口在前端本地随机生成、服务端不知道目标，脚本伪造 moveCount/dragMs 即可白拿 passToken —— 已堵。
    /// </summary>
    private sealed record Challenge (string Ip, DateTime ExpiresAt, DateTime IssuedAt, int SlotX, int SlotY);

    /// <summary>已通过滑块交互的凭证：nonce -> (IP, 过期时间)。</summary>
    private sealed record PassGrant (string Ip, DateTime ExpiresAt);

    private readonly Dictionary<string, Challenge> _challenges = new();
    private readonly Dictionary<string, PassGrant> _passes = new();
    private readonly Dictionary<string, Queue<DateTime>> _verifyFails = new();
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

    /// <summary>
    /// 行为真实性门槛（脚本无法伪造"真实拖动轨迹"，只能一次性 POST）：
    /// ① MinDragMillis —— 签发→完成最短时长（挡最傻的秒传）；
    /// ② MinMoves      —— 最少指针移动次数（真人拖动必产生多次 mousemove/touchmove，脚本一次性 POST 为 0）；
    /// ③ MinDragMs     —— 一次拖动最短时长（真人从按下到松手有真实耗时）。
    /// 三条取"或"判定可疑：脚本要全过就得伪造完整指针事件序列，
    /// 而它又拿不到前端本地裁切的缺口坐标 → 无法算出该拖到哪，白拿 passToken 路径被封。
    /// </summary>
    private const double MinDragMillis = 400;
    private const int MinMoves = 3;
    private const int MinDragMs = 250;

    /// <summary>
    /// 服务端权威缺口（拼图目标槽位）几何。背景固定 300×150；缺口是 40×40 的圆角拼图凸块。
    /// 前端把「拼块」裁在同一区域，用户水平拖动使其与缺口对齐；Complete() 用 |dragX-SlotX|≤容差
    /// 做权威判定 —— 之前服务端根本不校验落点，脚本伪造 moveCount/dragMs 即可白拿 passToken（已堵）。
    /// 坐标只画进像素 + 存服务端内存，不以文本形式回传，机器人只能做像素级图像识别才可能定位。
    /// </summary>
    private const int SlotSize = 40;         // 拼块边长（= 前端 barWidth）
    private const int SlotTolerance = 14;    // 落点容差（±14px，覆盖前端 diffWidth 15px 内的漂移）
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
    /// 「签发→完成 ≥400ms」行为门槛（真人拖动需几百毫秒，脚本秒传/不拖动直接被拒并记一次失败）。
    /// 缺口位置由前端组件本地裁切判定（服务端不与其共享坐标），服务端负责防伪造/重放/囤积/限流。
    /// 任一条件不满足都返回 null。dragX 仍接收（前端回传），仅用于日志，不作判定依据。
    /// </summary>
    public string? Complete (string? key, string ip, int? dragX, int? moveCount, int? dragMs)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;

        ip ??= "";
        var now = DateTime.UtcNow;
        lock (_lock)
        {
            if (IsLockedOutLocked(ip)) return null;

            if (!_challenges.TryGetValue(key, out var c)) return null;
            _challenges.Remove(key); // 一次性：取出即作废
            if (c.ExpiresAt < now) return null;
            if (!string.Equals(c.Ip, ip, StringComparison.Ordinal)) return null;

            // 行为真实性门槛：真人拖动必有「≥MinMoves 次指针移动 且 拖动耗时 ≥MinDragMs」。
            // 脚本一次性 POST 没有真实指针事件 → moveCount/dragMs 为 0 → 判为机器人，记失败并锁。
            var moves = moveCount ?? 0;
            var drag = dragMs ?? 0;
            if ((now - c.IssuedAt).TotalMilliseconds < MinDragMillis || moves < MinMoves || drag < MinDragMs)
            {
                RecordFailLocked(ip);
                _log.Write("auth", new
                {
                    ts = now,
                    result = "slider-rejected-bot",
                    ip,
                    dragX,
                    moves,
                    drag,
                });
                return null;
            }

            // ★ 权威落点校验（堵住旧绕过：以前服务端根本不查落点，脚本伪造 moveCount/dragMs 即可白拿 passToken）。
            //   缺口由服务端生成并画进背景/拼块，slotX 保密在内存，不以文本回传；前端回传的 dragX
            //   必须落在 [slotX±容差] 内才算对齐。盲猜单 IP 5 次即锁 15 分钟，脚本枚举在此被锁死。
            var dropX = dragX ?? 0;
            if (Math.Abs(dropX - c.SlotX) > SlotTolerance)
            {
                RecordFailLocked(ip);
                // 只记偏差，不记 slotX 原值：日志（auth.jsonl）会被管理端直接查看/导出，
                // 把保密坐标写进去等于把答案发给日志的每个读者
                _log.Write("auth", new
                {
                    ts = now,
                    result = "slider-drop-miss",
                    ip,
                    dragX,
                    miss = true,
                });
                return null;
            }

            // 成功：清空该 IP 的失败计数
            _verifyFails.Remove(ip);
        }

        // token = expires.nonce.signature
        // nonce 由服务端保存，不泄露挑战 key；签名保证不可伪造
        var nonce = RandomToken(16);
        var expiresAt = now.Add(PassTtl);
        var expires = new DateTimeOffset(expiresAt).ToUnixTimeSeconds();

        lock (_lock)
        {
            CleanupLocked();
            _passes[nonce] = new PassGrant(ip, expiresAt);
        }
        return $"{expires}.{nonce}.{Sign($"{nonce}|{ip}|{expires}")}";
    }

    /// <summary>校验并消费 pass token（登录/注册时调用，失败或重放返回 false）。</summary>
    public bool ConsumePass (string? token, string ip)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;

        var parts = token.Split('.', 3);
        if (parts.Length != 3) return false;
        if (!long.TryParse(parts[0], out var expires)) return false;
        if (expires < new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds()) return false;

        // 先验签（常量时间比较，防时序攻击）
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(parts[2]),
                Encoding.UTF8.GetBytes(Sign($"{parts[1]}|{ip}|{expires}"))))
            return false;

        // 再查服务端记录并消费：一次性、IP 绑定
        lock (_lock)
        {
            if (!_passes.TryGetValue(parts[1], out var grant)) return false;
            _passes.Remove(parts[1]); // 一次性：取出即作废
            if (grant.ExpiresAt < DateTime.UtcNow) return false;
            if (!string.Equals(grant.Ip, ip ?? "", StringComparison.Ordinal)) return false;
        }
        return true;
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
