using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WeChatWeb.Data;
using WeChatWeb.Hubs;
using WeChatWeb.Models;
using WeChatWeb.Services;
using Microsoft.AspNetCore.SignalR;

namespace WeChatWeb.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class MessagesController : ControllerBase
{
    private readonly DataStore _store;
    private readonly IHubContext<ChatHub> _hub;
    private readonly SensitiveWordService _sw;
    private readonly SecuritySettings _sec;
    private readonly LogService _log;
    // ★ 用户身份（JWT sub）维度消息洪泛上限。必须注入**单例** MessageFloodLimiter：
    // ASP.NET Core 默认控制器是 transient（每请求新建实例），若把计数做成控制器实例字段，
    // 每次请求都是全新空字典、计数积累不起来、检查形同虚设。放单例里跨请求共享才有效。
    private readonly MessageFloodLimiter _floodLimiter;

    public MessagesController(DataStore store, IHubContext<ChatHub> hub, SensitiveWordService sw, SecuritySettings sec, LogService log, MessageFloodLimiter floodLimiter)
    {
        _store = store;
        _hub = hub;
        _sw = sw;
        _sec = sec;
        _log = log;
        _floodLimiter = floodLimiter;
    }

    public record SendMessage(string ConversationId, string Type = "text", string Content = "", string? ImageId = null, int? VoiceSeconds = null, string? FileName = null, long? FileSize = null, string? StickerEmoji = null, string? QuoteJson = null, string? PatTarget = null);

    [HttpGet("messages/{conversationId}")]
    public IActionResult History(string conversationId, [FromQuery] string? beforeId, int count = 30)
    {
        var me = HttpContext.GetUserId();
        lock (_store)
        {
            if (!_store.CanAccessConversation(conversationId, me)) return NotFound();
            var take = Math.Clamp(count, 1, 100); // 防止一次拉 10 万条把内存打爆
            // 会话索引列表本就时间升序，直接切尾段即可 —— 不再做 O(n log n) 全列表排序
            //（用户从头翻到尾 = 累计 O(n²)，每页下拉都重排整段历史，纯浪费）。
            // 注意：升序列表切出的尾段天然是「老→新」升序，绝不能再 Reverse（会变降序打乱客户端）。
            var list = _store.MessagesOf(conversationId);
            if (string.IsNullOrEmpty(beforeId))
            {
                var tail = Math.Min(take, list.Count);
                return Ok(list.GetRange(list.Count - tail, tail));
            }
            // 游标定位：Id 索引 O(1) 取到参照消息，再在升序列表上二分找插入点，
            // 切出其左邻 take 条（严格更早，仍为升序），零排序零全表扫描。
            var before = _store.GetMessageById(beforeId);
            if (before == null) return Ok(new List<Message>(0));
            var idx = list.BinarySearch(before, MessageTimeComparer.Instance);
            if (idx < 0) return Ok(new List<Message>(0));
            var start = Math.Max(0, idx - take);
            return Ok(list.GetRange(start, idx - start));
        }
    }

    /// <summary>消息按时间比较器：DataStore 的会话索引列表是严格升序（同一毫秒按入列顺序），二分定位用。</summary>
    private sealed class MessageTimeComparer : IComparer<Message>
    {
        public static readonly MessageTimeComparer Instance = new();
        public int Compare(Message? a, Message? b) => a!.CreatedAt.CompareTo(b!.CreatedAt);
    }

    [HttpPost("messages")]
    public async Task<IActionResult> Send([FromBody] SendMessage req)
    {
        var me = HttpContext.GetUserId();

        // ---- 用户身份（JWT sub）维度消息洪泛上限（防"偷 token / 绕过人机验证 / 轮换 IP 重放"）----
        // IP 防火墙管"多 IP 刷"，这里管"同一身份短时间发太多"（单例 MessageFloodLimiter 跨请求共享计数）。
        // 命中 → 429 + 服务端记 flood 日志（哪个 user 在滥用，管理端可见）。
        if (_floodLimiter.IsOverLimit(me, _sec.FloodMessagesPerMinute, out var floodRetry))
        {
            _log.Write("auth", new
            {
                ts = DateTime.UtcNow,
                result = "flood-denied",
                user = me,
                ip = HttpContext.Connection.RemoteIpAddress?.ToString(),
                path = req.ConversationId,
                limit = Math.Max(1, _sec.FloodMessagesPerMinute),
            });
            HttpContext.Response.Headers["Retry-After"] = floodRetry.ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { error = "消息过于频繁，请稍后再试" });
        }

        // ---- 服务端硬校验：类型白名单 + 长度上限 + 引用 JSON 限制 + 文件 id 合法性 ----
        var type = (req.Type ?? "text").Trim();
        if (!Validate.MessageTypes.Contains(type))
            return BadRequest(new { error = $"不支持的消息类型 {type}" });
        // BIZ-SEC-003：system 消息（"你创建了群"、"已邀请 xx 入群"等）只能由服务端
        // SystemMessages.Post 生成（Sender=system）。客户端不得自报 type=system
        // 伪造系统提示来欺骗会话成员（尤其群主/管理员视角）。
        if (type == "system")
            return BadRequest(new { error = "不支持的消息类型" });

        if (req.Content is { Length: > Validate.MaxMessageText })
            return BadRequest(new { error = $"消息过长（上限 {Validate.MaxMessageText} 字）" });
        if (req.QuoteJson is { Length: > Validate.MaxSignatureField })
            return BadRequest(new { error = "引用内容过长" });
        if (req.StickerEmoji is { Length: > Validate.MaxSticker })
            return BadRequest(new { error = "表情内容不合法" });

        // 带附件的消息必须引用服务端已存的 fileId（不接受用户传路径）
        if (type is "image" or "voice" or "file")
        {
            if (!Validate.IsFileId(req.ImageId))
                return BadRequest(new { error = "附件无效，请先上传" });
        }
        if (type == "voice" && req.VoiceSeconds is < 0 or > Validate.MaxVoiceSeconds)
            return BadRequest(new { error = "语音时长不合法" });

        // 文件名净化：只留基名，挡掉 ../ 和危险扩展名
        string? fileName = null;
        if (type == "file")
        {
            fileName = Validate.CleanFileName(req.FileName);
            if (fileName == null) return BadRequest(new { error = "文件名不合法" });
        }

        // 文本消息正文：先打散任何 HTML/标签结构（存储型 XSS 纵深防御），再做敏感词过滤
        // 非文本消息也统一剥离，保证持久化数据与转发/日志里永远不含可注入标记
        var content = Validate.StripHtml(req.Content ?? "");
        content = type == "text" ? (_sw.Filter(content) ?? "") : content;

        // 引用 JSON 解析（重操作：JsonDocument.Parse + 字段读取）放到全局锁外先做，
        // 锁内只保留轻量的 keep 判定 + ToJson —— 减少每条消息在全局锁里的持有时间（聊天延迟关键点）。
        var parsedQuote = string.IsNullOrWhiteSpace(req.QuoteJson) ? null : Validate.ParseQuote(req.QuoteJson);

        Message msg;
        lock (_store)
        {
            if (!_store.CanAccessConversation(req.ConversationId, me)) return NotFound();
            if (!_store.Conversations.TryGetValue(req.ConversationId, out var conv)) return NotFound();

            // 附件归属校验：引用的 fileId 必须真实存在，且是「自己上传的」或
            // 「已被自己可访问的会话引用过」（转发判定）。此前只验格式不验归属，
            // 拿到他人私聊文件的 id 发进自己的会话即可越权下载。
            if (type is "image" or "voice" or "file")
            {
                if (!_store.Files.TryGetValue(req.ImageId!, out var fmeta))
                    return BadRequest(new { error = "附件不存在，请重新上传" });
                if (!_store.CanReadFile(req.ImageId!, fmeta.UploaderId, me))
                    return StatusCode(StatusCodes.Status403Forbidden,
                        new { error = "无权使用该附件" });
            }

            // 拉黑拒收：私聊中若对方把我拉黑，消息一律拒收（微信式「对方拒收了」）。
            // 此前 Blocked 只存不用 —— 被拉黑的人照样能继续发消息骚扰。
            if (conv.Kind == "private")
            {
                var other = conv.Members.FirstOrDefault(x => x != me);
                if (other != null && _store.IsBlocked(other, me))
                    return StatusCode(StatusCodes.Status403Forbidden,
                        new { error = "消息已发出，但被对方拒收了。" });
            }

            // 拍一拍（pat）：仅群聊可用；被拍者必须是该群成员且不能拍自己。
            // 放在锁内读会话/群，与其它路径同一把锁，避免字典被并发写。
            string? patTarget = null;
            if (type == "pat")
            {
                if (conv.Kind != "group")
                    return BadRequest(new { error = "拍一拍仅在群聊中可用" });
                if (string.IsNullOrWhiteSpace(req.PatTarget) || req.PatTarget == me)
                    return BadRequest(new { error = "拍一拍对象不合法" });
                if (!_store.Groups.TryGetValue(conv.MemberId, out var patGroup)
                    || !patGroup.Members.Any(m => m.UserId == req.PatTarget))
                    return BadRequest(new { error = "只能拍群成员" });
                patTarget = req.PatTarget;
            }

            // 投票卡片（poll）：仅群聊；content 必须是本群真实存在的投票 id
            //（防伪造 pollId 把别人的投票卡片刷进无关会话）。pollId 为服务端生成的 hex，原样入库。
            if (type == "poll")
            {
                if (conv.Kind != "group")
                    return BadRequest(new { error = "投票仅在群聊中可用" });
                if (string.IsNullOrWhiteSpace(req.Content) || req.Content.Length > 40
                    || !_store.Groups.TryGetValue(conv.MemberId, out var pollGroup)
                    || !pollGroup.Polls.Any(p => p.Id == req.Content))
                    return BadRequest(new { error = "投票不存在" });
                content = req.Content!;
            }

            // 引用（quoteJson）：归一化 + 业务强制（服务端纵深，防直接 POST 绕过前端限制）。
            // ① 不可引用自己发的消息；② 群聊里只能引用「文字」，被引用的是文件/图片/
            //    语音/表情/拍一拍（媒体类）→ 一律拒绝该引用。命中即「降级为不引用」
            //    （正文照发，仅丢弃引用块），与 ParseQuote 对非法 JSON 的降级行为一致。
            // ★ ParseQuote 已在锁外算好（parsedQuote）：锁内只做轻量 keep 判定 + ToJson。
            string? quoteJson = null;
            if (parsedQuote is { } q)
            {
                // ① 不可引用自己发的消息；② 群聊里只能引用「文字」，被引用的是
                //    文件/图片/语音/表情/拍一拍（媒体类）→ 一律拒绝该引用。
                bool keep = true;
                if (q.SenderId.Length > 0 && q.SenderId == me) keep = false;
                if (keep && conv.Kind == "group"
                    && q.OrigType.Length > 0 && q.OrigType != "text") keep = false;
                if (keep) quoteJson = q.ToJson(); // 只落 senderName/text 白名单字段
            }

            msg = new Message
            {
                Id = Guid.NewGuid().ToString("N"),
                ConversationId = req.ConversationId,
                SenderId = me,
                Type = type,
                Content = content,
                ImageId = req.ImageId,
                VoiceSeconds = type == "voice" ? req.VoiceSeconds : null,
                FileName = fileName,
                FileSize = req.FileSize is >= 0 ? req.FileSize : null,
                StickerEmoji = type == "sticker" ? req.StickerEmoji : null,
                Pat = type == "pat",
                PatTarget = patTarget,
                QuoteJson = quoteJson,
                CreatedAt = DateTime.UtcNow,
            };
            _store.Messages.Add(msg);
            _store.MsgIndexAdd(msg);   // 会话消息索引同步（性能）
            _store.OnNewMessage(msg);  // 未读计数：非发送者成员 +1（列表 O(1) 读取）
            conv.LastAt = msg.CreatedAt;
            _store.MarkDirty();
        }

        await ChatHub.BroadcastMessage(_hub, _store, msg);
        return Ok(msg);
    }

    /// <summary>删除消息：只能删自己的，且必须仍能访问该会话（退群后不能删）。</summary>
    [HttpDelete("messages/{id}")]
    public async Task<IActionResult> DeleteMessage(string id)
    {
        var me = HttpContext.GetUserId();
        string? convId = null;
        lock (_store)
        {
            var msg = _store.GetMessageById(id);   // Id 索引 O(1) 定位（此前全表线性扫）
            if (msg == null) return NotFound();
            if (msg.SenderId != me) return Forbid();
            if (!_store.CanAccessConversation(msg.ConversationId, me)) return NotFound();
            convId = msg.ConversationId;
            _store.OnMessageUncounted(msg);        // 未读计数 -1（消息不再计入）
            _store.Messages.Remove(msg);
            _store.MsgIndexRemove(msg);
            _store.MarkDirty();
        }

        // 广播给该会话全体成员（含自己其它端）：被删消息在其它端也要移除，
        // 否则多端会各自残留 / 重新拉历史时把删除的消息又刷回来。
        if (convId != null)
        {
            try
            {
                var connections = WeChatWeb.Hubs.ChatHub.MemberConnections(_store, convId);
                if (connections.Count > 0)
                    await _hub.Clients.Clients(connections).SendAsync("MessageDeleted", convId, id);
            }
            catch { /* 广播失败不影响删除本身（本端已删） */ }
        }
        return Ok();
    }

    [HttpPost("messages/{id}/recall")]
    public async Task<IActionResult> Recall(string id)
    {
        var me = HttpContext.GetUserId();
        Message? msg = null;
        lock (_store)
        {
            msg = _store.GetMessageById(id);   // Id 索引 O(1) 定位（此前全表线性扫）
            if (msg == null) return NotFound();
            if (msg.SenderId != me) return Forbid();
            // 已退群的用户也不能再撤回历史消息
            if (!_store.CanAccessConversation(msg.ConversationId, me)) return NotFound();
            if (DateTime.UtcNow - msg.CreatedAt > TimeSpan.FromMinutes(2)) return Conflict(new { error = "超过 2 分钟无法撤回" });
            if (msg.Recalled) return Ok(msg);
            _store.OnMessageUncounted(msg);    // 撤回后消息不再计入未读
            msg.Recalled = true;
            msg.Content = "";
            _store.MarkDirty();   // 撤回/清空必须落盘：否则服务一重启，撤回的消息带着原文复活（此前漏了）
        }
        await ChatHub.BroadcastMessage(_hub, _store, msg!);
        return Ok(msg);
    }
}

[ApiController]
[Route("api")]
[Authorize]
public class GroupsController : ControllerBase
{
    private readonly DataStore _store;
    private readonly IHubContext<ChatHub> _hub;
    public GroupsController(DataStore store, IHubContext<ChatHub> hub)
    {
        _store = store;
        _hub = hub;
    }

    private string NicknameOf (string userId)
    {
        lock (_store)
        {
            return _store.Users.TryGetValue(userId, out var u) ? u.Nickname : "未知用户";
        }
    }

    [HttpGet("groups")]
    public IActionResult List()
    {
        var me = HttpContext.GetUserId();
        lock (_store)
        {
            return Ok(_store.Groups.Values.Where(g => g.Members.Any(m => m.UserId == me)));
        }
    }

    public record CreateGroup(string Name, List<string> MemberIds, string? Notice);

    [HttpPost("groups")]
    public async Task<IActionResult> Create([FromBody] CreateGroup req)
    {
        var me = HttpContext.GetUserId();

        // 服务端校验：群名长度、成员数量上限、公告长度
        var name = Validate.Clean(req.Name, Validate.MaxGroupName);
        var notice = Validate.Clean(req.Notice, Validate.MaxGroupNotice) ?? "";
        if (req.Name is { Length: > Validate.MaxGroupName })
            return BadRequest(new { error = "群名称过长" });
        if (req.Notice is { Length: > Validate.MaxGroupNotice })
            return BadRequest(new { error = "群公告过长" });
        var limits = _store.GroupLimits;
        if (req.MemberIds is not null && req.MemberIds.Count > limits.MaxSelectOnCreate)
            return BadRequest(new { error = $"一次最多选择 {limits.MaxSelectOnCreate} 位好友" });

        Group created;
        lock (_store)
        {
            created = new Group
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = string.IsNullOrWhiteSpace(name) ? "群聊" : name,
                Notice = notice,
                OwnerId = me,
                Members = new() { new GroupMember { UserId = me, Role = "owner" } },
            };
            var maxMembers = _store.GroupLimits.MaxMembers;
            // 防骚扰：只能把自己的好友拉进群 —— 任意用户 id 强拉陌生人建群的路被堵死
            foreach (var id in (req.MemberIds ?? new List<string>()).Distinct().Take(maxMembers)
                         .Where(id => id != me && _store.IsContact(me, id) && _store.Users.ContainsKey(id)))
                created.Members.Add(new GroupMember { UserId = id });
            if (created.Members.Count > maxMembers)
                created.Members.RemoveRange(maxMembers - 1, created.Members.Count - maxMembers);
            _store.Groups[created.Id] = created;
            _store.Conversations[created.Id] = new Conversation { Id = created.Id, Kind = "group", MemberId = created.Id };
            _store.MarkDirty();
        }

        await SystemMessages.Post(_hub, _store, created.Id, $"{NicknameOf(me)} 创建了「{created.Name}」");
        return Ok(created);
    }
    [HttpGet("groups/{id}")]
    public IActionResult Detail(string id)
    {
        var me = HttpContext.GetUserId();
        lock (_store)
        {
            if (!_store.Groups.TryGetValue(id, out var g)) return NotFound();
            // 非成员不得读取群详情与成员名单（用 NotFound 避免群 id 可枚举）
            if (!g.Members.Any(m => m.UserId == me)) return NotFound();
            var members = g.Members.Select(m => new
            {
                m.UserId,
                m.Role,
                m.Muted,
                m.Title,
                // 群内显示名：本群昵称优先，否则大面昵称
                Nickname = !string.IsNullOrEmpty(m.Nickname)
                    ? m.Nickname
                    : _store.Users.TryGetValue(m.UserId, out var u) ? u.Nickname : "未知用户",
                GroupNick = m.Nickname,
                Avatar = _store.Users.TryGetValue(m.UserId, out var uu) ? uu.Avatar : "",
            }).ToList();
            // 群公告确认状态：当前用户是否已确认 + 确认人数 / 总人数
            // ★ 按「确认版本」判定：只有在该公告最近一次创建/修改之后确认的才算"已读"，
            //   公告被改动/新增 → 旧确认自动失效 → 各成员重新看到并需再确认。
            DateTime? readAt = g.NoticeReadAt == null ? null : (g.NoticeReadAt.TryGetValue(me, out var t0) ? t0 : null);
            var noticeVersion = g.NoticeUpdatedAt;
            var readByMe = readAt.HasValue && readAt.Value >= noticeVersion;
            var confirmed = 0;
            if (g.NoticeReadAt != null)
                confirmed = g.NoticeReadAt.Values.Count(t => t >= noticeVersion);
            var total = g.Members.Count;
            return Ok(new
            {
                g.Id, g.Name, g.Avatar, g.Notice, g.OwnerId,
                Members = members,
                NoticeVersion = noticeVersion,
                NoticeReadByMe = readByMe,
                NoticeReadAt = readAt ?? DateTime.MinValue,
                NoticeReadCount = confirmed,
                NoticeTotal = total,
                // 加群策略 + 待审批数（前端群设置页据此显示「开放/需审批」与待审批入口）
                JoinPolicy = string.IsNullOrEmpty(g.JoinPolicy) ? "approve" : g.JoinPolicy,
                JoinPending = g.JoinRequests?.Count ?? 0,
                IAmMember = true,
            });
        }
    }

    /* ---------------- 加群（QQ 式：输群号进群；安全边界见各端点注释） ---------------- */

    /// <summary>
    /// 按群号查群（前端「通讯录 → 加群」输群号入口）。
    /// ★ 安全边界（防越权）：非成员也允许查到「群是否存在 + 群名 + 人数 + 加群策略」这种**公开聚合信息**
    /// （群号本身是 32 位随机 GUID、不可枚举，等于群名片信息），但**绝不返回**成员名单、群公告、消息内容——
    /// 那些只在 [HttpGet groups/{id}]（成员校验）里给。所以输群号只能查到"有这个群、有多少人、要不要审批"，
    /// 不能看别人讲话。
    /// </summary>
    [HttpGet("groups/lookup")]
    public IActionResult Lookup([FromQuery] string no)
    {
        var me = HttpContext.GetUserId();
        var trimmed = no?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return BadRequest(new { error = "群号不能为空" });
        lock (_store)
        {
            if (!_store.Groups.TryGetValue(trimmed, out var g))
                return NotFound(new { error = "群不存在" });
            bool iAmMember = g.Members.Any(m => m.UserId == me);
            // 公开聚合信息（不泄露成员名单/公告/消息）
            return Ok(new
            {
                g.Id,
                g.Name,
                g.Avatar,
                MemberCount = g.Members.Count,
                JoinPolicy = string.IsNullOrEmpty(g.JoinPolicy) ? "approve" : g.JoinPolicy,
                IAmMember = iAmMember,
            });
        }
    }

    /// <summary>申请加群。open 群直接入；approve 群进待审批列表等群主/管理员通过。已成员 → Ok 无副作用。</summary>
    [HttpPost("groups/{id}/join")]
    public async Task<IActionResult> Join(string id, [FromBody] JoinGroup req)
    {
        var me = HttpContext.GetUserId();
        bool ok;
        string note;
        lock (_store)
        {
            if (!_store.Groups.TryGetValue(id, out var g)) { ok = false; note = "群不存在"; return NotFound(new { error = note }); }
            g.JoinRequests ??= new();
            // 已是成员：幂等返回
            if (g.Members.Any(m => m.UserId == me)) { ok = true; note = "你已是群成员"; return Ok(new { ok, message = note }); }
            // 已在待审批：去重
            if (g.JoinRequests.Any(r => r.UserId == me)) { ok = true; note = "申请已提交，等待群主审批"; return Ok(new { ok, message = note }); }
            var policy = string.IsNullOrEmpty(g.JoinPolicy) ? "approve" : g.JoinPolicy;
            if (policy == "open")
            {
                // 开放群：直接加入（仍受群人数上限约束）
                if (g.Members.Count >= _store.GroupLimits.MaxMembers) { ok = false; note = "群人数已达上限"; return BadRequest(new { error = note }); }
                g.Members.Add(new GroupMember { UserId = me });
                g.JoinRequests.RemoveAll(r => r.UserId == me);
                _store.MarkDirty();
                ok = true; note = "已加入群聊";
            }
            else
            {
                // 审批群：记一条申请（存昵称快照，避免申请人改名/删号后审批列表悬空）
                string? nick = null;
                if (_store.Users.TryGetValue(me, out var u)) nick = u.Nickname;
                g.JoinRequests.Add(new JoinRequest
                {
                    UserId = me,
                    Nickname = nick ?? "未知用户",
                    Note = Validate.Clean(req?.Note, 64) ?? "",
                });
                _store.MarkDirty();
                ok = true; note = "申请已提交，等待群主/管理员审批";
            }
        }
        if (ok && note == "已加入群聊")
            await SystemMessages.Post(_hub, _store, id, $"{NicknameOf(me)} 加入了群聊");
        return Ok(new { ok, message = note });
    }

    /// <summary>审批群待审批列表（仅群主/管理员）。非管理者 NotFound。</summary>
    [HttpGet("groups/{id}/join-requests")]
    public IActionResult JoinRequests(string id)
    {
        var me = HttpContext.GetUserId();
        lock (_store)
        {
            if (!_store.Groups.TryGetValue(id, out var g)) return NotFound();
            var actor = g.Members.FirstOrDefault(x => x.UserId == me);
            if (actor == null || (actor.Role != "owner" && actor.Role != "admin")) return NotFound();
            var list = (g.JoinRequests ?? new())
                .Where(r => !_store.Users.ContainsKey(r.UserId) || !_store.Users[r.UserId].Banned)
                .Select(r => new { r.UserId, r.Nickname, r.Note, At = r.At })
                .ToList();
            return Ok(list);
        }
    }

    /// <summary>审批通过（仅群主/管理员）：把申请人加入群并清掉对应申请；开放/上限约束同 open 群。</summary>
    [HttpPost("groups/{id}/join-requests/{userId}/accept")]
    public async Task<IActionResult> AcceptJoin(string id, string userId)
    {
        var me = HttpContext.GetUserId();
        bool ok;
        lock (_store)
        {
            if (!_store.Groups.TryGetValue(id, out var g)) { ok = false; return NotFound(); }
            var actor = g.Members.FirstOrDefault(x => x.UserId == me);
            if (actor == null || (actor.Role != "owner" && actor.Role != "admin")) { ok = false; return Forbid(); }
            if (!_store.Users.ContainsKey(userId)) { ok = false; return NotFound(); }
            if (g.Members.Any(m => m.UserId == userId)) { ok = true; } // 已成员则幂等
            else
            {
                if (g.Members.Count >= _store.GroupLimits.MaxMembers) { ok = false; return BadRequest(new { error = "群人数已达上限" }); }
                g.Members.Add(new GroupMember { UserId = userId });
            }
            g.JoinRequests ??= new();
            g.JoinRequests.RemoveAll(r => r.UserId == userId);
            _store.MarkDirty();
            ok = true;
        }
        if (ok) await SystemMessages.Post(_hub, _store, id, $"{NicknameOf(userId)} 加入了群聊");
        return Ok(new { ok });
    }

    /// <summary>审批拒绝（仅群主/管理员）：移除该申请。</summary>
    [HttpPost("groups/{id}/join-requests/{userId}/reject")]
    public IActionResult RejectJoin(string id, string userId)
    {
        var me = HttpContext.GetUserId();
        lock (_store)
        {
            if (!_store.Groups.TryGetValue(id, out var g)) return NotFound();
            var actor = g.Members.FirstOrDefault(x => x.UserId == me);
            if (actor == null || (actor.Role != "owner" && actor.Role != "admin")) return Forbid();
            g.JoinRequests ??= new();
            g.JoinRequests.RemoveAll(r => r.UserId == userId);
            _store.MarkDirty();
        }
        return Ok(new { ok = true });
    }

    public record JoinGroup(string? Note);

    /// <summary>
    /// 群公告「确认」：把当前用户的确认时间记到 Group.NoticeReadAt，并广播给全群，
    /// 让「已确认 x/y」即时同步（对应前端公告栏的确认回执）。
    /// </summary>
    [HttpPost("groups/{id}/notice/confirm")]
    public async Task<IActionResult> ConfirmNotice(string id)
    {
        var me = HttpContext.GetUserId();
        DateTime confirmedAt;
        int confirmed;
        int total;
        string owner;
        DateTime noticeVersion;
        lock (_store)
        {
            if (!_store.Groups.TryGetValue(id, out var g)) return NotFound();
            var m = g.Members.FirstOrDefault(x => x.UserId == me);
            if (m == null) return NotFound();
            g.NoticeReadAt ??= new();
            confirmedAt = DateTime.UtcNow;
            noticeVersion = g.NoticeUpdatedAt;
            g.NoticeReadAt[me] = confirmedAt;
            // 只统计「当前公告版本」之后的确认（公告被改 → 旧确认不算）
            confirmed = g.NoticeReadAt.Values.Count(t => t >= noticeVersion);
            total = g.Members.Count;
            owner = g.OwnerId;
            _store.MarkDirty();
        }
        // 广播「谁确认了群公告」：全群可见（让群主/管理员看到确认进度）
        try
        {
            var connections = ChatHub.MemberConnections(_store, id);
            if (connections.Count > 0)
            {
                await _hub.Clients.Clients(connections).SendAsync("NoticeConfirmed", id, new
                {
                    userId = me,
                    nickname = NicknameOf(me),
                    confirmedAt,
                    confirmed,
                    total,
                    owner,
                });
            }
        }
        catch { /* 广播失败不影响确认本身 */ }
        return Ok(new { confirmed, total, confirmedAt });
    }

    public record UpdateGroup(string? Name, string? Notice, bool? Muted, string? Avatar, string? JoinPolicy);

    [HttpPut("groups/{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateGroup req)
    {
        var me = HttpContext.GetUserId();
        if (req.Name is { Length: > Validate.MaxGroupName })
            return BadRequest(new { error = "群名称过长" });
        if (req.Notice is { Length: > Validate.MaxGroupNotice })
            return BadRequest(new { error = "群公告过长" });

        bool noticeBumped = false;
        DateTime noticeVersion;
        lock (_store)
        {
            if (!_store.Groups.TryGetValue(id, out var g)) return NotFound();
            var actor = g.Members.FirstOrDefault(x => x.UserId == me);
            if (actor == null) return NotFound();      // 非成员

            // 群名/公告：仅群主与管理员
            if (req.Name != null)
            {
                if (actor.Role == "member") return Forbid();
                g.Name = Validate.Clean(req.Name, Validate.MaxGroupName) ?? g.Name;
            }
            if (req.Notice != null)
            {
                if (actor.Role == "member") return Forbid();
                var cleaned = Validate.Clean(req.Notice, Validate.MaxGroupNotice) ?? "";
                // 公告「改动/新增」→ 提升版本：所有旧确认自动失效，各成员重新看到并需再确认
                if (cleaned != g.Notice)
                {
                    g.Notice = cleaned;
                    g.NoticeUpdatedAt = DateTime.UtcNow;
                    noticeBumped = true;
                }
            }
            // 免打扰是"我自己的"开关，绝不能改全群共享的 Conversation
            if (req.Muted.HasValue) actor.Muted = req.Muted.Value;

            // 群头像：仅群主/管理员，必须经 /files/upload(kind=avatar) 拿到真实 fileId（防越权引用他人文件）
            if (req.Avatar != null)
            {
                if (actor.Role == "member") return Forbid();
                if (!string.IsNullOrWhiteSpace(req.Avatar))
                {
                    if (!Validate.IsAvatarUrl(req.Avatar))
                        return BadRequest(new { error = "群头像需通过上传图片设置" });
                    if (!Validate.IsFileId(req.Avatar))
                        return BadRequest(new { error = "群头像无效，请重新上传" });
                    if (!_store.Files.TryGetValue(req.Avatar, out var av)
                        || !_store.CanReadFile(av.Id, av.UploaderId, me))
                        return BadRequest(new { error = "群头像文件不可用，请重新上传" });
                }
                g.Avatar = string.IsNullOrWhiteSpace(req.Avatar) ? "" : req.Avatar.Trim();
            }

            // 加群策略（open/approve）：仅群主/管理员可改；白名单防注入
            if (req.JoinPolicy != null)
            {
                if (actor.Role == "member") return Forbid();
                g.JoinPolicy = req.JoinPolicy == "open" ? "open" : "approve";
            }
            noticeVersion = g.NoticeUpdatedAt;
            _store.MarkDirty();
        }

        // 公告被改/新增：广播全群，在线成员自动「重新展示 + 需再确认」（之前确认的自动隐藏）
        if (noticeBumped)
        {
            try
            {
                var connections = ChatHub.MemberConnections(_store, id);
                if (connections.Count > 0)
                {
                    await _hub.Clients.Clients(connections).SendAsync("NoticeUpdated", id, new
                    {
                        version = noticeVersion,
                        editor = NicknameOf(me),
                    });
                }
            }
            catch { /* 广播失败不影响公告保存本身 */ }
        }

        return Ok(new { g_Id = id, noticeVersion, noticeBumped });
    }

    public record AddMember(string UserId, string Role = "member");

    [HttpPost("groups/{id}/members")]
    public async Task<IActionResult> AddMemberAction(string id, [FromBody] AddMember req)
    {
        var me = HttpContext.GetUserId();
        bool added;
        lock (_store)
        {
            if (!_store.Groups.TryGetValue(id, out var g)) return NotFound();
            if (!g.Members.Any(m => m.UserId == me && m.Role is "owner" or "admin"))
                return Forbid();
            if (!_store.Users.ContainsKey(req.UserId)) return BadRequest(new { error = "用户不存在" });
            if (g.Members.Any(m => m.UserId == req.UserId)) return Ok();
            // 防骚扰：只能邀请自己的好友入群（此前任何 userId 都能被拉进任意群）
            if (!_store.IsContact(me, req.UserId))
                return BadRequest(new { error = "只能邀请自己的好友入群" });

            // 群人数上限（默认 100，管理端可改）
            var limits = _store.GroupLimits;
            if (g.Members.Count >= limits.MaxMembers)
                return BadRequest(new { error = $"群人数已达上限（{limits.MaxMembers} 人）" });
            // 角色白名单：客户端不能通过 Role 注入 owner；且只有群主能授予管理员
            var role = req.Role == "admin" ? "admin" : "member";
            if (role == "admin")
            {
                if (g.Members.First(x => x.UserId == me).Role != "owner")
                    return Forbid();
                // 管理员数量上限（默认 5，不含群主）
                var adminCount = g.Members.Count(m => m.Role == "admin");
                if (adminCount >= _store.GroupLimits.MaxAdmins)
                    return BadRequest(new { error = $"群管理员已达上限（{_store.GroupLimits.MaxAdmins} 人）" });
            }
            g.Members.Add(new GroupMember { UserId = req.UserId, Role = role });
            added = true;
        }
        if (added)
            await SystemMessages.Post(_hub, _store, id, $"{NicknameOf(me)} 邀请了 {NicknameOf(req.UserId)} 入群");
        return Ok();
    }

    public record UpdateMemberRequest(string? Role, bool? Muted, string? Nickname, string? Title);

    [HttpPut("groups/{id}/members/{userId}")]
    public async Task<IActionResult> UpdateMember(string id, string userId, [FromBody] UpdateMemberRequest req)
    {
        var me = HttpContext.GetUserId();
        if (req.Nickname is { Length: > Validate.MaxNickname })
            return BadRequest(new { error = "群昵称过长" });
        if (req.Title is { Length: > Validate.MaxNickname })
            return BadRequest(new { error = "头衔过长" });

        bool changed = false;
        lock (_store)
        {
            if (!_store.Groups.TryGetValue(id, out var g)) return NotFound();

            // 鉴权：必须是群成员（否则任何人都能改别人的角色）
            var actor = g.Members.FirstOrDefault(x => x.UserId == me);
            if (actor == null) return NotFound();

            var m = g.Members.FirstOrDefault(x => x.UserId == userId);
            if (m == null) return NotFound();

            // 群主不能被降级/改角色
            if (m.Role == "owner" && req.Role != null) return Conflict(new { error = "群主不能被修改" });

            // 改角色：仅群主，且只能是 admin/member（防提权到 owner）
            if (req.Role != null)
            {
                if (actor.Role != "owner") return Forbid();
                if (req.Role is not ("admin" or "member"))
                    return BadRequest(new { error = "角色只能是 admin 或 member" });
                // 升为管理员时校验管理员数量上限（默认 5）
                if (req.Role == "admin" && m.Role != "admin"
                    && g.Members.Count(x => x.Role == "admin") >= _store.GroupLimits.MaxAdmins)
                    return BadRequest(new { error = $"群管理员已达上限（{_store.GroupLimits.MaxAdmins} 人）" });
                m.Role = req.Role;
                changed = true;
            }
            // 免打扰：改目标本人或群主/管理员；普通成员只能改自己
            if (req.Muted.HasValue)
            {
                if (userId != me && actor.Role == "member") return Forbid();
                m.Muted = req.Muted.Value;
                changed = true;
            }
            // 群昵称：本人或群主/管理员
            if (req.Nickname != null)
            {
                if (userId != me && actor.Role == "member") return Forbid();
                m.Nickname = Validate.Clean(req.Nickname, Validate.MaxNickname) ?? "";
                changed = true;
            }
            // 头衔：仅群主/管理员可为任意成员单独设置（本人或管理对象都要）；
            // 空串 = 清除该成员的头衔。
            if (req.Title != null)
            {
                if (actor.Role is not ("owner" or "admin")) return Forbid();
                m.Title = Validate.Clean(req.Title, Validate.MaxNickname) ?? "";
                changed = true;
            }
            _store.MarkDirty();
        }

        if (changed && req.Role != null)
            await SystemMessages.Post(_hub, _store, id, req.Role == "admin"
                ? $"{NicknameOf(HttpContext.GetUserId())} 将 {NicknameOf(userId)} 设为群管理员"
                : $"{NicknameOf(HttpContext.GetUserId())} 取消了 {NicknameOf(userId)} 的管理员");
        return Ok();
    }

    public record RemoveMember(string UserId);

    [HttpDelete("groups/{id}/members/{userId}")]
    public async Task<IActionResult> RemoveMemberAction(string id, string userId)
    {
        var me = HttpContext.GetUserId();
        lock (_store)
        {
            if (!_store.Groups.TryGetValue(id, out var g)) return NotFound();

            // 鉴权：必须是群成员且有管理权限（否则任何登录用户都能踢人）
            var actor = g.Members.FirstOrDefault(x => x.UserId == me);
            if (actor == null) return NotFound();
            if (actor.Role == "member" && userId != me) return Forbid();

            var target = g.Members.FirstOrDefault(x => x.UserId == userId);
            if (target == null) return NotFound();
            if (target.Role == "owner") return Conflict(new { error = "群主不能被移除" });
            // 管理员不能踢另一个管理员
            if (actor.Role == "admin" && target.Role == "admin") return Forbid();

            g.Members.Remove(target);
            _store.MarkDirty();
        }
        await SystemMessages.Post(_hub, _store, id, $"已将 {NicknameOf(userId)} 移出群聊");
        return Ok();
    }

    [HttpPost("groups/{id}/leave")]
    public async Task<IActionResult> Leave(string id)
    {
        var me = HttpContext.GetUserId();
        bool left = false;
        lock (_store)
        {
            if (!_store.Groups.TryGetValue(id, out var g)) return NotFound();
            var m = g.Members.FirstOrDefault(x => x.UserId == me);
            if (m == null) return Ok();
            if (m.Role == "owner") return Conflict(new { error = "群主需先转让或解散" });
            g.Members.Remove(m);
            _store.MarkDirty();
            left = true;
        }
        if (left)
            await SystemMessages.Post(_hub, _store, id, $"{NicknameOf(me)} 退出了群聊");
        return Ok();
    }

    public record TransferOwner(string UserId);

    /// <summary>转让群主：仅群主可操作，原群主降为管理员，目标成员升为群主。</summary>
    [HttpPost("groups/{id}/transfer")]
    public async Task<IActionResult> TransferOwnerAction(string id, [FromBody] TransferOwner req)
    {
        var me = HttpContext.GetUserId();
        bool done = false;
        lock (_store)
        {
            if (!_store.Groups.TryGetValue(id, out var g)) return NotFound();
            var owner = g.Members.FirstOrDefault(x => x.UserId == me && x.Role == "owner");
            if (owner == null) return Forbid();
            var target = g.Members.FirstOrDefault(x => x.UserId == req.UserId);
            if (target == null) return BadRequest(new { error = "该成员不在群内" });
            owner.Role = "admin";
            target.Role = "owner";
            g.OwnerId = req.UserId;
            _store.MarkDirty();
            done = true;
        }
        if (done)
            await SystemMessages.Post(_hub, _store, id, $"已将群主转让给 {NicknameOf(req.UserId)}");
        return Ok();
    }

    /// <summary>解散群聊：仅群主可操作，移除群与会话。</summary>
    [HttpDelete("groups/{id}")]
    public IActionResult Dissolve(string id)
    {
        var me = HttpContext.GetUserId();
        lock (_store)
        {
            if (!_store.Groups.TryGetValue(id, out var g)) return NotFound();
            if (!g.Members.Any(x => x.UserId == me && x.Role == "owner")) return Forbid();
            _store.Groups.Remove(id);
            _store.Conversations.Remove(id);
            // 孤儿消息清理：此前只删群和会话，历史消息永远留在内存与落盘里
            _store.ClearConversationMessages(id);   // 内部 MarkDirty，750ms 合并窗口落盘
        }

        // 会话已移除，不再广播
        return Ok();
    }

    /* ---------------- 群投票（微信式） ---------------- */

    public record CreatePollRequest(string Title, List<string> Options, bool Multi);
    public record PollVote(int Option);

    /// <summary>发起投票：标题 + ≥2 个选项，单选/多选。仅群成员。</summary>
    [HttpPost("groups/{id}/polls")]
    public IActionResult CreatePoll(string id, [FromBody] CreatePollRequest req)
    {
        var me = HttpContext.GetUserId();
        var title = Validate.Clean(req.Title, 60) ?? "";
        // 选项去重去空，最多 20 个（微信投票风格），每个上限 40 字
        var optionTexts = (req.Options ?? new List<string>())
            .Select(o => Validate.Clean(o, 40) ?? "")
            .Where(o => o.Length > 0)
            .Distinct()
            .Take(20)
            .ToList();
        if (string.IsNullOrWhiteSpace(title) || optionTexts.Count < 2)
            return BadRequest(new { error = "投票需要标题和至少 2 个选项" });

        Poll poll;
        lock (_store)
        {
            if (!_store.Groups.TryGetValue(id, out var g) || !g.Members.Any(m => m.UserId == me))
                return NotFound(new { error = "群不存在或你不是群成员" });
            poll = new Poll
            {
                Title = title,
                Multi = req.Multi,
                CreatedBy = me,
                Options = optionTexts.Select(o => new PollOption { Text = o }).ToList(),
            };
            g.Polls.Add(poll);
            _store.MarkDirty();
        }
        return Ok(poll);
    }

    /// <summary>群内投票列表（进聊天页拉一次 + PollUpdated 实时刷新）。</summary>
    [HttpGet("groups/{id}/polls")]
    public IActionResult Polls(string id)
    {
        var me = HttpContext.GetUserId();
        lock (_store)
        {
            if (!_store.Groups.TryGetValue(id, out var g) || !g.Members.Any(m => m.UserId == me))
                return NotFound(new { error = "群不存在或你不是群成员" });
            return Ok(g.Polls);
        }
    }

    /// <summary>投票：单选改投自动迁移；再点已投选项 = 取消该票。返回最新投票（全员实时推送）。</summary>
    [HttpPost("groups/{id}/polls/{pollId}/vote")]
    public async Task<IActionResult> Vote(string id, string pollId, [FromBody] PollVote req)
    {
        var me = HttpContext.GetUserId();
        Poll poll;
        lock (_store)
        {
            if (!_store.Groups.TryGetValue(id, out var g) || !g.Members.Any(m => m.UserId == me))
                return NotFound(new { error = "群不存在或你不是群成员" });
            poll = g.Polls.FirstOrDefault(p => p.Id == pollId);
            if (poll == null) return NotFound(new { error = "投票不存在" });
            if (poll.Closed) return BadRequest(new { error = "投票已截止" });
            if (req.Option < 0 || req.Option >= poll.Options.Count)
                return BadRequest(new { error = "选项不合法" });

            // 先记「是否已投该选项」，再把我从所有选项里撤出：
            //   单选 = 直接改投新选项；多选 = 已投再点 → 取消该票，未投 → 追加
            bool alreadyVoted = poll.Options[req.Option].UserIds.Contains(me);
            foreach (var o in poll.Options) o.UserIds.Remove(me);
            if (!poll.Multi || !alreadyVoted)
                poll.Options[req.Option].UserIds.Add(me);
            _store.MarkDirty();
        }
        await ChatHub.BroadcastPoll(_hub, _store, id, poll);
        return Ok(poll);
    }

    /// <summary>截止投票：发起人或群主。截止后只读。</summary>
    [HttpPost("groups/{id}/polls/{pollId}/close")]
    public async Task<IActionResult> Close(string id, string pollId)
    {
        var me = HttpContext.GetUserId();
        Poll poll;
        lock (_store)
        {
            if (!_store.Groups.TryGetValue(id, out var g) || !g.Members.Any(m => m.UserId == me))
                return NotFound(new { error = "群不存在或你不是群成员" });
            poll = g.Polls.FirstOrDefault(p => p.Id == pollId);
            if (poll == null) return NotFound(new { error = "投票不存在" });
            var isOwner = g.OwnerId == me;
            if (poll.CreatedBy != me && !isOwner) return Forbid();
            poll.Closed = true;
            _store.MarkDirty();
        }
        await ChatHub.BroadcastPoll(_hub, _store, id, poll);
        return Ok(poll);
    }
}
