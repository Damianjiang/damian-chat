using System.Security.Cryptography;

namespace WeChatWeb.Services;

/// <summary>PBKDF2-SHA256 密码哈希（10 万次迭代）。存储格式：PBKDF2$迭代$salt$hash。</summary>
public static class PasswordHasher
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const string Prefix = "PBKDF2$";

    public static string Hash (string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Prefix}{Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public static bool Verify (string password, string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
            return false; // 明文历史数据一律视为无效（种子数据在建库时即哈希）
        var parts = stored.Split('$');
        if (parts.Length != 4) return false;
        if (!int.TryParse(parts[1], out var iter) || iter is < 10_000 or > 1_000_000) return false;
        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iter, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
