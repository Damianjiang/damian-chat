<template>
  <v-sheet class="wx-chat-surface wx-panes" flat>
    <!-- 顶栏 -->
    <v-app-bar class="wx-chat-bar" color="surface" density="compact" flat>
      <v-btn v-if="isMobile" icon="mdi-arrow-left" variant="text" @click="goBack" />

      <user-avatar
        v-if="!isMobile && peerAvatar"
        class="mr-2"
        :nickname="title"
        :size="30"
        :src="peerAvatar"
      />

      <v-toolbar-title class="text-body-large font-weight-medium">{{ title }}</v-toolbar-title>

      <!-- 对方正在输入…（微信式：顶栏下方小字；5s 无新信号自动消失） -->
      <span v-if="typingName" class="wx-typing-tip">
        <span class="wx-typing-dot" />
        {{ t('chat.typing') }}
      </span>

      <div class="d-flex align-center ml-auto">
        <v-btn
          v-if="isGroup"
          icon="mdi-cog-outline"
          size="small"
          :title="t('chat.group.manage')"
          variant="text"
          @click="router.push({ name: 'group', params: { id: session?.memberId }, query: { convId } })"
        />

        <v-menu>
          <template #activator="{ props }">
            <v-btn
              v-bind="props"
              icon="mdi-dots-vertical"
              size="small"
              :title="t('chat.more')"
              variant="text"
            />
          </template>

          <v-list density="compact">
            <v-list-item v-if="isGroup" prepend-icon="mdi-account-multiple" :title="t('chat.menu.members')" @click="openGroupMembers" />
            <v-list-item prepend-icon="mdi-magnify" :title="t('group.search.msg')" @click="searchOn = !searchOn" />
            <v-list-item prepend-icon="mdi-sync" :title="t('chat.menu.refresh')" @click="refreshMessages" />
            <!-- 清空：群聊仅群主/管理员可操作（普通成员隐藏，服务端也会 403）；私聊成员可清 -->
            <v-list-item v-if="!isGroup || canManageGroup" prepend-icon="mdi-broom" :title="t('sess.clear')" @click="clearDialog = true" />

            <v-divider />

            <v-list-item :prepend-icon="session?.muted ? 'mdi-volume-off' : 'mdi-volume-high'" :title="session?.muted ? t('sess.unmute') : t('sess.mute')" @click="toggleMute" />
            <v-list-item :prepend-icon="session?.pinned ? 'mdi-pin-off' : 'mdi-pin'" :title="session?.pinned ? t('sess.unpin') : t('sess.pin')" @click="togglePin" />

            <template v-if="isGroup">
              <v-divider />
              <v-list-item :prepend-icon="session?.folded ? 'mdi-folder-move' : 'mdi-folder-lock-open'" :title="session?.folded ? t('chat.fold.restore') : t('sess.fold')" @click="toggleFold" />
            </template>
          </v-list>
        </v-menu>
      </div>
    </v-app-bar>

    <!-- 多选模式操作条 -->
    <div v-if="multiMode" class="wx-multi-bar">
      <v-btn icon="mdi-close" size="small" variant="text" @click="exitMulti" />
      <span class="text-body-medium">{{ t('chat.multi.selected', { n: selectedIds.size }) }}</span>
      <v-spacer />
      <v-btn :disabled="selectedIds.size === 0" size="small" variant="text" @click="batchDelete">{{ t('common.delete') }}</v-btn>
    </div>

    <!-- 查找聊天记录（紧凑搜索行） -->
    <v-text-field
      v-if="searchOn"
      v-model="searchKw"
      autofocus
      class="wx-search-bar mx-2 my-1"
      clearable
      density="compact"
      hide-details
      :placeholder="t('chat.search.ph')"
      prepend-inner-icon="mdi-magnify"
      variant="solo"
      @keydown.esc="closeSearch"
    />

    <!-- 群成员面板（电脑右侧滑出） -->
    <v-navigation-drawer
      v-if="showMembers && isGroup"
      class="wx-group-panel"
      right
      temporary
      width="320"
    >
      <v-list>
        <v-list-item v-for="m in groupMembers" :key="m.userId">
          <template #prepend>
            <v-avatar :color="avatarColor(m.userId)"><span>{{ m.nickname.slice(0, 1) }}</span></v-avatar>
          </template>

          <v-list-item-title>
            {{ m.nickname }}
            <v-chip v-if="m.role === 'owner'" class="ml-2" size="x-small">{{ t('group.role.owner') }}</v-chip>
            <v-chip v-else-if="m.role === 'admin'" class="ml-2" size="x-small">{{ t('group.role.admin') }}</v-chip>
            <v-chip v-if="m.title" class="ml-2" size="x-small" color="secondary" variant="tonal">{{ m.title }}</v-chip>
          </v-list-item-title>
        </v-list-item>
      </v-list>
    </v-navigation-drawer>

    <!-- 群公告：置顶在「所有聊天记录」之上。
         规则：有公告且「我」未确认 → 展示横幅（可确认/看全文）；
         「我」已确认 → 自动隐藏，只留一个「查看公告」入口；
         公告被修改/新增（版本提升）→ 我的旧确认自动失效 → 重新展示并需再确认。 -->
    <div v-if="isGroup && group?.notice && !noticeConfirmedMe" class="wx-notice-banner pa-2">
      <div class="d-flex align-center justify-space-between text-body-medium gap-2">
        <div class="d-flex align-center gap-2 text-body-medium" @click="noticeDialog = true">
          <v-icon color="primary" icon="mdi-bullhorn" size="20" />
          <span class="text-medium-emphasis">{{ t('group.notice') }}：</span>
          <span class="text-truncate text-body-medium" style="max-width: 55%; cursor: pointer">{{ group.notice }}</span>
          <v-icon class="ml-1" color="primary" icon="mdi-information-outline" size="18" :title="t('chat.notice.viewFull')" />
        </div>
        <div class="d-flex align-center gap-2 flex-shrink-0">
          <span v-if="noticeProgress.total > 0" class="text-body-small text-medium-emphasis">
            {{ t('group.notice.confirmed') }} {{ noticeProgress.confirmed }}/{{ noticeProgress.total }}
          </span>
          <v-btn
            color="primary"
            density="compact"
            size="x-small"
            variant="flat"
            @click="confirmNotice"
          >{{ t('group.notice.confirm') }}</v-btn>
        </div>
      </div>
    </div>

    <!-- 已确认：横幅自动隐藏，留一个可点入口看公告全文 -->
    <div v-if="isGroup && group?.notice && noticeConfirmedMe" class="wx-notice-collapsed pa-1 px-2" @click="noticeDialog = true">
      <div class="d-flex align-center gap-1 text-body-small text-medium-emphasis">
        <v-icon color="primary" icon="mdi-bullhorn" size="16" />
        <span>{{ t('chat.notice.view') }}</span>
        <v-icon color="primary" icon="mdi-chevron-right" size="16" />
      </div>
    </div>

    <!-- 群公告全文弹窗：公告过长时点顶部横幅正文打开，完整可读 -->
    <v-dialog v-model="noticeDialog" max-width="460">
      <v-card>
        <v-card-title class="text-body-large d-flex align-center gap-2">
          <v-icon color="primary" icon="mdi-bullhorn" size="22" />
          {{ t('group.notice') }}
        </v-card-title>
        <v-card-text class="wx-notice-full text-body-medium" style="white-space: pre-wrap; word-break: break-word; max-height: 40vh; overflow-y: auto">
          {{ group?.notice }}
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn v-if="!noticeConfirmedMe" color="primary" variant="text" @click="noticeDialog = false; confirmNotice()">{{ t('group.notice.confirm') }}</v-btn>
          <v-btn variant="text" @click="noticeDialog = false">{{ t('common.close') }}</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 消息区 -->
    <div ref="listRef" class="flex-grow-1 overflow-y-auto wx-scroll wx-msg-area" @scroll.passive="onMsgScroll">
      <!-- 正在看历史时不抢滚动，只在这里提示（通用做法） -->
      <button v-if="newCount > 0" class="wx-new-tip" type="button" @click="jumpToNew">
        {{ t('chat.newMsgs', { n: newCount }) }}
      </button>

      <v-btn
        v-if="hasMoreHistory"
        class="mx-auto my-2"
        color="primary"
        :loading="store.historyLoading"
        size="x-small"
        variant="text"
        @click="loadEarlier"
      >{{ t('chat.load.earlier') }}</v-btn>

      <!-- 打开会话时的加载骨架：此前首帧直接显示「暂无聊天记录」，等历史回来才换成消息，
           中间那一下空态闪现看着像"数据丢了" -->
      <div v-if="firstLoading" class="wx-loading-wrap">
        <div v-for="i in 5" :key="i" class="wx-skeleton wx-skeleton-row" :style="{ width: `${[62, 48, 70, 55, 40][i - 1]}%`, alignSelf: i % 2 === 0 ? 'flex-end' : 'flex-start' }" />
      </div>
      <template v-else-if="groupedByDate.length > 0">

        <template v-for="g in groupedByDate" :key="g.key">
          <div class="wx-time-divider"><span>{{ g.label }}</span></div>

          <!-- key 用 localKey（乐观气泡）优先：服务端确认后就地替换对象但 key 不变，
               节点不重新挂载，进场动画不会重播（此前用 id 会"闪一下"） -->
          <message-bubble
            v-for="(m, mi) in g.messages"
            :key="m.localKey ?? m.id"
            :avatar="avatarOf(m.senderId)"
            :is-group="isGroup"
            :me-id="meId"
            :message="m"
            :selected="multiMode && selectedIds.has(m.id)"
            :sender-name="displayName(m.senderId)"
            :show-time="needsTime(g.messages, mi)"
            :pat-target-name="patTargetName(m)"
            @copied="onCopied"
            @delete="onDelete"
            @favorite="onFavorite"
            @forward="onForward"
            @preview="openPreview"
            @quote="onQuote"
            @recall="onRecall"
            @retry="onRetry"
            @select="onSelectOne"
            @avatar-tap="onAvatarTap"
            @pat="onPat"
          />
        </template>
      </template>

      <div v-else-if="!hasMoreHistory && !firstLoading" class="d-flex flex-column align-center justify-center pa-8 text-medium-emphasis">
        <div class="text-body-medium">{{ t('sess.empty') }}</div>
        <div class="text-body-small mt-1">{{ t('chat.empty.hint') }}</div>
      </div>

      <div v-if="quoteDraft" class="wx-quote-preview pa-2 d-flex align-center justify-space-between text-body-small text-medium-emphasis">
        <span>{{ t('chat.replying', { who: quoteDraft.senderName, text: quoteDraft.text }) }}</span>
        <v-btn icon="mdi-close" size="x-small" @click="quoteDraft = null" />
      </div>

      <div v-if="searchOn && searchKw && groupedByDate.every(g => g.messages.length === 0)" class="text-center text-body-small text-medium-emphasis pa-6">
        {{ t('chat.search.none') }}
      </div>
    </div>

    <!-- 群禁言（QQ 式）：全员禁言 / 被限时或永久禁言时，用提示条替换输入框 -->
    <div v-if="muteBanner" class="wx-notice-banner pa-3 text-center text-body-medium">{{ muteBanner }}</div>
    <!-- 输入区（工具行常驻：图片/文件/语音/投票） -->
    <chat-input-toolbar
      v-else
      :is-group="isGroup"
      :members="groupMembers"
      :placeholder="t('input.message.ph')"
      show-toolbar-above
      ref="toolbarRef"
      @send="onSend"
      @typing="onSelfTyping"
      @poll="openPollDialog"
    />

    <!-- 发起群投票（微信式：标题 + 选项，单选/多选） -->
    <v-dialog v-model="pollDialog" max-width="380">
      <v-card>
        <v-card-title class="text-body-large">{{ t('poll.create') }}</v-card-title>
        <v-card-text>
          <v-text-field v-model="pollTitle" autofocus density="compact" :label="t('poll.title.ph')" :placeholder="t('poll.title.ph')" variant="outlined" />
          <div class="d-flex flex-column ga-2">
            <v-text-field
              v-for="(_, i) in pollOptions"
              :key="i"
              v-model="pollOptions[i]"
              density="compact"
              hide-details
              :label="t('poll.option.ph', { n: i + 1 })"
              :placeholder="t('poll.option.ph', { n: i + 1 })"
              variant="outlined"
            >
              <template #append-inner>
                <v-icon
                  v-if="pollOptions.length > 2"
                  icon="mdi-close-circle"
                  size="18"
                  @click="pollOptions.splice(i, 1)"
                />
              </template>
            </v-text-field>
          </div>
          <v-btn
            class="mt-2"
            color="primary"
            density="compact"
            :disabled="pollOptions.length >= 20"
            prepend-icon="mdi-plus"
            variant="text"
            @click="pollOptions.push('')"
          >{{ t('poll.addOption') }}</v-btn>
          <v-checkbox v-model="pollMulti" class="mt-0" color="primary" density="compact" hide-details :label="t('poll.multi')" />
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="pollDialog = false">{{ t('common.cancel') }}</v-btn>
          <v-btn color="primary" :disabled="!pollValid" :loading="pollCreating" @click="createPoll">{{ t('common.confirm') }}</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 图片 / 视频 / 音频预览 -->
    <media-previewer v-model="previewOpen" :alt="previewAlt" :kind="previewKind" :url="previewUrl" />

    <!-- 转发选择 -->
    <v-bottom-sheet v-model="forwardOpen" max-width="400" scrollable>
      <v-card class="pa-2">
        <div class="text-title-small pa-2">{{ t('chat.forward.to') }}</div>

        <v-list density="compact">
          <v-list-item
            v-for="s in store.sessions"
            :key="s.id"
            :title="s.title"
            @click="doForward(s)"
          >
            <template #prepend>
              <v-avatar :color="avatarColor(s.id)" :size="32">
                <span>{{ s.title.slice(0, 1) }}</span>
              </v-avatar>
            </template>
          </v-list-item>
        </v-list>
      </v-card>
    </v-bottom-sheet>

    <!-- 清空聊天记录确认 -->
    <v-dialog v-model="clearDialog" max-width="300">
      <v-card>
        <v-card-title class="text-body-large">{{ t('sess.clear.title') }}</v-card-title>
        <v-card-text class="text-body-small text-medium-emphasis">{{ t('chat.clear.text') }}</v-card-text>

        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="clearDialog = false">{{ t('common.cancel') }}</v-btn>
          <v-btn color="red" :loading="clearing" @click="clearMessages">{{ t('sess.clear.btn') }}</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <v-snackbar v-model="snack" :timeout="1800">{{ snackMsg }}</v-snackbar>
  </v-sheet>
</template>

<script setup lang="ts">
  import type { Conversation, Group, Message } from '@/types'
  import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
  import { useRoute, useRouter } from 'vue-router'
  import ChatInputToolbar from '@/components/ChatInputToolbar.vue'
  import MediaPreviewer from '@/components/MediaPreviewer.vue'
  import MessageBubble from '@/components/MessageBubble.vue'
  import UserAvatar from '@/components/UserAvatar.vue'
  import { api } from '@/lib/api'
  import { confirm } from '@/lib/confirm'
  import { isPageMode } from '@/lib/device'
  import { fileUrl } from '@/lib/files'
  import { t } from '@/lib/i18n'
  import { sendTyping } from '@/lib/signalr'
  import { avatarColor, messageSummary, timeLabel } from '@/lib/utils'
  import { useAuthStore } from '@/stores/auth'
  import { useConversationsStore } from '@/stores/conversations'
  import { usePollsStore } from '@/stores/polls'

  const route = useRoute()
  const router = useRouter()
  const store = useConversationsStore()
  const auth = useAuthStore()

  const isMobile = isPageMode
  const meId = computed(() => auth.user?.id ?? 'me')

  const convId = computed(() => String(route.params.id ?? ''))
  const session = computed(() => store.sessions.find(s => s.id === convId.value))
  const isGroup = computed(() => session.value?.kind === 'group')
  const title = computed(() => session.value?.title ?? t('chat.fallback.title'))

  /* ---------- 群禁言状态（QQ 式）：全员禁言 / 我被限时或永久禁言 → 输入框换成提示条 ---------- */
  const myGroupMember = computed(() => group.value?.members.find(m => m.userId === meId.value))
  const amMuted = computed(() =>
    !!myGroupMember.value?.muted
    || !!(myGroupMember.value?.mutedUntil && new Date(myGroupMember.value.mutedUntil) > new Date()))
  const muteBanner = computed(() => {
    if (!isGroup.value) return ''
    if (amMuted.value) {
      const until = myGroupMember.value?.mutedUntil
      if (until && new Date(until) > new Date()) {
        const mins = Math.max(1, Math.ceil((new Date(until).getTime() - Date.now()) / 60_000))
        return t('chat.muted.me.until', { n: mins })
      }
      return t('chat.muted.me')
    }
    if (group.value?.muteAll && myGroupMember.value?.role === 'member') return t('chat.muted.all')
    return ''
  })

  /** 顶栏对方头像（PC 双栏时展示） */
  const peerAvatar = computed(() => session.value?.avatar ?? '')

  const listRef = ref<HTMLElement | null>(null)
  const previewOpen = ref(false)
  const previewUrl = ref('')
  const previewAlt = ref('')
  const previewKind = ref<'image' | 'video' | 'audio'>('image')
  /** 输入工具栏引用：引用消息/公告插入后聚焦输入框，让用户直接可打字 */
  const toolbarRef = ref<{ append: (t: string) => void, focus: () => void } | null>(null)
  const showMembers = ref(false)
  const quoteDraft = ref<{ senderName: string, text: string, senderId: string, origType: string } | null>(null)
  const forwardOpen = ref(false)
  const forwardMsg = ref<Message | null>(null)
  const snack = ref(false)
  const snackMsg = ref('')
  /** 当前群公告「我」是否已确认（来自 group.noticeReadByMe，确认后置位） */
  const noticeConfirmedMe = ref(false)
  /** 群公告全文弹窗（正文过长时点击横幅打开） */
  const noticeDialog = ref(false)
  const refreshing = ref(false)

  const group = ref<Group | null>(null)
  const groupMembers = computed(() => group.value?.members ?? [])

  /** 群公告「我」在群里的角色是否可管理（群主/管理员）：决定显示「确认」按钮还是直接「已确认」 */
  const canManageGroup = computed(() => {
    if (!isGroup.value || !session.value) return false
    const myRole = group.value?.members.find(m => m.userId === meId.value)?.role ?? 'member'
    return myRole === 'owner' || myRole === 'admin'
  })

  /** 群公告确认进度：实时 NoticeConfirmed 事件优先，否则用群详情的静态统计。 */
  const noticeProgress = computed(() => {
    const live = isGroup.value && store.noticeConfirmed[convId.value]
    if (live) return { confirmed: live.confirmed, total: live.total }
    const g = group.value as any
    if (g?.noticeReadCount != null) return { confirmed: g.noticeReadCount, total: g.noticeTotal ?? 0 }
    return { confirmed: 0, total: 0 }
  })

  const nameMap = ref<Record<string, string>>({})
  const avatarMap = ref<Record<string, string>>({})

  // 窗口化渲染：默认显示最近 60 条，「显示更早的消息」按 60 条增量向上拉取
  const displayLimit = ref(60) // 窗口化：首屏 60 条，「更早」按 60 增量
  const allMessages = computed<Message[]>(() => store.messagesByConv[convId.value] ?? [])
  const visibleMessages = computed(() =>
    allMessages.value.length > displayLimit.value
      ? allMessages.value.slice(allMessages.value.length - displayLimit.value)
      : allMessages.value)

  const hasMoreHistory = computed(() =>
    allMessages.value.length > displayLimit.value || !!store.historyHasMore[convId.value])

  /**
   * 首屏加载中：一条消息都还没有、且（首次打开未完成 或 正在拉历史）→ 显示骨架。
   * 只靠 store.historyLoading 不够：openConversation 里先读 IDB 再拉历史，
   * 那段空窗 historyLoading 还是 false，空态会先闪一下。
   */
  const initializing = ref(true)
  const firstLoading = computed(() =>
    (initializing.value || store.historyLoading) && allMessages.value.length === 0)

  /** 翻历史页的防重入：自动触发（滚到顶）+ 手动按钮共用同一闸门 */
  const loadingEarlier = ref(false)

  async function loadEarlier () {
    if (loadingEarlier.value) return
    loadingEarlier.value = true
    const list = listRef.value
    const beforeH = list?.scrollHeight ?? 0
    suppressScroll = true
    try {
      if (store.historyHasMore[convId.value]) {
        const oldestVisible = allMessages.value.length > displayLimit.value
          ? allMessages.value[allMessages.value.length - displayLimit.value]
          : allMessages.value[0]
        // cap 放大到「可见窗口 + 本页余量」：否则内存 500 条封顶时，
        // 刚拉回来的更早消息会被 keep-newest 淘汰掉，翻页永远不刷新
        await store.loadHistory(convId.value, oldestVisible?.id, { cap: displayLimit.value + 90 })
      }
      displayLimit.value += 60
      await nextTick(() => {
        const el = listRef.value
        if (!el) return
        // 锚定视口：新内容从顶部插入，把新增高度补回 scrollTop，视线不跳
        //（此前直接归零 scrollTop，每次翻页都"闪"回最顶部）
        const delta = el.scrollHeight - beforeH
        if (delta > 0) el.scrollTop += delta
        else el.scrollTop = 0
      })
    } finally {
      suppressScroll = false
      loadingEarlier.value = false
    }
  }

  /** 距离上一条消息超过 5 分钟才显示时间戳，避免每条都带时间很吵 */
  function needsTime (list: Message[], index: number): boolean {
    if (index <= 0) return true
    const prev = list[index - 1]
    const cur = list[index]
    if (!prev?.createdAt || !cur?.createdAt) return false
    return (new Date(cur.createdAt).getTime() - new Date(prev.createdAt).getTime()) > 5 * 60 * 1000
  }

  const groupedByDate = computed(() => {
    const msgs = filteredMessages.value
    // key = 日期标签本身（稳定）：用下标做 key 时向前翻页 prepend 历史，
    // 所有分组下标整体位移，每个分组节点都被就地打补丁（换 label + 重 diff 内部消息），白白放大重渲染
    const groups: { key: string, label: string, messages: Message[] }[] = []
    let current: { key: string, label: string, messages: Message[] } | null = null
    for (const m of msgs) {
      const label = timeLabel(m.createdAt)
      // 追踪当前分组，免掉每条消息一次 groups.at(-1)（长会话每次新消息全量重算时是纯开销）
      if (!current || label !== current.label) {
        current = { key: label, label, messages: [] }
        groups.push(current)
      }
      current.messages.push(m)
    }
    return groups
  })

  /* ---------- 查找聊天记录 ---------- */
  const searchOn = ref(false)
  const searchKw = ref('')

  function closeSearch () {
    searchOn.value = false
    searchKw.value = ''
  }

  // 有新消息（收/发）时自动退出搜索态，保证消息始终可见
  // （原逻辑搜索过滤会把不含关键词的新消息“藏”起来，看起来像收发失败）
  watch(() => allMessages.value.length, (n, o) => {
    if (searchOn.value && n > (o ?? 0)) closeSearch()
  })

  const filteredMessages = computed(() => {
    const kw = searchKw.value.trim().toLowerCase()
    if (!searchOn.value || !kw) return visibleMessages.value
    return visibleMessages.value.filter(m =>
      (m.content ?? '').toLowerCase().includes(kw)
      || (m.fileName ?? '').toLowerCase().includes(kw))
  })

  /* ---------- 清空聊天记录 ---------- */
  const clearDialog = ref(false)
  const clearing = ref(false)

  async function clearMessages () {
    clearing.value = true
    try {
      await api.delete(`/conversations/${convId.value}/messages`)
      // 用 store.clearConversationLocal 统一清「内存 + 会话预览 + IDB 缓存」，
      // 否则本地 IDB 里还留着旧消息，重开会话会被 idbGetMessages 回填 → "清空后又刷回来"。
      store.clearConversationLocal(convId.value)
      displayLimit.value = 60
      clearDialog.value = false
      snackMsg.value = t('chat.clear.ok')
    } catch {
      snackMsg.value = t('chat.clear.fail')
    } finally {
      clearing.value = false
      snack.value = true
    }
  }

  function displayName (id: string) {
    if (id === meId.value) return t('msg.pat.me')
    return nameMap.value[id] ?? groupMembers.value.find(x => x.userId === id)?.nickname ?? (isGroup.value ? t('msg.pat.group') : t('msg.pat.other'))
  }

  function avatarOf (id: string) {
    // 群聊/单聊里「我」发的消息：自己的头像不在群成员/联系人 map 里，
    // 直接用登录用户资料里的头像 fileId（MessageBubble 会经 fileUrl 解析渲染）。
    if (id === meId.value) return auth.user?.avatar ?? ''
    return avatarMap.value[id] ?? ''
  }

  /** 拍一拍文案里的「被拍者」名字：群成员昵称 / 单聊对方名；空则兜底 */
  function patTargetName (m: Message): string {
    const targetId = m.patTarget
    if (!targetId) return ''
    if (targetId === meId.value) return t('msg.pat.me')
    return displayName(targetId)
  }

  /** 单击对方头像 → 看该成员/对方的资料卡（群聊带 gid + convId，返回时能原路回群设置/会话） */
  function onAvatarTap (m: Message) {
    const targetId = m.senderId
    if (!targetId || targetId === meId.value) return
    if (isGroup.value && session.value) {
      router.push({
        name: 'contact-profile',
        params: { id: targetId },
        query: { from: 'group', gid: session.value.memberId, convId: convId.value },
      })
    } else {
      router.push({ name: 'contact-profile', params: { id: targetId } })
    }
  }

  /** 双击对方头像（群聊）→ 发一条「我拍了拍 XX」消息（乐观上屏，服务端确认后替换） */
  function onPat (m: Message) {
    const targetId = m.senderId
    if (!targetId || targetId === meId.value || !isGroup.value || !session.value) return
    const payload: Partial<Message> = { type: 'pat', patTarget: targetId }
    store.send(session.value, payload).catch(() => {
      snackMsg.value = t('chat.pat.fail')
      snack.value = true
    })
    scrollBottom()
  }

  /* ---------- 对方正在输入（微信式提示） ---------- */
  const typingUser = ref<{ userId: string, at: number } | null>(null)
  let typingClearTimer: ReturnType<typeof setTimeout> | null = null
  watch(() => store.typingBy[convId.value], t => {
    if (!t) { typingUser.value = null; return }
    typingUser.value = t
    if (typingClearTimer) clearTimeout(typingClearTimer)
    // 微信式：5s 无新输入信号自动消失
    typingClearTimer = setTimeout(() => { typingUser.value = null }, 5000)
  })
  const typingName = computed(() => typingUser.value
    ? (nameMap.value[typingUser.value.userId] || t('msg.pat.other'))
    : '')

  /** 自己敲字 → 通过实时通道广播「我正在输入」（组件内部已按 3s 节流） */
  function onSelfTyping () {
    if (convId.value) void sendTyping(convId.value)
  }

  /* ---------- 群投票（微信式）：打开群聊拉全量；创建后以 poll 消息卡片进聊天流 ---------- */
  const pollDialog = ref(false)
  const pollTitle = ref('')
  const pollOptions = ref<string[]>(['', ''])
  const pollMulti = ref(false)
  const pollCreating = ref(false)
  const pollValid = computed(() =>
    pollTitle.value.trim().length > 0
    && pollOptions.value.filter(o => o.trim()).length >= 2)

  function openPollDialog () {
    pollTitle.value = ''
    pollOptions.value = ['', '']
    pollMulti.value = false
    pollDialog.value = true
  }

  async function createPoll () {
    if (!pollValid.value || !isGroup.value || pollCreating.value) return
    pollCreating.value = true
    try {
      const res = await api.post(`/groups/${convId.value}/polls`, {
        title: pollTitle.value.trim(),
        options: pollOptions.value.map(o => o.trim()).filter(o => o),
        multi: pollMulti.value,
      })
      pollDialog.value = false
      // 投票卡片消息进聊天流（content = pollId），全员实时可见、可投
      let s = session.value
      if (!s) {
        s = {
          id: convId.value,
          kind: 'group',
          title: title.value,
          avatar: '',
          memberId: convId.value,
          unread: 0,
          pinned: false,
          muted: false,
        } as Conversation
      }
      store.send(s, { type: 'poll', content: res.data.id } as Partial<Message>).catch(() => {})
      usePollsStore().load(convId.value)
      snackMsg.value = t('poll.create.ok')
      snack.value = true
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? t('poll.create.fail')
      snack.value = true
    } finally {
      pollCreating.value = false
    }
  }

  function onSend (payload: Record<string, any>) {
    // ★ 关键修复：从「通讯录」点进新会话时，该会话刚由服务端 ensure 创建，尚未同步进
    //   store.sessions（loadSessions 还没跑完 / 或新开的页没拉），此时 session 为 null，
    //   旧逻辑直接 return —— 用户点「发消息」却毫无反应 / 发送失败。
    //   这里用 convId + 路由参数兜底构造一个最小 Conversation，保证能发出消息。
    let s = session.value
    if (!s) {
      s = {
        id: convId.value,
        kind: isGroupFromRoute() ? 'group' : 'private',
        title: title.value,
        avatar: '',
        memberId: isGroupFromRoute() ? (group.value?.id ?? convId.value) : convId.value,
        unread: 0,
        pinned: false,
        muted: false,
      } as Conversation
    }
    if (!s) return
    if (quoteDraft.value) payload.quoteJson = JSON.stringify(quoteDraft.value)
    quoteDraft.value = null
    // 搜索态下自己刚发的消息可能被关键词过滤掉（看着像发送失败），发了就退出搜索
    if (searchOn.value) closeSearch()
    // 乐观发送：气泡已即时上屏，失败时 store 会把它标记为 failed（点红色 ! 可重试）
    store.send(s, payload as Partial<Message>).catch(() => {})
    // 自己发的消息始终滚到底部（微信行为，即使在翻历史记录）
    scrollBottom()
  }

  /** 路由是否为群聊会话（兜底构造 Conversation 时用）：群会话 id 即群 id。 */
  function isGroupFromRoute () {
    return group.value?.id === convId.value
  }

  /** 重发失败消息（保持 localKey，气泡原地更新） */
  async function onRetry (m: Message) {
    if (!m.localKey) return
    await store.retrySend(convId.value, m.localKey)
  }

  function onCopied (_text: string) {
    snackMsg.value = t('common.copied')
    snack.value = true
  }

  // ---- 多选（PC 右键 / 手机长按 → 多选 → 批量删除/收藏） ----
  const multiMode = ref(false)
  const selectedIds = ref<Set<string>>(new Set())

  function onSelectOne (m: Message) {
    multiMode.value = true
    const next = new Set(selectedIds.value)
    if (next.has(m.id)) next.delete(m.id)
    else next.add(m.id)
    selectedIds.value = next
  }

  function exitMulti () {
    multiMode.value = false
    selectedIds.value = new Set()
  }

  async function batchDelete () {
    const ids = [...selectedIds.value]
    if (ids.length === 0) return
    const ok = await confirm({
      title: t('chat.multi.delete.title'),
      text: t('chat.multi.delete.text', { n: ids.length }),
      confirmText: t('common.delete'),
    })
    if (!ok) return
    let okn = 0
    for (const id of ids) {
      try {
        await api.delete(`/messages/${id}`)
        okn++
        // 统一走 store.removeMessage：清内存 + 会话预览 + IDB 单条缓存，
        // 避免被删消息从本地缓存"刷新回来"。
        store.removeMessage(convId.value, id)
      } catch { /* 单条失败继续 */ }
    }
    snackMsg.value = t('chat.multi.delete.ok', { n: okn })
    snack.value = true
    exitMulti()
  }

  async function onDelete (m: Message) {
    const ok = await confirm({
      title: t('chat.delete.title'),
      text: t('chat.delete.text', { s: (m.content ?? m.fileName ?? '').slice(0, 40) || t('chat.msg.placeholder') }),
      confirmText: t('common.delete'),
    })
    if (!ok) return
    try {
      await api.delete(`/messages/${m.id}`)
      // 统一走 store.removeMessage：清内存 + 会话预览 + IDB 单条缓存
      store.removeMessage(convId.value, m.id)
      snackMsg.value = t('chat.delete.ok')
      snack.value = true
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? t('chat.delete.fail')
      snack.value = true
    }
  }

  function onFavorite (_m: Message, result: 'added' | 'removed' | 'failed', errorText?: string) {
    snackMsg.value = result === 'added'
      ? t('chat.fav.ok')
      : (result === 'removed'
        ? t('chat.fav.removed')
        : (errorText ?? t('msg.fav.fail')))
    snack.value = true
  }

  function onQuote (m: Message) {
    // 引用（回复）限制 + 安全兜底：
    // ① 自己发的消息不可引用（自己回复自己没意义）—— 全局（单聊/群聊）拒绝；
    // ② 群聊里只允许引用「文字」消息，文件/图片/语音/表情/拍一拍一律不可引用。
    // 菜单项已限制（MessageBubble 仅 !isMe && type==='text' 才显示"回复"），这里是双保险。
    if (m.senderId === meId.value) {
      snackMsg.value = t('chat.quote.self')
      snack.value = true
      return
    }
    if (isGroup.value && m.type !== 'text') {
      snackMsg.value = t('chat.quote.textOnly')
      snack.value = true
      return
    }
    // 引用摘要：图片/文件/语音/表情用类型化摘要（避免图片被引成"消息"），文本用内容
    const text = m.type === 'text' ? (m.content || t('chat.msg.placeholder')) : messageSummary(m)
    quoteDraft.value = {
      senderName: displayName(m.senderId),
      text,
      senderId: m.senderId,
      origType: m.type,
    }
    // 点了"回复"后把输入框聚焦并弹回底部，让用户直接接着打字（此前聚焦缺失 → 看起来"无反应"）
    nextTick(() => {
      toolbarRef.value?.focus()
      scrollBottom()
    })
  }

  async function onRecall (m: Message) {
    if (m.senderId !== meId.value) return
    await store.recall(m)
  }

  async function openPreview (m: Message) {
    if (m.imageId) {
      // 按消息类型/文件扩展名决定预览器用 <video> / <audio> / <img> 渲染
      previewKind.value = mediaKind(m)
      previewUrl.value = await fileUrl(m.imageId)
      previewOpen.value = true
    }
  }

  /** 消息对应的媒体类型：image → image；file 按扩展名分 video/audio。 */
  function mediaKind (m: Message): 'image' | 'video' | 'audio' {
    if (m.type === 'image') return 'image'
    if (m.type !== 'file') return 'image'
    const n = (m.fileName ?? '').toLowerCase()
    const i = n.lastIndexOf('.')
    const ext = i > 0 ? n.slice(i + 1) : ''
    if (['mp4', 'webm', 'mov', 'm4v', 'mkv', 'avi', '3gp', 'flv', 'ogv'].includes(ext)) return 'video'
    if (['mp3', 'm4a', 'ogg', 'wav', 'aac', 'flac'].includes(ext)) return 'audio'
    return 'image'
  }

  function onForward (m: Message) {
    forwardMsg.value = m
    forwardOpen.value = true
  }

  async function doForward (target: Conversation) {
    const m = forwardMsg.value
    if (!m) return
    // 拍一拍不能跨会话转发（patTarget 是原群成员 id，丢到别的群会被服务端拒收）；
    // 转成一条普通文本「我拍了拍 xx」再发，保留语义又不带非法目标。
    if (m.type === 'pat') {
      const who = m.senderId === meId.value ? t('msg.pat.me') : displayName(m.senderId)
      const payload: Partial<Message> = { type: 'text', content: t('msg.pat', { who, target: displayName(m.patTarget ?? '') }) }
      await store.send(target, payload)
      forwardOpen.value = false
      snackMsg.value = t('chat.forward.ok', { name: target.title })
      return
    }
    const payload: Partial<Message> = { type: m.type }
    if (m.type === 'text') payload.content = m.content
    if (m.type === 'image') payload.imageId = m.imageId
    if (m.type === 'file') {
      payload.fileName = m.fileName
      payload.fileSize = m.fileSize
    }
    if (m.type === 'sticker') payload.stickerEmoji = m.stickerEmoji
    await store.send(target, payload)
    forwardOpen.value = false
    snackMsg.value = t('chat.forward.ok', { name: target.title })
    snack.value = true
  }

  /* ---------- 会话操作：免打扰 / 置顶 / 折叠（写入服务端 + 本地会话同步） ---------- */
  async function toggleMute () {
    if (!session.value) return
    const next = !session.value.muted
    try {
      await store.toggleMute(convId.value, next)
      session.value.muted = next
      snackMsg.value = next ? t('chat.mute.on') : t('chat.mute.off')
      snack.value = true
    } catch { snackMsg.value = t('chat.op.fail') }
  }

  async function togglePin () {
    if (!session.value) return
    const next = !session.value.pinned
    try {
      await store.togglePin(convId.value, next)
      session.value.pinned = next
      snackMsg.value = next ? t('chat.pin.on') : t('chat.pin.off')
      snack.value = true
    } catch { snackMsg.value = t('chat.op.fail') }
  }

  async function toggleFold () {
    if (!session.value || !isGroup.value) return
    const next = !session.value.folded
    try {
      await store.toggleFold(convId.value, next)
      // 折叠后跳回主列表（让被折叠的群从主列表消失）
      snackMsg.value = next ? t('chat.fold.on') : t('chat.fold.off')
      snack.value = true
      router.push({ name: 'chat' })
    } catch { snackMsg.value = t('chat.op.fail') }
  }

  async function loadGroup () {
    if (!isGroup.value || !session.value) return
    try {
      const res = await api.get(`/groups/${session.value.memberId}`)
      group.value = res.data
      // 记录「我」是否已确认群公告（按公告版本判定：公告被改过则旧确认已失效）
      noticeConfirmedMe.value = !!(res.data as any).noticeReadByMe
      for (const m of res.data.members as Group['members']) {
        nameMap.value[m.userId] = m.nickname
        avatarMap.value[m.userId] = m.avatar
      }
    } catch { /* ignore */ }
  }

  /** 群公告被修改/新增（NoticeUpdated 实时事件）：重拉群详情 + 重置确认态 →
   *  公告重新展示、旧确认隐藏，成员需再次确认。仅对「当前打开的群」生效。 */
  watch(
    () => store.noticeVersion[convId.value],
    async v => {
      if (v == null || !isGroup.value) return
      await loadGroup()
    },
  )

  /** 确认群公告：提交后本地置位并刷新进度；全群通过 NoticeConfirmed 实时同步。 */
  async function confirmNotice () {
    if (!session.value || noticeConfirmedMe.value) return
    try {
      const res = await api.post(`/groups/${session.value.memberId}/notice/confirm`)
      noticeConfirmedMe.value = true
      const g = group.value as any
      if (g) {
        g.noticeReadCount = res.data?.confirmed ?? (g.noticeReadCount ?? 0)
        g.noticeTotal = res.data?.total ?? g.noticeTotal
      }
    } catch {
      // 后端暂未部署新接口：退化为本地标记，避免一直卡在「确认」按钮
      noticeConfirmedMe.value = true
    }
  }

  /** 「…」菜单的「群成员」：可靠跳转到群设置页（该页有完整成员宫格 + 管理员/群主徽标 + 邀请/移除/禁言）。
   *  此前在聊天页用临时右侧抽屉展示，PC 双栏下经常"点了没反应"，统一走独立路由。 */
  function openGroupMembers () {
    if (!isGroup.value || !session.value) return
    showMembers.value = false
    router.push({ name: 'group', params: { id: session.value.memberId }, query: { convId: convId.value } })
  }

  /** 手动刷新聊天记录：补拉最新一页 + 刷新会话列表（浏览器卡死/切回标签后补上错过的消息）。 */
  async function refreshMessages () {
    refreshing.value = true
    try {
      await store.refreshConversation(convId.value)
      // 若是群，顺带刷新公告确认进度
      if (isGroup.value && session.value) await loadGroup()
    } finally {
      refreshing.value = false
    }
  }

  async function loadNames () {
    if (isGroup.value || !session.value) return
    const res = await api.get('/contacts').catch(() => null)
    if (res) {
      for (const c of res.data) {
        if (c.user?.id === session.value.memberId) {
          nameMap.value[c.user.id] = c.remark || c.user.nickname
          avatarMap.value[c.user.id] = c.user.avatar
          break
        }
      }
    }
  }

  function goBack () {
    router.push({ name: 'chat' })
  }

  /** 是否开启了「减少动态效果」（平滑滚动也要尊重它） */
  const reduceMotion = typeof window !== 'undefined'
    && !!window.matchMedia?.('(prefers-reduced-motion: reduce)').matches

  /**
   * 滚到底部：离底不远时走 smooth，新消息是"推上来"的，比瞬移跟手；
   * 离得很远（刚从几千条历史的顶部切回来）则直接跳 —— 否则会看一段漫长的滚动动画。
   */
  function scrollBottom (smooth = true) {
    nextTick(() => {
      const el = listRef.value
      if (!el) return
      const distance = el.scrollHeight - el.scrollTop - el.clientHeight
      if (smooth && !reduceMotion && distance < 600) {
        el.scrollTo({ top: el.scrollHeight, behavior: 'smooth' })
      } else {
        el.scrollTop = el.scrollHeight
      }
      atBottom.value = true
      newCount.value = 0
    })
  }

  /**
   * 是否贴着底部（距底 < 60px 算贴着）。
   * 用户往上翻历史时必须保持当前位置，不能被新消息拽下去 —— 主流 IM 都这样。
   */
  const atBottom = ref(true)
  const newCount = ref(0)

  function onMsgScroll () {
    const el = listRef.value
    if (!el) return
    const distance = el.scrollHeight - el.scrollTop - el.clientHeight
    atBottom.value = distance < 60
    if (atBottom.value) newCount.value = 0
    // 滚到顶部附近（< 140px）自动翻上一页历史：不再要求用户手动点「显示更早的消息」
    if (el.scrollTop < 140 && hasMoreHistory.value && !firstLoading.value) {
      void loadEarlier()
    }
  }

  function jumpToNew () {
    scrollBottom()
  }

  /** 图片等媒体加载完成后容器高度变化：贴底状态补一次滚动，避免"看起来没到底"的错位 */
  function onMediaLoad () {
    if (!atBottom.value) return
    scrollBottom(false)
  }

  let suppressScroll = false
  /** 上一次「长度|最后一条id」签名：字符串原始值，拿旧值比对，避免每次新对象触发空跑 */
  const lastSignatureRef = ref<string | null>(null)
  watch(() => {
    const list = store.messagesByConv[convId.value]
    // 无新增（len/lastId 都没变）也要能比对差额：改用「可复用的复元组」，
    // 仅当内容变化才触发（此前每次返回新对象，数组的整体重赋值也会触发空转）。
    // 返回原始值组合即可复用 watch 旧值 comparisons（Vue 对二元元组做引用相等判断也会失效，
    // 但这里用两个独立 getter 逐项比较最稳定）。
    return `${list?.length ?? 0}|${list?.at(-1)?.id ?? ''}`
  }, nv => {
    if (suppressScroll) return
    const [lenStr, lastId] = nv.split('|')
    const len = Number(lenStr)
    const prev = lastSignatureRef.value
    lastSignatureRef.value = nv
    if (prev === null) return
    const [prevLenStr, prevLastId] = prev.split('|')
    const delta = len - Number(prevLenStr)
    if (delta <= 0) {
      // 消息减少（删除/撤回）：仅在贴底状态保持贴底，不强制把在看历史的用户拉到底部
      if (atBottom.value) scrollBottom(false)
      return
    }
    // 向上翻页 prepend 历史时最后一条消息 id 不变：不提示、不滚动
    if (lastId === prevLastId) return
    nextTick(() => {
      const el = listRef.value
      if (!el) return
      if (atBottom.value) {
        scrollBottom() // 贴着底部：跟随新消息（平滑推上来）
      } else {
        newCount.value += delta // 正在看历史：只提示，不抢滚动
      }
    })
  })
  watch(() => route.params.id, async () => {
    if (!convId.value) return
    quoteDraft.value = null
    showMembers.value = false
    noticeConfirmedMe.value = false
    noticeDialog.value = false
    displayLimit.value = 60
    group.value = null
    initializing.value = true
    searchOn.value = route.query.search === '1'
    // ★ 关键修复：从「通讯录」点「发消息」时，会话刚由服务端 ensure 创建，
    //   本地 store.sessions 里还没有它 → session 为 null，title/loadGroup/发送 全坏。
    //   先同步会话列表，让新会话出现在 sessions 里，再加载历史与群信息。
    await store.loadSessions().catch(() => {})
    await loadGroup()
    await loadNames()
    try {
      await store.openConversation(convId.value)
    } finally {
      initializing.value = false // 失败也要收掉骨架，否则永远转圈
    }
    scrollBottom()
  })

  onMounted(async () => {
    // 媒体（图片/表情图）异步加载会改变内容高度：贴底时补滚，保持"看到的始终是最底部"
    listRef.value?.addEventListener('load', onMediaLoad, true)
    // ★ 浏览器卡死 / 切到后台标签再回来：自动补拉最新聊天记录（无需用户手动刷新）。
    //   标签页在后台时 JS 被冻结/节流，SignalR 连接会断、实时消息收不到；
    //   一旦变回可见（visibilitychange）或窗口重新聚焦（focus），就重新拉一页最新历史，
    //   把"离线/卡死期间错过的对方消息"补上 —— 解决"重新进也看不到对方发的东西"。
    document.addEventListener('visibilitychange', onTabVisible)
    window.addEventListener('focus', onWinFocus)
    window.addEventListener('online', onWinFocus)
    searchOn.value = route.query.search === '1'
    // ★ 先同步会话列表（从通讯录进来的新会话可能本地还没有）再加载历史/群信息
    await store.loadSessions().catch(() => {})
    await Promise.all([loadGroup(), loadNames()])
    // 群聊：拉取投票列表（投票卡片数据源；后续靠 PollUpdated 实时刷新）
    if (isGroup.value) usePollsStore().load(convId.value)
    try {
      await store.openConversation(convId.value)
    } finally {
      initializing.value = false
    }
    scrollBottom()
  })

  /** 标签页变回可见：距上次补拉超过 3s 才重新拉（避免频繁切换抖动）。 */
  let lastCatchUp = 0
  function onTabVisible () {
    if (document.visibilityState !== 'visible') return
    const now = Date.now()
    if (now - lastCatchUp < 3000) return
    lastCatchUp = now
    void refreshOnReturn()
  }

  /** 窗口重新聚焦 / 网络恢复：同样补拉一次（focus 可能更频繁，加更大冷却）。 */
  function onWinFocus () {
    const now = Date.now()
    if (now - lastCatchUp < 5000) return
    lastCatchUp = now
    void refreshOnReturn()
  }

  async function refreshOnReturn () {
    // 仅当本会话确实被打开过才补拉（避免未初始化时误发请求）
    if (!store.messagesByConv[convId.value]) return
    await store.refreshConversation(convId.value)
    // 补拉后若贴底就滚到底，让用户直接看到最新消息
    if (atBottom.value) scrollBottom(false)
  }

  onBeforeUnmount(() => {
    listRef.value?.removeEventListener('load', onMediaLoad, true)
    document.removeEventListener('visibilitychange', onTabVisible)
    window.removeEventListener('focus', onWinFocus)
    window.removeEventListener('online', onWinFocus)
    // 输入中提示兜底计时器
    if (typingClearTimer) {
      clearTimeout(typingClearTimer)
      typingClearTimer = null
    }
  })
</script>

<style scoped>
.wx-multi-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 4px 8px;
  background: rgb(var(--v-theme-surface));
  border-bottom: 1px solid rgb(var(--v-theme-wechat-divider));
}

.wx-msg-area {
  position: relative;
}

/* 对方正在输入…（顶栏小字 + 闪烁打字点） */
.wx-typing-tip {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  margin-left: 10px;
  font-size: 12px;
  color: rgb(var(--v-theme-wechat-text-secondary));
  white-space: nowrap;
  align-self: center;
}

.wx-typing-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: #07c160;
  animation: wx-typing-blink 1s infinite;
}

@keyframes wx-typing-blink {
  50% { opacity: 0.2; }
}

/* 新消息提示：贴右下角，点击跳到底部 */
.wx-new-tip {
  position: sticky;
  bottom: 12px;
  left: 50%;
  transform: translateX(-50%);
  z-index: 6;
  display: block;
  width: max-content;
  margin: 0 auto;
  padding: 5px 14px;
  border: none;
  border-radius: 14px;
  background: #fa5151;
  color: #fff;
  font-size: 13px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.2);
  cursor: pointer;
  animation: wx-tip-in 0.24s ease;
}

@keyframes wx-tip-in {
  from {
    opacity: 0;
    transform: translate(-50%, 6px) scale(0.92);
  }

  to {
    opacity: 1;
    transform: translate(-50%, 0) scale(1);
  }
}

.wx-msg-area {
  background: rgb(var(--v-theme-wechat-chat-bg));
  /* 顶部/底部留白：避免第一条消息被顶栏压住、最后一条贴着输入框 */
  padding: 12px 0 16px;
  display: flex;
  flex-direction: column;
}

/* 首屏加载骨架：宽窄交替、左右错落，暗示"正在载入一批对话" */
.wx-loading-wrap {
  display: flex;
  flex-direction: column;
  gap: 14px;
  padding: 18px;
}

.wx-skeleton-row {
  height: 38px;
}

.wx-chat-bar {
  border-bottom: 1px solid rgb(var(--v-theme-wechat-divider));
}

.wx-group-panel {
  z-index: 20;
}

  .wx-quote-preview {
    background: rgba(128, 128, 128, 0.08);
    border-top: 1px solid rgb(var(--v-theme-wechat-divider));
  }

  .wx-notice-banner {
    background: rgba(7, 193, 96, 0.08);
    border-bottom: 1px solid rgb(var(--v-theme-wechat-divider));
  }

  /* 已确认后：横幅自动隐藏，留一行可点的「查看群公告」入口 */
  .wx-notice-collapsed {
    cursor: pointer;
    border-bottom: 1px solid rgb(var(--v-theme-wechat-divider));
  }
  .wx-notice-collapsed:hover {
    background: rgba(7, 193, 96, 0.06);
  }
</style>
