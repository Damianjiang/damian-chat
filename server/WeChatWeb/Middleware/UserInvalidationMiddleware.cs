using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using WeChatWeb.Data;

namespace WeChatWeb.Middleware;

/// <summary>
/// 用户失效检查（BE-SEC-047 / BIZ-SEC-023）：
/// 无状态 JWT 的 access token 有效期 2h，仅验签 + 有效期无法即时反映「封号 / 删号」。
/// 本中间件在每个已认证请求上按 sub 反查 DataStore：用户已被删除或处于封禁状态时，
/// 清空身份并返回 401，使封号/删号对「已签发的旧 access token」也即时失效。
///
/// 说明：
/// ・仅对「已认证」请求（context.User 有 sub）做检查；匿名端点（登录/注册/图形验证等）不受影响。
/// ・每次请求取 lock(store) 做 O(1) 字典查找，与本库所有控制器的加锁约定一致；查找极轻量，
///   不做额外缓存以免引入一致性复杂度。
/// ・不向客户端泄露「你被封了」的细节，统一回 401「登录已失效」。
/// </summary>
public sealed class UserInvalidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly DataStore _store;

    public UserInvalidationMiddleware(RequestDelegate next, DataStore store)
    {
        _next = next;
        _store = store;
    }

    public Task InvokeAsync(HttpContext context)
    {
        var sub = context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrEmpty(sub))
        {
            lock (_store)
            {
                if (!_store.Users.TryGetValue(sub, out var u) || u.Banned)
                {
                    context.User = new ClaimsPrincipal(new ClaimsIdentity());
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.ContentType = "application/json; charset=utf-8";
                    return context.Response.WriteAsync("{\"error\":\"登录已失效，请重新登录\"}");
                }
            }
        }
        return _next(context);
    }
}
