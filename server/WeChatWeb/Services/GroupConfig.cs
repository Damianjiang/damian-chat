using System.Text.Json;

namespace WeChatWeb.Services;

/// <summary>
/// 群聊限制配置（落盘 Data/config/group-config.json，管理端 GUI 可改，运行中即时生效）。
///
/// ★ 路径与安全配置一起搬出源码目录：部署覆盖源码时不会再把这些限制值盖回默认值。
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

    public static string FilePath => ConfigPaths.File("group-config.json");

    /// <summary>旧位置（Data/group-config.json）：仅用于首次启动迁移。</summary>
    private static string LegacyFilePath => Path.Combine(Directory.GetCurrentDirectory(), "data", "group-config.json");

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
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            lock (_gate)
            {
                var snapshot = new GroupConfig { MaxMembers = MaxMembers, MaxAdmins = MaxAdmins, MaxSelectOnCreate = MaxSelectOnCreate };
                File.WriteAllText(FilePath, JsonSerializer.Serialize(snapshot, Opts));
            }
            SensitiveFile.RestrictToOwner(FilePath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[配置] 写入 {FilePath} 失败：{ex.GetType().Name}: {ex.Message}（仅内存生效）");
        }
    }

    public static GroupConfig Load ()
    {
        ConfigPaths.MigrateIfMissing(FilePath, LegacyFilePath);
        try
        {
            if (File.Exists(FilePath))
            {
                if (!SensitiveFile.TryReadAllText(FilePath, out var raw))
                    Console.Error.WriteLine($"[GroupConfig] 警告：无法读取 {FilePath}，使用默认群配置。");
                else
                {
                    var c = JsonSerializer.Deserialize<GroupConfig>(raw);
                    if (c != null) return c.Normalized();
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[GroupConfig] {FilePath} 解析失败，使用默认群配置：{ex.GetType().Name}: {ex.Message}");
        }
        return new GroupConfig();
    }

    /// <summary>从磁盘重新读取并**原地**更新（群限制热加载）。返回 true = 已应用。</summary>
    public bool Reload ()
    {
        try
        {
            if (!File.Exists(FilePath)) return false;
            if (!SensitiveFile.TryReadAllText(FilePath, out var raw)) return false;
            var c = JsonSerializer.Deserialize<GroupConfig>(raw);
            if (c == null) return false;
            c = c.Normalized();
            lock (_gate)
            {
                MaxMembers = c.MaxMembers;
                MaxAdmins = c.MaxAdmins;
                MaxSelectOnCreate = c.MaxSelectOnCreate;
            }
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[GroupConfig] 热加载失败（保留当前值）：{ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private GroupConfig Normalized ()
    {
        MaxMembers = Math.Clamp(MaxMembers, 2, 1000);
        MaxAdmins = Math.Clamp(MaxAdmins, 0, Math.Max(0, MaxMembers - 1));
        MaxSelectOnCreate = Math.Clamp(MaxSelectOnCreate, 2, Math.Max(2, MaxMembers - 1));
        return this;
    }
}
