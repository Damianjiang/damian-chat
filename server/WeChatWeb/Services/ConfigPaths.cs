namespace WeChatWeb.Services;

/// <summary>
/// 运行期可写配置的**唯一**落盘位置。
///
/// ★ 为什么必须从源码目录搬走（真实事故）：
///   旧版本把 security.json 放在 server/WeChatWeb/security.json（= 站点源码根）。
///   每次「部署最新版」= 把源码目录整体覆盖/重新构建 → 管理员在 GUI 里改过的注册密码
///   被源码里的旧文件（或首次启动自动生成的随机值）覆盖，表现为「下一次部署密码又变了」。
///   群限制/限速同理：值写进去了，但部署时被旧文件盖回去，于是「怎么设都没用」。
///
/// 现在的规则：
///   • 默认落在 <c>{工作目录}/Data/config/</c>（Data 是运行数据区，不进 git、不参与发布覆盖）；
///   • 可用环境变量 <c>WX_CONFIG_DIR</c> 指定到任意持久化目录（Docker 挂载卷等）；
///   • 首次启动自动从旧位置迁移一次，老部署的注册密钥/群限制不丢。
/// </summary>
public static class ConfigPaths
{
    private static readonly Lazy<string> LazyDir = new(ResolveDir);

    /// <summary>配置目录（已确保存在；创建失败时仍返回路径，写入方自行降级）。</summary>
    public static string Dir => LazyDir.Value;

    /// <summary>配置文件完整路径。</summary>
    public static string File (string name) => Path.Combine(Dir, name);

    private static string ResolveDir ()
    {
        var env = Environment.GetEnvironmentVariable("WX_CONFIG_DIR");
        var dir = string.IsNullOrWhiteSpace(env)
            ? Path.Combine(Directory.GetCurrentDirectory(), "Data", "config")
            : Path.GetFullPath(env.Trim());
        try { Directory.CreateDirectory(dir); } catch { /* 只读环境：调用方降级为内存生效 */ }
        return dir;
    }

    /// <summary>
    /// 旧位置 → 新位置的一次性迁移（目标已存在时绝不覆盖，保证管理员改过的值优先）。
    /// </summary>
    public static void MigrateIfMissing (string target, params string[] legacyPaths)
    {
        try
        {
            if (System.IO.File.Exists(target)) return;
            foreach (var legacy in legacyPaths)
            {
                if (string.IsNullOrWhiteSpace(legacy) || !System.IO.File.Exists(legacy)) continue;
                var parent = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                System.IO.File.Copy(legacy, target, overwrite: false);
                Console.WriteLine($"[配置] 已迁移 {legacy} → {target}（此后只读写新位置，部署不会再覆盖）");
                return;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[配置] 迁移失败 {target}：{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>文件指纹（长度 + 最后写入时间），配置热加载用它判断「真的变了」。</summary>
    public static (long Length, DateTime WriteUtc) Stamp (string path)
    {
        try
        {
            var fi = new FileInfo(path);
            return fi.Exists ? (fi.Length, fi.LastWriteTimeUtc) : (0, DateTime.MinValue);
        }
        catch
        {
            return (0, DateTime.MinValue);
        }
    }
}
