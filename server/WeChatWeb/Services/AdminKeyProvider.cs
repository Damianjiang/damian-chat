using System.Security.Cryptography;

namespace WeChatWeb.Services;

/// <summary>
/// 管理密钥提供者：首次运行自动生成 32 字节随机密钥并持久化到 data/admin.key。
/// appsettings.json 里的 Admin:Key 只在显式配置时生效；默认留空 = 自动生成。
/// 这样开源/默认配置不会带着可猜测的固定管理口令（wxadmin-dev-key 已被废弃）。
/// </summary>
public static class AdminKeyProvider
{
    public const string EnvVar = "WX_ADMIN_KEY_FILE";

    /// <summary>配置文件路径：环境变量优先，默认当前工作目录（server/WeChatWeb）下的 data/admin.key。</summary>
    public static string KeyPath =>
        Environment.GetEnvironmentVariable(EnvVar)
        ?? Path.Combine(Directory.GetCurrentDirectory(), "data", "admin.key");

    /// <summary>
    /// 取管理密钥：appsettings 配置值 &gt; data/admin.key 文件 &gt; 新生成并写文件。
    /// </summary>
    public static string GetKey (IConfiguration config)
    {
        var configured = config["Admin:Key"];
        if (!string.IsNullOrWhiteSpace(configured) && configured != "wxadmin-dev-key") return configured;

        try
        {
            if (File.Exists(KeyPath))
            {
                // 自愈读：文件存在但读不到（历史遗留空 DACL）时先修 ACL 再读。
                // 绝不能静默换新密钥 —— 那会让 WxAdmin/GUI 拿着旧 key 恒定 401。
                if (SensitiveFile.TryReadAllText(KeyPath, out var saved) && saved.Trim().Length >= 32)
                    return saved.Trim();

                Console.Error.WriteLine(
                    $"[AdminKey] 警告：{KeyPath} 存在但无法读取，将生成新管理密钥。" +
                    "本机 WxAdmin/GUI 读的是同一个文件，密钥不一致会导致管理端全部 401。" +
                    "请按上面的提示修复该文件权限后重启。");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AdminKey] 读取 {KeyPath} 失败：{ex.GetType().Name}: {ex.Message}，将生成新密钥。");
        }

        var key = RandomToken();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(KeyPath)!);
            File.WriteAllText(KeyPath, key);
            SensitiveFile.RestrictToOwner(KeyPath);
        }
        catch
        {
            // 只读环境：仅内存生效，重启后会变（GUI 会显示当前值）
        }
        return key;
    }

    /// <summary>重新生成并持久化（GUI「重置管理密钥」用）。</summary>
    public static string Regenerate ()
    {
        var key = RandomToken();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(KeyPath)!);
            File.WriteAllText(KeyPath, key);
            SensitiveFile.RestrictToOwner(KeyPath);
        }
        catch { /* 只读环境忽略 */ }
        return key;
    }

    private static string RandomToken () =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
}