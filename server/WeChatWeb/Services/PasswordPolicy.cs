namespace WeChatWeb.Services;

/// <summary>
/// 密码强度策略。原先只校验长度（8-32 位），"12345678" 这类弱口令可以直接注册/创建，
/// 属于典型的弱口令缺口。这里要求「字母 + 数字」组合，并拉黑最常见的弱口令。
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 32;

    /// <summary>常见弱口令（忽略大小写）。</summary>
    private static readonly HashSet<string> Weak = new(StringComparer.OrdinalIgnoreCase)
    {
        "12345678", "123456789", "1234567890", "11111111", "00000000",
        "password", "password1", "passw0rd", "admin123", "administrator",
        "qwerty123", "abc12345", "a1234567", "asd12345", "1234567a",
        "88888888", "66666666", "00000001", "test1234", "user1234",
    };

    /// <summary>校验密码；返回 null 表示通过，否则返回给用户看的原因。</summary>
    public static string? Validate (string? password)
    {
        if (string.IsNullOrEmpty(password)) return "请输入密码";
        if (password.Length < MinLength) return $"密码至少 {MinLength} 位";
        if (password.Length > MaxLength) return $"密码最多 {MaxLength} 位";

        var hasLetter = false;
        var hasDigit = false;
        var hasOther = false;
        foreach (var c in password)
        {
            if (char.IsLetter(c)) hasLetter = true;
            else if (char.IsDigit(c)) hasDigit = true;
            else hasOther = true;
        }

        // 至少两类字符（字母/数字/符号任选两类）
        var kinds = (hasLetter ? 1 : 0) + (hasDigit ? 1 : 0) + (hasOther ? 1 : 0);
        if (kinds < 2) return "密码需包含字母与数字两类字符";

        if (Weak.Contains(password)) return "该密码过于简单，请换一个";

        // 纯重复字符
        if (password.Distinct().Count() <= 2) return "密码不能是重复字符";

        return null;
    }

    /// <summary>强度评分 0-4，用于后台展示。</summary>
    public static int Strength (string? password)
    {
        if (string.IsNullOrEmpty(password)) return 0;
        var score = 0;
        if (password.Length >= 8) score++;
        if (password.Length >= 12) score++;
        if (password.Any(char.IsLetter) && password.Any(char.IsDigit)) score++;
        if (password.Any(c => !char.IsLetterOrDigit(c))) score++;
        if (Weak.Contains(password)) score = Math.Min(score, 1);
        return Math.Clamp(score, 0, 4);
    }
}