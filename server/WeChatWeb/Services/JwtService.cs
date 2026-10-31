using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using WeChatWeb.Models;

namespace WeChatWeb.Services;

public sealed class JwtService
{
    private readonly IConfiguration _config;

    public JwtService(IConfiguration config)
    {
        _config = config;
    }

    private byte[] SecretKey => JwtKeyProvider.GetKey(_config);

    public (string accessToken, string refreshToken) CreateTokens(User user)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("phone", user.Phone),
            // 签发时刻（秒）：UserInvalidationMiddleware 据此拒绝「改密码之前签发」的旧令牌
            new Claim("pwd", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()),
        };
        var notBefore = DateTime.UtcNow;
        var expires = DateTime.UtcNow.AddHours(2);
        var signingCredentials = new SigningCredentials(new SymmetricSecurityKey(SecretKey), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            "wechat-web",
            "wechat-web",
            claims,
            notBefore,
            expires,
            signingCredentials);

        var refreshToken = Guid.NewGuid().ToString("N");
        return (new JwtSecurityTokenHandler().WriteToken(token), refreshToken);
    }

    public ClaimsPrincipal? Validate(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        var principal = handler.ValidateToken(token,
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = "wechat-web",
                ValidateAudience = true,
                ValidAudience = "wechat-web",
                ValidateLifetime = true,
                // BE-SEC-042：仅接受 HS256，与 Program.cs 中的认证管线保持同一白名单
                ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
                IssuerSigningKey = new SymmetricSecurityKey(SecretKey),
                ClockSkew = TimeSpan.FromMinutes(1),
            }, out var securityToken);
        return principal;
    }
}
