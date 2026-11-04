using WeChatWeb.Data;

namespace WeChatWeb.Services;

/// <summary>
/// 朋友圈可见性统一判定。此前 MomentsController.CanView 与 DiscoveryFilesControllers
/// 里两个 MomentVisibleTo 克隆各存一份（同语义不同实现，漂移风险大），现在三处都调这里。
/// 规则：
///  · 自己恒可见；private 仅自己；friends 仅双向好友；
///  · 公开（all）动态此前对任何登录用户可见 ——「允许陌生人查看十条朋友圈」开关
///    只在设置页存了个值，服务端从未强制（隐私开关形同虚设）。现在：
///    关 = 陌生人完全不可见；开 = 陌生人仅可见作者最近 10 条公开动态（微信同款语义）；
///    好友不受该开关约束。
/// 调用方必须持有 DataStore 全局锁（与全库约定一致）。
/// </summary>
public static class MomentVisibility
{
    /// <summary>陌生人可见的公开动态窗口大小（Timeline 预聚合路径复用同一常量）。</summary>
    public const int StrangerWindowSize = 10;

    private const int StrangerWindow = StrangerWindowSize;

    public static bool IsVisible (DataStore store, Models.Moment m, string me)
    {
        if (m.AuthorId == me) return true;
        if (m.Scope == "private") return false;
        if (m.Scope == "friends")
            return store.Contacts.TryGetValue(me, out var mine)
                && mine.Any(c => c.OtherUserId == m.AuthorId);

        // 公开动态：作者关闭「陌生人可见」→ 陌生人不可见（好友放行）
        if (!store.Contacts.TryGetValue(me, out var myContacts)
            || !myContacts.Any(c => c.OtherUserId == m.AuthorId))
        {
            if (!store.Users.TryGetValue(m.AuthorId, out var author)
                || (author.Privacy.TryGetValue("strangerMoments", out var v) && !v))
                return false;
            // 开关开着：只放作者最近 10 条公开动态（按时间倒序窗口，微信「十条」语义）
            var recent = store.Moments
                .Where(x => x.AuthorId == m.AuthorId && x.Scope == "all")
                .OrderByDescending(x => x.CreatedAt)
                .Take(StrangerWindow)
                .Select(x => x.Id);
            if (!recent.Contains(m.Id)) return false;
        }
        return true;
    }
}
