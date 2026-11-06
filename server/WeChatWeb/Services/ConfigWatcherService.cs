using WeChatWeb.Data;

namespace WeChatWeb.Services;

/// <summary>
/// 配置热加载：每 2 秒比对一次配置文件指纹，变了就把内容原地合并进运行实例。
///
/// 为什么用轮询而不是 FileSystemWatcher：
///   • Windows 上编辑器/杀软会触发重复事件、跨 Docker 挂载卷 inotify 常常收不到；
///   • 配置文件很小、2 秒一次 stat 的开销可以忽略，行为却完全确定。
///
/// 这样 GUI 可以「直接写文件」而不是走管理 API（用户要求），保存后 1~2 秒内自动生效：
/// 注册密码、登录/接口限速、防火墙阈值、群人数/管理员上限都不再需要重启服务。
/// </summary>
public sealed class ConfigWatcherService : BackgroundService
{
    private readonly SecuritySettings _sec;
    private readonly DataStore _store;
    private readonly FileStore _files;
    private readonly LogService _log;

    private (long Length, DateTime WriteUtc) _secStamp;
    private (long Length, DateTime WriteUtc) _groupStamp;
    private (long Length, DateTime WriteUtc) _filesStamp;

    public ConfigWatcherService (SecuritySettings sec, DataStore store, FileStore files, LogService log)
    {
        _sec = sec;
        _store = store;
        _files = files;
        _log = log;
    }

    protected override async Task ExecuteAsync (CancellationToken stoppingToken)
    {
        // 首轮只记录指纹：启动时已经 Load 过，不需要再读一遍
        _secStamp = ConfigPaths.Stamp(SecuritySettings.FilePath);
        _groupStamp = ConfigPaths.Stamp(GroupConfig.FilePath);
        _filesStamp = ConfigPaths.Stamp(FileStorageConfig.FilePath);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var secStamp = ConfigPaths.Stamp(SecuritySettings.FilePath);
                if (secStamp != _secStamp)
                {
                    _secStamp = secStamp;
                    if (_sec.Reload())
                    {
                        Console.WriteLine($"[配置] security.json 已热加载：注册密码/验证码/限速/防火墙（{SecuritySettings.FilePath}）");
                        _log.Write("system", new
                        {
                            ts = DateTime.UtcNow,
                            result = "config-hot-reload",
                            file = "security.json",
                            registerKeySet = !string.IsNullOrEmpty(_sec.RegisterKey),
                            authPerMinute = _sec.AuthPerMinute,
                            apiPerMinute = _sec.ApiPerMinute,
                            floodMessagesPerMinute = _sec.FloodMessagesPerMinute,
                        });
                    }
                }

                var groupStamp = ConfigPaths.Stamp(GroupConfig.FilePath);
                if (groupStamp != _groupStamp)
                {
                    _groupStamp = groupStamp;
                    if (_store.GroupLimits.Reload())
                    {
                        var g = _store.GroupLimits;
                        Console.WriteLine($"[配置] group-config.json 已热加载：群上限 {g.MaxMembers} 人 / 管理员 {g.MaxAdmins} / 建群可选 {g.MaxSelectOnCreate}");
                        _log.Write("system", new
                        {
                            ts = DateTime.UtcNow,
                            result = "config-hot-reload",
                            file = "group-config.json",
                            maxMembers = g.MaxMembers,
                            maxAdmins = g.MaxAdmins,
                            maxSelectOnCreate = g.MaxSelectOnCreate,
                        });
                    }
                }

                var filesStamp = ConfigPaths.Stamp(FileStorageConfig.FilePath);
                if (filesStamp != _filesStamp)
                {
                    _filesStamp = filesStamp;
                    if (_files.Config.Reload())
                    {
                        var f = _files.Config;
                        Console.WriteLine($"[配置] files-config.json 已热加载：单文件 {f.MaxFileBytes / 1024 / 1024}MB / 限速 {f.MaxUploadBytesPerSecond / 1024 / 1024}MB/s / 保留 {f.KeepDays} 天");
                        _log.Write("system", new
                        {
                            ts = DateTime.UtcNow,
                            result = "config-hot-reload",
                            file = "files-config.json",
                            maxFileMb = f.MaxFileBytes / 1024 / 1024,
                            keepDays = f.KeepDays,
                        });
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 服务停止，正常退出
        }
    }
}
