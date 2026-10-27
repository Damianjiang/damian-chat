import type { Contact, FriendRequest, Group, User } from '@/types'
/**
 * stores/contacts.ts
 */
import { defineStore } from 'pinia'
import { api } from '@/lib/api'
import { useConversationsStore } from './conversations'

export const useContactsStore = defineStore('contacts', {
  state: () => ({
    items: [] as Contact[],
    indexData: {} as Record<string, { nickname: string, id: string, firstLetter: string, remark: string | null, avatar: string }[]>,
    requests: [] as FriendRequest[],
    groups: [] as Group[],
    keyword: '',
  }),
  getters: {
    sorted: state => state.items.toSorted((a, b) => a.firstLetter.localeCompare(b.firstLetter) || a.user.nickname.localeCompare(b.user.nickname, 'zh')),
  },
  actions: {
    async load (keyword?: string) {
      this.keyword = keyword ?? ''
      const res = await api.get('/contacts', { params: { keyword } })
      this.items = res.data
    },
    async loadIndex () {
      const res = await api.get('/contacts/index')
      this.indexData = res.data
    },
    async loadRequests () {
      const res = await api.get('/contacts/requests')
      this.requests = res.data
    },
    async acceptRequest (id: string) {
      await api.post(`/contacts/requests/${id}/accept`)
      this.requests = this.requests.filter(r => r.id !== id)
      await this.load()
      await this.loadIndex()
      // 通过后后端会创建单聊会话，刷新会话列表
      const conv = useConversationsStore()
      await conv.loadSessions()
    },
    async rejectRequest (id: string) {
      await api.post(`/contacts/requests/${id}/reject`)
      this.requests = this.requests.filter(r => r.id !== id)
    },
    /** 按手机号或大面号（WxId）添加好友：同一输入同时作为 phone 与 wxId 下发，后端命中任一即可。 */
    async addFriend (account: string, message: string) {
      await api.post('/contacts', { phone: account, wxId: account, message })
    },
    async setRemark (id: string, remark: string) {
      await api.put(`/contacts/${id}/remark`, { remark })
      const c = this.items.find(x => x.id === id)
      if (c) {
        c.remark = remark
      }
    },
    async toggleBlock (id: string, blocked: boolean) {
      await api.put(`/contacts/${id}/block`, { blocked })
      const c = this.items.find(x => x.id === id)
      if (c) {
        c.blocked = blocked
      }
    },
    /** 已拉黑用户列表（设置页「被拉黑的用户」）。 */
    async loadBlocked () {
      const res = await api.get('/contacts/blocked')
      return res.data as Array<{ id: string, blocked: boolean, isFriend: boolean, user: User }>
    },
    /** 解除拉黑（恢复还原）：按对方用户 id。 */
    async unblock (userId: string) {
      await api.put('/contacts/block', { userId, blocked: false })
      const c = this.items.find(x => x.user?.id === userId)
      if (c) c.blocked = false
      await this.load()
      await this.loadIndex()
    },
    /** 删除好友关系（双向）。传联系人记录 id 或对方用户 id 均可。 */
    async removeFriend (contactId: string) {
      await api.delete(`/contacts/${contactId}`)
      this.items = this.items.filter(x => x.id !== contactId)
      await this.load()
      await this.loadIndex()
      // 好友关系解除，单聊会话仍保留；刷新会话列表（会话可能已不可发起）
      const conv = useConversationsStore()
      await conv.loadSessions().catch(() => {})
    },
    async loadGroups () {
      const res = await api.get('/groups')
      this.groups = res.data
    },
    async createGroup (name: string, memberIds: string[], notice?: string) {
      const res = await api.post('/groups', { name, memberIds, notice })
      this.groups.push(res.data)
      return res.data as Group
    },
  },
})
