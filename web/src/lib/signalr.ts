import type { Message } from '@/types'
/**
 * lib/signalr.ts
 *
 * SignalR client：自动重连 + 首次连接退避重试 + 离线 catch-up 钩子。
 * 此前首次 start 失败（如服务重启窗口）会永久掉线且 started 标志不回滚，
 * 用户只能刷新页面 —— 现在失败后按 1s/3s/10s/30s/60s 退避重试，
 * 自动重连耗尽触发 onclose 时也兜底重启。
 */
import { type HubConnection, HubConnectionBuilder } from '@microsoft/signalr'
import { getAccessToken } from './api'

export interface ChatHubEvents {
  onNewMessage: (message: Message) => void
  onMarkRead: (conversationId: string, userId: string) => void
  onPresence: (userId: string, online: boolean) => void
  onCatchup: () => void
  /** 群公告被成员确认（含确认者、确认进度）。conversationId = 群 id。 */
  onNoticeConfirmed: (
    groupId: string,
    payload: { userId: string, nickname: string, confirmedAt: string, confirmed: number, total: number, owner: string },
  ) => void
  /** 某条消息被删除（多端同步：其它端移除该条 + 清本地缓存，防"刷新回来"）。 */
  onMessageDeleted: (conversationId: string, messageId: string) => void
  /** 某会话被清空（多端同步：其它端清掉该会话全部消息 + 清本地缓存）。 */
  onMessagesCleared: (conversationId: string) => void
  /** 群公告被修改/新增：版本提升，旧确认失效，在线成员需重新展示并再确认。 */
  onNoticeUpdated: (groupId: string, payload: { version: string, editor: string }) => void
  /** 对方正在输入（本人不在发送成员内；at 为服务端时间戳） */
  onTyping?: (conversationId: string, userId: string, at: string) => void
}

let connection: HubConnection | null = null
let stoppedByUser = false
let retryTimer: ReturnType<typeof setTimeout> | null = null

/** 退避序列（毫秒），封顶 60s */
const RETRY_DELAYS = [1000, 3000, 10_000, 30_000, 60_000]

function build (events: ChatHubEvents): HubConnection {
  const conn = new HubConnectionBuilder()
    .withUrl('/hubs/chat', {
      accessTokenFactory: () => getAccessToken(),
    })
    .withAutomaticReconnect([0, 1000, 3000, 10_000, 30_000])
    .build()

  conn.on('NewMessage', m => events.onNewMessage(m as Message))
  conn.on('MarkRead', (conversationId, userId) => events.onMarkRead(conversationId, userId))
  conn.on('Presence', (userId, online) => events.onPresence(userId, online))
  conn.on('NoticeConfirmed', (groupId, payload) => events.onNoticeConfirmed(groupId, payload))
  conn.on('MessageDeleted', (conversationId, messageId) => events.onMessageDeleted(conversationId, messageId))
  conn.on('MessagesCleared', conversationId => events.onMessagesCleared(conversationId))
  conn.on('NoticeUpdated', (groupId, payload) => events.onNoticeUpdated(groupId, payload))
  conn.on('Typing', (conversationId, userId, at) => events.onTyping?.(conversationId, userId, at))

  // 自动重连（普通闪断）成功恢复后，同样要补偿离线窗口内错过的消息：
  // 此前只有「手动 start 重试」路径会回调 onCatchup，自动重连路径从不补偿，
  // 用户停页面期间断线几秒 → 新消息静默丢失（未读不涨），只能靠切窗口兜底。
  conn.onreconnected(() => {
    if (connection === conn) events.onCatchup()
  })

  // 自动重连耗尽后 onclose 触发 —— 手动兜底重启（保持会话不断线）
  conn.onclose(error => {
    if (stoppedByUser || connection !== conn) {
      return
    }
    console.warn('[signalr] connection closed, will restart', error)
    void startWithRetry(conn, events, 0)
  })

  return conn
}

/** 带退避的启动重试；主动停止或连接已被替换时静默放弃。 */
async function startWithRetry (conn: HubConnection, events: ChatHubEvents, attempt: number): Promise<void> {
  try {
    await conn.start()
    // 成功恢复后补偿离线期间的消息
    events.onCatchup()
  } catch (error: unknown) {
    if (stoppedByUser || connection !== conn) {
      return
    }
    const delay = RETRY_DELAYS[Math.min(attempt, RETRY_DELAYS.length - 1)]
    console.warn(`[signalr] start failed (attempt ${attempt + 1}), retry in ${delay}ms`, error)
    await new Promise<void>(resolve => {
      retryTimer = setTimeout(resolve, delay)
    })
    if (stoppedByUser || connection !== conn) {
      return
    }
    return startWithRetry(conn, events, attempt + 1)
  }
}

export async function createChatConnection (events: ChatHubEvents): Promise<HubConnection> {
  stoppedByUser = false
  if (retryTimer) {
    clearTimeout(retryTimer)
    retryTimer = null
  }
  if (connection) {
    await connection.stop().catch(() => {})
  }
  const conn = build(events)
  connection = conn
  await startWithRetry(conn, events, 0)
  return conn
}

export function stopChatConnection () {
  stoppedByUser = true
  if (retryTimer) {
    clearTimeout(retryTimer)
    retryTimer = null
  }
  connection?.stop().catch(() => {})
  connection = null
}

/** 「对方正在输入」上行（连接未就绪时静默丢弃；调用方按 3s 节流） */
export async function sendTyping (conversationId: string) {
  const conn = connection
  if (!conn || conn.state !== 'Connected') return
  await conn.invoke('SendTyping', conversationId).catch(() => {})
}
