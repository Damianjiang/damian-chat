/**
 * stores/solitaires.ts —— 群接龙状态（按群缓存）。
 *
 * 数据流与群投票一致：
 *   • 打开群聊页 load(groupId) 拉全量；
 *   • SolitaireUpdated 实时事件 → 原位替换（所有打开的接龙卡片联动刷新）；
 *   • 参与/取消/截止走接口，服务端返回最新对象，本端立即更新，他端走实时事件。
 */
import { defineStore } from 'pinia'
import { api } from '@/lib/api'
import type { Solitaire } from '@/types'

export const useSolitairesStore = defineStore('solitaires', {
  state: () => ({
    byConv: {} as Record<string, Solitaire[]>,
  }),
  getters: {
    solitaireOf: s => (conversationId: string, id: string) =>
      s.byConv[conversationId]?.find(x => x.id === id) ?? null,
  },
  actions: {
    async load (conversationId: string) {
      try {
        const res = await api.get(`/groups/${conversationId}/solitaires`)
        this.byConv[conversationId] = res.data as Solitaire[]
      } catch { /* 非群聊或无权限：保持空 */ }
    },
    applyUpdated (conversationId: string, solitaire: Solitaire) {
      const list = this.byConv[conversationId]
      if (!list) return
      const idx = list.findIndex(x => x.id === solitaire.id)
      if (idx >= 0) list[idx] = solitaire
      else list.push(solitaire)
    },
    async join (conversationId: string, id: string, note: string) {
      const res = await api.post(`/groups/${conversationId}/solitaires/${id}/join`, { note })
      const s = res.data as Solitaire
      this.applyUpdated(conversationId, s)
      return s
    },
    async leave (conversationId: string, id: string) {
      const res = await api.post(`/groups/${conversationId}/solitaires/${id}/leave`, {})
      const s = res.data as Solitaire
      this.applyUpdated(conversationId, s)
      return s
    },
    async close (conversationId: string, id: string) {
      const res = await api.post(`/groups/${conversationId}/solitaires/${id}/close`, {})
      const s = res.data as Solitaire
      this.applyUpdated(conversationId, s)
      return s
    },
  },
})
