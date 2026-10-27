using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WeChatWeb.Services;

namespace WeChatWeb.Controllers;

/// <summary>
/// 滑块人机验证接口（匿名可访问，仅用于登录/注册前的挑战）。
/// 挑战 key 一次性、2 分钟过期；交互完成后换取一次性 pass token，登录/注册时必须消费。
/// </summary>
[ApiController]
[Route("api/captcha")]
public class SliderCaptchaController : ControllerBase
{
    private readonly SliderCaptchaService _slider;
    private readonly LogService _log;

    public SliderCaptchaController (SliderCaptchaService slider, LogService log)
    {
        _slider = slider;
        _log = log;
    }

    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";

    /// <summary>签发滑块挑战。限流：每 IP 每分钟 30 次；失败锁定 / 挑战囤积直接 429（防脚本爆破）。</summary>
    [HttpPost("init")]
    [EnableRateLimiting("captcha")]
    public IActionResult Init ()
    {
        // 服务端生成背景图（含一块「高饱和目标色块」）。缺口坐标由服务端权威持有，**不在 JSON 明文下发**
        // ——前端用像素扫描定位色块、裁拼块、拖到其上对齐；/verify 时服务端校验落点
        // |dragX - slotX| ≤ 容差才放行。机器人即使伪造拖动时长/次数，落点对不上也会被拒并计入失败锁定。
        var (key, image) = _slider.Issue(Ip);
        if (key is null || image is null)
        {
            _log.Write("auth", new { ts = DateTime.UtcNow, result = "slider-issue-denied", ip = Ip });
            var locked = _slider.IsLockedOut(Ip);
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { ret = new { code = 429, msg = locked ? "失败次数过多，请 15 分钟后再试" : "挑战获取过于频繁，请稍后再试" }, data = (object?)null });
        }
        return Ok(new { ret = new { code = 200 }, data = new { key, image } });
    }

    /// <summary>用户完成拼图交互后换取 pass token。key 一次性，重放返回失败。</summary>
    [HttpPost("verify")]
    [EnableRateLimiting("captcha")]
    public IActionResult Verify ([FromBody] VerifyRequest req)
    {
        var token = _slider.Complete(req.Key, Ip, req.DragX, req.MoveCount, req.DragMs);
        if (token is null)
        {
            _log.Write("auth", new { ts = DateTime.UtcNow, result = "slider-fail", ip = Ip });
            // 组件约定：ret.code 不为 200 即判定验证失败
            return Ok(new { ret = new { code = 400, msg = "验证已失效，请重试" }, data = (object?)null });
        }
        return Ok(new { ret = new { code = 200 }, data = new { passToken = token } });
    }

    public record VerifyRequest (string? Key, int? DragX, int? MoveCount, int? DragMs);
}