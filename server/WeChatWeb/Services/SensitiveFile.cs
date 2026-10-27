using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace WeChatWeb.Services;

/// <summary>
/// 敏感文件/目录落盘权限收紧（best-effort，失败只告警、绝不留空 DACL）。
///
/// ★ 历史坑（2026-10-15 修复）：
///   旧 Windows 实现是 `icacls "&lt;dir&gt;" /t /grant:r "&lt;user&gt;:(OI)(CI)(R,W,D)"`。
///   icacls 对**目录**返回 exit=0、看起来成功，但 `/t` 递归到**文件**时
///   `(OI)(CI)` 继承标志对文件非法 —— 结果是每个文件的 DACL 被**清空**：
///     data/admin.key   读不到 → 管理端每次重启换新密钥 → 控制台/GUI 恒定 401
///     data/jwt.secret  读不到 → JWT 密钥每次重启重新生成 → 全员被登出
///     data/store.json  读不到 → DataStore.Load 静默失败 → 服务带空数据运行
///     logs/*.jsonl     读不到 → 管理端日志页永远空白
///   且 RestrictToOwner 用的 `/inheritance:reset` 根本不是合法 icacls 选项
///   （exit=87），而 Run() 又忽略退出码，等于从来没生效过。
///
///   现改为 .NET ACL API：
///   1) 只用「授权」语义（AddAccessRule），绝不清空已有 DACL；
///   2) 对目录带 (OI)(CI)，让后续新建文件自动继承，无需逐个追调；
///   3) 每一步都有 try/catch + 告警，失败不会把文件锁死。
/// </summary>
public static class SensitiveFile
{
    /* ==================== 目录 ==================== */

    /// <summary>
    /// 把目录收紧为「仅当前账户」可读写，并**保留 (OI)(CI) 继承**：
    /// 之后新建/移入的文件、子目录自动继承同一权限，不必逐个落盘再追调。
    /// 对目录内**已存在**的子项只做「补授」，不清空原 ACL。
    /// </summary>
    public static void RestrictDirToOwner (string dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

        if (OperatingSystem.IsWindows())
        {
            try
            {
                var me = WindowsAcl.CurrentUser();
                // 1) 目录自身：只留当前账户，带对象/容器继承
                var ds = new DirectorySecurity();
                ds.SetAccessRuleProtection(true, false);
                ds.AddAccessRule(new FileSystemAccessRule(
                    me, WindowsAcl.OwnerRights,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));
                new DirectoryInfo(dir).SetAccessControl(ds);

                // 2) 已存在的子项逐个补授（历史被清空 DACL 的文件在这里被救回来）
                WindowsAcl.RepairTree(dir, me);
            }
            catch (Exception ex)
            {
                Warn(nameof(RestrictDirToOwner), dir, ex);
            }
        }
        else
        {
            // Unix：目录 700，后续新建文件 600 由各写入点补
            Run("chmod", $"700 \"{dir}\"");
        }
    }

    /// <summary>
    /// 启动/每次落盘后调用：把「敏感目录」一次性锁死。
    /// 目录清单与收紧策略固定，业务代码不用每个 SaveNow 都调 icacls（性能 + 简化）。
    /// </summary>
    public static void RestrictSensitiveDirs (params string[] dirs)
    {
        foreach (var d in dirs)
        {
            if (string.IsNullOrEmpty(d)) continue;
            try { Directory.CreateDirectory(d); } catch { /* 不存在忽略 */ }
            RestrictDirToOwner(d);
        }
    }

    /* ==================== 文件 ==================== */

    /// <summary>把文件收紧为「仅当前账户」可读可写。只授权，不清空。</summary>
    public static void RestrictToOwner (string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        if (OperatingSystem.IsWindows())
        {
            try
            {
                var fs = new FileSecurity();
                fs.SetAccessRuleProtection(true, false);
                fs.AddAccessRule(new FileSystemAccessRule(
                    WindowsAcl.CurrentUser(), WindowsAcl.OwnerRights, AccessControlType.Allow));
                new FileInfo(path).SetAccessControl(fs);
            }
            catch (Exception ex)
            {
                Warn(nameof(RestrictToOwner), path, ex);
            }
        }
        else
        {
            Run("chmod", $"600 \"{path}\"");
        }
    }

    /// <summary>
    /// 读前自愈：文件存在但读不了（历史遗留的空 DACL）时，先尝试修 ACL 再读一次。
    /// 返回 false 表示「确实读不到」，调用方必须如实告警，不许静默降级。
    /// </summary>
    public static bool TryReadAllText (string path, out string content)
    {
        content = "";
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;

        try
        {
            content = File.ReadAllText(path);
            return true;
        }
        catch
        {
            // 大概率是历史遗留的「DACL 被清空」。能修就修，修不了如实失败。
            try
            {
                RestrictToOwner(path);
                content = File.ReadAllText(path);
                return true;
            }
            catch (Exception ex)
            {
                Warn(nameof(TryReadAllText), path, ex);
                return false;
            }
        }
    }

    /* ==================== 告警 ==================== */

    /// <summary>
    /// 权限问题必须在控制台显眼可见 —— 这类故障的共性是「看起来正常，实际全废」。
    /// </summary>
    internal static void Warn (string op, string path, Exception ex)
    {
        Console.Error.WriteLine(
            $"[敏感文件权限] {op} 失败：{path} — {ex.GetType().Name}: {ex.Message}");
        Console.Error.WriteLine(
            "[敏感文件权限] 该文件可能无法被本服务读写。请以文件所有者身份执行：" +
            $"icacls \"{path}\" /reset  然后重启服务。");
    }

    private static void Run (string file, string args)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi);
        if (p != null) p.WaitForExit(3000); // 限时 3s，避免卡住
    }
}

/// <summary>
/// Windows ACL 细节：单独隔出来并标注 [SupportedOSPlatform("windows")]，
/// 避免 CA1416「此调用站点可跨平台访问」的编译告警（本项目要求 0 警告）。
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsAcl
{
    /// <summary>收紧粒度：读/写/改/删（不含「改权限」，避免把 DACL 控制权也交出去）。</summary>
    internal const FileSystemRights OwnerRights =
        FileSystemRights.Read | FileSystemRights.Write | FileSystemRights.Modify | FileSystemRights.Delete;

    /// <summary>
    /// 当前服务账户。优先用 SID（不依赖域名解析，最稳），解析不到再退回 DOMAIN\user。
    /// </summary>
    internal static IdentityReference CurrentUser ()
    {
        try
        {
            var sid = WindowsIdentity.GetCurrent().User;
            if (sid != null) return sid;
        }
        catch { /* 极少数无 WindowsIdentity 的环境，走下面兜底 */ }
        return new NTAccount(Environment.UserDomainName + "\\" + Environment.UserName);
    }

    /// <summary>递归给子项补授当前账户权限。只加不清空，失败逐个告警不中断。</summary>
    internal static void RepairTree (string dir, IdentityReference me)
    {
        var d = new DirectoryInfo(dir);

        foreach (var f in d.EnumerateFiles())
        {
            try
            {
                var fs = new FileSecurity();
                fs.SetAccessRuleProtection(true, false);
                fs.AddAccessRule(new FileSystemAccessRule(me, OwnerRights, AccessControlType.Allow));
                f.SetAccessControl(fs);
            }
            catch (Exception ex)
            {
                SensitiveFile.Warn("RepairFile", f.FullName, ex);
            }
        }

        foreach (var sub in d.EnumerateDirectories())
        {
            try
            {
                var ds = new DirectorySecurity();
                ds.SetAccessRuleProtection(true, false);
                ds.AddAccessRule(new FileSystemAccessRule(
                    me, OwnerRights,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));
                sub.SetAccessControl(ds);
            }
            catch (Exception ex)
            {
                SensitiveFile.Warn("RepairDir", sub.FullName, ex);
            }
            RepairTree(sub.FullName, me);
        }
    }
}
