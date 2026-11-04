using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WeChatWeb.Data;
using WeChatWeb.Models;
using WeChatWeb.Services;

namespace WeChatWeb.Controllers;

/// <summary>
/// 收藏：消息与朋友圈动态。访问权按「收藏者本人」隔离，targetType 白名单 + 数量上限。
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
public class FavoritesController : ControllerBase
{
    private readonly DataStore _store;

    /// <summary>每人最多收藏数（防止刷爆内存）。</summary>
    private const int MaxPerUser = 500;

    public FavoritesController(DataStore store) => _store = store;

    [HttpGet("favorites")]
    public IActionResult List()
    {
        var me = HttpContext.GetUserId();
        lock (_store)
        {
            var list = _store.Favorites
                .Where(f => f.UserId == me)
                .OrderByDescending(f => f.CreatedAt)
                .Take(MaxPerUser)
                .Select(f => new { f.Id, f.TargetType, f.TargetId, f.Title, f.Preview, f.ImageUrl, f.CreatedAt })
                .ToList();
            return Ok(list);
        }
    }

    public record AddFavorite(string TargetType, string TargetId, string? Title, string? Preview, string? ImageUrl);

    [HttpPost("favorites")]
    public IActionResult Add([FromBody] AddFavorite req)
    {
        var me = HttpContext.GetUserId();
        if (req.TargetType is not ("message" or "moment"))
            return BadRequest(new { error = "只能收藏消息或动态" });

        lock (_store)
        {
            if (_store.Favorites.Count(f => f.UserId == me) >= MaxPerUser)
                return BadRequest(new { error = "收藏已达上限" });

            // 重复收藏：取消（与主流行 IM 一致）
            var exists = _store.Favorites.FirstOrDefault(f =>
                f.UserId == me && f.TargetType == req.TargetType && f.TargetId == req.TargetId);
            if (exists != null)
            {
                _store.Favorites.Remove(exists);
                _store.MarkDirty();
                return Ok(new { toggled = false });
            }

            // 校验目标真实存在且当前用户有权访问（消息必须在自己能访问的会话里）
            if (req.TargetType == "message")
            {
                var msg = _store.GetMessageById(req.TargetId);
                if (msg == null || !_store.CanAccessConversation(msg.ConversationId, me))
                    return NotFound();
            }
            else
            {
                var mom = _store.Moments.FirstOrDefault(m => m.Id == req.TargetId);
                if (mom == null) return NotFound();
                // 可见性与朋友圈主链路同规（MomentVisibility）：此前这里自写了一份
                // 只有 private/friends 的简化版，漏了「陌生人十条」开关 —— 陌生人可以
                // 收藏一条自己永远不可见的公开动态，长期留存服务端
                if (!MomentVisibility.IsVisible(_store, mom, me)) return NotFound();
            }

            _store.Favorites.Add(new Favorite
            {
                UserId = me,
                TargetType = req.TargetType,
                TargetId = req.TargetId,
                Title = Validate.Clean(req.Title, 120) ?? "",
                Preview = Validate.Clean(req.Preview, Validate.MaxSignature) ?? "",
                // 只允许站内文件路径，防存储型 XSS（javascript: / 外链）
                ImageUrl = req.ImageUrl is { Length: > 0 } && req.ImageUrl.StartsWith("/api/files/", StringComparison.Ordinal) ? req.ImageUrl : null,
            });
            _store.MarkDirty();
            return Ok(new { toggled = true });
        }
    }

    [HttpDelete("favorites/{id}")]
    public IActionResult Delete(string id)
    {
        var me = HttpContext.GetUserId();
        lock (_store)
        {
            var f = _store.Favorites.FirstOrDefault(x => x.Id == id && x.UserId == me);
            if (f == null) return NotFound();
            _store.Favorites.Remove(f);
            _store.MarkDirty();
            return Ok();
        }
    }
}