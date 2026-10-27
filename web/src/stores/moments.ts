import type { Moment } from '@/types'
/**
 * stores/moments.ts
 */
import { defineStore } from 'pinia'
import { api } from '@/lib/api'
import { useAuthStore } from '@/stores/auth'

export const useMomentsStore = defineStore('moments', {
  state: () => ({
    items: [] as Moment[],
    nextBefore: null as string | null,
    loading: false,
    hasMore: true,
  }),
  actions: {
    async load (more = false) {
      if (this.loading) {
        return
      }
      this.loading = true
      try {
        if (!more) {
          this.items = []
          this.nextBefore = null
          this.hasMore = true
        }
        if (!this.hasMore) {
          return
        }
        const res = await api.get('/moments', { params: { before: this.nextBefore, count: 10 } })
        this.items.push(...res.data.items)
        this.nextBefore = res.data.nextBefore
        this.hasMore = !!res.data.nextBefore
      } finally {
        this.loading = false
      }
    },
    /** 深链兜底：按 id 拉取单条动态（GET /moments/{id}），有则替换/插入列表 */
    async fetchOne (id: string) {
      const res = await api.get(`/moments/${id}`)
      const m = res.data as Moment
      const idx = this.items.findIndex(x => x.id === id)
      if (idx === -1) {
        this.items.unshift(m)
      } else {
        this.items[idx] = m
      }
      return m
    },
    async post (text: string, images: string[], scope: string) {
      const res = await api.post('/moments', { text, images, scope })
      this.items.unshift(res.data)
      return res.data
    },
    /** 点赞/取消：API 成功后局部更新，避免全量刷新丢失滚动位置 */
    async like (id: string, liked: boolean) {
      await (liked ? api.post(`/moments/${id}/like`) : api.delete(`/moments/${id}/like`))
      const m = this.items.find(x => x.id === id)
      if (!m) {
        return
      }
      const auth = useAuthStore()
      const me = auth.user
      if (!me) {
        return
      }
      const i = m.likes.findIndex(l => l.userId === me.id)
      if (liked) {
        if (i === -1) {
          m.likes.push({ userId: me.id, nickname: me.nickname })
        }
      } else if (i !== -1) {
        m.likes.splice(i, 1)
      }
    },
    /** 评论：后端返回带 nickname 的 DTO，直接局部追加（此前 load(false) 全量重置丢滚动位置） */
    async comment (id: string, text: string) {
      const res = await api.post(`/moments/${id}/comments`, { text })
      const m = this.items.find(x => x.id === id)
      if (m) {
        m.comments.push(res.data)
      }
      return res.data
    },
    /** 删除动态：API 成功后从列表移除 */
    async remove (id: string) {
      await api.delete(`/moments/${id}`)
      this.items = this.items.filter(x => x.id !== id)
    },
  },
})
