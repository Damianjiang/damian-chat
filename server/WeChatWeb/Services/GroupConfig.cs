using System.Text.Json;

namespace WeChatWeb.Services;

/// <summary>
/// 群聊限制配置（落盘 data/group-config.json，管理端 GUI 可改，运行中即时生效）。
/// </summary>
public sealed class GroupConfig
{
    /// <summary>单群最大人数（含群主，默认 100）。</summary>
    public int MaxMembers { get; set; } = 100;

    /// <summary>单群最大管理员数（不含群主，默认 5）。</summary>
    public int MaxAdmins { get; set; } = 5;

    /// <summary>建群时可选成员上限（防一次拉 500 人，默认 99）。</summary>
    public int MaxSelectOnCreate { get; set; } = 99;

    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static string FilePath =>
        Path.Combine(Directory.GetCurrentDirectory(), "data", "group-config.json");

    private readonly object _gate = new();

    public GroupConfig Clone ()
    {
        lock (_gate)
            return new GroupConfig { MaxMembers = MaxMembers, MaxAdmins = MaxAdmins, MaxSelectOnCreate = MaxSelectOnCreate };
    }

    public void Apply (int? maxMembers, int? maxAdmins, int? maxSelect)
    {
        lock (_gate)
        {
            if (maxMembers is > 0) MaxMembers = Math.Clamp(maxMembers.Value, 2, 1000);
            // 管理员数不能超过成员上限
            if (maxAdmins is > 0) MaxAdmins = Math.Clamp(maxAdmins.Value, 0, Math.Max(0, MaxMembers - 1));
            if (maxSelect is > 0) MaxSelectOnCreate = Math.Clamp(maxSelect.Value, 2, Math.Max(2, MaxMembers - 1));
        }
        Save();
    }

    public void Save ()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Clone(), Opts));
        }
        catch { /* 只读环境：仅内存生效 */ }
    }

    public static GroupConfig Load ()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                if (!SensitiveFile.TryReadAllText(FilePath, out var raw))
                    Console.Error.WriteLine($"[GroupConfig] 警告：无法读取 {FilePath}，使用默认群配置。");
                else
                {
                    var c = JsonSerializer.Deserialize<GroupConfig>(raw);
                    if (c != null) return c;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[GroupConfig] {FilePath} 解析失败，使用默认群配置：{ex.GetType().Name}: {ex.Message}");
        }
        return new GroupConfig();
    }
}