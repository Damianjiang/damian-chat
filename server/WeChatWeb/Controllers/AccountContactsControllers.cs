using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WeChatWeb.Data;
using WeChatWeb.Models;
using WeChatWeb.Services;

namespace WeChatWeb.Controllers;

public static class UserPrincipal
{
    /// <summary>
    /// 当前用户 id。
    /// 【安全】没有身份时直接抛 401 —— 早期版本会静默回退成 "me"（演示管理员），
    /// 任何漏挂 [Authorize] 的接口都会以管理员身份执行，属于典型提权后门。
    /// </summary>
    public static string GetUserId(this HttpContext ctx)
    {
        var id = ctx.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(id))
            throw new UnauthorizedAccessException("未认证");
        return id;
    }

    /// <summary>取当前用户 id，未认证返回 null（用于可选鉴权的场景）。</summary>
    public static string? TryGetUserId(this HttpContext ctx) =>
        ctx.User?.FindFirstValue(ClaimTypes.NameIdentifier);

    public static User? FindUser(this HttpContext ctx, DataStore store)
    {
        var id = ctx.GetUserId();
        return store.Users.TryGetValue(id, out var u) ? u : null;
    }
}

[ApiController]
[Route("api")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly DataStore _store;
    public UsersController(DataStore store) => _store = store;

    [HttpGet("users/me")]
    public IActionResult Me()
    {
        var user = HttpContext.FindUser(_store);
        return user is null ? NotFound() : Ok(PublicProfile(user, true));
    }

    /// <summary>
    /// 公开资料（扫码名片 / 搜索用户时使用）。
    /// 手机号、大面号只有「互为好友 且 对方未关闭该权限」时才返回；
    /// 封禁状态等管理字段一律不下发（此前会把全站手机号变成可批量枚举的资源）。
    /// </summary>
    [HttpGet("users/{id}")]
    public IActionResult ById(string id)
    {
        var me = HttpContext.GetUserId();
        lock (_store)
        {
            if (!_store.Users.TryGetValue(id, out var u)) return NotFound(new { error = "用户不存在" });
            if (u.Id == me) return Ok(PublicProfile(u, true));

            var isFriend = _store.Contacts.TryGetValue(me, out var mine)
                && mine.Any(c => c.OtherUserId == id);
            return Ok(PublicProfile(u, isFriend));
        }
    }

    /// <summary>资料投影：按对方隐私设置逐字段裁剪，避免泄露手机号/大面号。</summary>
    internal static PublicUserDto PublicProfile (Models.User u, bool isFriend)
    {
        bool Allow(string key) => isFriend && (!u.Privacy.TryGetValue(key, out var v) || v);
        return new PublicUserDto
        {
            Id = u.Id,
            Nickname = u.Nickname,
            Avatar = u.Avatar,
            Phone = Allow("phone") ? u.Phone : null,
            WxId = Allow("wxId") ? u.WxId : null,
            Signature = u.Signature,
            Region = u.Region,
            Gender = u.Gender,
        };
    }

    /// <summary>对外用户资料（绝不含 Password 哈希）。</summary>
    internal sealed class PublicUserDto
    {
        public string Id { get; set; } = "";
        public string Nickname { get; set; } = "";
        public string Avatar { get; set; } = "";
        public string? Phone { get; set; }
        public string? WxId { get; set; }
        public string Signature { get; set; } = "";
        public string Region { get; set; } = "";
        public string Gender { get; set; } = "unknown";
    }

    public record UpdateProfile(string? Nickname, string? Signature, string? Region, string? Gender, string? Avatar, Dictionary<string, bool>? Privacy);

    [HttpPut("users/me")]
    public IActionResult UpdateMe([FromBody] UpdateProfile req)
    {
        var user = HttpContext.FindUser(_store);
        if (user is null) return NotFound();
        // 服务端校验：昵称/签名/地区长度、性别白名单；头像改为「上传模型」
        // （不再接受外部 URL —— 外站头像每个渲染都会向外发请求：探测在线/泄露源 IP/cookie 侧信道，
        // 且外站图无法服务端杀毒）。头像必须是 /files/upload 存的 32 位 hex fileId（走带鉴权 blob 渲染），
        // 且必须是「我能访问的」文件（自己上传过或在可访问会话里收到过），不能引用别人的/不存在的。
        if (req.Nickname is { Length: > Validate.MaxNickname })
            return BadRequest(new { error = "昵称过长" });
        if (req.Signature is { Length: > Validate.MaxSignature })
            return BadRequest(new { error = "个性签名过长" });
        if (req.Region is { Length: > Validate.MaxRegion })
            return BadRequest(new { error = "地区过长" });
        if (req.Gender is not null && req.Gender is not ("male" or "female" or "unknown"))
            return BadRequest(new { error = "性别取值不合法" });
        if (!Validate.IsAvatarUrl(req.Avatar))
            return BadRequest(new { error = "头像需通过上传设置（图片文件），不再支持外部链接" });
        if (req.Privacy is { Count: > 50 })
            return BadRequest(new { error = "隐私项过多" });

        var me = HttpContext.GetUserId();
        lock (_store)
        {
            // 头像 fileId 必须真实存在且我可访问（防越权引用他人文件 / 悬挂 id）
            if (!string.IsNullOrWhiteSpace(req.Avatar) && !Validate.IsFileId(req.Avatar))
                return BadRequest(new { error = "头像无效，请重新上传" });
            if (!string.IsNullOrWhiteSpace(req.Avatar))
            {
                if (!_store.Files.TryGetValue(req.Avatar, out var av)
                    || !_store.CanReadFile(av.Id, av.UploaderId, me))
                    return BadRequest(new { error = "头像文件不可用，请重新上传" });
            }

            if (req.Nickname != null)
                user.Nickname = Validate.Clean(req.Nickname, Validate.MaxNickname) ?? user.Nickname;
            if (req.Signature != null)
                user.Signature = Validate.Clean(req.Signature, Validate.MaxSignature) ?? "";
            if (req.Region != null)
                user.Region = Validate.Clean(req.Region, Validate.MaxRegion) ?? "";
            if (req.Gender != null) user.Gender = req.Gender;
            if (req.Avatar != null) user.Avatar = string.IsNullOrWhiteSpace(req.Avatar) ? "" : req.Avatar.Trim();
            if (req.Privacy != null)
            {
                // 键白名单：只接受应用真正使用的隐私键，挡掉任意键注入污染 store.json
                // （此前只限数量不限键名，攻击者可塞入海量/自定义键持久化到磁盘）。
                foreach (var kv in req.Privacy)
                    if (Validate.KnownPrivacyKeys.Contains(kv.Key))
                        user.Privacy[kv.Key] = kv.Value;
            }
            _store.MarkDirty();
        }

        // 不能直接返回 User（Password 现参与序列化以便落盘），必须走公开 DTO
        return Ok(PublicProfile(user, true));
    }
}

[ApiController]
[Route("api")]
[Authorize]
public class ContactsController : ControllerBase
{
    private readonly DataStore _store;
    public ContactsController(DataStore store) => _store = store;

    [HttpGet("contacts")]
    public IActionResult List([FromQuery] string? keyword)
    {
        var me = HttpContext.GetUserId();
        lock (_store)
        {
            var contacts = _store.Contacts.TryGetValue(me, out var list) ? list : new List<Contact>();
            var result = contacts.Select(c => new
            {
                c.Id,
                c.FirstLetter,
                c.Groups,
                c.Remark,
                c.Blocked,
                User = _store.Users.TryGetValue(c.OtherUserId, out var u) ? UsersController.PublicProfile(u, true) : null,
            }).Where(x => x.User != null &&
                (string.IsNullOrWhiteSpace(keyword) ||
                 x.User!.Nickname.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                 (x.Remark ?? "").Contains(keyword, StringComparison.OrdinalIgnoreCase)));
            return Ok(result.Select(x => (object)new { x.Id, x.FirstLetter, x.Groups, x.Remark, x.Blocked, User = x.User }));
        }
    }

    [HttpGet("contacts/index")]
    public IActionResult Index()
    {
        var me = HttpContext.GetUserId();
        lock (_store)
        {
            var contacts = _store.Contacts.TryGetValue(me, out var list) ? list : new List<Contact>();
            var grouped = contacts
                .Where(c => _store.Users.TryGetValue(c.OtherUserId, out _))
                .GroupBy(c => c.FirstLetter.Length > 0 ? char.ToUpperInvariant(c.FirstLetter[0]) : '#')
                .ToDictionary(g => g.Key.ToString(), g => g.Select(c => new { id = c.Id, nickname = _store.Users[c.OtherUserId].Nickname, remark = c.Remark, firstLetter = c.FirstLetter, avatar = _store.Users[c.OtherUserId].Avatar }).ToList());
            return Ok(grouped);
        }
    }

    [HttpGet("contacts/requests")]
    public IActionResult Requests()
    {
        lock (_store)
        {
            var me = HttpContext.GetUserId();
            var pending = _store.FriendRequests
                .Where(r => r.Status == "pending" && (r.ReceiverId == me || string.IsNullOrEmpty(r.ReceiverId)))
                .ToList();
            return Ok(pending.Select(r => new
            {
                r.Id,
                r.Message,
                r.CreatedAt,
                From = _store.Users.TryGetValue(r.FromUserId, out var u) ? UsersController.PublicProfile(u, true) : null,
            }));
        }
    }

    [HttpPost("contacts/requests/{id}/accept")]
    public IActionResult AcceptRequest(string id)
    {
        return HandleRequest(id, true);
    }

    [HttpPost("contacts/requests/{id}/reject")]
    public IActionResult RejectRequest(string id)
    {
        return HandleRequest(id, false);
    }

    private IActionResult HandleRequest(string id, bool accept)
    {
        lock (_store)
        {
            var me = HttpContext.GetUserId();
            var req = _store.FriendRequests.FirstOrDefault(r => r.Id == id && r.Status == "pending" && r.ReceiverId == me);
            if (req == null) return NotFound();
            req.Status = accept ? "accepted" : "rejected";
            _store.MarkDirty();   // 申请状态变化（尤其 reject 分支）也要落盘
            if (accept)
            {
                // 双向好友 + 共享单聊会话
                _store.AddMutualContact(me, req.FromUserId);
            }
            return Ok(req);
        }
    }

    public record AddContact(string? Phone, string? Message, string? UserId, string? WxId = null);

    /// <summary>按手机号 / 大面号（WxId）或用户 Id 发起好友申请；演示机器人自动通过。</summary>
    [HttpPost("contacts")]
    public IActionResult Add([FromBody] AddContact req)
    {
        var me = HttpContext.GetUserId();
        lock (_store)
        {
            var phone = (req.Phone ?? "").Trim();
            var wxId = (req.WxId ?? "").Trim();
            var target = !string.IsNullOrEmpty(req.UserId)
                ? _store.Users.Values.FirstOrDefault(u => u.Id == req.UserId)
                // 前端把同一输入同时作为 phone 与 wxId 传过来，命中任一即可（手机号或大面号）
                : _store.Users.Values.FirstOrDefault(u =>
                    (phone.Length > 0 && u.Phone == phone)
                    || (wxId.Length > 0 && !string.IsNullOrEmpty(u.WxId) && u.WxId == wxId));
            if (target == null) return BadRequest(new { error = "该手机号或大面号不存在" });
            if (target.Id == me) return BadRequest(new { error = "不能添加自己为好友" });
            if (_store.Contacts.TryGetValue(me, out var mine) && mine.Any(c => c.OtherUserId == target.Id))
                return BadRequest(new { error = "对方已经是你的好友" });

            // 申请留言长度限制 + 打散 HTML 标记（显示在对方申请列表里，属跨用户文本）。
            // 走 Clean：超限直接拒绝（比截断更明确），并破坏任何 <tag> 结构
            var message = Validate.Clean(req.Message, 100) ?? "";

            var fr = new FriendRequest { FromUserId = me, ReceiverId = target.Id, Message = message };
            if (target.IsBot)
            {
                fr.Status = "accepted";
                _store.FriendRequests.Add(fr);
                _store.AddMutualContact(me, target.Id);
                return Ok(new { status = "accepted", request = fr });
            }
            _store.FriendRequests.Add(fr);
            _store.MarkDirty();
            return Ok(new { status = "pending", request = fr });
        }
    }

    public record SetRemark(string Remark);
    public record SetBlock(bool Blocked);

    [HttpPut("contacts/{id}/remark")]
    public IActionResult Remark(string id, [FromBody] SetRemark req)
    {
        lock (_store)
        {
            var c = _store.Contacts.TryGetValue(HttpContext.GetUserId(), out var l) ? l.FirstOrDefault(x => x.Id == id) : null;
            if (c == null) return NotFound();
            // 备注长度上限：与昵称一致，防止塞入超长字符串
            c.Remark = req.Remark is { Length: > Validate.MaxNickname } ? null : req.Remark;
            _store.MarkDirty();
            return Ok(c);
        }
    }

    [HttpPut("contacts/{id}/block")]
    public IActionResult Block(string id, [FromBody] SetBlock req)
    {
        lock (_store)
        {
            var c = _store.Contacts.TryGetValue(HttpContext.GetUserId(), out var l) ? l.FirstOrDefault(x => x.Id == id) : null;
            if (c == null) return NotFound();
            c.Blocked = req.Blocked;
            _store.MarkDirty();
            return Ok(c);
        }
    }

    /// <summary>已拉黑的用户列表（设置页「被拉黑的用户」入口）。每项含对方资料，可恢复/可删除好友。</summary>
    [HttpGet("contacts/blocked")]
    public IActionResult Blocked()
    {
        var me = HttpContext.GetUserId();
        lock (_store)
        {
            var ids = _store.BlockedUserIds(me);
            var result = ids
                .Where(_store.Users.ContainsKey)
                .Select(otherId => new
                {
                    id = otherId,
                    Blocked = true,
                    IsFriend = _store.IsContact(me, otherId),
                    User = _store.Users.TryGetValue(otherId, out var u) ? UsersController.PublicProfile(u, _store.IsContact(me, otherId)) : null,
                })
                .Where(x => x.User != null)
                .ToList();
            return Ok(result.Select(x => (object)new { x.id, x.Blocked, x.IsFriend, x.User }));
        }
    }

    public record SetBlockedUser(string UserId, bool Blocked);

    /// <summary>解除拉黑（"恢复还原"）：把该联系人的 Blocked 置 false。</summary>
    [HttpPut("contacts/block")]
    public IActionResult Unblock([FromBody] SetBlockedUser req)
    {
        lock (_store)
        {
            var me = HttpContext.GetUserId();
            var c = _store.Contacts.TryGetValue(me, out var l) ? l.FirstOrDefault(x => x.OtherUserId == req.UserId) : null;
            if (c == null) return NotFound();
            c.Blocked = req.Blocked;
            _store.MarkDirty();
            return Ok(new { ok = true });
        }
    }

    /// <summary>删除好友关系（双向删除联系人）。</summary>
    [HttpDelete("contacts/{id}")]
    public IActionResult RemoveFriend(string id)
    {
        lock (_store)
        {
            var me = HttpContext.GetUserId();
            // id 是联系人记录 id（与 /contacts/{id}/block 一致）；兼容直接传对方用户 id
            var c = _store.Contacts.TryGetValue(me, out var l) ? l.FirstOrDefault(x => x.Id == id) : null;
            var other = c?.OtherUserId;
            if (other == null)
            {
                // 直接给了对方用户 id（设置页拉黑列表用对方 id）：找记录
                var rec = _store.Contacts.TryGetValue(me, out var l2) ? l2.FirstOrDefault(x => x.OtherUserId == id) : null;
                if (rec == null) return NotFound();
                other = rec.OtherUserId;
            }
            _store.RemoveFriend(me, other);
            _store.MarkDirty();
            return Ok(new { ok = true });
        }
    }
}

[ApiController]
[Route("api")]
[Authorize]
public class ConversationsController : ControllerBase
{
    private readonly DataStore _store;
    private readonly Microsoft.AspNetCore.SignalR.IHubContext<WeChatWeb.Hubs.ChatHub> _hub;

    public ConversationsController(DataStore store, Microsoft.AspNetCore.SignalR.IHubContext<WeChatWeb.Hubs.ChatHub> hub)
    {
        _store = store;
        _hub = hub;
    }

    [HttpGet("conversations")]
    public IActionResult List()
    {
        var me = HttpContext.GetUserId();
        lock (_store)
        {
            var convs = _store.Conversations.Values
                .Where(c => c.Kind == "private" ? c.Members.Contains(me) : _store.Groups.TryGetValue(c.MemberId, out var g) && g.Members.Any(m => m.UserId == me))
                .Select(c =>
                {
                    // 私聊：标题/头像取"对方"（共享会话里另一成员）
                    var otherId = c.Kind == "private" ? (c.Members.FirstOrDefault(x => x != me) ?? c.MemberId) : c.MemberId;
                    var isGroup = c.Kind == "group";
                    return new
                    {
                        c.Id,
                        kind = c.Kind,
                        c.Pinned,
                        c.Muted,
                        c.Folded,
                        Unread = _store.UnreadFor(c.Id, me),
                        c.LastAt,
                        MemberId = otherId,
                        Title = !isGroup ? _store.Users.TryGetValue(otherId, out var u) ? u.Nickname : "" : _store.Groups.TryGetValue(otherId, out var gg) ? gg.Name : "",
                        Avatar = !isGroup ? _store.Users.TryGetValue(otherId, out var uu) ? uu.Avatar : "" : _store.Groups.TryGetValue(otherId, out var guu) ? guu.Avatar : "",
                        // 索引列表按时间升序，最后一条即最新 —— 免去每会话一次全量排序
                        LastMessage = _store.MessagesOf(c.Id).LastOrDefault(),
                    };
                })
                // ★ 置顶必须为第一排序键：此前按 LastAt 为主、Pinned 只当并列次键，
                //   导致「置顶的群/会话最后一条消息较旧时仍被挤下去」——置顶形同虚设。
                //   改为：先置顶、置顶组内再按最后活跃时间倒序；未置顶整体在后。
                .OrderByDescending(x => x.Pinned)
                .ThenByDescending(x => x.LastAt ?? DateTime.MinValue)
                .ToList();
            return Ok(convs);
        }
    }

    public record EnsureConversation(string MemberId);

    /// <summary>取或建会话：好友单聊（ContactProfile 点"发消息"）或群聊（群设置点"发消息"）。</summary>
    [HttpPost("conversations")]
    public IActionResult Ensure([FromBody] EnsureConversation req)
    {
        var me = HttpContext.GetUserId();
        lock (_store)
        {
            // 群聊：会话 id 即群 id，缺则补建
            if (_store.Groups.TryGetValue(req.MemberId ?? "", out var g))
            {
                if (!g.Members.Any(m => m.UserId == me))
                    return StatusCode(StatusCodes.Status403Forbidden, new { error = "你不在该群聊中" });
                if (!_store.Conversations.TryGetValue(g.Id, out var gconv))
                {
                    gconv = new Conversation { Id = g.Id, Kind = "group", MemberId = g.Id };
                    _store.Conversations[g.Id] = gconv;
                }
                return Ok(new { gconv.Id, kind = "group", MemberId = g.Id, title = g.Name });
            }

            if (!_store.Users.ContainsKey(req.MemberId ?? "")) return NotFound(new { error = "用户不存在" });
            if (!_store.Contacts.TryGetValue(me, out var mine) || !mine.Any(c => c.OtherUserId == req.MemberId))
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "请先添加该好友" });
            var conv = _store.GetOrCreatePairConversation(me, req.MemberId!);
            return Ok(new { conv.Id, kind = conv.Kind, MemberId = req.MemberId, title = _store.Users[req.MemberId!].Nickname });
        }
    }

    public record SetFlags(bool? Pinned, bool? Muted, bool? Folded, bool? Read = null);

    /// <summary>清空该会话的聊天记录（服务端删除 + 客户端同步清空）。</summary>
    [HttpDelete("conversations/{id}/messages")]
    public async Task<IActionResult> ClearMessages(string id)
    {
        var me = HttpContext.GetUserId();
        bool cleared;
        lock (_store)
        {
            if (!_store.CanAccessConversation(id, me)) return NotFound();
            if (!_store.Conversations.TryGetValue(id, out var conv)) return NotFound();

            // 安全收紧：群聊是共享历史，普通成员清空会抹掉全群记录（破坏他人数据 + 可反复执行 = DoS）。
            // 只有群主/管理员可清空群聊；私聊成员可清自己的会话视图。
            if (conv.Kind == "group")
            {
                if (!_store.Groups.TryGetValue(conv.MemberId, out var g))
                    return NotFound();
                var role = g.Members.FirstOrDefault(x => x.UserId == me)?.Role;
                if (role != "owner" && role != "admin")
                    return Forbid();
            }
            _store.ClearConversationMessages(id);
            cleared = true;
        }
        if (!cleared) return NotFound();
        // 广播清空：其它端同步把该会话消息清掉（多端 + 本地缓存一致）
        try
        {
            var connections = WeChatWeb.Hubs.ChatHub.MemberConnections(_store, id);
            if (connections.Count > 0)
                await _hub.Clients.Clients(connections).SendAsync("MessagesCleared", id);
        }
        catch { /* 广播失败不影响清空本身 */ }
        return Ok(new { ok = true });
    }

    /// <summary>已读状态只广播给自己的其它端（多端红点同步；对方无已读回执）。</summary>
    private async Task BroadcastReadAsync (string conversationId, string userId)
    {
        try
        {
            await WeChatWeb.Hubs.ChatHub.BroadcastRead(_hub, _store, conversationId, userId);
        }
        catch { /* 广播失败不影响已读 */ }
    }

    [HttpPut("conversations/{id}")]
    public IActionResult Update(string id, [FromBody] SetFlags req)
    {
        var me = HttpContext.GetUserId();
        lock (_store)
        {
            if (!_store.CanAccessConversation(id, me)) return NotFound();
            var conv = _store.Conversations[id];
            // 共享 flag（置顶/免打扰/折叠）越权写防护：群会话的 flag 存于「全体共享」的
            // Conversation 对象，任意普通成员改一次就会连累全群（横向越权写他人视图）。
            // 群会话仅 owner/admin 可改共享 flag；已读走 per-user 的 MarkRead，不受此限。
            // 私聊维持原共享语义（仅两端）。完整按用户隔离需把 flag 改成像 ReadMarks
            // 那样按 userId 存（数据模型改动，后续单独做）。
            bool flagWrite = req.Pinned.HasValue || req.Muted.HasValue || req.Folded.HasValue;
            if (flagWrite && conv.Kind == "group")
            {
                var role = _store.Groups.TryGetValue(conv.MemberId, out var gg)
                    ? gg.Members.FirstOrDefault(x => x.UserId == me)?.Role
                    : null;
                if (role is not ("owner" or "admin"))
                    return Forbid();
            }
            if (req.Pinned.HasValue) conv.Pinned = req.Pinned.Value;
            if (req.Muted.HasValue) conv.Muted = req.Muted.Value;
            if (req.Folded.HasValue) conv.Folded = req.Folded.Value;
            // 已读：记录该用户的已读时间并广播给会话内其他人（多端红点同步）
            if (req.Read == true)
            {
                _store.MarkRead(id, me);
                _ = BroadcastReadAsync(id, me);
            }
            _store.MarkDirty();
            return Ok(new { id, Pinned = conv.Pinned, Muted = conv.Muted, Folded = conv.Folded, Unread = _store.UnreadFor(id, me) });
        }
    }
}

[ApiController]
[Route("api/account")]
[Authorize]
public class AccountController : ControllerBase
{
    private readonly DataStore _store;
    private readonly LogService _log;

    public AccountController(DataStore store, LogService log)
    {
        _store = store;
        _log = log;
    }

    public record ChangePasswordRequest(string OldPassword, string NewPassword);

    /// <summary>修改密码：校验旧密码，重新哈希存储，并吊销该账号全部 refresh token（其它设备需重新登录）。</summary>
    [HttpPost("password")]
    public IActionResult ChangePassword([FromBody] ChangePasswordRequest req)
    {
        var me = HttpContext.GetUserId();
        if (PasswordPolicy.Validate(req.NewPassword) is { } pwErr)
            return BadRequest(new { error = "新密码长度需 8-32 位" });

        List<Data.ConnectionState>? myConnections = null;
        lock (_store)
        {
            if (!_store.Users.TryGetValue(me, out var user)) return NotFound();
            if (!PasswordHasher.Verify(req.OldPassword ?? "", user.Password))
                return BadRequest(new { error = "原密码不正确" });
            user.Password = PasswordHasher.Hash(req.NewPassword);
            // 改密即时失效：记改密时刻 → UserInvalidationMiddleware 拒绝「改密前签发」的 access token。
            // 截断到整秒：令牌里的 pwd 时间戳是秒级精度，亚秒部分会让「同一秒签发的新令牌」被误杀。
            var nowUtc = DateTime.UtcNow;
            user.PasswordChangedAt = new DateTime(nowUtc.Ticks - nowUtc.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
            foreach (var kv in _store.RefreshTokens.Where(kv => kv.Value.UserId == me).ToList())
                _store.RefreshTokens.Remove(kv.Key);
            // 本机其它实时连接一并踢线（前端会话校验失败自动回登录页）
            myConnections = _store.ConnectionsOf(me)
                .Select(id => _store.Connections[id])
                .Where(c => c.CallerContext != null)
                .ToList();
            _store.MarkDirty();
        }
        foreach (var c in myConnections)
        {
            try { c.CallerContext?.Abort(); } catch { /* 连接可能已断开 */ }
        }
        // 立即落盘放到锁外（SaveNow 全库序列化不进全局锁；仍保证重启不回滚旧密码）
        _store.SaveNow();

        _log.Write("auth", new { ts = DateTime.UtcNow, result = "password-changed", user = me });
        return Ok(new { ok = true });
    }
}
