namespace WeChatWeb.Models;

public class User
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Phone { get; set; } = "";

    /// <summary>
    /// PBKDF2 哈希。需要落盘（否则重启后所有人无法登录），
    /// 但绝不能出现在任何 API 响应里 —— 所有对外返回都走 PublicProfile 等显式 DTO。
    /// </summary>
    public string Password { get; set; } = "";

    public string Nickname { get; set; } = "";
    public string Avatar { get; set; } = "";

    /// <summary>大面号（唯一、可搜索，首次生成后不变）。</summary>
    public string WxId { get; set; } = "";
    public string Signature { get; set; } = "";
    public string Region { get; set; } = "";
    public string Gender { get; set; } = "unknown";
    public string? Remark { get; set; }
    public bool Banned { get; set; }
    public bool IsBot { get; set; }
    public Dictionary<string, bool> Privacy { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>密码最近一次修改时刻（UTC）。改密时：refresh token 全部吊销 + 踢掉实时连接，
    /// 中间件按令牌内的 pwd 签发时间戳拒绝「改密前签发」的 access token（不必等 2h 自然过期）。</summary>
    public DateTime PasswordChangedAt { get; set; } = DateTime.MinValue;
}

public class Contact
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OtherUserId { get; set; } = "";
    public string FirstLetter { get; set; } = "A";
    public List<string> Groups { get; set; } = new();
    public string? Remark { get; set; }
    public bool Blocked { get; set; }
}

public class Conversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "private";

    /// <summary>私聊双方（[a, b]）。群聊由 Group.Members 决定可见性。</summary>
    public List<string> Members { get; set; } = new();

    public string MemberId { get; set; } = "";
    public bool Pinned { get; set; }
    public bool Muted { get; set; }
    /// <summary>会话是否被「我」折叠（隐藏在主列表，集中在"折叠群聊"里显示）。</summary>
    public bool Folded { get; set; }

    /// <summary>
    /// 每个用户各自的已读时间（userId -> 最后阅读的消息时间）。
    /// 未读数按「晚于自己的已读时间、且不是自己发的」实时计算（DataStore.UnreadFor）。
    /// </summary>
    public Dictionary<string, DateTime> ReadMarks { get; set; } = new();

    /// <summary>
    /// 每个用户各自的未读数（userId -> 计数）。写路径增量维护（新消息 +1、已读清 0、
    /// 撤回/删除 -1），列表/详情接口直接 O(1) 读取，不再每次全量重扫会话历史消息。
    /// 首次访问未命中时回退全量扫描并写回（兼容本字段引入前的旧数据）。
    /// </summary>
    public Dictionary<string, int> UnreadCounts { get; set; } = new();

    public DateTime? LastAt { get; set; }
}

public class Message
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ConversationId { get; set; } = "";
    public string SenderId { get; set; } = "";
    public string Type { get; set; } = "text";
    public string Content { get; set; } = "";
    public string? ImageId { get; set; }
    public int? VoiceSeconds { get; set; }
    public string? FileName { get; set; }
    public long? FileSize { get; set; }
    public string? StickerEmoji { get; set; }
    public string? QuoteJson { get; set; }
    /// <summary>是否为「拍一拍」消息（双击头像触发，全群/对方可见）。</summary>
    public bool Pat { get; set; }
    /// <summary>被拍的人（群聊里点头像的人；私聊就是对方）。</summary>
    public string? PatTarget { get; set; }
    public bool Recalled { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Group
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Avatar { get; set; } = "";
    public string Notice { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public List<GroupMember> Members { get; set; } = new();
    /// <summary>加群策略：open=知道群号可直接进 / approve=需群主审批。默认 approve（更安全，默认不开放陌生人进群）。</summary>
    public string JoinPolicy { get; set; } = "approve";
    /// <summary>待审批的加群申请（approve 群用；open 群直接入群不走此列表）。非成员输群号申请后落到这里等群主审批。</summary>
    public List<JoinRequest> JoinRequests { get; set; } = new();
    /// <summary>各成员的群公告「已确认」时间（userId → 确认时刻），用于公告回执展示。</summary>
    public Dictionary<string, DateTime> NoticeReadAt { get; set; } = new();
    /// <summary>群公告最近一次「创建/修改」时刻。确认状态按此版本判定：
    /// 公告被改动/新增后，旧确认自动失效 → 各成员会重新看到并确认。</summary>
    public DateTime NoticeUpdatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>群投票（微信式：发起/投票/实时看票数，可多选，发起人或群主可截止）。</summary>
    public List<Poll> Polls { get; set; } = new();
    /// <summary>入群问题（JoinPolicy=question 时生效）：申请者答对即自动进群（QQ 式「回答问题入群」）。</summary>
    public string JoinQuestion { get; set; } = "";
    /// <summary>入群问题的标准答案（不区分大小写比较；仅群主/管理员与入群流程可见）。</summary>
    public string JoinAnswer { get; set; } = "";
    /// <summary>全员禁言（QQ 式）：开启后普通成员不能在群内发言，群主/管理员不受限。</summary>
    public bool MuteAll { get; set; }
}

/// <summary>群投票。</summary>
public class Poll
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public List<PollOption> Options { get; set; } = new();
    /// <summary>是否多选；false = 单选（换票时自动改投）。</summary>
    public bool Multi { get; set; }
    /// <summary>是否已截止（截止后只读，发起人/群主可截止）。</summary>
    public bool Closed { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class PollOption
{
    public string Text { get; set; } = "";
    /// <summary>投给该选项的用户 id 列表（实时统计票数与占比）。</summary>
    public List<string> UserIds { get; set; } = new();
}

public class GroupMember
{
    public string UserId { get; set; } = "";
    public string Role { get; set; } = "member";
    /// <summary>永久禁言（QQ 式「禁言」；限时禁言用 MutedUntil）。</summary>
    public bool Muted { get; set; }
    /// <summary>限时禁言截止时刻（QQ 式：10 分钟/1 小时/12 小时/1 天…），到期自动解除。</summary>
    public DateTime? MutedUntil { get; set; }
    /// <summary>我在本群的昵称（空则显示大面昵称）。</summary>
    public string Nickname { get; set; } = "";
    /// <summary>群头衔（群主/管理员可为每位成员单独设置，如「群管」「元老」「管理员」），空则不显示。</summary>
    public string Title { get; set; } = "";
}

/// <summary>加群申请（群主/管理员审批）。存申请人快照，避免被申请用户改名/删号后申请悬空。</summary>
public class JoinRequest
{
    public string UserId { get; set; } = "";
    /// <summary>申请时刻的昵称快照（审批列表展示用）。</summary>
    public string Nickname { get; set; } = "";
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string Note { get; set; } = "";
}

public class Moment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AuthorId { get; set; } = "";
    public string Text { get; set; } = "";
    public List<string> Images { get; set; } = new();
    public string Scope { get; set; } = "all";
    public List<string> Likes { get; set; } = new();
    public List<MomentComment> Comments { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class MomentComment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class FriendRequest
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FromUserId { get; set; } = "";
    public string ReceiverId { get; set; } = "";
    public string Message { get; set; } = "";
    public string Status { get; set; } = "pending";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>上传文件元数据（实际文件存 FilesDir 下，元数据在内存便于按 id 取）。</summary>
public class FileMeta
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; } = "";
    public long Size { get; set; }
    public string ContentType { get; set; } = "";
    public string Path { get; set; } = "";
    public string UploaderId { get; set; } = "";

    /// <summary>内容 SHA-256（去重用；相同内容只存一份）。</summary>
    public string Hash { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>收藏：消息或动态。</summary>
public class Favorite
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = "";
    public string TargetType { get; set; } = "message";   // message | moment
    public string TargetId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Preview { get; set; } = "";
    public string? ImageUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>删除用户时的级联清理摘要（管理端展示 / 审计日志用）。</summary>
public class UserDeleteSummary
{
    public string UserId { get; set; } = "";
    public int RemovedContacts { get; set; }
    public int RemovedConversations { get; set; }
    public int RemovedGroupMemberships { get; set; }
    public int RemovedGroups { get; set; }
    public int RemovedMoments { get; set; }
    public int RemovedFriendRequests { get; set; }
    public int RemovedFavorites { get; set; }
    public int RemovedRefreshTokens { get; set; }
    public int KickedConnections { get; set; }
    public int RemovedFiles { get; set; }
}
