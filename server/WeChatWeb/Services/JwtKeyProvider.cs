namespace WeChatWeb.Services;

/// <summary>
/// JWT 签名密钥提供者：配置缺失/过短/为已知占位符时自动生成 64 字节随机密钥，
/// 持久化到 data/jwt.secret（0600 权限语义由文件系统决定），所有实例共享同一密钥。
/// </summary>
public static class JwtKeyProvider
{
    private const string Placeholder = "wechat-web-dev-secret-change-me-in-production";
    private static readonly object Gate = new();
    private static byte[]? _cached;

    public static string FilePath =>
        Environment.GetEnvironmentVariable("WX_JWT_SECRET_FILE")
        ?? Path.Combine(Directory.GetCurrentDirectory(), "data", "jwt.secret");

    public static byte[] GetKey (IConfiguration config)
    {
        if (_cached != null) return _cached;
        lock (Gate)
        {
            if (_cached != null) return _cached;

            var configured = config["Jwt:Secret"];
            if (!string.IsNullOrWhiteSpace(configured) && configured != Placeholder && configured.Length >= 32)
            {
                _cached = System.Text.Encoding.UTF8.GetBytes(configured);
                return _cached;
            }

            try
            {
                if (File.Exists(FilePath))
                {
                    // 自愈读：文件存在但读不到（历史遗留空 DACL）时先修 ACL 再读，
                    // 避免「静默重新生成密钥」把所有在线用户一次全登出。
                    if (SensitiveFile.TryReadAllText(FilePath, out var existing) && existing.Trim().Length >= 32)
                    {
                        _cached = System.Text.Encoding.UTF8.GetBytes(existing.Trim());
                        return _cached;
                    }
                    Console.Error.WriteLine(
                        $"[JwtKey] 警告：{FilePath} 存在但无法读取，将生成新密钥。" +
                        "所有已签发令牌会立即失效（用户需重新登录）。请按上面的提示修复该文件权限。");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[JwtKey] 读取 {FilePath} 失败：{ex.GetType().Name}: {ex.Message}，将生成新密钥。");
            }

            var secret = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, secret);
                SensitiveFile.RestrictToOwner(FilePath);
            }
            catch
            {
                // 只读环境：仅本进程内存生效
            }
            _cached = System.Text.Encoding.UTF8.GetBytes(secret);
            return _cached;
        }
    }
}
