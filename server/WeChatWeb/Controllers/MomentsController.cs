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

    public MomentsController (DataStore store) => _store = store;

    /// <summary>动态可见性：自己 / 公开 / 好友可见；private 仅自己。</summary>
    private bool CanView (Moment m, string me)
    {
        if (m.AuthorId == me) return true;
        if (m.Scope == "private") return false;
        if (m.Scope == "friends")
            return _store.Contacts.TryGetValue(me, out var mine) && mine.Any(c => c.OtherUserId == m.AuthorId);
        return true;
    }

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
            // 隐私过滤：private 仅自己；friends 仅好友可见
            var query = _store.Moments.Where(m => CanView(m, me)).OrderByDescending(m => m.CreatedAt).AsEnumerable();
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
