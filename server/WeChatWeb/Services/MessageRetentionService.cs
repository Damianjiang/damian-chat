using WeChatWeb.Data;

namespace WeChatWeb.Services;

/// <summary>
/// 聊天记录保留期后台任务：服务端保留 1 周聊天记录。
///
/// 与 <see cref="FileCleanupService"/>（按保留期清过期文件）不同，本服务只清「消息」，
/// 绝不动文件：图片/视频等附件仍由 FileCleanupService 按文件保留期管理，
/// 文件默认保留期与消息保留期同步为 7 天（GUI 可改），避免消息在但图/语音先被删导致裂图。
///
/// 节奏：启动 60s 后先清一次（DataStore 构造期已清过一次，这里兜底热数据），
/// 之后每 30 分钟清一次。每次清理若 0 条过期 → 零开销快路径直接返回。
/// </summary>
public sealed class MessageRetentionService : BackgroundService
{
    private readonly DataStore _data;
    private readonly ILogger<MessageRetentionService> _log;

    public MessageRetentionService (DataStore data, ILogger<MessageRetentionService> log)
    {
        _data = data;
        _log = log;
    }

    protected override async Task ExecuteAsync (CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
        catch (OperationCanceledException) { return; } // 正常退出

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var removed = _data.PruneOldMessages(DataStore.RetentionMessages);
                if (removed > 0)
                    _log.LogInformation("聊天记录保留期清理：删除 {Count} 条超过 1 周的消息", removed);
            }
            catch (Exception ex) { _log.LogWarning(ex, "聊天记录清理失败"); }

            // 瞬态保养：过期刷新令牌 + 已终结超 3 天的好友申请（此前只在撞到刷新端点时
            // 顺带清令牌，长期不刷新的用户其过期令牌永久滞留内存）
            try
            {
                var (tokens, requests) = _data.PruneTransient();
                if (tokens > 0 || requests > 0)
                    _log.LogInformation("瞬态保养：清掉 {Tokens} 个过期刷新令牌、{Requests} 条历史好友申请", tokens, requests);
            }
            catch (Exception ex) { _log.LogWarning(ex, "瞬态保养失败"); }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }
    }
}
