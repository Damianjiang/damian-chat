using WeChatWeb.Data;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace WeChatWeb.Services;

/// <summary>
/// 文件存储配置（落盘到 data/files.json，可由管理端 GUI 修改，重启/运行中即时生效）。
/// </summary>
public sealed class FileStorageConfig
{
    /// <summary>单文件最大字节（默认 1GB）。</summary>
    public long MaxFileBytes { get; set; } = 1L * 1024 * 1024 * 1024;

    /// <summary>上传限速：字节/秒（默认 10MB/s，防占满带宽）。</summary>
    public long MaxUploadBytesPerSecond { get; set; } = 10L * 1024 * 1024;

    /// <summary>文件保留天数（默认 7 天，与聊天记录保留期对齐），后台定时清理过期文件。</summary>
    public int KeepDays { get; set; } = 7;

    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static string FilePath =>
        Path.Combine(Directory.GetCurrentDirectory(), "data", "files-config.json");

    private readonly object _gate = new();

    public FileStorageConfig Clone ()
    {
        lock (_gate)
            return new FileStorageConfig
            {
                MaxFileBytes = MaxFileBytes,
                MaxUploadBytesPerSecond = MaxUploadBytesPerSecond,
                KeepDays = KeepDays,
            };
    }

    public void Apply (long? maxBytes, long? bytesPerSecond, int? keepDays)
    {
        lock (_gate)
        {
            // 单文件上限：最小 1MB，最大 1GB（与 Kestrel 请求体上限对齐，超过 1GB 会被服务器直接拒）
            if (maxBytes is > 0) MaxFileBytes = Math.Clamp(maxBytes.Value, 1L * 1024 * 1024, 1L * 1024 * 1024 * 1024);
            if (bytesPerSecond is > 0) MaxUploadBytesPerSecond = Math.Clamp(bytesPerSecond.Value, 64 * 1024, 200L * 1024 * 1024);
            if (keepDays is >= 1) KeepDays = Math.Clamp(keepDays.Value, 1, 365);
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

    public static FileStorageConfig Load ()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                if (!SensitiveFile.TryReadAllText(FilePath, out var raw))
                    Console.Error.WriteLine($"[FileStore] 警告：无法读取 {FilePath}，使用默认存储配置。");
                else
                {
                    var c = JsonSerializer.Deserialize<FileStorageConfig>(raw);
                    if (c != null) return c;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FileStore] {FilePath} 解析失败，使用默认存储配置：{ex.GetType().Name}: {ex.Message}");
        }
        return new FileStorageConfig();
    }
}

/// <summary>
/// 文件存储服务：
/// ① 内容 SHA-256 去重 —— 同一文件重复上传/转发只占一份磁盘（解决"重复存储炸存储"）；
/// ② 定时清理过期文件（默认 5 天），同时清掉不再被任何消息引用的元数据；
/// ③ 落盘元数据（data/files.json），重启后仍可下载。
/// </summary>
public sealed class FileStore
{
    private readonly DataStore _data;
    private readonly ILogger<FileStore> _log;
    // 文件元数据落盘走紧凑 JSON（此前 WriteIndented 缩进慢数倍且文件更大）
    private static readonly JsonSerializerOptions Opts = new();
    /// <summary>内容哈希 → 文件元数据 索引：上传去重 O(1) 判定（此前每次上传在全局锁内全表线性扫）。</summary>
    private readonly Dictionary<string, Models.FileMeta> _hashIndex = new(StringComparer.Ordinal);

    public FileStore (DataStore data, ILogger<FileStore> log)
    {
        _data = data;
        _log = log;
        RestoreIndex();
        lock (data)
        {
            foreach (var f in data.Files.Values)
                if (!string.IsNullOrEmpty(f.Hash)) _hashIndex[f.Hash] = f;
        }
    }

    /// <summary>当前配置（由管理 API 修改后即时生效）。</summary>
    public FileStorageConfig Config { get; private set; } = FileStorageConfig.Load();

    public void UpdateConfig (long? maxBytes, long? bytesPerSecond, int? keepDays)
    {
        Config.Apply(maxBytes, bytesPerSecond, keepDays);
        _log.LogInformation("文件存储配置已更新：单文件上限 {Max}，限速 {Speed}/s，保留 {Days} 天",
            Config.MaxFileBytes, Config.MaxUploadBytesPerSecond, Config.KeepDays);
    }

    public string Dir => Path.Combine(Directory.GetCurrentDirectory(), "data", "files");

    private string IndexPath => Path.Combine(Directory.GetCurrentDirectory(), "data", "files.json");

    /* ---------------- 去重写入 ---------------- */

    /// <summary>
    /// 写入文件：同内容（同 SHA-256）直接复用已有 id，不重复占磁盘。
    /// 返回 (id, 是否命中去重)。
    /// </summary>
    /// <summary>随机文件 ID：24 字节密码学随机数，外部无法由内容或顺序推算。</summary>
    private static string NewFileId ()
    {
        return Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16))
            .ToLowerInvariant();
    }

    public async Task<(string Id, bool Deduplicated)> SaveAsync (Stream source, string ext, string uploaderId, string originalName, CancellationToken ct)
    {
        Directory.CreateDirectory(Dir);
        var temp = Path.Combine(Dir, Guid.NewGuid().ToString("N") + ".tmp");

        try
        {
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                await using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
                {
                    var buffer = new byte[64 * 1024];
                    int read;
                    while ((read = await source.ReadAsync(buffer, ct)) > 0)
                    {
                        hash.AppendData(buffer, 0, read);
                        await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                    }
                }
                var sha = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();

                // 命中去重：已有同内容文件 → 删临时文件，复用（O(1) 走 hash→id 索引）
                lock (_data)
                {
                    if (_hashIndex.TryGetValue(sha, out var existing)
                        && existing != null && _data.Files.ContainsKey(existing.Id)
                        && System.IO.File.Exists(existing.Path))
                    {
                        TryDelete(temp);
                        return (existing.Id, true);
                    }
                }

                // ID 不再用内容哈希（哈希可被反推/枚举），改为随机不可枚举 ID。
                // 去重仍按 SHA-256 命中已有文件，不影响「同内容只存一份」。
                var id = NewFileId();
                var final = Path.Combine(Dir, id + ext);
                // overwrite:true：并发上传同一内容时两个临时文件都想落到同一个最终名，
                // 不覆盖会直接 500（此前可被并发同内容上传触发）；内容相同覆盖无差别。
                System.IO.File.Move(temp, final, overwrite: true);

                lock (_data)
                {
                    var meta = new Models.FileMeta
                    {
                        Id = id,
                        FileName = originalName,
                        Size = new FileInfo(final).Length,
                        // 服务端权威 MIME：绝不采用客户端声明的 Content-Type（可伪造 text/html 等）
                        ContentType = FileContent.ContentTypeFor(ext),
                        Path = final,
                        Hash = sha,
                        UploaderId = uploaderId,
                        CreatedAt = DateTime.UtcNow,
                    };
                    _data.Files[id] = meta;
                    _hashIndex[sha] = meta;
                }
                PersistIndex();
                return (id, false);
            }
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /* ---------------- 定时清理 ---------------- */

    /// <summary>清理超过保留期的文件（后台服务每小时跑一次）。</summary>
    public int CleanupExpired ()
    {
        var days = Config.KeepDays;
        var cutoff = DateTime.UtcNow.AddDays(-days);
        var removed = 0;

        List<string> orphans;
        List<string> toDelete;
        lock (_data)
        {
            var expired = _data.Files.Values.Where(f => f.CreatedAt < cutoff).ToList();
            // 仍被引用的文件不删：聊天消息（图片/语音/文件附件）、朋友圈配图、收藏封面、
            // 以及任何用户头像/资料图 —— 只查消息会删掉头像与朋友圈图（用户文件"无故消失"）
            var referenced = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in _data.Messages)
                if (!string.IsNullOrEmpty(m.ImageId)) referenced.Add(m.ImageId!);
            foreach (var mo in _data.Moments)
                foreach (var img in mo.Images)
                    if (!string.IsNullOrEmpty(img)) referenced.Add(img);
            foreach (var f in _data.Favorites)
                if (!string.IsNullOrEmpty(f.ImageUrl)) referenced.Add(f.ImageUrl);
            foreach (var u in _data.Users.Values)
                if (!string.IsNullOrEmpty(u.Avatar)) referenced.Add(u.Avatar);

            toDelete = new List<string>();
            foreach (var f in expired)
            {
                if (referenced.Contains(f.Id)) continue;
                _data.Files.Remove(f.Id);
                // 哈希索引同步剔除（下次同内容上传会重新入列）
                if (!string.IsNullOrEmpty(f.Hash) && _hashIndex.TryGetValue(f.Hash, out var h) && ReferenceEquals(h, f))
                    _hashIndex.Remove(f.Hash);
                toDelete.Add(f.Path);
                removed++;
            }

            // 顺带清掉磁盘上的孤儿文件（元数据已无记录）
            orphans = new();
            try
            {
                foreach (var path in Directory.EnumerateFiles(Dir))
                {
                    var id = Path.GetFileNameWithoutExtension(path);
                    if (id.EndsWith(".tmp", StringComparison.Ordinal) || id.Length != 32) continue;
                    if (!_data.Files.ContainsKey(id)) orphans.Add(path);
                }
            }
            catch { /* 目录不存在 */ }
        }

        // 文件 IO 移到锁外：删除可能耗时，不能拖住全站消息锁
        foreach (var p in toDelete) TryDelete(p);
        foreach (var p in orphans) TryDelete(p);
        PersistIndex();
        if (removed > 0) _log.LogInformation("文件清理：删除 {Count} 个超过 {Days} 天的文件", removed, days);
        return removed;
    }

    private static void TryDelete (string path)
    {
        try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); } catch { /* 占用中下次再清 */ }
    }

    /* ---------------- 元数据落盘 ---------------- */

    public void PersistIndex ()
    {
        try
        {
            // 锁内只做浅快照：全量序列化 + 写盘在全局锁外（此前每次上传都让聊天业务锁
            // 等待整份 files.json 序列化 + 磁盘写，上传高峰期会拖慢所有请求）
            List<Models.FileMeta> snapshot;
            lock (_data) snapshot = _data.Files.Values.ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(IndexPath)!);
            // 原子替换（tmp + Move）：直接 WriteAllText 中途崩溃会留下半个 files.json，
            // 重启后全部文件元数据丢失、文件变成孤儿
            var tmp = IndexPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(snapshot, Opts));
            File.Move(tmp, IndexPath, overwrite: true);
        }
        catch { /* 只读环境忽略 */ }
    }

    private void RestoreIndex ()
    {
        try
        {
            if (!File.Exists(IndexPath)) return;
            // 自愈读：历史遗留的空 DACL 会让这里静默返回空索引 → 所有上传文件
            // 「重启后无法下载」。先修 ACL 再读，读不到就显式告警。
            if (!SensitiveFile.TryReadAllText(IndexPath, out var raw))
            {
                Console.Error.WriteLine(
                    $"[FileStore] 警告：无法读取 {IndexPath}，本次启动文件索引为空，" +
                    "重启前上传的文件将无法下载。请修复该文件权限后重启。");
                return;
            }
            var list = JsonSerializer.Deserialize<List<Models.FileMeta>>(raw);
            if (list == null) return;
            lock (_data)
            {
                foreach (var f in list.Where(f => !string.IsNullOrEmpty(f.Id)))
                    _data.Files[f.Id] = f;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FileStore] {IndexPath} 解析失败，文件索引为空：{ex.GetType().Name}: {ex.Message}");
        }
    }
}

/// <summary>后台定时任务：每小时清理一次过期文件。</summary>
public sealed class FileCleanupService : BackgroundService
{
    private readonly FileStore _files;
    private readonly ILogger<FileCleanupService> _log;

    public FileCleanupService (FileStore files, ILogger<FileCleanupService> log)
    {
        _files = files;
        _log = log;
    }

    protected override async Task ExecuteAsync (CancellationToken stoppingToken)
    {
        // 启动 30 秒后先清一次，之后每小时一次
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            var n = _files.CleanupExpired();
            if (n > 0) _log.LogInformation("启动清理：删除 {Count} 个过期文件", n);
        }
        catch (OperationCanceledException) { /* 正常退出 */ }
        catch (Exception ex) { _log.LogWarning(ex, "文件清理失败"); }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { _files.CleanupExpired(); }
            catch (Exception ex) { _log.LogWarning(ex, "文件清理失败"); }
        }
    }
}