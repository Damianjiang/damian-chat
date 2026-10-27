using Microsoft.AspNetCore.SignalR;
using WeChatWeb.Data;
using WeChatWeb.Hubs;
using WeChatWeb.Models;

namespace WeChatWeb.Services;

/// <summary>
/// 生成系统提示消息（居中灰字），写入会话历史并广播。
/// </summary>
public static class SystemMessages
{
    public static async Task Post(IHubContext<ChatHub> hub, DataStore store, string conversationId, string text)
    {
        if (string.IsNullOrEmpty(conversationId)) return;
        Message msg;
        lock (store)
        {
            msg = new Message
            {
                Id = Guid.NewGuid().ToString("N"),
                ConversationId = conversationId,
                SenderId = "system",
                Type = "system",
                Content = text,
                CreatedAt = DateTime.UtcNow,
            };
            store.Messages.Add(msg);
            store.MsgIndexAdd(msg);   // 会话消息索引同步（漏掉会导致 History/未读看不到系统消息）
            store.OnNewMessage(msg);  // 系统消息同样计入未读（与服务端硬校验的语义一致）
            if (store.Conversations.TryGetValue(conversationId, out var conv))
                conv.LastAt = msg.CreatedAt;
            store.MarkDirty();        // 系统消息同样要落盘（漏掉重启即丢）
        }
        await ChatHub.BroadcastMessage(hub, store, msg);
    }
}
