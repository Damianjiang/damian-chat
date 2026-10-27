import type { Conversation, Message } from '@/types'
/**
 * stores/conversations.ts
 *
 * 会话 + 消息 + 实时（SignalR 事件在 lib/realtime.ts 中接入本 store）。
 */
import { defineStore } from 'pinia'
import { api } from '@/lib/api'
import { idbClearMessages, idbCountMessages, idbGetMessages, idbPutMessages, idbRemoveMessage } from '@/lib/idb'
import { useAuthStore } from '@/stores/auth'

/**
 * 消息本地缓存（IndexedDB）去抖写入：
 *  刷屏时同一会话短时间内多次收到/发送 → 合并为一次 IDB 写（先 delete 旧切片再批量 put）。
 *  失败静默降级（IDB 不可用不影响在线消息流）。
 */
const msgWriteTimers: Record<string, ReturnType<typeof setTimeout>> = {}
function scheduleMessagePersist (conversationId: string) {
  clearTimeout(msgWriteTimers[conversationId])
  msgWriteTimers[conversationId] = setTimeout(() => {
    delete msgWriteTimers[conversationId]
    const store = useConversationsStore()
    const list = store.messagesByConv[conversationId]
    if (list && list.length > 0) {
      // 只落库已确认的消息：pending/failed 是本地乐观气泡，
      // 写进缓存会让下次打开残留一个"永远发不出去"的幽灵气泡
      const settled = list.filter(m => !m.pending && !m.failed).slice(-500)
      if (settled.length > 0) {
        void idbPutMessages(conversationId, settled).catch(() => {})
      }
    }
  }, 400)
}

/** 清「聊天记录」缓存（设置页勾选时调用）：IDB 持久层 + 内存层一并清掉。 */
async function clearConversationCache () {
  for (const k of Object.keys(msgWriteTimers)) {
    clearTimeout(msgWriteTimers[k])
  }
  for (const k of Object.keys(msgWriteTimers)) {
    delete msgWriteTimers[k]
  }
  await idbClearMessages()
  // 内存层也清掉：否则设置页显示"已清空"但当前打开的会话消息还在内存里，
  // 用户以为没清掉（"清理缓存也有问题"的根因之一）
  try {
    const store = useConversationsStore()
    store.messagesByConv = {}
    store.historyHasMore = {}
    store.noticeConfirmed = {}
    store.typingBy = {}
  } catch { /* 未初始化 store 时忽略 */ }
}

/** 聊天记录缓存条数（设置页大小显示）。 */
async function conversationCacheSize (): Promise<number> {
  return idbCountMessages()
}

export { clearConversationCache, conversationCacheSize }
/** 已读上报防抖：同一会话 500ms 内的多次已读合并为一次 PUT */
const readTimers: Record<string, ReturnType<typeof setTimeout>> = {}
function scheduleReadSync (conversationId: string, fn: () => void) {
  clearTimeout(readTimers[conversationId])
  readTimers[conversationId] = setTimeout(() => {
    delete readTimers[conversationId]
    fn()
  }, 500)
}

/** 本地乐观气泡 id/localKey 前缀（服务端 id 永不以此开头，便于一眼区分） */
const TMP_PREFIX = 'tmp-'

function makeLocalKey () {
  return `${TMP_PREFIX}${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`
}

/**
 * 乐观气泡 ↔ 服务端真实消息 对账。
 *
 * SignalR 回声可能比 POST 响应先到（服务端先广播后返回 / 网络抖动），
 * 所以「POST 成功」和「实时回声」两条路径都必须能把临时气泡就地换成真实消息，
 * 且**必须保留 localKey**：v-for 的 key 一旦变化节点就会重新挂载，
 * 气泡会把进场动画再播一遍，看起来就是"闪一下"。
 *
 * @returns 是否命中并替换了一条待确认的乐观气泡
 */
function settlePending (list: Message[], incoming: Message): boolean {
  const i = list.findIndex(m =>
    !!m.pending
    && m.senderId === incoming.senderId
    && m.type === incoming.type
    && (m.content ?? '') === (incoming.content ?? '')
    && (m.imageId ?? '') === (incoming.imageId ?? '')
    && (m.fileName ?? '') === (incoming.fileName ?? '')
    && (m.patTarget ?? '') === (incoming.patTarget ?? ''))
  const target = at(list, i)
  if (!target) {
    return false
  }
  replaceAt(list, i, { ...incoming, localKey: target.localKey })
  return true
}

/** 按下标取消息并做存在性收窄（开启 noUncheckedIndexedAccess 后 list[i] 是 Message | undefined） */
function at (list: Message[], i: number): Message | null {
  return i >= 0 ? list[i] ?? null : null
}

/** 就地替换一条消息（保留数组下标，Vue 3 的 Proxy 能捕获下标赋值并触发更新） */
function replaceAt (list: Message[], i: number, next: Message): void {
  if (i >= 0 && i < list.length) {
    list[i] = next
  }
}

export const useConversationsStore = defineStore('conversations', {
  state: () => ({
    sessions: [] as Conversation[],
    activeId: null as string | null,
    messagesByConv: {} as Record<string, Message[]>,
    presence: {} as Record<string, boolean>,
    historyLoading: false,
    historyHasMore: {} as Record<string, boolean>,
    /** 群公告确认进度（groupId → 最新确认快照）。实时 NoticeConfirmed 事件更新。 */
    noticeConfirmed: {} as Record<string, { last: { nickname: string, at: string }, confirmed: number, total: number }>,
    /** 群公告「版本」计数（groupId → 次数）。群公告被修改/新增时 +1，聊天页据此重拉详情并重置确认态。 */
    noticeVersion: {} as Record<string, number>,
    /** 「对方正在输入」（会话 id → { userId, at }）。实时 Typing 事件更新，聊天页按超时自动消失。 */
    typingBy: {} as Record<string, { userId: string, at: number }>,
  }),
  getters: {
    activeSession: state => state.sessions.find(s => s.id === state.activeId) ?? null,
    activeMessages: state => (state.activeId ? state.messagesByConv[state.activeId] ?? [] : []),
  },
  actions: {
    async loadSessions () {
      const res = await api.get('/conversations')
      this.sessions = res.data
    },
    /**
     * 删除一条消息的多端同步（服务端 MessageDeleted 广播 / 本端删除成功后调用）：
     * 内存移除 + 会话预览同步 + 立即重写该会话的 IDB 缓存。
     * 关键：IDB 是"删除的消息刷新回来"的根因——openConversation 会先回填 IDB，
     * 若被删消息仍留在本地缓存，切走再切回 / 重新打开会话就会复活。
     */
    removeMessage (conversationId: string, messageId: string) {
      const list = this.messagesByConv[conversationId]
      if (list) {
        const i = list.findIndex(m => m.id === messageId)
        if (i !== -1) list.splice(i, 1)
      }
      const s = this.sessions.find(x => x.id === conversationId)
      if (s?.lastMessage?.id === messageId) {
        s.lastMessage = this.messagesByConv[conversationId]?.at(-1)
        s.lastAt = s.lastMessage?.createdAt
      }
      // 只精准删这一条 IDB 缓存（不能整段重写：跨设备接收端可能从没打开过该会话，
      // 整段重写会把对方该会话的缓存全清光）。
      void idbRemoveMessage(messageId).catch(() => {})
      // 同时把待执行的整段去抖写也撤掉，避免 400ms 后把刚删的又写回 IDB
      clearTimeout(msgWriteTimers[conversationId])
    },
    /** 清空某会话的多端同步（服务端 MessagesCleared 广播 / 本端清空成功后调用）。 */
    clearConversationLocal (conversationId: string) {
      this.messagesByConv[conversationId] = []
      this.historyHasMore[conversationId] = false
      const s = this.sessions.find(x => x.id === conversationId)
      if (s) {
        s.lastMessage = undefined
        s.lastAt = undefined
        s.unread = 0
      }
      void idbPutMessages(conversationId, []).catch(() => {})
      // 撤掉该会话正在排队的去抖写（400ms 后可能把刚清空的又写回 IDB）
      clearTimeout(msgWriteTimers[conversationId])
      delete msgWriteTimers[conversationId]
    },
    /** 收到实时消息（去重/合并 + 会话更新）。 */
    applyIncoming (message: Message) {
      const list = this.messagesByConv[message.conversationId] ??= []
      const idx = list.findIndex(m => m.id === message.id)
      if (idx !== -1) {
        // 同 id 再收到：撤回等状态更新（服务端下发 recalled=true），合并替换。
        // 保留 localKey —— 这条可能正是我们刚乐观发出去、被回声追上的那条。
        const prev = at(list, idx)
        if (prev) {
          list[idx] = prev.localKey ? { ...message, localKey: prev.localKey } : message
        }
      } else if (!settlePending(list, message)) {
        // 不是自己待确认的乐观气泡 → 正常新消息入列
        list.push(message)
        // 内存上限：单会话最多 500 条（更早的按需加载）
        if (list.length > 500) {
          list.splice(0, list.length - 500)
        }
        // 新消息落到本地缓存（聊天记录存浏览器）：去抖批量写 IDB
        scheduleMessagePersist(message.conversationId)
      }
      const s = this.sessions.find(x => x.id === message.conversationId)
      if (s) {
        s.lastMessage = message
        s.lastAt = message.createdAt
        // 自己发的消息（多端同步回来的）不算未读
        const meId = useAuthStore().user?.id
        if (message.senderId === meId) {
          return
        }
        if (this.activeId === message.conversationId) {
          // 正开着这个会话：本地红点不加，且必须把「已读」同步到服务端，
          // 否则刷新/重登后服务端未读数照涨 —— 红点复活。
          // 防抖：消息密集时（如刷屏）每条一个 PUT 会打爆服务端，500ms 合并为一次
          s.unread = 0
          scheduleReadSync(message.conversationId, () => {
            void api.put(`/conversations/${message.conversationId}`, { read: true }).catch(() => {})
          })
        } else {
          s.unread++
        }
      }
    },
    /**
     * 打开会话：清红点 + 同步服务端已读。
     * silent 模式仅同步已读，不改变 activeId / 不重复加载历史（实时消息路径用）。
     */
    async openConversation (id: string, opts: { silent?: boolean } = {}) {
      if (!opts.silent) {
        this.activeId = id
        if (!this.messagesByConv[id]) {
          this.messagesByConv[id] = []
          // ① 先从浏览器缓存（IDB）回填本地聊天记录，秒开、离线可用
          const cached = await idbGetMessages(id)
          if (cached.length > 0) {
            // 兜底：若缓存里混进了未确认的乐观气泡（旧版本写入的），不回填
            this.messagesByConv[id] = cached.filter(m => !m.pending && !m.failed).slice(-500)
            this.historyHasMore[id] = cached.length >= 500
          }
        }
        // ★ 关键修复：无论内存里是否已有该会话消息，切到它时都向服务端补拉最新一页。
        //   此前 if(!messagesByConv[id]) 包住 loadHistory —— 只要之前实时消息/翻历史建过
        //   数组，切会话就不再补拉，导致「切到别的会话，断线/后台期间错过的消息全都收不到」。
        //   loadHistory 自带 id 去重（已收到的不重复入列），是幂等的安全补拉。
        await this.loadHistory(id)
      }
      const s = this.sessions.find(x => x.id === id)
      if (s && s.unread > 0) {
        s.unread = 0
        // 同步到服务端：服务端记录已读时间，刷新/重登后红点不会复活
        try {
          await api.put(`/conversations/${id}`, { read: true })
        } catch { /* 已读失败不阻塞 */ }
      }
    },
    /**
     * opts.cap：内存上限（默认 500）。翻历史时由调用方传入更大的 cap
     *（当前可见窗口 + 新页余量），否则 500 条封顶 + 保留最新会话枚举淘汰的是
     * 「最旧的」，刚拉回来的更早消息立刻被挤掉 → 点「显示更早的消息」永远刷不出新数据。
     * 硬顶 2000：避免无限翻页把内存吃穿。
     */
    async loadHistory (id: string, beforeId?: string, opts: { cap?: number } = {}) {
      const cap = Math.min(2000, Math.max(500, opts.cap ?? 500))
      this.historyLoading = true
      try {
        const res = await api.get(`/messages/${id}`, { params: { beforeId, count: 30 } })
        const page: Message[] = res.data
        const existing = this.messagesByConv[id] ?? []
        // 按 id 去重：重连 catch-up 后实时消息已在列表中，直接拼接会出现重复气泡
        const seen = new Set(existing.map(m => m.id))
        const fresh = page.filter(m => !seen.has(m.id))
        // ★ 乐观发送竞争：这一页历史可能已经包含我们刚发出、还在 pending 的消息
        //   （比如刚进会话就打字发送，历史请求此刻才回来）。先拿它对账掉本地 pending 气泡，
        //   对上了就不要再重复入列 —— 否则同一条消息会出现两次。
        const rest: Message[] = []
        for (const fm of fresh) {
          if (!settlePending(existing, fm)) {
            rest.push(fm)
          }
        }
        let merged = [...rest, ...existing]
        // 内存上限：按调用方的 cap 淘汰最旧（默认 500，翻历史时动态放大）
        if (merged.length > cap) {
          merged = merged.slice(-cap)
        }
        this.messagesByConv[id] = merged
        this.historyHasMore[id] = page.length === 30
        // 服务端拉到的聊天记录也落到浏览器缓存（1 天窗口内）
        scheduleMessagePersist(id)
      } finally {
        this.historyLoading = false
      }
    },
    /**
     * 发送消息（乐观上屏）。
     *
     * 此前要等服务端往返气泡才出现 —— 本地网络再快也有几十到几百毫秒的"按了没反应"，
     * 这是整个聊天页最影响手感的一处。现在先把本地气泡插进去（pending），
     * 服务端确认后就地替换成真实消息（保留 localKey，不重挂载、不重播动画）；
     * 失败则标记为 failed，点击可重试。
     */
    async send (conv: Conversation, msg: Partial<Message>) {
      const meId = useAuthStore().user?.id ?? ''
      const localKey = makeLocalKey()
      const optimistic: Message = {
        id: localKey,
        conversationId: conv.id,
        senderId: meId,
        type: msg.type ?? 'text',
        content: msg.content ?? '',
        imageId: msg.imageId,
        voiceSeconds: msg.voiceSeconds,
        fileName: msg.fileName,
        fileSize: msg.fileSize,
        stickerEmoji: msg.stickerEmoji,
        quoteJson: msg.quoteJson,
        pat: msg.type === 'pat' ? true : undefined,
        patTarget: msg.patTarget,
        createdAt: new Date().toISOString(),
        localKey,
        pending: true,
      }

      const list = (this.messagesByConv[conv.id] ??= [])
      list.push(optimistic) // ★ 立刻上屏，不等网络
      const s0 = this.sessions.find(x => x.id === conv.id)
      if (s0) {
        s0.lastMessage = optimistic
        s0.lastAt = optimistic.createdAt
      }

      const body = {
        conversationId: conv.id,
        type: msg.type ?? 'text',
        content: msg.content ?? '',
        imageId: msg.imageId,
        voiceSeconds: msg.voiceSeconds,
        fileName: msg.fileName,
        fileSize: msg.fileSize,
        stickerEmoji: msg.stickerEmoji,
        quoteJson: msg.quoteJson,
        patTarget: msg.patTarget,
      }

      try {
        const res = await api.post('/messages', body)
        const created = res.data as Message
        // 回声可能已经抢先对账掉了这条；无论哪种，都以真实消息为准就地替换
        const i = list.findIndex(m => m.localKey === localKey)
        replaceAt(list, i, { ...created, localKey })
        const s = this.sessions.find(x => x.id === conv.id)
        if (s) {
          s.lastMessage = created
          s.lastAt = created.createdAt
        }
        // 自己发的也落浏览器缓存
        scheduleMessagePersist(conv.id)
        return created
      } catch (error) {
        const i = list.findIndex(m => m.localKey === localKey)
        const cur = at(list, i)
        if (cur) {
          replaceAt(list, i, { ...cur, pending: false, failed: true })
        }
        throw error
      }
    },
    /** 重发一条发送失败的消息（保持 localKey，避免气泡重挂载闪一下） */
    async retrySend (convId: string, localKey: string) {
      const list = this.messagesByConv[convId]
      if (!list) {
        return
      }
      const i = list.findIndex(x => x.localKey === localKey)
      const m = at(list, i)
      if (!m || !m.failed) {
        return
      }
      replaceAt(list, i, { ...m, pending: true, failed: false })
      try {
        const res = await api.post('/messages', {
          conversationId: convId,
          type: m.type,
          content: m.content ?? '',
          imageId: m.imageId,
          voiceSeconds: m.voiceSeconds,
          fileName: m.fileName,
          fileSize: m.fileSize,
          stickerEmoji: m.stickerEmoji,
          quoteJson: m.quoteJson,
          patTarget: m.patTarget,
        })
        const created = res.data as Message
        const j = list.findIndex(x => x.localKey === localKey)
        replaceAt(list, j, { ...created, localKey })
        scheduleMessagePersist(convId)
      } catch {
        const j = list.findIndex(x => x.localKey === localKey)
        const cur = at(list, j)
        if (cur) {
          replaceAt(list, j, { ...cur, pending: false, failed: true })
        }
      }
    },
    async recall (message: Message) {
      const res = await api.post(`/messages/${message.id}/recall`)
      message.recalled = true
      message.content = ''
      this.applyIncoming(res.data)
    },
    setPresence (userId: string, online: boolean) {
      this.presence[userId] = online
    },
    /** 群公告被成员确认（实时事件）：更新确认进度，聊天页据此即时刷新「已确认 x/y」。 */
    applyNoticeConfirmed (groupId: string, payload: { userId: string, nickname: string, confirmedAt: string, confirmed: number, total: number }) {
      this.noticeConfirmed[groupId] = {
        last: { nickname: payload.nickname, at: payload.confirmedAt },
        confirmed: payload.confirmed,
        total: payload.total,
      }
    },
    /** 群公告被修改/新增（实时 NoticeUpdated 事件）：版本提升 → 旧确认全部失效。
     *  清掉该群确认进度 + 版本号 +1，聊天页 watch 该版本后重拉详情并重置确认态，
     *  实现「公告改动/新增后自动重新展示并需再确认」。 */
    applyNoticeUpdated (groupId: string) {
      delete this.noticeConfirmed[groupId]
      this.noticeVersion[groupId] = (this.noticeVersion[groupId] ?? 0) + 1
    },
    /**
     * 「对方正在输入」（实时 Typing 事件）：记录谁在敲字，聊天页据此显示提示。
     * 只记录会话内他人（服务端已过滤成员 + 排除发送者），提示按时间戳超时自动消失。
     */
    applyTyping (conversationId: string, userId: string, meId: string) {
      if (!conversationId || !userId || userId === meId) return
      this.typingBy[conversationId] = { userId, at: Date.now() }
    },
    /**
     * 手动/可见性变化时「刷新聊天记录」：重新拉取该会话最新一页，补上离线/卡死期间错过的消息。
     * 保留已有列表，按 id 去重合并，避免重复气泡。仅要求该会话确实被打开过（有本地列表）。
     */
    async refreshConversation (id: string) {
      if (!this.messagesByConv[id]) return
      await this.loadHistory(id)
      await this.loadSessions().catch(() => {})
    },
    markRead (conversationId: string) {
      const s = this.sessions.find(x => x.id === conversationId)
      if (s) {
        s.unread = 0
      }
    },
    async togglePin (id: string, pinned: boolean) {
      await api.put(`/conversations/${id}`, { pinned })
      const s = this.sessions.find(x => x.id === id)
      if (s) {
        s.pinned = pinned
      }
      await this.loadSessions()
    },
    async toggleMute (id: string, muted: boolean) {
      await api.put(`/conversations/${id}`, { muted })
      const s = this.sessions.find(x => x.id === id)
      if (s) s.muted = muted
    },
    /** 折叠 / 取消折叠会话（隐藏/恢复主列表，集中在"折叠群聊"里管理）。 */
    async toggleFold (id: string, folded: boolean) {
      await api.put(`/conversations/${id}`, { folded })
      const s = this.sessions.find(x => x.id === id)
      if (s) s.folded = folded
      // 折叠/恢复会改变主列表可见性，刷新一次会话列表
      await this.loadSessions().catch(() => {})
    },
    /**
     * 登录/刷新后自动拉取服务端「最近 1 天」聊天记录，灌入浏览器缓存（IDB）。
     * 并发上限（一次 4 个会话），避免登录瞬间把服务端拉爆；
     * 每个会话取最新一页（默认 30 条）即够 1 天窗口；已缓存的跳过不重复写。
     */
    async pullRecentHistoryIntoCache (limit = 20) {
      if (this.sessions.length === 0) {
        return
      }
      const target = this.sessions.slice(0, limit)
      const concurrency = 2
      let cursor = 0
      const worker = async () => {
        while (cursor < target.length) {
          const conv = target[cursor++]
          if (!conv) {
            break
          }
          try {
            // 内存已有所需会话则跳过（避免重复拉取）
            const have = this.messagesByConv[conv.id]
            if (have && have.length > 0) {
              scheduleMessagePersist(conv.id)
              continue
            }
            const res = await api.get(`/messages/${conv.id}`, { params: { count: 100 } })
            const page: Message[] = res.data
            const existing = this.messagesByConv[conv.id] ?? []
            const seen = new Set(existing.map(m => m.id))
            const fresh = page.filter(m => !seen.has(m.id))
            if (fresh.length === 0) {
              continue
            }
            const merged = [...fresh, ...existing].slice(-500)
            this.messagesByConv[conv.id] = merged
            await idbPutMessages(conv.id, merged)
          } catch { /* 单会话失败不影响整体 */ }
        }
      }
      await Promise.all(Array.from({ length: concurrency }, () => worker()))
    },
  },
})
