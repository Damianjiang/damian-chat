using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using WeChatWeb.Models;
using WeChatWeb.Services;

namespace WeChatWeb.Data;

/// <summary>
/// 持久化的数据快照（与 data/store.json 的字段一一对应，PascalCase）。
/// 用独立 DTO 做存盘，避免把瞬态（刷新令牌 / 在线连接）写进磁盘。
/// </summary>
public sealed class StoreSnapshot
{
    public List<User> Users { get; set; } = new();
    public Dictionary<string, List<Contact>> Contacts { get; set; } = new();
    public Dictionary<string, Conversation> Conversations { get; set; } = new();
    public List<Message> Messages { get; set; } = new();
    public List<Group> Groups { get; set; } = new();
    public List<Moment> Moments { get; set; } = new();
    public List<FriendRequest> FriendRequests { get; set; } = new();
    public List<Favorite> Favorites { get; set; } = new();
    public List<FileMeta> Files { get; set; } = new();
}

/// <summary>刷新令牌（7 天，轮转）。瞬态，不落盘。</summary>
public sealed class RefreshToken
{
    public string Value { get; set; } = "";
    public string UserId { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
}

/// <summary>一条 SignalR 在线连接。瞬态，不落盘。</summary>
public sealed class ConnectionState
{
    public string ConnectionId { get; set; } = "";
    public string UserId { get; set; } = "";
    public DateTime ConnectedAt { get; set; }
    public DateTime LastActive { get; set; }
    /// <summary>Hub 调用上下文：封号/删号时经 <c>AbortAsync</c> 即时踢线（即时失效，无需等客户端心跳）。</summary>
    public HubCallerContext? CallerContext { get; set; }
}

/// <summary>在线用户视图（管理端 /api/admin/online 用）。</summary>
public sealed class OnlineUser
{
    public string UserId { get; set; } = "";
    public DateTime LastActive { get; set; }
}

/// <summary>
/// 群限制：包装可热更新的 GroupConfig（data/group-config.json），
/// 暴露 AdminController 需要的只读属性 + Apply。
/// </summary>
public sealed class GroupLimits
{
    private readonly GroupConfig _cfg = GroupConfig.Load();
    public int MaxMembers => _cfg.MaxMembers;
    public int MaxAdmins => _cfg.MaxAdmins;
    public int MaxSelectOnCreate => _cfg.MaxSelectOnCreate;

    public void Apply (int? maxMembers, int? maxAdmins, int? maxSelectOnCreate)
        => _cfg.Apply(maxMembers, maxAdmins, maxSelectOnCreate);
}

/// <summary>
/// 内存数据核心 + 落盘（data/store.json，tmp+Move 原子替换）。
///
/// 线程模型：所有集合的并发访问以「本实例」为全局锁（调用方 <c>lock (store)</c>，
/// 控制器/Hub 均如此；.NET 的 Monitor 可重入，嵌套 lock 安全）。
/// 在线连接（Connections）与保存计时（MarkDirty）用各自独立的小锁，避免与全局锁交叉持有。
/// </summary>
public sealed class DataStore
{
    /* ---------------- 持久化集合（落盘） ---------------- */
    public Dictionary<string, User> Users { get; } = new();
    public Dictionary<string, List<Contact>> Contacts { get; } = new();
    public Dictionary<string, Conversation> Conversations { get; } = new();
    public List<Message> Messages { get; } = new();
    public Dictionary<string, Group> Groups { get; } = new();
    public List<Moment> Moments { get; } = new();
    public List<FriendRequest> FriendRequests { get; } = new();
    public List<Favorite> Favorites { get; } = new();
    public Dictionary<string, FileMeta> Files { get; } = new();

    /* ---------------- 瞬态（不落盘） ---------------- */
    public Dictionary<string, RefreshToken> RefreshTokens { get; } = new();
    public Dictionary<string, ConnectionState> Connections { get; } = new();
    public GroupLimits GroupLimits { get; } = new();

    /* 会话消息索引（convId → 时间升序列表）：历史/未读 O(1) 定位，替代全表线性扫。 */
    private readonly Dictionary<string, List<Message>> _msgIndex = new();
    /* 消息 Id 索引（msgId → Message）：游标定位 / 删除 / 撤回 O(1) 取消息（此前全表线性扫）。 */
    private readonly Dictionary<string, Message> _msgById = new();
    /* 文件 → 引用它的会话集合（下载/取票高频路径 O(1) 归属判定）。 */
    private readonly Dictionary<string, HashSet<string>> _fileRefs = new();

    private Timer? _saveTimer;
    private readonly object _saveGate = new();
    // 落盘只做机读、不做人读：紧凑 JSON（不缩进）比 WriteIndented 快数倍、文件更小，
    // 直接砍掉保存路径的 CPU 与 IO 开销（高并发下尤其关键）。
    private static readonly JsonSerializerOptions Opts = new();

    private static string DataDir () => Path.Combine(Directory.GetCurrentDirectory(), "data");
    private string StorePath => Path.Combine(DataDir(), "store.json");

    /// <summary>store.json 权限收紧节流：每 60s 至多调一次 icacls/chmod（落盘很频繁，避免拖慢热路径）。</summary>
    private DateTime _lastRestrictUtc = DateTime.MinValue;

    /// <summary>
    /// 启动时 store.json 存在但读不到（权限被锁死 / IO 失败）。
    /// 此时内存里是**空库**，一旦保存就会把磁盘上的好数据覆盖成空库 ——
    /// 这是最危险的数据丢失路径，所以整体禁用落盘：宁可丢本次运行的新数据，
    /// 也绝不用空库覆盖真实数据。
    /// </summary>
    private bool _loadFailed;

    public DataStore ()
    {
        var backfilled = Load();
        // 服务端保留 1 周聊天记录：启动即把超过保留期的消息清掉，
        // 不让重启前的旧数据在内存/磁盘里继续停留
        PruneOldMessages(RetentionMessages);
        // 无 store.json 时写出空骨架（重启不重建空数据）；或启动期补齐了 WxId 也要落盘
        if (!File.Exists(StorePath) || backfilled) SaveNow();
    }

    /// <summary>服务端聊天记录保留期：1 周（更早的交给浏览器 IndexedDB 缓存，启动/定时清理）。</summary>
    public static TimeSpan RetentionMessages { get; } = TimeSpan.FromDays(7);

    /* ================= 加载 / 保存 ================= */

    /// <summary>返回是否有数据被启动期修正（如补齐 WxId），供构造决定是否落盘。</summary>
    private bool Load ()
    {
        var changed = false;
        if (!File.Exists(StorePath)) return false;
        StoreSnapshot? snap;
        try
        {
            // 自愈读：store.json 存在但读不到（历史遗留：icacls /t 把 DACL 清空）时，
            // 先尝试修 ACL 再读。绝不静默返回空数据 —— 那会让服务「看起来正常」地
            // 带着空用户/空消息运行，用户全部登录不上，而磁盘上的数据其实还在。
            if (!SensitiveFile.TryReadAllText(StorePath, out var raw))
            {
                _loadFailed = true;
                Console.Error.WriteLine(
                    $"[DataStore] 严重：无法读取 {StorePath}，本次以空数据启动。" +
                    "用户/消息/群/朋友圈都不会显示。请修复该文件权限（见上方 [敏感文件权限] 提示）后重启。");
                return false;
            }
            snap = JsonSerializer.Deserialize<StoreSnapshot>(raw);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DataStore] store.json 解析失败，本次以空数据启动：{ex.GetType().Name}: {ex.Message}");
            return false; // 损坏：保留空数据，避免带病启动
        }
        if (snap == null) return false;

        Users.Clear();
        foreach (var u in snap.Users ?? new()) if (!string.IsNullOrEmpty(u.Id)) Users[u.Id] = u;

        // 历史/早期用户可能没生成过大面号（WxId），启动时补齐，避免前端显示「大面号：-」
        foreach (var u in Users.Values)
            if (string.IsNullOrEmpty(u.WxId))
            {
                u.WxId = "wx" + Guid.NewGuid().ToString("N")[..8];
                changed = true;
            }

        Contacts.Clear();
        foreach (var kv in snap.Contacts ?? new()) Contacts[kv.Key] = kv.Value ?? new List<Contact>();

        Conversations.Clear();
        foreach (var kv in snap.Conversations ?? new())
            Conversations[kv.Key] = kv.Value ?? new Conversation { Id = kv.Key };

        Messages.Clear();
        foreach (var m in snap.Messages ?? new()) Messages.Add(m);

        Groups.Clear();
        foreach (var g in snap.Groups ?? new()) if (!string.IsNullOrEmpty(g.Id)) Groups[g.Id] = g;

        Moments.Clear();
        foreach (var m in snap.Moments ?? new()) Moments.Add(m);

        FriendRequests.Clear();
        foreach (var r in snap.FriendRequests ?? new()) FriendRequests.Add(r);

        Favorites.Clear();
        foreach (var f in snap.Favorites ?? new()) Favorites.Add(f);

        Files.Clear();
        foreach (var f in snap.Files ?? new()) if (!string.IsNullOrEmpty(f.Id)) Files[f.Id] = f;

        // 重建会话消息索引 + 文件引用索引
        foreach (var m in Messages) MsgIndexAdd(m);
        return changed;
    }

    /// <summary>
    /// 立即原子落盘（tmp + Move 替换，中途崩溃不损坏）。
    /// 性能关键点：只在「快照」阶段持全局锁（快速克隆各集合），耗时的
    /// 序列化 + 写盘放到锁外做 —— 避免高并发下每个请求在这把锁上排队等整库序列化，
    /// 产生延迟尖刺。并发保存时后写覆盖先写（都是全量快照），不影响一致性。
    /// </summary>
    public void SaveNow ()
    {
        // 数据丢失保护：启动时没读到 store.json（内存是空库），此时保存等于
        // 用空库覆盖磁盘上的真实数据。整体跳过落盘并显式告警。
        if (_loadFailed)
        {
            Console.Error.WriteLine(
                $"[DataStore] 已跳过保存：启动时无法读取 {StorePath}，拒绝以空数据覆盖。" +
                "请修复该文件权限后重启服务。");
            return;
        }

        var snap = new StoreSnapshot();
        lock (this)
        {
            snap.Users = Users.Values.ToList();
            snap.Contacts = Contacts.ToDictionary(kv => kv.Key, kv => kv.Value);
            snap.Conversations = Conversations.ToDictionary(kv => kv.Key, kv => kv.Value);
            snap.Messages = Messages.ToList();
            snap.Groups = Groups.Values.ToList();
            snap.Moments = Moments.ToList();
            snap.FriendRequests = FriendRequests.ToList();
            snap.Favorites = Favorites.ToList();
            snap.Files = Files.Values.ToList();
        }
        try
        {
            Directory.CreateDirectory(DataDir());
            var json = JsonSerializer.Serialize(snap, Opts);
            var tmp = StorePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, StorePath, overwrite: true);
            // 敏感数据文件（含密码哈希）落盘即收紧为仅当前用户可读写。
            // 60s 节流：落盘很频繁（750ms 合并窗口），避免每写一次都调 icacls/chmod 拖慢热路径。
            var now = DateTime.UtcNow;
            if ((now - _lastRestrictUtc).TotalSeconds > 60)
            {
                _lastRestrictUtc = now;
                SensitiveFile.RestrictToOwner(StorePath);
            }
        }
        catch { /* 只读/磁盘满：内存数据仍可用，不阻塞主流程 */ }
    }

    /// <summary>合并窗口落盘：750ms 内的多次脏标记合并为一次写盘（刷屏不逐条写）。</summary>
    public void MarkDirty ()
    {
        lock (_saveGate)
        {
            if (_saveTimer is null)
            {
                _saveTimer = new Timer(_ =>
                {
                    try { SaveNow(); } catch { /* ignore */ }
                }, null, 750, Timeout.Infinite);
            }
            else
            {
                _saveTimer.Change(750, Timeout.Infinite);
            }
        }
    }

    /* ================= 用户 ================= */

    /// <summary>注册：建用户 + 空联系人表；昵称默认取账号。调用方需持有 lock(store)。</summary>
    public User RegisterUser (string account, string password)
    {
        var u = new User
        {
            Id = "u-" + Guid.NewGuid().ToString("N")[..12],
            Phone = account,
            Password = PasswordHasher.Hash(password),
            Nickname = account,
            WxId = "wx" + Guid.NewGuid().ToString("N")[..8],
            CreatedAt = DateTime.UtcNow,
        };
        Users[u.Id] = u;
        if (!Contacts.ContainsKey(u.Id)) Contacts[u.Id] = new List<Contact>();
        return u;
    }

    /// <summary>
    /// 删除用户并级联清理其全部关联数据（管理端「删除用户」用）：
    /// ① 该用户自己的联系人表；② 其他用户联系人表里指向它的记录；
    /// ③ 含该用户的私聊会话 + 其消息 + 索引；④ 群成员（含群主移交/删群）与群会话；
    /// ⑤ 它发布的朋友圈及点赞/评论；⑥ 与它相关的好友申请；⑦ 它的收藏；
    /// ⑧ 它的刷新令牌 + 在线连接（瞬态，立即失效）；⑨ 它上传的文件（去重命中则保留）。
    /// 调用方需持有 lock(store)。返回清理摘要。
    /// </summary>
    public Models.UserDeleteSummary RemoveUser (string userId)
    {
        var s = new Models.UserDeleteSummary { UserId = userId };
        if (string.IsNullOrEmpty(userId)) return s;

        // ② 其他用户联系人表里指向它的记录（先收集，避免边迭代边改）
        foreach (var kv in Contacts.ToList())
        {
            s.RemovedContacts += kv.Value.RemoveAll(c => c.OtherUserId == userId);
        }

        // ③ 私聊会话：含该用户为成员 → 删会话 + 清消息
        foreach (var cid in Conversations.Values.Where(c => c.Members.Contains(userId)).Select(c => c.Id).ToList())
        {
            ClearConversationMessagesLocked(cid);
            Conversations.Remove(cid);
            s.RemovedConversations++;
        }

        // ④ 群成员 + 群会话：移出每个群；群主离开则移交群主位，群无剩余成员则解散
        foreach (var g in Groups.Values.ToList())
        {
            var mine = g.Members.Find(m => m.UserId == userId);
            if (mine == null) continue;
            g.Members.Remove(mine);
            s.RemovedGroupMemberships++;

            if (g.Members.Count == 0)
            {
                // 群已无成员 → 解散：清群会话消息 + 删会话 + 删群
                ClearConversationMessagesLocked(g.Id);
                Conversations.Remove(g.Id);
                Groups.Remove(g.Id);
                s.RemovedGroups++;
            }
            else if (g.OwnerId == userId)
            {
                // 群主离开 → 移交给仍在群内的第一个成员
                var next = g.Members[0];
                g.OwnerId = next.UserId;
                next.Role = "owner";
            }
        }

        // ⑤ 它发布的朋友圈：删动态 + 移除它在别人动态里的点赞/评论
        s.RemovedMoments = Moments.RemoveAll(m => m.AuthorId == userId);
        foreach (var mo in Moments)
        {
            mo.Likes.RemoveAll(x => x == userId);
            mo.Comments.RemoveAll(x => x.UserId == userId);
        }

        // ⑥ 与它相关的好友申请（发起或接收方）
        s.RemovedFriendRequests = FriendRequests.RemoveAll(r => r.FromUserId == userId || r.ReceiverId == userId);

        // ⑦ 它的收藏
        s.RemovedFavorites = Favorites.RemoveAll(f => f.UserId == userId);

        // ⑧ 瞬态：刷新令牌 + 在线连接立即失效
        var myTokenKeys = RefreshTokens.Where(kv => kv.Value.UserId == userId).Select(kv => kv.Key).ToList();
        s.RemovedRefreshTokens = myTokenKeys.Count;
        foreach (var key in myTokenKeys) RefreshTokens.Remove(key);
        foreach (var key in Connections.Where(kv => kv.Value.UserId == userId).Select(kv => kv.Key).ToList())
            Connections.Remove(key);

        // ⑨ 它上传的文件：一次性建"仍被引用"的文件集合（消息图片/朋友圈图/收藏封面/用户头像，各扫一遍），
        // 再 O(1) 判定每个待删文件是否保留字节。避免每文件全表扫（O(文件×消息) 热点，文件多时长时间卡全局锁）
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in Messages) if (!string.IsNullOrEmpty(m.ImageId)) referenced.Add(m.ImageId!);
        foreach (var mo in Moments) foreach (var img in mo.Images) if (!string.IsNullOrEmpty(img)) referenced.Add(img);
        foreach (var fc in Favorites) if (!string.IsNullOrEmpty(fc.ImageUrl)) referenced.Add(fc.ImageUrl);
        foreach (var u in Users.Values) if (!string.IsNullOrEmpty(u.Avatar)) referenced.Add(u.Avatar);

        foreach (var f in Files.Values.Where(f => f.UploaderId == userId).ToList())
        {
            var stillRef = referenced.Contains(f.Id);
            Files.Remove(f.Id);
            if (!stillRef) FileDeleteBestEffort(f.Path);
            s.RemovedFiles++;
        }

        // ① 它自己的数据最后清：用户记录 + 联系人表
        Users.Remove(userId);
        Contacts.Remove(userId);

        SaveNow();
        return s;
    }

    /// <summary>文件字节删除（best-effort，与 FileStore.TryDelete 同语义）：失败仅忽略，不影响数据清理。</summary>
    private static void FileDeleteBestEffort (string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { if (File.Exists(path)) File.Delete(path); } catch { /* 占用/只读：忽略 */ }
    }

    /* ================= 联系人 / 好友关系 ================= */

    /// <summary>双向建立好友：a↔b 各加一条联系记录（幂等）。调用方需持有 lock(store)。</summary>
    public void AddMutualContact (string a, string b)
    {
        if (!Contacts.ContainsKey(a)) Contacts[a] = new List<Contact>();
        if (!Contacts.ContainsKey(b)) Contacts[b] = new List<Contact>();
        if (Contacts[a].All(c => c.OtherUserId != b))
            Contacts[a].Add(new Contact { OtherUserId = b, FirstLetter = FirstLetterOf(b) });
        if (Contacts[b].All(c => c.OtherUserId != a))
            Contacts[b].Add(new Contact { OtherUserId = a, FirstLetter = FirstLetterOf(a) });
    }

    private string FirstLetterOf (string userId)
    {
        var nick = Users.TryGetValue(userId, out var u) ? u.Nickname : "";
        if (string.IsNullOrWhiteSpace(nick)) return "#";
        var ch = char.ToUpperInvariant(nick.Trim()[0]);
        return ch is >= 'A' and <= 'Z' ? ch.ToString() : "#";
    }

    /// <summary>me 是否已把 other 加为好友。调用方需持有 lock(store)。</summary>
    public bool IsContact (string me, string other)
    {
        if (!Contacts.TryGetValue(me, out var l)) return false;
        return l.Any(c => c.OtherUserId == other);
    }

    /// <summary>me 的联系人表里是否把 other 拉黑。调用方需持有 lock(store)。</summary>
    public bool IsBlocked (string me, string other)
    {
        if (!Contacts.TryGetValue(me, out var l)) return false;
        return l.Any(c => c.OtherUserId == other && c.Blocked);
    }

    /// <summary>
    /// 双向删除好友关系：me 与 other 各自的联系人表里都去掉对方（幂等）。
    /// 保留各自的共享会话（聊天记录/群不受影响），仅解除「好友」状态。
    /// 调用方需持有 lock(store)。
    /// </summary>
    public void RemoveFriend (string me, string other)
    {
        if (me == other) return;
        if (Contacts.TryGetValue(me, out var mine))
            mine.RemoveAll(c => c.OtherUserId == other);
        if (Contacts.TryGetValue(other, out var theirs))
            theirs.RemoveAll(c => c.OtherUserId == me);
    }

    /// <summary>
    /// me 已拉黑的用户 id 集合（联系人表里 Blocked=true 的项）。调用方需持有 lock(store)。
    /// </summary>
    public IReadOnlyList<string> BlockedUserIds (string me)
    {
        if (!Contacts.TryGetValue(me, out var l)) return Array.Empty<string>();
        return l.Where(c => c.Blocked).Select(c => c.OtherUserId).ToList();
    }

    /// <summary>
    /// 取/建 me 对 other 的联系人记录（若好友关系不存在则不建，返回 null）。
    /// 调用方需持有 lock(store)。
    /// </summary>
    public Contact? GetContact (string me, string other)
    {
        return Contacts.TryGetValue(me, out var l) ? l.FirstOrDefault(c => c.OtherUserId == other) : null;
    }

    /* ================= 会话 ================= */

    /// <summary>取或建「私聊」会话：按双方 id 排序生成稳定 id，幂等。调用方需持有 lock(store)。</summary>
    public Conversation GetOrCreatePairConversation (string a, string b)
    {
        var key = a.CompareTo(b) <= 0 ? a + "|" + b : b + "|" + a;
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        var cid = "c-" + hash[..Math.Min(hash.Length, 30)];
        if (Conversations.TryGetValue(cid, out var existing)) return existing;
        var conv = new Conversation
        {
            Id = cid,
            Kind = "private",
            Members = new List<string> { a, b },
            MemberId = b,
        };
        Conversations[cid] = conv;
        return conv;
    }

    /// <summary>用户是否能访问该会话（私聊成员 / 群成员）。调用方需持有 lock(store)。</summary>
    public bool CanAccessConversation (string conversationId, string userId)
    {
        if (!Conversations.TryGetValue(conversationId, out var conv)) return false;
        if (conv.Kind == "private") return conv.Members.Contains(userId);
        return Groups.TryGetValue(conv.MemberId, out var g) && g.Members.Any(m => m.UserId == userId);
    }

    /// <summary>已读：记录该用户在此会话的已读时间戳。调用方需持有 lock(store)。</summary>
    public void MarkRead (string conversationId, string userId)
    {
        if (Conversations.TryGetValue(conversationId, out var conv))
        {
            conv.ReadMarks[userId] = DateTime.UtcNow;
            // 已读必须同时清零增量未读计数：UnreadFor 优先读 UnreadCounts，
            // 只写 ReadMarks 不动它的话，残留的计数会在刷新/重登后把红点盖回来
            conv.UnreadCounts[userId] = 0;
        }
    }

    /// <summary>清空某会话全部消息 + 索引 + 文件引用，并立即落盘（群解散用）。</summary>
    public void ClearConversationMessages (string conversationId)
    {
        lock (this)
        {
            ClearConversationMessagesLocked(conversationId);
        }
        SaveNow();
    }

    /// <summary>清会话消息 + 索引 + 文件引用的核心逻辑（纯逻辑，不落盘）。调用方需持有 lock(store)。</summary>
    private void ClearConversationMessagesLocked (string conversationId)
    {
        Messages.RemoveAll(m => m.ConversationId == conversationId);
        _msgIndex.Remove(conversationId);
        foreach (var set in _fileRefs.Values)
            set.Remove(conversationId);
        foreach (var key in _fileRefs.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key).ToList())
            _fileRefs.Remove(key);
    }

    /* ================= 消息索引 ================= */

    public void MsgIndexAdd (Message m)
    {
        if (string.IsNullOrEmpty(m.ConversationId)) return;
        if (!_msgIndex.TryGetValue(m.ConversationId, out var list))
        {
            list = new List<Message>();
            _msgIndex[m.ConversationId] = list;
        }
        // 新消息只入列一次，去掉 O(n) 的 Contains 判重（长会话每条消息都会全扫列表，纯浪费）
        list.Add(m);
        // 消息 Id 索引同步（游标/删除/撤回 O(1) 定位）
        _msgById[m.Id] = m;

        if (!string.IsNullOrEmpty(m.ImageId))
        {
            if (!_fileRefs.TryGetValue(m.ImageId!, out var set))
            {
                set = new HashSet<string>();
                _fileRefs[m.ImageId!] = set;
            }
            set.Add(m.ConversationId);
        }
    }

    public void MsgIndexRemove (Message m)
    {
        if (string.IsNullOrEmpty(m.ConversationId))
        {
            _msgById.Remove(m.Id);
            return;
        }
        List<Message>? list = null;
        if (_msgIndex.TryGetValue(m.ConversationId, out list)) list?.Remove(m);
        _msgById.Remove(m.Id);

        if (!string.IsNullOrEmpty(m.ImageId) && list != null)
        {
            // 该会话里是否还有其他消息引用同一文件：只在「本会话」内查（O(单会话)），
            // 不再 O(全库) 扫 Messages（删消息是高频操作，全表扫在人/消息多时拖垮删除路径）
            var stillReferenced = list.Any(x => x.ImageId == m.ImageId);
            if (_fileRefs.TryGetValue(m.ImageId!, out var set))
            {
                if (stillReferenced) set.Add(m.ConversationId);
                else
                {
                    set.Remove(m.ConversationId);
                    if (set.Count == 0) _fileRefs.Remove(m.ImageId!);
                }
            }
        }
    }

    /// <summary>
    /// 过期消息清理：只保留最近 <paramref name="retention"/> 内的消息（服务端仅存 1 天聊天记录）。
    /// 被删消息同步从会话索引 + 文件引用索引中移除，随后立即落盘。
    ///
    /// 性能：采用「保留集重建索引」而非逐条 Remove（逐条 Remove 是 O(n²)）。
    /// 只有真正有消息被删（removedCount&gt;0）时才重建两个索引 + 落盘，
    /// 后续 30 分钟一次的定时清理通常删 0 条 → 零开销快路径直接返回。
    /// </summary>
    public int PruneOldMessages (TimeSpan retention)
    {
        var cutoff = DateTime.UtcNow - retention;
        List<Message> keep;
        int removedCount;
        lock (this)
        {
            keep = Messages.Where(m => m.CreatedAt >= cutoff).ToList();
            removedCount = Messages.Count - keep.Count;
            if (removedCount == 0) return 0;
            Messages.Clear();
            Messages.AddRange(keep);
            // 全量重建会话消息索引 + 文件引用索引，保证与保留集严格一致
            _msgIndex.Clear();
            _fileRefs.Clear();
            foreach (var m in keep) MsgIndexAdd(m);
        }
        // 落盘放到全局锁外：SaveNow 会做全库序列化，持锁期间做会把所有请求卡在这把锁上
        SaveNow();
        return removedCount;
    }

    /// <summary>会话消息（时间升序）；无则空列表。调用方需持有 lock(store)。</summary>
    public List<Message> MessagesOf (string conversationId)
    {
        if (_msgIndex.TryGetValue(conversationId, out var list)) return list;
        return new List<Message>();
    }

    /// <summary>按消息 Id O(1) 取消息（游标分页 / 删除 / 撤回定位用）。调用方需持有 lock(store)。</summary>
    public Message? GetMessageById (string messageId)
    {
        if (string.IsNullOrEmpty(messageId)) return null;
        return _msgById.TryGetValue(messageId, out var m) ? m : null;
    }

    /// <summary>
    /// 新消息入列后维护未读计数：非发送者的会话成员各 +1（写路径增量，列表 O(1) 读）。
    /// 私聊 = 另一方；群聊 = 除发送者外所有成员。调用方需持有 lock(store)。
    /// </summary>
    public void OnNewMessage (Message m)
    {
        if (m == null || string.IsNullOrEmpty(m.ConversationId)) return;
        if (Conversations.TryGetValue(m.ConversationId, out var conv))
            BumpUnread(conv, m);
    }

    /// <summary>
    /// 消息不再计入未读（撤回 / 删除）：对该消息所有非发送者的未读计数 -1。
    /// 调用方需持有 lock(store)。
    /// </summary>
    public void OnMessageUncounted (Message m)
    {
        if (m == null || string.IsNullOrEmpty(m.ConversationId)) return;
        if (Conversations.TryGetValue(m.ConversationId, out var conv))
            UnbumpUnread(conv, m);
    }

    private void BumpUnread (Conversation conv, Message m)
    {
        var dict = conv.UnreadCounts;
        foreach (var uid in UnreadTargets(conv, m))
            dict[uid] = (dict.TryGetValue(uid, out var c) ? c : 0) + 1;
    }

    private void UnbumpUnread (Conversation conv, Message m)
    {
        var dict = conv.UnreadCounts;
        foreach (var uid in UnreadTargets(conv, m))
        {
            if (dict.TryGetValue(uid, out var c) && c > 0) dict[uid] = c - 1;
        }
    }

    /// <summary>某条消息「应计未读」的接收者：私聊=另一方；群聊=除发送者外所有成员。调用方需持有 lock(store)。</summary>
    private IEnumerable<string> UnreadTargets (Conversation conv, Message m)
    {
        if (conv.Kind == "private")
        {
            foreach (var mem in conv.Members)
                if (mem != m.SenderId) yield return mem;
        }
        else
        {
            if (Groups.TryGetValue(conv.MemberId, out var g))
                foreach (var gm in g.Members)
                    if (gm.UserId != m.SenderId) yield return gm.UserId;
        }
    }

    /// <summary>
    /// 未读数：优先用写路径增量维护的 <c>UnreadCounts</c>（O(1)）；
    /// 该用户未命中（历史数据 / 字段引入前）则回退全量扫描并写回（首次自愈，兼容旧数据）。
    /// 调用方需持有 lock(store)。
    /// </summary>
    public int UnreadFor (string conversationId, string userId)
    {
        if (!Conversations.TryGetValue(conversationId, out var conv)) return 0;
        var dict = conv.UnreadCounts;
        if (dict.TryGetValue(userId, out var c)) return Math.Max(0, c);
        // 回退全量扫描（ReadMarks 语义与原实现一致）并写回，使后续 O(1)
        var marked = conv.ReadMarks.TryGetValue(userId, out var t) ? t : DateTime.MinValue;
        int n = 0;
        foreach (var m in MessagesOf(conversationId))
            if (!m.Recalled && m.SenderId != userId && m.CreatedAt > marked) n++;
        dict[userId] = n;
        return n;
    }

    /// <summary>
    /// 瞬态保养：清掉过期刷新令牌 + 终态（accepted/rejected）且超 3 天的历史好友申请，
    /// 返回 (清理令牌数, 清理申请数)。调用方无需 lock（内部自锁瞬态集合）。
    /// </summary>
    public (int tokens, int requests) PruneTransient ()
    {
        var now = DateTime.UtcNow;
        int tokens = 0, requests = 0;
        lock (this)
        {
            foreach (var kv in RefreshTokens.ToList())
                if (kv.Value.ExpiresAt <= now) { RefreshTokens.Remove(kv.Key); tokens++; }
            var threshold = now - TimeSpan.FromDays(3);
            foreach (var r in FriendRequests.ToList())
                if (r.Status != "pending" && r.CreatedAt < threshold) { FriendRequests.Remove(r); requests++; }
        }
        return (tokens, requests);
    }

    /// <summary>文件归属：上传者本人，或在任一自己可访问的会话里收到过该文件。调用方需持有 lock(store)。</summary>
    public bool CanReadFile (string fileId, string uploaderId, string me)
    {
        if (string.IsNullOrEmpty(fileId)) return uploaderId == me;
        if (uploaderId == me) return true;
        if (!_fileRefs.TryGetValue(fileId, out var convs)) return false;
        foreach (var c in convs)
            if (CanAccessConversation(c, me)) return true;
        return false;
    }

    /* ================= 在线连接 / 在线状态 ================= */

    /// <summary>
    /// 登记/更新一条在线连接。可选带上 <paramref name="ctx"/>（HubCallerContext）：
    /// 封号/删号时经 <c>KickConnections</c> 对其 <c>AbortAsync</c> 即时踢线。
    /// </summary>
    public void MarkConnected (string connectionId, string userId, HubCallerContext? ctx = null)
    {
        lock (this)
        {
            var now = DateTime.UtcNow;
            // 保留该连接已有的 CallerContext（同一连接重连 OnConnected 再次登记时）
            if (Connections.TryGetValue(connectionId, out var prev) && prev.CallerContext != null && ctx == null)
                ctx = prev.CallerContext;
            Connections[connectionId] = new ConnectionState
            {
                ConnectionId = connectionId,
                UserId = userId,
                ConnectedAt = now,
                LastActive = now,
                CallerContext = ctx,
            };
        }
    }

    public void MarkDisconnected (string connectionId)
    {
        lock (this)
        {
            Connections.Remove(connectionId);
        }
    }

    /// <summary>
    /// 封号/删号时把该用户所有在线连接从在线表移除（在线状态即时不再显示），
    /// 返回被踢连接数。底层 TCP 连接的强制断开由 <c>UserInvalidationMiddleware</c> /
    /// 信号中继的 <c>Context.Abort()</c> 在请求层即时完成（按 sub 反查封禁状态），
    /// 这里只需清理在线表，避免与中间件重复 Abort 造成时序竞争。
    /// 调用方无需预先 lock（内部自锁在线连接表）。
    /// </summary>
    public int KickConnections (string userId)
    {
        if (string.IsNullOrEmpty(userId)) return 0;
        int kicked = 0;
        lock (this)
        {
            foreach (var kv in Connections.ToList())
            {
                if (kv.Value.UserId == userId)
                {
                    Connections.Remove(kv.Key);
                    kicked++;
                }
            }
        }
        return kicked;
    }

    /// <summary>某用户当前全部在线连接。调用方需持有 lock(store)（或本方法内部已锁）。</summary>
    public IReadOnlyList<string> ConnectionsOf (string userId)
    {
        lock (this)
        {
            return Connections.Where(kv => kv.Value.UserId == userId).Select(kv => kv.Key).ToList();
        }
    }

    /// <summary>当前在线用户（每人取最后活跃时间）。调用方无需预先 lock。</summary>
    public IReadOnlyList<OnlineUser> OnlineUsers ()
    {
        lock (this)
        {
            return Connections.Values
                .GroupBy(c => c.UserId)
                .Select(g => new OnlineUser { UserId = g.Key, LastActive = g.Max(c => c.LastActive) })
                .ToList();
        }
    }

    /// <summary>
    /// 需要接收某用户在线状态变更的人：自己 + 双向联系人 + 同群成员 + 把我加为联系人者。
    /// 调用方需持有 lock(store)。
    /// </summary>
    public IEnumerable<string> RelatedUserIds (string userId)
    {
        var set = new HashSet<string> { userId };
        if (Contacts.TryGetValue(userId, out var mine))
            foreach (var c in mine) set.Add(c.OtherUserId);
        foreach (var kv in Contacts)
            if (kv.Key != userId && kv.Value.Any(c => c.OtherUserId == userId)) set.Add(kv.Key);
        foreach (var g in Groups.Values)
            if (g.Members.Any(m => m.UserId == userId))
                foreach (var m in g.Members) set.Add(m.UserId);
        return set;
    }
}
