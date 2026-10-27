import type { User } from '@/types'
/**
 * stores/auth.ts
 */
import { defineStore } from 'pinia'
import { api, clearTokens, saveTokens } from '@/lib/api'
import { clearMediaCache } from '@/lib/files'
import { idbClearMedia, idbClearMessages } from '@/lib/idb'
import { useContactsStore } from '@/stores/contacts'
import { useConversationsStore } from '@/stores/conversations'
import { useMomentsStore } from '@/stores/moments'

export interface AuthPayload {
  accessToken: string
  refreshToken: string
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

    async fetchMe () {
      const res = await api.get('/users/me')
      this.user = res.data
      return res.data as User
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
      // 清空本地业务数据：换账号登录不得看到上一个账号的会话/联系人/朋友圈残留
      useConversationsStore().$reset()
      useContactsStore().$reset()
      useMomentsStore().$reset()
      // 浏览器缓存（IDB 聊天记录 + 媒体 blob）也随登出清除：
      // 防止同浏览器换账号后看到上一账号缓存的消息 / 文件内容
      void idbClearMessages().catch(() => {})
      void clearMediaCache().catch(() => {})
    },
  },
})
