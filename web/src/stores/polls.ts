/**
 * stores/polls.ts —— 群投票状态（按会话/群缓存）。
 *
 * 数据流：
 *   • 打开群聊页时 load(groupId) 拉全量投票列表；
 *   • PollUpdated 实时事件 → applyUpdated 原位替换（所有打开的投票卡片联动刷新）；
 *   • 投票走 vote()（服务端返回最新 poll，本端立即更新，他端走实时事件）。
 */
import { defineStore } from 'pinia'
import { api } from '@/lib/api'
import type { Poll } from '@/types'

export const usePollsStore = defineStore('polls', {
  state: () => ({
    pollsByConv: {} as Record<string, Poll[]>,
  }),
  getters: {
    pollOf: s => (conversationId: string, pollId: string) =>
      s.pollsByConv[conversationId]?.find(p => p.id === pollId) ?? null,
  },
  actions: {
    /** 拉取某群全部投票（进聊天页时调用）。 */
    async load (conversationId: string) {
      try {
        const res = await api.get(`/groups/${conversationId}/polls`)
        this.pollsByConv[conversationId] = res.data as Poll[]
      } catch { /* 非群聊或无权限：保持空 */ }
    },
    /** 实时事件：投票被更新（他人投票/截止）→ 原位替换。 */
    applyUpdated (conversationId: string, poll: Poll) {
      const list = this.pollsByConv[conversationId]
      if (!list) return
      const idx = list.findIndex(p => p.id === poll.id)
      if (idx >= 0) list[idx] = poll
      else list.push(poll)
    },
    /** 投票（单选改投 / 多选追加与再点取消均由服务端裁决），返回更新后的投票。 */
    async vote (conversationId: string, pollId: string, option: number) {
      const res = await api.post(`/groups/${conversationId}/polls/${pollId}/vote`, { option })
      const poll = res.data as Poll
      this.applyUpdated(conversationId, poll)
      return poll
    },
    /** 截止投票（发起人或群主）。 */
    async close (conversationId: string, pollId: string) {
      const res = await api.post(`/groups/${conversationId}/polls/${pollId}/close`)
      const poll = res.data as Poll
      this.applyUpdated(conversationId, poll)
      return poll
    },
  },
})
