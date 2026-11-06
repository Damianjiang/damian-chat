import type { User } from '@/types'
/**
 * stores/auth.ts
 */
import { defineStore } from 'pinia'
import { api, clearTokens, saveTokens } from '@/lib/api'
import { clearMediaCache } from '@/lib/files'
import { setCacheKey } from '@/lib/idb-crypto'
import { idbClearMedia, idbClearMessages } from '@/lib/idb'
import { useContactsStore } from '@/stores/contacts'
import { useConversationsStore } from '@/stores/conversations'
import { useMomentsStore } from '@/stores/moments'
import { usePollsStore } from '@/stores/polls'

export interface AuthPayload {
  accessToken: string
  refreshToken: string
  /** 本机缓存加密密钥（服务端按用户签发）：IDB 聊天记录/媒体 AES-GCM 用 */
  cacheKey?: string
  user: User
}

export const useAuthStore = defineStore('auth', {
  state: () => ({
    user: null as User | null,
    theme: 'system' as 'light' | 'dark' | 'system',
  }),
  getters: {
    // 基于 state 的响应式判定：token 可能在 refresh 后变化，但 user 与 token 生命周期一致
    isLoggedIn: state => !!state.user,
  },
  actions: {
    /** 内部：保存令牌并写入用户 */
    applySession (payload: AuthPayload) {
      saveTokens(payload.accessToken, payload.refreshToken)
      // 缓存加密密钥（sessionStorage，关标签页即清；新标签页由 refresh 响应补上）
      if (payload.cacheKey) setCacheKey(payload.cacheKey)
      this.user = payload.user
      return payload.user
    },

    /** 账号 + 密码 + 图形验证码登录（防机器人：验证码 + 限流 + 锁定由服务端强制） */
    async login (account: string, password: string, captchaId: string, captcha: string, passToken: string) {
      const res = await api.post('/auth/login', { account, password, captchaId, captcha, passToken })
      return this.applySession(res.data)
    },

    /** 注册：账号 + 密码 + 管理员注册密钥，成功即自动登录 */
    async register (account: string, password: string, registerKey: string, captchaId: string, captcha: string, passToken: string) {
      const res = await api.post('/auth/register', { account, password, registerKey, captchaId, captcha, passToken })
      return this.applySession(res.data)
    },

    /** 邮箱注册：邮箱 + 验证码 + 密码 + 注册密钥（登录账号=邮箱，大面号由服务端随机生成） */
    async registerEmail (email: string, emailCode: string, password: string, registerKey: string, captchaId: string, captcha: string, passToken: string) {
      const res = await api.post('/auth/register', { email, emailCode, password, registerKey, captchaId, captcha, passToken })
      return this.applySession(res.data)
    },

    async fetchMe () {
      const res = await api.get('/users/me')
      this.user = res.data
      return res.data as User
    },

    /**
     * 清空本地业务数据（logout 与换号护栏共用的同一套清理，此前两处各抄一份
     * 且都漏了 polls store —— 账号 B 会继承账号 A 的群投票快照）。
     * 同时清 IDB 消息缓存与模块级防抖定时器（$reset 只清内存 store，
     * 400ms 内的遗留 IDB 写可能把上一个账号的消息写进新会话缓存）。
     */
    async clearLocalAccountData () {
      useConversationsStore().$reset()
      try {
        (await import('@/stores/conversations')).clearConversationCache()
      } catch { /* 模块未加载则忽略 */ }
      useContactsStore().$reset()
      useMomentsStore().$reset()
      usePollsStore().$reset()
      // 浏览器缓存（IDB 聊天记录 + 媒体 blob）一并清除：
      // 防止同浏览器换账号后看到上一账号缓存的消息 / 文件内容
      void idbClearMessages().catch(() => {})
      void clearMediaCache().catch(() => {})
    },

    /** 退出：断开实时连接 + 吊销 refresh token + 清本地 */
    async logout () {
      // 动态 import 避免与 realtime.ts 的循环依赖
      try {
        (await import('@/lib/realtime')).stopRealtime()
      } catch { /* 未连接则忽略 */ }
      // refresh 令牌现走 httpOnly cookie（服务端 logout 从 cookie 读取并清 cookie）；
      // 不再从 localStorage 取 refresh（那里已不再存储）。
      try {
        await api.post('/auth/logout')
      } catch { /* 网络失败也继续本地登出 */ }
      clearTokens()
      this.user = null
      await this.clearLocalAccountData()
    },

    /**
     * 换号护栏：同一浏览器换了登录账号时，主动清上一个账号的本地残留。
     * 补上 logout 之外的缺口 —— token 静默过期（没走登出）→ 别人登进来，
     * 打开会话时 IDB 里上一账号的聊天记录/媒体会被直接回填渲染。
     * 登录成功与刷新恢复两条入口都要调。
     */
    async accountSwitchGuard (userId: string) {
      let last = ''
      try { last = localStorage.getItem('wx_last_uid') ?? '' } catch { /* 忽略 */ }
      try { localStorage.setItem('wx_last_uid', userId) } catch { /* 忽略 */ }
      if (!last || last === userId) return
      await this.clearLocalAccountData()
    },
  },
})
