<template>
  <v-sheet class="wx-panes" flat>
    <div class="wx-pane-header pa-2 d-flex flex-column gap-2">
      <div class="wx-pane-titlebar">
        <span class="wx-pane-title">{{ t('nav.contacts') }}</span>

        <v-btn
          class="ml-auto"
          icon="mdi-account-plus"
          size="small"
          variant="text"
          @click="addSheet = true"
        />
      </div>

      <v-text-field
        v-model="kw"
        clearable
        density="compact"
        hide-details
        :placeholder="t('contacts.search')"
        prepend-inner-icon="mdi-magnify"
        variant="solo"
      />
    </div>

    <div ref="listRef" class="flex-grow-1 overflow-y-auto position-relative wx-scroll">
      <template v-if="keywordMode">
        <search-result-list :items="filtered" @select="openById" />
      </template>

      <template v-else>
        <v-list v-if="contacts.requests.length > 0" class="wx-entry-list wx-stagger">
          <v-list-subheader>{{ t('contacts.new') }}</v-list-subheader>

          <v-list-item v-for="r in contacts.requests" :key="r.id">
            <template #prepend>
              <v-avatar :color="avatarColor(r.from?.id ?? r.id)"><span>{{ (r.from?.nickname ?? '?').slice(0, 1) }}</span></v-avatar>
            </template>

            <template #title>
              {{ r.from?.nickname }}<span class="text-body-small ml-2 text-medium-emphasis">{{ r.message }}</span>
            </template>

            <template #append>
              <v-btn color="primary" size="x-small" @click="answerRequest(r.id, true)">{{ t('contacts.accept') }}</v-btn>
              <v-btn size="x-small" variant="text" @click="answerRequest(r.id, false)">{{ t('contacts.reject') }}</v-btn>
            </template>
          </v-list-item>
        </v-list>

        <v-list class="wx-entry-list wx-stagger">
          <!-- 群聊：点击打开「我的群聊」列表（我参加的群 + 未读 + 成员数），点群直接进群 -->
          <v-list-item @click="groupsSheet = true">
            <template #prepend><v-icon color="primary" icon="mdi-account-multiple" /></template>
            <v-list-item-title>{{ t('contacts.groups') }}<span v-if="myGroups.length" class="text-body-small ml-2 text-medium-emphasis">{{ myGroups.length }}</span></v-list-item-title>
            <template #append><v-badge v-if="groupUnread > 0" color="wechat-unread" :content="groupUnread" /><v-icon v-else icon="mdi-chevron-right" /></template>
          </v-list-item>
        </v-list>

        <div v-for="(items, letter) in grouped" :id="`letter-${letter}`" :key="letter">
          <v-list-subheader class="wx-letter-head">{{ letter }}</v-list-subheader>

          <v-list class="wx-stagger">
            <v-list-item
              v-for="c in items"
              :key="c.id"
              class="wx-contact-row"
              @click="openContact(c)"
              @contextmenu.prevent="onContactMenu(c, $event)"
            >
              <template #prepend>
                <v-avatar :color="avatarColor(c.id)" :size="36">
                  <img v-if="c.user?.avatar" :alt="c.user?.nickname" :src="c.user.avatar">
                  <span v-else>{{ (c.user?.nickname ?? '?').slice(0, 1) }}</span>
                </v-avatar>
              </template>

              <template #title>
                <div class="d-flex align-center">
                  {{ c.remark || c.user?.nickname }}
                  <v-chip v-if="c.blocked" class="ml-2" color="error" size="x-small" variant="flat">{{ t('contacts.blocked.tag') }}</v-chip>
                </div>
                <div class="text-body-small text-medium-emphasis">{{ c.user?.phone }}</div>
              </template>
            </v-list-item>
          </v-list>
        </div>
      </template>

      <contact-alpha-index
        v-if="!keywordMode && availableLetters.length > 1"
        class="wx-alpha-float"
        :container="listRef"
        :letters="availableLetters"
        @scroll-to="scrollToLetter"
      />
    </div>

    <!-- 联系人右键 / 长按菜单：拉黑(取消)、删除好友 -->
    <ContextMenu
      v-model="contactMenu"
      :x="menuPoint.x" :y="menuPoint.y" :width="180"
    >

    >
      <v-card v-if="menuContact" width="180">
        <v-list density="compact">
          <v-list-item @click="toggleBlockContact">
            <v-list-item-title>{{ menuContact.blocked ? t('contacts.unblock') : t('contacts.block') }}</v-list-item-title>
            <template #append><v-icon :icon="menuContact.blocked ? 'mdi-account-remove' : 'mdi-account-off'" /></template>
          </v-list-item>

          <v-divider />

          <v-list-item class="text-error" @click="removeContact">
            <v-list-item-title>{{ t('contacts.deleteFriend') }}</v-list-item-title>
            <template #append><v-icon icon="mdi-delete-outline" /></template>
          </v-list-item>
        </v-list>
      </v-card>
    </ContextMenu>

    <v-bottom-sheet v-model="addSheet" max-width="440" scrollable>
      <v-card class="pa-4">
        <v-text-field v-model="newPhone" :label="t('contacts.add.account')" :placeholder="t('contacts.add.account.ph')" />
        <v-text-field v-model="newMsg" :label="t('contacts.add.msg')" />

        <div class="d-flex justify-end gap-2 mt-2">
          <v-btn variant="text" @click="addSheet = false">{{ t('common.cancel') }}</v-btn>
          <v-btn color="primary" :disabled="!newPhone" @click="submitAdd">{{ t('contacts.add.send') }}</v-btn>
        </div>
      </v-card>
    </v-bottom-sheet>

    <!-- 我的群聊：从「通讯录 → 群聊」入口打开，列出我参加的所有群（未读 + 成员数），点群直接进群 -->
    <v-bottom-sheet v-model="groupsSheet" max-width="440" scrollable>
      <v-card class="pa-2">
        <div class="text-body-large font-weight-medium pa-2">{{ t('contacts.my.groups') }}</div>

        <!-- 通过群号加群（QQ 式）：输群号 → lookup → 按加群方式分流：
             open 直接进 / question 显示问题作答 / approve 填理由交申请 / none 拒绝所有人 -->
        <div class="pa-2">
          <div class="text-body-small text-medium-emphasis mb-1">{{ t('contacts.join.by.no') }}</div>
          <div class="d-flex align-center ga-2">
            <v-text-field
              v-model="joinNo"
              class="flex-grow-1"
              density="compact"
              hide-details
              :placeholder="t('contacts.join.no.ph')"
              variant="solo"
              @keyup.enter="lookUpJoin"
            />
            <v-btn color="primary" :disabled="!joinNo.trim() || joinBusy" @click="lookUpJoin">{{ t('contacts.join.btn') }}</v-btn>
          </div>

          <!-- 查到群后的第二步：按加群方式显示问题作答 / 入群理由 -->
          <div v-if="joinPending" class="mt-2 pa-2 rounded-lg" style="background: rgba(127,127,127,0.1)">
            <div class="text-body-medium font-weight-medium">{{ joinPending.name }}</div>
            <div class="text-caption text-medium-emphasis mb-1">{{ joinPolicyHint(joinPending.policy) }}</div>
            <template v-if="joinPending.policy === 'question'">
              <div class="text-body-small mb-1">{{ joinPending.question }}</div>
              <v-text-field
                v-model="joinAnswer"
                density="compact"
                hide-details
                :placeholder="t('contacts.join.answer.ph')"
                variant="solo"
                @keyup.enter="confirmJoin"
              />
            </template>
            <v-text-field
              v-if="joinPending.policy === 'approve'"
              v-model="joinNote"
              class="mt-1"
              density="compact"
              hide-details
              :placeholder="t('contacts.join.reason.ph')"
              variant="solo"
              @keyup.enter="confirmJoin"
            />
            <div class="d-flex justify-end ga-2 mt-2">
              <v-btn density="compact" variant="text" @click="joinPending = null">{{ t('common.cancel') }}</v-btn>
              <v-btn color="primary" density="compact" :disabled="joinBusy || (joinPending.policy === 'question' && !joinAnswer.trim())" @click="confirmJoin">{{ t('common.confirm') }}</v-btn>
            </div>
          </div>

          <div v-if="joinHint" class="text-caption text-medium-emphasis mt-1">{{ joinHint }}</div>
        </div>

        <v-divider class="my-1" />

        <v-list class="pa-0" lines="two">
          <v-list-item v-for="g in myGroups" :key="g.id" @click="openGroup(g)">
            <template #prepend>
              <v-avatar :color="avatarColor(g.id)" :size="40">
                <img v-if="g.avatar" :alt="g.name" :src="g.avatar">
                <span v-else>{{ g.name.slice(0, 1) }}</span>
              </v-avatar>
            </template>
            <template #title>
              <div class="d-flex align-center">
                <span>{{ g.name }}</span>
                <v-badge v-if="groupUnreadOf(g) > 0" class="ml-2" color="wechat-unread" :content="groupUnreadOf(g)" />
              </div>
              <div class="text-body-small text-medium-emphasis">{{ t('contacts.members.count', { n: g.members?.length ?? 0 }) }}</div>
            </template>
            <template #append><v-icon icon="mdi-chevron-right" /></template>
          </v-list-item>

          <v-list-item v-if="myGroups.length === 0" class="justify-center" disabled>
            <v-list-item-title class="text-medium-emphasis">{{ t('contacts.groups.none') }}</v-list-item-title>
          </v-list-item>
        </v-list>

        <div class="d-flex justify-end ga-2 pa-2">
          <v-btn variant="text" @click="groupsSheet = false">{{ t('common.close') }}</v-btn>
        </div>
      </v-card>
    </v-bottom-sheet>

    <v-snackbar v-model="snack" :timeout="1800">{{ snackMsg }}</v-snackbar>
  </v-sheet>
</template>

<script setup lang="ts">
  import type { Contact, Group } from '@/types'
  import { computed, onMounted, ref } from 'vue'
  import { useRouter } from 'vue-router'
  import ContactAlphaIndex from '@/components/ContactAlphaIndex.vue'
  import SearchResultList from '@/components/SearchResultList.vue'
  import { avatarColor } from '@/lib/utils'
  import { api } from '@/lib/api'
  import { t } from '@/lib/i18n'
  import { useContactsStore } from '@/stores/contacts'
  import ContextMenu from '@/components/ContextMenu.vue'
  import { confirm } from '@/lib/confirm'
  import { useConversationsStore } from '@/stores/conversations'

  const router = useRouter()
  const contacts = useContactsStore()
  const conversations = useConversationsStore()

  const kw = ref('')
  const listRef = ref<HTMLElement | null>(null)
  const addSheet = ref(false)
  const newPhone = ref('')
  const newMsg = ref('')
  const snack = ref(false)
  const snackMsg = ref('')

  /* 「群聊」入口 → 我的群聊列表弹窗（真实数据：我参加的所有群，点群直接进） */
  const groupsSheet = ref(false)

  /* 通过群号加群（QQ 式）：输群号 → lookup（后端只回公开聚合信息，不泄露成员名单）→
     开放群直接 POST /join 入群；审批群交申请等群主点头。已成员幂等。 */
  const joinNo = ref('')
  const joinBusy = ref(false)
  const joinHint = ref('')
  /** lookup 到的待加入群（question/approve 需要第二步输入：答案 / 理由） */
  const joinPending = ref<{ id: string, name: string, policy: string, question?: string } | null>(null)
  const joinAnswer = ref('')
  const joinNote = ref('')

  function joinPolicyHint (policy: string) {
    switch (policy) {
      case 'open': return t('group.join.open.desc')
      case 'question': return t('group.join.question.desc')
      case 'none': return t('group.join.none2')
      default: return t('group.join.approve')
    }
  }

  async function lookUpJoin () {
    const no = joinNo.value.trim()
    if (!no) return
    joinBusy.value = true
    joinHint.value = ''
    try {
      const lu = await api.get('/groups/lookup', { params: { no } })
      const g = lu.data
      if (g.iAmMember) {
        joinNo.value = ''
        groupsSheet.value = false
        snackMsg.value = t('contacts.join.iamember')
        snack.value = true
        router.push({ name: 'group', params: { id: g.id } })
        return
      }
      if (g.joinPolicy === 'none') {
        joinHint.value = t('contacts.join.closed')
        return
      }
      if (g.joinPolicy === 'question' || g.joinPolicy === 'approve') {
        // 需要作答/填理由：留在表单里等用户确认（confirmJoin 提交）
        joinPending.value = { id: g.id, name: g.name, policy: g.joinPolicy, question: g.joinQuestion }
        joinAnswer.value = ''
        joinNote.value = ''
        return
      }
      // open：直接进
      await doJoin(g.id, {}, 'open')
    } catch (error: any) {
      const code = error?.response?.status
      if (code === 404) {
        joinHint.value = t('contacts.join.notfound')
      } else {
        joinHint.value = error?.response?.data?.error ?? t('contacts.join.err')
      }
    } finally {
      joinBusy.value = false
    }
  }

  async function confirmJoin () {
    const g = joinPending.value
    if (!g || joinBusy.value) return
    joinBusy.value = true
    try {
      await doJoin(g.id, { note: joinNote.value || undefined, answer: joinAnswer.value || undefined }, g.policy)
    } finally {
      joinBusy.value = false
    }
  }

  async function doJoin (id: string, payload: { note?: string, answer?: string }, policy: string) {
    const res = await api.post(`/groups/${id}/join`, payload)
    joinNo.value = ''
    joinPending.value = null
    groupsSheet.value = false
    if (policy === 'open' || policy === 'question') {
      // 直接入群（open / question 答对）：跳进群聊
      snackMsg.value = t('contacts.joined.name', { name: id })
      snack.value = true
      await conversations.loadSessions()
      router.push({ name: 'group', params: { id } })
    } else {
      snackMsg.value = res.data?.message ?? t('contacts.join.sent')
      snack.value = true
    }
  }

  /* 联系人右键 / 长按菜单（拉黑 / 删除好友） */
  const contactMenu = ref(false)
  const menuContact = ref<Contact | null>(null)
  const menuPoint = ref({ x: 0, y: 0 })

  function onContactMenu (c: Contact, e: MouseEvent) {
    menuContact.value = c
    menuPoint.value = { x: e.clientX, y: e.clientY }
    contactMenu.value = true
  }

  async function toggleBlockContact () {
    const c = menuContact.value
    if (!c) return
    const next = !c.blocked
    if (next) {
      const ok = await confirm({
        title: t('contacts.block.confirm'),
        text: t('contacts.block.confirm.text', { name: c.user?.nickname ?? t('contacts.peer') }),
        confirmText: t('contacts.block'),
      })
      if (!ok) return
    } else {
      const ok = await confirm({
        title: t('contacts.unblock.confirm'),
        text: t('contacts.unblock.confirm.text', { name: c.user?.nickname ?? t('contacts.peer') }),
        confirmText: t('contacts.unblock'),
        danger: false,
      })
      if (!ok) return
    }
    try {
      await contacts.toggleBlock(c.id, next)
      snackMsg.value = t(next ? 'contacts.blocked.ok' : 'contacts.unblocked.ok', { name: c.user?.nickname ?? t('contacts.peer') })
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? t('contacts.op.fail')
    }
    contactMenu.value = false
    snack.value = true
  }

  async function removeContact () {
    const c = menuContact.value
    if (!c) return
    const ok = await confirm({
      title: t('contacts.deleteFriend.confirm'),
      text: t('contacts.deleteFriend.confirm.text', { name: c.user?.nickname ?? t('contacts.peer') }),
      confirmText: t('common.delete'),
    })
    if (!ok) return
    try {
      await contacts.removeFriend(c.id)
      snackMsg.value = t('contacts.deleteFriend.ok', { name: c.user?.nickname ?? '' })
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? t('contacts.deleteFriend.fail')
    }
    contactMenu.value = false
    snack.value = true
  }

  const keywordMode = computed(() => kw.value.trim().length > 0)

  /** 群聊未读消息总数：所有群会话(kind==='group')的 unread 求和。
   *  红点语义 = 消息数（不是群数量）；实时消息进来会实时更新 sessions[].unread。 */
  const groupUnread = computed(() =>
    conversations.sessions.filter(s => s.kind === 'group').reduce((sum, s) => sum + (s.unread || 0), 0))

  /** 我参加的所有群（数据源 contacts.groups，onMounted 已 loadGroups）。 */
  const myGroups = computed(() => contacts.groups)

  /** 某群的未读总数（按 group conversation 的 memberId=groupId 匹配会话 unread）。 */
  function groupUnreadOf (g: Group) {
    const s = conversations.sessions.find(x => x.kind === "group" && x.memberId === g.id)
    return s?.unread || 0
  }

  /** 点群进入该群聊天详情（GroupPane 路由 name: group）。 */
  function openGroup (g: Group) {
    groupsSheet.value = false
    router.push({ name: "group", params: { id: g.id } })
  }

  const filtered = computed(() => {
    const k = kw.value.trim().toLowerCase()
    return contacts.items
      .filter(c =>
        c.user?.nickname?.toLowerCase().includes(k)
        || (c.user?.phone ?? '').includes(k)
        || (c.remark ?? '').toLowerCase().includes(k))
      .map(c => ({ id: c.id, title: c.remark || c.user?.nickname || '', subtitle: c.user?.phone }))
  })

  const grouped = computed<Record<string, Contact[]>>(() => {
    const map: Record<string, Contact[]> = {}
    for (const c of contacts.sorted) {
      const key = c.firstLetter || '#'
      if (!map[key]) map[key] = []
      map[key].push(c)
    }
    return map
  })

  function openContact (c: Contact) {
    if (c.user?.id) router.push({ name: 'contact-profile', params: { id: c.user.id } })
  }

  function openById (item: { id: string }) {
    const c = contacts.items.find(x => x.id === item.id)
    if (c) openContact(c)
  }

  /** 只显示实际存在的字母，避免右侧拖出一长条 A-Z */
  const availableLetters = computed(() => Object.keys(grouped.value).toSorted())

  function scrollToLetter (l: string) {
    const el = listRef.value?.querySelector(`#letter-${l}`)
    el?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }

  // 搜索为纯本地过滤（filtered computed）：此前 onSearch 调 contacts.load(kw)
  // 会把 store.items 替换成搜索结果，退出搜索态后通讯录不恢复，且每键一次请求

  /** 通过/拒绝好友申请：失败必须给反馈，否则用户点了没反应会反复点。 */
  async function answerRequest (id: string, accept: boolean) {
    const target = contacts.requests.find(r => r.id === id)
    try {
      if (accept) await contacts.acceptRequest(id)
      else await contacts.rejectRequest(id)
      snackMsg.value = accept ? t('contacts.request.accepted', { name: target?.from?.nickname ?? t('contacts.peer') }) : t('contacts.request.rejected')
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? t('contacts.op.retry')
    }
    snack.value = true
  }

  async function submitAdd () {
    try {
      await contacts.addFriend(newPhone.value, newMsg.value)
      snackMsg.value = t('contacts.request.sent')
      addSheet.value = false
      newPhone.value = ''
      newMsg.value = ''
      await contacts.loadRequests()
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? t('scan.fail')
    }
    snack.value = true
  }

  onMounted(async () => {
    // 通讯录页本身展示「新朋友 / 群聊 / 联系人」；群聊红点是"群未读消息总数"，
    // 未读数在 conversations store 里 —— 挂载时拉一次会话列表让它可用，
    // 之后实时消息进来 applyIncoming 会自动更新 sessions[].unread（红点随之实时变化）。
    conversations.loadSessions().catch(() => {})
    await Promise.allSettled([contacts.load(), contacts.loadIndex(), contacts.loadRequests(), contacts.loadGroups()])
  })
</script>

<style scoped>
/* 标题行：标题绝对居中，右侧按钮靠右 */
.wx-pane-titlebar {
  position: relative;
  display: flex;
  align-items: center;
  min-height: 32px;
  width: 100%;
}

.wx-alpha-float {
  position: absolute;
  right: 4px;
  top: 50%;
  transform: translateY(-50%);
  z-index: 3;
}

.wx-letter-head {
  position: sticky;
  top: 0;
  z-index: 2;
  background: rgb(var(--v-theme-wechat-chat-bg));
  /* 补内缩：之前紧贴左边框，字母和头像都对不齐 */
  padding-left: 16px !important;
  min-height: 28px;
  display: flex;
  align-items: center;
  font-size: 12px;
  color: rgb(var(--v-theme-wechat-text-secondary));
}

.wx-entry-list {
  border-bottom: 1px solid rgb(var(--v-theme-wechat-divider));
}
</style>
