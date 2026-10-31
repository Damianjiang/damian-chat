/**
 * lib/realtime.ts
 *
 * 将 SignalR 事件接入 store；登录后调用 initRealtime，登出调用 stopRealtime。
 */
import { createChatConnection, stopChatConnection } from '@/lib/signalr'
import { useAuthStore } from '@/stores/auth'
import { useConversationsStore } from '@/stores/conversations'
import { usePollsStore } from '@/stores/polls'
let started = false

/** 群公告「已确认」进度（groupId → { 确认者名/时间, 已确认数, 总数 }），挂到 store 让群聊页即时更新。 */
type NoticeConfirmedPayload = {
  userId: string
  nickname: string
  confirmedAt: string
  confirmed: number
  total: number
  owner: string
}

export function initRealtime () {
  if (started) {
    return
  }
  started = true
  const store = useConversationsStore()

  createChatConnection({
    onNewMessage: m => store.applyIncoming(m),
    // MarkRead 只在「我自己读了」（手机读了，电脑端同步清红点）时才清本地红点。
    // 此前忽略 userId，对方一读会话，我这里的红点也被清掉 —— 跨用户污染。
    onMarkRead: (conversationId, userId) => {
      if (userId && userId === useAuthStore().user?.id) {
        store.markRead(conversationId)
      }
    },
    onPresence: (userId, online) => store.setPresence(userId, online),
    // 删除/清空的多端同步：服务端广播后，其它端内存 + 本地 IDB 缓存立即移除，
    // 否则被删消息会从 IDB 回填「刷回来」（切走再切回 / 刷新 / 断线重连场景）。
    onMessageDeleted: (conversationId, messageId) => store.removeMessage(conversationId, messageId),
    onMessagesCleared: conversationId => store.clearConversationLocal(conversationId),
    onNoticeConfirmed: (groupId, payload: NoticeConfirmedPayload) => {
      // 让打开该群的聊天页即时看到「已确认 x/y」与最新确认者
      store.applyNoticeConfirmed(groupId, payload)
    },
    onNoticeUpdated: (groupId, _payload) => {
      // 群公告被改/新增 → 旧确认失效，清快照 + 版本号 +1，聊天页据此重新展示并需再确认
      store.applyNoticeUpdated(groupId)
    },
    onTyping: (conversationId, userId) => {
      // 「对方正在输入」：只认会话成员的信号，挂到 store 供聊天页显示提示气泡
      store.applyTyping(conversationId, userId, useAuthStore().user?.id ?? '')
    },
    onPollUpdated: (conversationId, poll) => {
      // 群投票有变化（他人投票/截止）：打开的投票卡片实时刷新
      usePollsStore().applyUpdated(conversationId, poll)
    },
    onCatchup: async () => {
      // 离线消息补偿：重连后拉取会话列表与当前打开会话的历史
      await store.loadSessions()
      if (store.activeId) {
        await store.loadHistory(store.activeId)
      }
    },
  }).catch(() => {
    // 建连失败（内部已带退避重试，重试期间 promise 未决）：
    // 只有彻底放弃才走到这里 —— 回滚标志，允许下次登录/路由触发重连
    started = false
  })
}

export function stopRealtime () {
  stopChatConnection()
  started = false
}
