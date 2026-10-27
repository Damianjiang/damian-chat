using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using WeChatWeb.Data;
using WeChatWeb.Models;
using WeChatWeb.Services;

namespace WeChatWeb.Hubs;

/// <summary>
/// 实时消息 Hub。
/// 推送模型：成员定向 —— 服务端按「会话成员 → 各自全部连接」精确投递，
/// 不依赖客户端加入 SignalR 组（此前组广播要求前端先 JoinConversation，
/// 而前端从未调用，导致所有实时消息静默丢失，只有刷新页面才能看到新消息）。
/// 安全要点：连接身份来自 JWT；控制器侧已做会话成员校验，这里不做二次放行。
/// </summary>
public sealed class ChatHub : Hub
{
    private readonly DataStore _store;

    /// <summary>
    /// 连接上限（WS-PERF-001 / BIZ-SEC-024）：
    /// ・全局最大连接数：防「僵尸连接堆积 / 连接风暴」把内存打爆（资源耗尽型 DoS）；
    /// ・单用户最大连接数：超限时拒绝新连接（多端过多视为异常；正常多端 ≤ 阈值），
    ///   既限制资源又天然构成「异常多开」拦截。二者可由 appsettings 的 ChatHub:* 覆盖。
    /// </summary>
    private readonly int _maxConnections;
    private readonly int _maxPerUser;
    private readonly LogService _log;

    public ChatHub(DataStore store, IConfiguration config, LogService log)
    {
        _store = store;
        _log = log;
        _maxConnections = int.TryParse(config["ChatHub:MaxConnections"], out var a) ? Math.Max(1, a) : 5000;
        _maxPerUser = int.TryParse(config["ChatHub:MaxPerUser"], out var b) ? Math.Max(1, b) : 8;
    }

    /// <summary>当前连接的用户 id（由 JWT 的 sub 解析）。</summary>
    private string? CurrentUserId =>
        Context.UserIdentifier ?? Context.User?.FindFirst("sub")?.Value;

    public override async Task OnConnectedAsync()
    {
        var user = CurrentUserId;

        // 封禁/删号拦截：旧 access token 仍可用（签发后最长 2h），但被封用户绝不允许再挂实时通道。
        // 需要在这里按 sub 反查 DataStore（UserInvalidationMiddleware 只管 HTTP 请求，管不到 WS）。
        if (!string.IsNullOrEmpty(user))
        {
            string? nickname = null;
            var rejected = false;
            lock (_store)
            {
                if (!_store.Users.TryGetValue(user, out var u) || u.Banned) rejected = true;
                else nickname = u.Nickname;
            }
            if (rejected)
            {
                _log.Write("auth", new
                {
                    ts = DateTime.UtcNow,
                    result = "ws-banned-or-deleted",
                    user,
                });
                Context.Abort();
                return;
            }
        }

        // 连接上限：全局 + 单用户。超限直接 Abort（关闭这条连接，不影响其它连接）。
        lock (_store)
        {
            if (_store.Connections.Count >= _maxConnections)
            {
                _log.Write("auth", new
                {
                    ts = DateTime.UtcNow,
                    result = "ws-global-cap",
                    cap = _maxConnections,
                    user,
                });
                Context.Abort();
                return;
            }

            var mine = _store.Connections.Values.Count(c => c.UserId == user);
            if (mine >= _maxPerUser)
            {
                _log.Write("auth", new
                {
                    ts = DateTime.UtcNow,
                    result = "ws-user-cap",
                    cap = _maxPerUser,
                    user,
                });
                Context.Abort();
                return;
            }
        }

        if (!string.IsNullOrEmpty(user))
        {
            // 登记连接时带上 HubCallerContext 引用：封号/删号可远程 Abort 该连接（即时踢线）
            _store.MarkConnected(Context.ConnectionId, user, Context);
            await SendPresenceToRelated(user, true);
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var user = CurrentUserId;
        _store.MarkDisconnected(Context.ConnectionId);
        if (!string.IsNullOrEmpty(user))
        {
            var stillOnline = _store.Connections.Values.Any(c => c.UserId == user);
            if (!stillOnline)
                await SendPresenceToRelated(user, false);
        }
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// 在线状态只推送给「有社交关系」的人（双向联系人 + 同群成员 + 自己其它端）。
    /// 此前广播给所有登录用户 —— 任何陌生人都能监控任意用户何时上线（偷窥/踩点向量）。
    /// </summary>
    private async Task SendPresenceToRelated (string user, bool online)
    {
        List<string> targetConnections;
        lock (_store)
        {
            targetConnections = _store.RelatedUserIds(user)
                .SelectMany(_store.ConnectionsOf)
                .ToList();
        }
        if (targetConnections.Count > 0)
            await Clients.Clients(targetConnections).SendAsync("Presence", user, online);
    }

    /// <summary>
    /// 「对方正在输入」：客户端在输入框敲键时节流调用本方法，
    /// 服务端校验「我是该会话成员」后，把提示定向推送给**该会话其他成员**的全部连接。
    /// 轻量、不落盘、不产生消息记录；无成员校验的广播会泄漏「谁在看哪个会话」。
    /// </summary>
    public async Task SendTyping(string conversationId)
    {
        var user = CurrentUserId;
        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(conversationId)) return;

        List<string> targetConnections;
        lock (_store)
        {
            if (!_store.Conversations.TryGetValue(conversationId, out var conv)) return;
            // 成员校验：非会话成员不能触发 Typing（也探测不到该会话的存在）
            if (!_store.CanAccessConversation(conversationId, user)) return;

            var memberIds = conv.Kind == "private"
                ? conv.Members.Where(m => m != user)
                : _store.Groups.TryGetValue(conv.MemberId, out var g)
                    ? g.Members.Select(m => m.UserId).Where(m => m != user)
                    : Enumerable.Empty<string>();
            targetConnections = memberIds.SelectMany(_store.ConnectionsOf).ToList();
        }
        if (targetConnections.Count > 0)
            await Clients.Clients(targetConnections).SendAsync("Typing", conversationId, user, DateTime.UtcNow);
    }

    /// <summary>会话成员的全部在线连接（成员 → 连接映射展开）。</summary>
    public static List<string> MemberConnections (DataStore store, string conversationId)
    {
        lock (store)
        {
            if (!store.Conversations.TryGetValue(conversationId, out var conv))
                return new List<string>();
            IEnumerable<string> memberIds = conv.Kind == "private"
                ? conv.Members
                : store.Groups.TryGetValue(conv.MemberId, out var g)
                    ? g.Members.Select(m => m.UserId)
                    : Enumerable.Empty<string>();
            return memberIds.SelectMany(store.ConnectionsOf).ToList();
        }
    }

    /// <summary>该用户自己全部在线连接（多端同步用：已读状态只同步自己的其它端）。</summary>
    private static List<string> UserConnections (DataStore store, string userId)
    {
        lock (store) return store.ConnectionsOf(userId).ToList();
    }

    /// <summary>供控制器调用：向会话全体成员的新连接推送新消息（含自己多端同步）。</summary>
    public static async Task BroadcastMessage(IHubContext<ChatHub> hub, DataStore store, Message message)
    {
        var connections = MemberConnections(store, message.ConversationId);
        if (connections.Count > 0)
            await hub.Clients.Clients(connections).SendAsync("NewMessage", message);
    }

    /// <summary>
    /// 供控制器调用：已读状态只广播给自己的其它端（多端红点同步）。
    /// 不发给对方 —— 微信单聊没有已读回执，对方是否阅读不应打扰发送者。
    /// </summary>
    public static async Task BroadcastRead(IHubContext<ChatHub> hub, DataStore store, string conversationId, string userId)
    {
        var connections = UserConnections(store, userId);
        if (connections.Count > 0)
            await hub.Clients.Clients(connections).SendAsync("MarkRead", conversationId, userId);
    }
}
