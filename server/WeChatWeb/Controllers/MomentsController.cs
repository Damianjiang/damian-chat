using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WeChatWeb.Data;
using WeChatWeb.Models;
using WeChatWeb.Services;

namespace WeChatWeb.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class MomentsController : ControllerBase
{
    private readonly DataStore _store;
    private readonly ActionRateLimiter _actionLimiter;

    public MomentsController (DataStore store, ActionRateLimiter actionLimiter)
    {
        _store = store;
        _actionLimiter = actionLimiter;
    }

    /// <summary>动态可见性：自己 / 公开 / 好友可见；private 仅自己。
    /// 公开动态额外受作者「允许陌生人查看十条朋友圈」约束（统一走 MomentVisibility）。</summary>
    private bool CanView (Moment m, string me) => MomentVisibility.IsVisible(_store, m, me);

    /// <summary>作者只给公开字段（此前序列化整个 User，手机号会被朋友圈批量拖走）。
    /// 微信号（WxId）属隐私字段，须受「好友且对方允许」控制 —— 朋友圈作者信息里干脆不下发。</summary>
    private object? AuthorDto (string authorId)
        => _store.Users.TryGetValue(authorId, out var u)
            ? new { u.Id, u.Nickname, u.Avatar }
            : null;

    /// <summary>动态 DTO：作者/点赞/评论的昵称在服务端组装，前端直接可用。</summary>
    private object MomentDto (Moment m)
    {
        return new
        {
            m.Id,
            m.Text,
            Images = m.Images,
            Scope = m.Scope,
            m.CreatedAt,
            Author = AuthorDto(m.AuthorId),
            Likes = m.Likes.Select(l => new { userId = l, nickname = _store.Users.TryGetValue(l, out var u) ? u.Nickname : "未知用户" }).ToList(),
            Comments = m.Comments.Select(c => new
            {
                c.Id,
                c.UserId,
                nickname = _store.Users.TryGetValue(c.UserId, out var cu) ? cu.Nickname : "未知用户",
                c.Text,
                c.CreatedAt,
            }).ToList(),
        };
    }

    /// <summary>单条动态详情（深链/收藏跳转兜底：列表分页里可能没有这一条）。</summary>
    [HttpGet("moments/{id}")]
    public IActionResult Detail(string id)
    {
        var me = HttpContext.GetUserId();
        lock (_store)
        {
            var m = _store.Moments.FirstOrDefault(x => x.Id == id);
            if (m == null || !CanView(m, me)) return NotFound();
            return Ok(MomentDto(m));
        }
    }

    [HttpGet("moments")]
    public IActionResult Timeline([FromQuery] string? before, int count = 10)
    {
        var me = HttpContext.GetUserId();
        lock (_store)
        {
            // 隐私过滤按「作者」预聚合后一次遍历：此前对每条动态都跑一遍 CanView，
            // 其中「陌生人十条窗口」要对作者全量动态再排序取 10 —— 全表 O(n²)，
            // 且全程持全局锁，动态一多整站请求都在锁上排队。语义与 MomentVisibility 一致：
            //   自己=全部；好友=非 private；陌生人=受开关约束的最近 10 条公开动态
            bool IsFriend (string authorId) =>
                _store.Contacts.TryGetValue(me, out var mine) && mine.Any(c => c.OtherUserId == authorId);
            var byAuthor = new Dictionary<string, List<Moment>>();
            foreach (var m in _store.Moments)
            {
                if (m.Scope == "private" && m.AuthorId != me) continue;
                if (!byAuthor.TryGetValue(m.AuthorId, out var list)) byAuthor[m.AuthorId] = list = new();
                list.Add(m);
            }
            IEnumerable<Moment> visible = byAuthor.SelectMany(kvp =>
            {
                if (kvp.Key == me) return (IEnumerable<Moment>)kvp.Value;
                if (IsFriend(kvp.Key)) return kvp.Value.Where(m => m.Scope != "private");
                if (!_store.Users.TryGetValue(kvp.Key, out var author)
                    || (author.Privacy.TryGetValue("strangerMoments", out var v) && !v))
                    return Enumerable.Empty<Moment>();
                return kvp.Value.Where(m => m.Scope == "all")
                    .OrderByDescending(m => m.CreatedAt).Take(MomentVisibility.StrangerWindowSize);
            });
            var query = visible.OrderByDescending(m => m.CreatedAt).AsEnumerable();
            if (!string.IsNullOrEmpty(before))
            {
                var b = _store.Moments.FirstOrDefault(m => m.Id == before);
                if (b != null) query = query.Where(m => m.CreatedAt < b.CreatedAt);
            }
            // 翻页判断必须用夹紧后的 take：用原始 count 判断会在 count>50 时
            // 把"已到末页"误判为"还有下一页"，前端空转加载
            var take = Math.Clamp(count, 1, 50);
            var page = query.Take(take).ToList();
            return Ok(new
            {
                items = page.Select(MomentDto),
                nextBefore = page.Count == take ? page[^1].Id : null,
            });
        }
    }

    public record CreateMoment(string Text, List<string> Images, string Scope = "all");

    [HttpPost("moments")]
    public IActionResult Create([FromBody] CreateMoment req)
    {
        var me = HttpContext.GetUserId();

        // 频控：发动态会持久化正文 + 多张图片引用，并能推送给全部好友（骚扰面）。
        // 按发起人限制，避免单账号把全站额度吃光。
        if (_actionLimiter.IsOverLimit("moment-create", me, 20, TimeSpan.FromHours(1), out var moRetry))
        {
            HttpContext.Response.Headers["Retry-After"] = moRetry.ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { error = $"发布过于频繁，{moRetry} 秒后再试", retryAfter = moRetry });
        }

        // 服务端校验：正文长度、图片数量、图片必须引用本服务上传的 fileId、可见范围白名单
        if (req.Text is { Length: > Validate.MaxMomentText })
            return BadRequest(new { error = "动态正文过长" });
        var images = (req.Images ?? new()).ToList();
        if (images.Count > Validate.MaxImagesPerMoment)
            return BadRequest(new { error = $"最多 {Validate.MaxImagesPerMoment} 张图片" });
        if (images.Any(i => !Validate.IsFileId(i)))
            return BadRequest(new { error = "图片无效，请重新上传" });
        var scope = req.Scope is "all" or "friends" or "private" ? req.Scope : "all";

        Moment created;
        lock (_store)
        {
            // 归属校验：只允许引用「本人上传」的文件。此前只验 32 位 hex 格式，任何人可以把
            // （别人私发的、或已对其撤权的）文件 id 塞进自己的公开朋友圈 —— 下载侧的
            // 「出现在我可公开看见的朋友圈里则可读」豁免会让该文件对所有人恢复可下载。
            // 必须持锁校验：文件清理/删除端点在锁内改 Files 字典，锁外读会偶发 500
            foreach (var imgId in images)
            {
                if (!_store.Files.TryGetValue(imgId, out var fm) || fm.UploaderId != me)
                    return BadRequest(new { error = "图片无效，请重新上传" });
            }
            // 每用户动态数上限：此前无界，脚本可无限发（Timeline 过滤成本随之线性涨）
            if (_store.Moments.Count(m => m.AuthorId == me) >= 500)
                return BadRequest(new { error = "你的动态已达上限（500 条），请先删除部分旧动态" });
            created = new Moment
            {
                Id = Guid.NewGuid().ToString("N"),
                AuthorId = me,
                Text = Validate.Clean(req.Text, Validate.MaxMomentText) ?? "",
                Images = images,
                Scope = scope,
                CreatedAt = DateTime.UtcNow,
            };
            _store.Moments.Add(created);
            _store.MarkDirty();
            // 返回组装后的 DTO：此前返回裸实体（无 author/likes/comments 组装），前端新动态卡片作者显示为空
            return Ok(MomentDto(created));
        }
    }

    [HttpPost("moments/{id}/like")]
    public IActionResult Like(string id)
    {
        var me = HttpContext.GetUserId();
        lock (_store)
        {
            var m = _store.Moments.FirstOrDefault(x => x.Id == id);
            if (m == null || !CanView(m, me)) return NotFound();   // 不可见即 404
            if (!m.Likes.Contains(me)) m.Likes.Add(me);
            _store.MarkDirty();
            return Ok(new { likes = m.Likes.Count });
        }
    }

    [HttpDelete("moments/{id}/like")]
    public IActionResult Unlike(string id)
    {
        var me = HttpContext.GetUserId();
        lock (_store)
        {
            var m = _store.Moments.FirstOrDefault(x => x.Id == id);
            if (m == null || !CanView(m, me)) return NotFound();
            m.Likes.Remove(me);
            _store.MarkDirty();
            return Ok(new { likes = m.Likes.Count });
        }
    }

    public record AddComment(string Text);

    [HttpPost("moments/{id}/comments")]
    public IActionResult Comment(string id, [FromBody] AddComment req)
    {
        var me = HttpContext.GetUserId();
        if (req.Text is { Length: > Validate.MaxComment })
            return BadRequest(new { error = "评论过长" });
        var text = Validate.Clean(req.Text, Validate.MaxComment);
        if (string.IsNullOrEmpty(text)) return BadRequest(new { error = "评论内容不能为空" });

        lock (_store)
        {
            var m = _store.Moments.FirstOrDefault(x => x.Id == id);
            // 可见性校验：不能给「仅自己可见」或非好友可见的动态评论
            if (m == null || !CanView(m, me)) return NotFound();
            // 单条动态评论上限：此前可无限刷（每条永久驻留 store.json）
            if (m.Comments.Count >= 500)
                return BadRequest(new { error = "这条动态的评论已达上限（500 条）" });
            var c = new MomentComment { Id = Guid.NewGuid().ToString("N"), UserId = me, Text = text, CreatedAt = DateTime.UtcNow };
            m.Comments.Add(c);
            _store.MarkDirty();
            // 返回带昵称的 DTO，前端直接追加渲染（此前裸实体缺 nickname）
            return Ok(new { c.Id, c.UserId, nickname = _store.Users.TryGetValue(me, out var u) ? u.Nickname : "未知用户", c.Text, c.CreatedAt });
        }
    }

    [HttpDelete("moments/{id}")]
    public IActionResult Remove(string id)
    {
        var me = HttpContext.GetUserId();
        lock (_store)
        {
            var m = _store.Moments.FirstOrDefault(x => x.Id == id && x.AuthorId == me);
            if (m == null) return NotFound();
            _store.Moments.Remove(m);
            _store.MarkDirty();
            return Ok();
        }
    }
}
