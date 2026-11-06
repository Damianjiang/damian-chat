<template>
  <v-sheet class="wx-chat-surface wx-panes" flat>
    <!-- 顶栏 -->
    <div class="wx-pane-bar wx-chat-bar">
      <v-btn v-if="isMobile" icon="mdi-arrow-left" variant="text" @click="goBack" />

      <user-avatar
        v-if="!isMobile && peerAvatar"
        class="mr-2"
        :nickname="title"
        :size="30"
        :src="peerAvatar"
      />

      <!-- 对方正在输入…（微信同款：直接替换标题文字；此前标题旁挂小字条，
           绝对居中的标题稍长就和它叠在一起）。5s 无新信号自动消失 -->
      <v-toolbar-title class="text-body-large font-weight-medium">{{ typingName ? t('chat.typing') : title }}</v-toolbar-title>

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
    </div>

    <!-- 多选模式操作条：全选 / 逐条转发 / 合并转发 / 删除（QQ 式聊天记录打包）；
         勾选 ≥1 条即可转发/删除，投票等不可转发的消息会在转发时跳过并提示 -->
    <div v-if="multiMode" class="wx-multi-bar">
      <v-btn icon="mdi-close" size="small" variant="text" @click="exitMulti" />
      <span class="text-body-medium">{{ t('chat.multi.selected', { n: selectedIds.size }) }}</span>
      <v-btn size="small" variant="text" @click="toggleSelectAll">{{ allSelected ? t('chat.multi.clearAll') : t('chat.multi.selectAll') }}</v-btn>
      <v-spacer />
      <v-btn :disabled="selectedIds.size === 0" size="small" variant="text" @click="startForward('multi-one')">{{ t('chat.multi.forwardOne') }}</v-btn>
      <v-btn :disabled="selectedIds.size === 0" size="small" variant="text" @click="startForward('merged')">{{ t('chat.multi.forwardMerged') }}</v-btn>
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
    <div v-if="isGroup && group?.notice && !noticeConfirmedMe" class="wx-notice-banner pa-3">
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

      <v-btn
        v-if="hasMoreHistory"
        class="mx-auto my-2"
        color="primary"
        :loading="store.historyLoading"
        size="x-small"
        variant="text"
        @click="loadEarlier"
      >{{ t('chat.load.earlier') }}</v-btn>

      <!-- 打开会话时的加载态：三个横排呼吸点（微信同款）。
           曾经用 5 条骨架横杠竖着堆一列占位，观感就是"一竖条"，用户明确否掉；
           这里固定为 row + nowrap 的单行三点，只做透明度呼吸，不做位移。 -->
      <div v-if="firstLoading" class="wx-msg-loading">
        <span class="wx-msg-loading-dot" />
        <span class="wx-msg-loading-dot" />
        <span class="wx-msg-loading-dot" />
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
            :select-mode="multiMode"
            :sender-name="displayName(m.senderId)"
            :show-time="needsTime(g.messages, mi)"
            :pat-target-name="patTargetName(m)"
            @copied="onCopied"
            @delete="onDelete"
            @favorite="onFavorite"
            @forward="onForward"
            @open-forward="openViewer"
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

      <!-- 微信式「↓ N条新消息」：翻看历史期间来了新消息才出现（贴底跟随时不打扰），
           点击回到底部并清零 + 标记已读，手动滚回底部也会自动隐藏。
           ★ 必须是滚动内容的最后一个子元素：sticky-bottom 只有在元素的自然位置位于
           内容末尾时才会钉在可视区底部（放在开头永远滚出视野外 = 看不见）。 -->
      <button v-if="newCount > 0" class="wx-new-tip" type="button" @click="jumpToNew">
        <v-icon icon="mdi-chevron-down" size="14" />
        <span>{{ t('chat.newMsgs', { n: newCount }) }}</span>
      </button>
    </div>

    <!-- 引用回复预览条：固定在输入框正上方（此前在消息滚动区里，
         一往上翻历史"正在回复"条就跟着滚走了，草稿还默默留着） -->
    <div v-if="quoteDraft" class="wx-quote-preview pa-2 d-flex align-center justify-space-between text-body-small text-medium-emphasis">
      <span class="wx-quote-preview-text">{{ t('chat.replying', { who: quoteDraft.senderName, text: quoteDraft.text }) }}</span>
      <v-btn icon="mdi-close" size="x-small" @click="quoteDraft = null" />
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
      @solitaire="openSolitaireDialog"
    />

    <!-- 发起群投票（微信式：标题 + 选项，单选/多选）。
         scrollable：选项最多 20 个，手机上整卡曾被顶出屏幕、确认按钮够不着 -->
    <v-dialog v-model="pollDialog" max-width="380" scrollable>
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
          <!-- 重复选项提示：按钮同时置灰，避免"点确认→服务端报错"，也避免误以为是数量不够 -->
          <div v-if="pollDuplicate" class="text-body-small text-error">{{ t('poll.duplicate') }}</div>
          <v-checkbox v-model="pollMulti" class="mt-0" color="primary" density="compact" hide-details :label="t('poll.multi')" />
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="pollDialog = false">{{ t('common.cancel') }}</v-btn>
          <v-btn color="primary" :disabled="!pollValid" :loading="pollCreating" @click="createPoll">{{ t('common.confirm') }}</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 发起群接龙（报名/报数）：标题 + 可选说明，创建后以卡片消息进聊天流 -->
    <v-dialog v-model="solitaireDialog" max-width="380">
      <v-card>
        <v-card-title class="text-body-large">{{ t('solitaire.create') }}</v-card-title>
        <v-card-text>
          <v-text-field
            v-model="solitaireTitle"
            autofocus
            density="compact"
            :label="t('solitaire.title.ph')"
            :placeholder="t('solitaire.title.ph')"
            variant="outlined"
          />
          <v-textarea
            v-model="solitaireNote"
            auto-grow
            density="compact"
            :label="t('solitaire.note.optional')"
            :placeholder="t('solitaire.note.optional.ph')"
            rows="2"
            variant="outlined"
          />
          <div class="text-body-small text-medium-emphasis mt-1">{{ t('solitaire.createHint') }}</div>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="solitaireDialog = false">{{ t('common.cancel') }}</v-btn>
          <v-btn color="primary" :disabled="!solitaireValid" :loading="solitaireCreating" @click="createSolitaire">{{ t('common.confirm') }}</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 图片 / 视频 / 音频预览 -->
    <media-previewer v-model="previewOpen" :alt="previewAlt" :kind="previewKind" :url="previewUrl" />

    <!-- 转发选择（全屏：真实头像 + 搜索 + 多选批量，微信/QQ 同款） -->
    <forward-picker
      v-model="forwardOpen"
      :exclude-conv-id="convId"
      :mode="forwardMode === 'single' ? 'single' : 'multi'"
      :sending="forwardSending"
      :sessions="store.sessions"
      @confirm="onForwardConfirm"
    />

    <!-- 合并转发阅读器（QQ 式「聊天记录」全屏展开） -->
    <forward-viewer v-model="viewerOpen" :bundle="viewerBundle" />

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
  import ForwardPicker from '@/components/ForwardPicker.vue'
  import ForwardViewer from '@/components/ForwardViewer.vue'
  import MediaPreviewer from '@/components/MediaPreviewer.vue'
  import MessageBubble from '@/components/MessageBubble.vue'
  import UserAvatar from '@/components/UserAvatar.vue'

  /** 合并转发快照（与服务端 Validate.ParseForwardJson 输出一致） */
  type ForwardBundle = { v?: number, msgs: Array<{ s?: string, t?: number, k: string, c?: string, f?: string, fs?: number, i?: string, e?: string }> }
  import { api } from '@/lib/api'
  import { confirm } from '@/lib/confirm'
  import { isPageMode } from '@/lib/device'
  import { fileUrl } from '@/lib/files'
  import { mediaKindOf } from '@/lib/media'
  import { t } from '@/lib/i18n'
  import { sendTyping } from '@/lib/signalr'
  import { avatarColor, messageSummary, timeLabel, unescapeEntities } from '@/lib/utils'
  import { useAuthStore } from '@/stores/auth'
  import { useContactsStore } from '@/stores/contacts'
  import { useConversationsStore } from '@/stores/conversations'
  import { usePollsStore } from '@/stores/polls'
  import { useSolitairesStore } from '@/stores/solitaires'

  const route = useRoute()
  const router = useRouter()
  const store = useConversationsStore()
  const auth = useAuthStore()
  const contacts = useContactsStore()

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

  // 窗口化渲染：默认显示最近 60 条，「显示更早的消息」按 60 条增量向上拉取。
  // 渲染上限 RENDER_CAP：此前无上限，长会话一路翻下去 DOM 里会堆两三千个
  // 气泡子树（每条带头像 img/菜单/时间判断），低端机一帧 diff 就卡死。
  //
  // ★ 曾经的「滑动窗口（winStart）」设计已移除：窗口尾一旦离开最新消息，
  //   用户就会「明明没动，却看不到刚发/刚收的消息」，只能刷新 —— 这是用户反复
  //   反馈的「有时候不显示最新消息」。现在只保留一个始终贴尾的渲染窗口，
  //   上翻 = 扩大 displayLimit（最多 RENDER_CAP 条，与 store 内存上限一致），
  //   任何时刻渲染的末尾都是最新一条消息。代价是翻到上限后按钮提示「已到底」。
  const RENDER_CAP = 500
  const displayLimit = ref(60) // 窗口化：首屏 60 条，「更早」按 60 增量
  const allMessages = computed<Message[]>(() => store.messagesByConv[convId.value] ?? [])
  /**
   * 渲染层最后一道去重防线。
   * 数据层（store.applyIncoming）已经按 id / clientMsgId 对账，但异常时序
   * （POST 超时重试、回声比响应先到、历史页与实时流交叉）仍可能让同一条消息
   * 在数组里出现两次。这里在真正 v-for 之前再压一遍，保证用户绝不看到两个一样的气泡。
   */
  function dedupeMessages (list: Message[]): Message[] {
    if (list.length < 2) return list
    // 先收集「已确认」消息的幂等 id：同 id 的 pending 气泡是它的残影，直接丢弃
    const confirmed = new Set<string>()
    for (const m of list) {
      if (!m.pending && m.clientMsgId) confirmed.add(m.clientMsgId)
    }
    const seenId = new Set<string>()
    const seenCid = new Set<string>()
    const out: Message[] = []
    for (const m of list) {
      if (seenId.has(m.id)) continue
      if (m.clientMsgId) {
        if (m.pending && confirmed.has(m.clientMsgId)) continue
        if (!m.pending) {
          if (seenCid.has(m.clientMsgId)) continue
          seenCid.add(m.clientMsgId)
        }
      }
      seenId.add(m.id)
      out.push(m)
    }
    return out
  }

  const visibleMessages = computed(() => {
    const all = allMessages.value
    // 永远渲染「最后 displayLimit 条」：末尾必定是最新消息，
    // 不存在「窗口停在中间看不到新消息」的状态。
    return dedupeMessages(all.length > displayLimit.value ? all.slice(all.length - displayLimit.value) : all)
  })

  const hasMoreHistory = computed(() => {
    // 已到渲染上限：不再提示「加载更早」。否则点下去只会往 store 里多拉 30 条，
    // 但渲染窗口固定 500 条，界面上什么都不会变 —— 用户视角就是「按钮坏了」。
    if (displayLimit.value >= RENDER_CAP) return false
    if (allMessages.value.length > displayLimit.value) return true
    return !!store.historyHasMore[convId.value]
  })

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
    // 会话切换守卫：await 期间用户切到别的会话时，convId 会变 —— 过期运行
    // 直接退出，否则会把 A 会话的位移量/窗口状态算到 B 会话头上（跳屏+错窗口）
    const conv = convId.value
    try {
      const lenBefore = allMessages.value.length
      // 服务端还有更早的、且本地已经全部渲染出来 → 先翻服务端页（前插进 store）
      if (store.historyHasMore[conv] && lenBefore <= displayLimit.value) {
        const oldestVisible = visibleMessages.value[0]
        // cap 放大到「可见窗口 + 本页余量」：否则内存 500 条封顶时，
        // 刚拉回来的更早消息会被 keep-newest 淘汰掉，翻页永远不刷新
        await store.loadHistory(conv, oldestVisible?.id, { cap: displayLimit.value + 90 })
      }
      if (convId.value !== conv) return
      // 只扩大渲染窗口（末尾始终是最新消息），不再移动窗口。
      // loadHistory 前插后窗口自动多显示 offset 条更早内容，无需换算下标。
      if (displayLimit.value < RENDER_CAP && allMessages.value.length > displayLimit.value) {
        displayLimit.value = Math.min(RENDER_CAP, displayLimit.value + 60)
      }
      await nextTick(() => {
        const el = listRef.value
        if (!el || convId.value !== conv) return
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

  /** 拍一拍（群聊双击成员头像 / 私聊双击对方头像 / 右键菜单）→ 发一条「我拍了拍 XX」消息。
      群聊对象 = 该条消息的发送者（群成员）；私聊对象 = 对方（私聊里非我发的消息 senderId 恒为对方）。
      乐观上屏，服务端按「群成员 / 私聊对方」校验对象合法性，被好友删除或拉黑时发不出。 */
  function onPat (m: Message) {
    const targetId = m.senderId
    if (!targetId || targetId === meId.value || !session.value) return
    const payload: Partial<Message> = { type: 'pat', patTarget: targetId }
    store.send(session.value, payload).catch(() => {
      snackMsg.value = t('chat.pat.fail')
      snack.value = true
    })
    scrollBottom()
  }

  /* ---------- 对方正在输入（微信式提示；群聊不显示 —— 微信同款） ---------- */
  const typingUser = ref<{ userId: string, at: number } | null>(null)
  let typingClearTimer: ReturnType<typeof setTimeout> | null = null
  watch(() => store.typingBy[convId.value], t => {
    if (!t || isGroup.value) { typingUser.value = null; return }
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
    // 群聊不显示「正在输入」（微信同款）：上行也不必发，省一次广播/唤醒
    if (isGroup.value) return
    if (convId.value) void sendTyping(convId.value)
  }

  /* ---------- 群投票（微信式）：打开群聊拉全量；创建后以 poll 消息卡片进聊天流 ---------- */
  const pollDialog = ref(false)
  const pollTitle = ref('')
  const pollOptions = ref<string[]>(['', ''])
  const pollMulti = ref(false)
  const pollCreating = ref(false)
  /** 归一化后的选项（去空 + trim）：校验和提交共用，避免两处口径不一致 */
  const pollCleanOptions = computed(() => pollOptions.value.map(o => o.trim()).filter(o => o.length > 0))
  /** 有重复选项：按钮置灰 + 红字提示（服务端也会拒，但不该让用户白点一次） */
  const pollDuplicate = computed(() => {
    const opts = pollCleanOptions.value
    return opts.length >= 2 && new Set(opts).size < opts.length
  })
  const pollValid = computed(() =>
    pollTitle.value.trim().length > 0
    && pollCleanOptions.value.length >= 2
    && !pollDuplicate.value)

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
      // 发送失败要提示原因（禁言/被拒等）——静默吞掉会留下一个永远转圈的待定气泡
      store.send(s, { type: 'poll', content: res.data.id } as Partial<Message>).catch((err: any) => {
        snackMsg.value = err?.response?.data?.error ?? t('msg.send.failed')
        snack.value = true
      })
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

  /* ---------- 群接龙（微信式）：标题 + 说明，创建后以卡片消息进聊天流 ---------- */
  const solitaireDialog = ref(false)
  const solitaireTitle = ref('')
  const solitaireNote = ref('')
  const solitaireCreating = ref(false)
  const solitaireValid = computed(() => solitaireTitle.value.trim().length > 0)

  function openSolitaireDialog () {
    solitaireTitle.value = ''
    solitaireNote.value = ''
    solitaireDialog.value = true
  }

  async function createSolitaire () {
    if (!solitaireValid.value || !isGroup.value || solitaireCreating.value) return
    solitaireCreating.value = true
    try {
      const res = await api.post(`/groups/${convId.value}/solitaires`, {
        title: solitaireTitle.value.trim(),
        note: solitaireNote.value.trim(),
      })
      solitaireDialog.value = false
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
      store.send(s, { type: 'solitaire', content: res.data.id } as Partial<Message>).catch((err: any) => {
        snackMsg.value = err?.response?.data?.error ?? t('msg.send.failed')
        snack.value = true
      })
      useSolitairesStore().load(convId.value)
      snackMsg.value = t('solitaire.created')
      snack.value = true
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? t('solitaire.fail')
      snack.value = true
    } finally {
      solitaireCreating.value = false
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
    store.send(s, payload as Partial<Message>).catch((error: any) => {
      // 服务端业务拒绝（拉黑/删除好友/禁言等）：弹 snackbar 告知具体原因
      const err = error?.response?.data?.error
      if (err) {
        snackMsg.value = err
        snack.value = true
      }
    })
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

  // ---- 多选（PC 右键 / 手机长按 → 多选 → 行尾勾选钮点选/取消 → 批量删除/转发） ----
  const multiMode = ref(false)
  const selectedIds = ref<Set<string>>(new Set())

  /** 可勾选的消息（系统/拍一拍是居中灰字，不参与勾选 —— 微信同款） */
  const selectableMessages = computed(() =>
    allMessages.value.filter(m => m.type !== 'system' && m.type !== 'pat'))

  const allSelected = computed(() =>
    selectableMessages.value.length > 0
    && selectableMessages.value.every(m => selectedIds.value.has(m.id)))

  function toggleSelectAll () {
    const next = new Set(selectedIds.value)
    if (allSelected.value) selectableMessages.value.forEach(m => next.delete(m.id))
    else selectableMessages.value.forEach(m => next.add(m.id))
    selectedIds.value = next
  }

  /** 点消息行切换勾选（多选模式下点行任意位置生效，再点一次取消） */
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
      // 确认框里展示的是用户看到的文字：历史数据先解码，否则弹窗里出现 &amp;
      text: t('chat.delete.text', { s: unescapeEntities(m.content ?? m.fileName ?? '').slice(0, 40) || t('chat.msg.placeholder') }),
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
    const text = m.type === 'text' ? (unescapeEntities(m.content) || t('chat.msg.placeholder')) : messageSummary(m)
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

  /** 消息对应的媒体类型：image → image；file 按扩展名分 video/audio（清单在 lib/media.ts）。 */
  function mediaKind (m: Message): 'image' | 'video' | 'audio' {
    if (m.type === 'image') return 'image'
    if (m.type !== 'file') return 'image'
    return mediaKindOf(m.fileName)
  }

  function onForward (m: Message) {
    if (!forwardable(m)) {
      snackMsg.value = t('chat.forward.unsupported')
      snack.value = true
      return
    }
    forwardSource.value = [m]
    forwardMode.value = 'single'
    forwardOpen.value = true
  }

  /* ---------- 转发：全屏选择器（带头像、多选批量）+ QQ 式合并转发 ---------- */

  const forwardMode = ref<'single' | 'multi-one' | 'merged'>('single')
  const forwardSource = ref<Message[]>([])
  const forwardSending = ref(false)
  const viewerOpen = ref(false)
  const viewerBundle = ref<ForwardBundle | null>(null)

  /** 投票 / 接龙卡片不可转发（id 只在原群有效）；拍一拍/系统消息进不了打包 */
  function forwardable (m: Message) {
    return m.type !== 'poll' && m.type !== 'solitaire' && m.type !== 'pat' && m.type !== 'system'
  }

  /** 多选栏入口：multi-one = 每个目标各收逐条消息；merged = 打包成一张「聊天记录」卡片。
   *  投票/拍一拍/系统消息不可转发：自动跳过并提示（勾选本身不设限，勾了就行） */
  function startForward (mode: 'multi-one' | 'merged') {
    const picked = [...selectedIds.value]
      .map(id => allMessages.value.find(x => x.id === id))
      .filter((m): m is Message => !!m)
    const msgs = picked.filter(forwardable).reverse()   // 转发按时间正序
    if (msgs.length === 0) {
      snackMsg.value = t('chat.forward.unsupported')
      snack.value = true
      return
    }
    if (picked.length - msgs.length > 0) {
      snackMsg.value = t('chat.forward.skipped', { n: picked.length - msgs.length })
      snack.value = true
    }
    forwardSource.value = msgs
    forwardMode.value = mode
    forwardOpen.value = true
  }

  /** 单条消息的转发载荷（quote 降级为纯文本；投票/接龙/系统返回 null 不可转） */
  function forwardPayload (m: Message): Partial<Message> | null {
    if (m.type === 'poll' || m.type === 'solitaire' || m.type === 'system') return null
    if (m.type === 'forward') return { type: 'forward', content: '[聊天记录]', forwardJson: m.forwardJson }
    if (m.type === 'quote' || m.type === 'text') return { type: 'text', content: m.content }
    const payload: Partial<Message> = { type: m.type }
    if (m.type === 'image') payload.imageId = m.imageId
    if (m.type === 'file') {
      payload.fileName = m.fileName
      payload.fileSize = m.fileSize
    }
    if (m.type === 'sticker') payload.stickerEmoji = m.stickerEmoji
    return payload
  }

  /** 打包快照：发送人昵称/时间/类型/内容；quote 降级文本；媒体带 fileId */
  function buildBundle (msgs: Message[]): ForwardBundle {
    return {
      v: 1,
      msgs: msgs.map(m => ({
        s: m.senderId === meId.value ? (auth.user?.nickname || t('msg.pat.me')) : displayName(m.senderId),
        t: new Date(m.createdAt).getTime(),
        k: m.type === 'quote' ? 'text' : m.type,
        c: m.content,
        f: m.fileName,
        fs: m.fileSize,
        i: m.imageId,
        e: m.stickerEmoji,
      })),
    }
  }

  async function onForwardConfirm (targets: Conversation[]) {
    if (targets.length === 0 || forwardSending.value) return
    forwardSending.value = true
    let fail = 0
    let lastError = ''
    try {
      for (const conv of targets) {
        try {
          if (forwardMode.value === 'merged') {
            await store.send(conv, {
              type: 'forward',
              content: '[聊天记录]',
              forwardJson: JSON.stringify(buildBundle(forwardSource.value)),
            } as Partial<Message>)
          } else {
            for (const m of forwardSource.value) {
              // 拍一拍跨会话 patTarget 非法：降级为一条文本保留语义
              if (m.type === 'pat') {
                const who = m.senderId === meId.value ? t('msg.pat.me') : displayName(m.senderId)
                await store.send(conv, { type: 'text', content: t('msg.pat', { who, target: displayName(m.patTarget ?? '') }) })
                continue
              }
              const payload = forwardPayload(m)
              if (!payload) continue
              await store.send(conv, payload)
            }
          }
        } catch (err: any) {
          fail++
          lastError = err?.response?.data?.error ?? ''
        }
      }
      if (fail === 0) {
        snackMsg.value = t('chat.forward.okMulti', { n: targets.length })
        snack.value = true
        forwardOpen.value = false
        if (multiMode.value) exitMulti()
      } else {
        snackMsg.value = lastError || t('chat.forward.failSome', { n: fail })
        snack.value = true
      }
    } finally {
      forwardSending.value = false
    }
  }

  function openViewer (m: Message) {
    try {
      viewerBundle.value = m.forwardJson ? (JSON.parse(m.forwardJson) as ForwardBundle) : null
    } catch {
      viewerBundle.value = null
    }
    viewerOpen.value = true
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
    // 优先读本地联系人 store（登录时已拉全量）：此前每次切会话都全量 GET /contacts，
    // 慢网络下白加一个来回；store 为空才回退拉一次
    const local = contacts.items.find(c => c.user?.id === session.value!.memberId)
    if (local) {
      nameMap.value[local.user.id] = local.remark || local.user.nickname
      avatarMap.value[local.user.id] = local.user.avatar
      return
    }
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

  /** 进会话「持续贴底」的 rAF 句柄（0 = 未运行）。声明提前，供 scrollBottom 取消。 */
  let settleRaf = 0

  /**
   * 滚到底部：离底不远时走 smooth，新消息是"推上来"的，比瞬移跟手；
   * 离得很远（刚从几千条历史的顶部切回来）则直接跳 —— 否则会看一段漫长的滚动动画。
   */
  function scrollBottom (smooth = true) {
    // 主动滚到底 = 用户要停在最新位置，停掉进会话时的高度跟随，
    // 否则它的 rAF 会紧接着把 scrollTop 硬设一遍，和平滑滚动打架（表现为抖动）。
    if (settleRaf) {
      cancelAnimationFrame(settleRaf)
      settleRaf = 0
    }
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

  /** 上一次滚动位置：用来判断「是否真的在向上滑」，避免锚定回填的 scroll 反过来再触发翻页 */
  let lastScrollTop = 0
  /** 自动翻页冷却截止时间戳：一次翻页 + 锚定要几百毫秒，期间绝不能再触发 */
  let earlierCooldownUntil = 0

  function onMsgScroll () {
    // rAF 合帧：scroll 事件每秒可触发上百次，逐次读 scrollHeight 会强制同步布局，
    // 低端机上图片陆续加载高度抖动时尤其致命；合到一帧只量一次
    if (scrollRaf) return
    scrollRaf = requestAnimationFrame(() => {
      scrollRaf = 0
      const el = listRef.value
      if (!el || suppressScroll) return
      const top = el.scrollTop
      // 位移 > 2px 才算「用户在滑」，纯抖动 / 锚定补偿不算
      const goingUp = top < lastScrollTop - 2
      lastScrollTop = top
      const distance = el.scrollHeight - top - el.clientHeight
      atBottom.value = distance < 60
      if (atBottom.value) newCount.value = 0
      // 「加载更早」只在 ① 真的向上滑 ② 已到接近顶部（80px，原来 140px 在 424px 高的
      // 视口里占三分之一屏，轻轻往回带一点就反复触发）③ 过了冷却期 时才触发。
      // 三条缺一不可：否则翻页后的锚定补偿会把 scrollTop 拉回顶部附近，
      // 立刻又触发下一页 —— 用户看到的就是「划到最上面后鬼畜」。
      if (goingUp
        && top < 80
        && hasMoreHistory.value
        && !firstLoading.value
        && !loadingEarlier.value
        && Date.now() >= earlierCooldownUntil) {
        earlierCooldownUntil = Date.now() + 800
        void loadEarlier()
      }
    })
  }

  function jumpToNew () {
    scrollBottom()
    // 点浮标 = 已经回到最新位置看到新消息了：清红点并标记已读（微信同款）
    const cid = convId.value
    if (cid) {
      store.markRead(cid)
      void api.put(`/conversations/${cid}`, { read: true }).catch(() => {})
    }
  }

  /**
   * 进会话后「坚决贴底」。
   *
   * 气泡里的图片/头像/表情是异步解码加载的，容器高度会在之后几百毫秒里持续长高；
   * 之前只在 0 / 300 / 700ms 补滚三次，图片多的会话 700ms 后还在长 ——
   * 用户于是「进聊天停在最上面，得往下翻或发条消息才回到底部」。
   * 这里改成有时限的高度跟随：只要用户没主动往上滑（atBottom 仍为真），
   * 一旦检测到 scrollHeight 变化（内容长高）就立刻贴到最底，最多跟随 duration 毫秒。
   * 只在高度变化时写 scrollTop，避免每帧强制重排。
   */
  function settleToBottom (duration = 2500) {
    const start = Date.now()
    let lastH = -1
    const tick = () => {
      settleRaf = 0
      const el = listRef.value
      if (!el || !atBottom.value) return   // 用户已手动上滑 → 立刻让位，不抢滚动
      if (el.scrollHeight !== lastH) {
        lastH = el.scrollHeight
        el.scrollTop = el.scrollHeight
      }
      if (Date.now() - start < duration) settleRaf = requestAnimationFrame(tick)
    }
    if (settleRaf) cancelAnimationFrame(settleRaf)
    atBottom.value = true
    newCount.value = 0
    tick()
  }

  /** 图片等媒体加载完成后容器高度变化：贴底状态补一次滚动，避免"看起来没到底"的错位 */
  function onMediaLoad () {
    if (!atBottom.value) return
    scrollBottom(false)
  }

  let suppressScroll = false
  /** scroll 事件的 rAF 合帧句柄（0 = 当前无挂起回调） */
  let scrollRaf = 0
  /** 上一次「长度|最后一条id」签名：字符串原始值，拿旧值比对，避免每次新对象触发空跑 */
  const lastSignatureRef = ref<string | null>(null)
  watch(() => {
    const list = store.messagesByConv[convId.value]
    // 无新增（len/lastId 都没变）也要能比对差额：改用「可复用的复元组」，
    // 仅当内容变化才触发（此前每次返回新对象，数组的整体重赋值也会触发空转）。
    // 返回原始值组合即可复用 watch 旧值 comparisons（Vue 对二元元组做引用相等判断也会失效，
    // 但这里用两个独立 getter 逐项比较最稳定）。
    const lastId = list && list.length > 0 ? list[list.length - 1]! : null
    return `${list?.length ?? 0}|${lastId?.id ?? ''}`
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
  // 回到前台时补已读：后台期间收到的消息红点还在，回来看到了才标记已读
  const onVisible = () => {
    if (document.visibilityState !== 'visible') return
    const cid = convId.value
    if (!cid || !store.messagesByConv[cid]) return
    store.markRead(cid)
    void api.put(`/conversations/${cid}`, { read: true }).catch(() => {})
  }
  document.addEventListener('visibilitychange', onVisible)
  onBeforeUnmount(() => document.removeEventListener('visibilitychange', onVisible))

  // 切会话竞态守卫：A→B 快速导航时两个 watcher 并发执行，
  // 若 A 的 openConversation 后完成会把 activeId 覆盖回 A —— 用令牌让过期运行直接退出
  let convSwitchSeq = 0
  watch(() => route.params.id, async () => {
    if (!convId.value) return
    const seq = ++convSwitchSeq
    quoteDraft.value = null
    showMembers.value = false
    noticeConfirmedMe.value = false
    noticeDialog.value = false
    displayLimit.value = 60
    // 切会话：滚动方向基准与翻页冷却一并复位，否则上一个会话的
    // lastScrollTop 会让新会话第一次上滑被判成「向下」，自动翻页失效
    lastScrollTop = 0
    earlierCooldownUntil = 0
    // 新消息浮标随会话切换重置：lastSignature 是「上个会话」的长度签名，不重置的话
    // 下一条新消息的 delta = 新会话长度 - 旧会话长度，从少切到多会假报「40条新消息」、
    // 从多切到少永远走 delta<=0 分支不显示（用户视角就是这个功能时有时无）。
    lastSignatureRef.value = null
    newCount.value = 0
    atBottom.value = true
    // 群昵称/头像映射随会话切换重置：不清的话上一个群的群昵称会串进私聊/别的群，
    // 且 map 只增不减（每个会话条目常驻内存）
    nameMap.value = {}
    avatarMap.value = {}
    group.value = null
    initializing.value = true
    searchOn.value = route.query.search === '1'
    // ★ 关键修复：从「通讯录」点「发消息」时，会话刚由服务端 ensure 创建，
    //   本地 store.sessions 里还没有它 → session 为 null，title/loadGroup/发送 全坏。
    //   先同步会话列表，让新会话出现在 sessions 里，再加载历史与群信息。
    await store.loadSessions().catch(() => {})
    await loadGroup()
    await loadNames()
    if (seq !== convSwitchSeq) return   // 期间又切走了：本次是过期运行，放弃收尾
    try {
      await store.openConversation(convId.value)
    } finally {
      if (seq === convSwitchSeq) initializing.value = false // 失败也要收掉骨架，否则永远转圈
    }
    if (seq === convSwitchSeq) {
      // 进会话贴底：持续跟随异步内容高度（图片/头像加载），不再靠固定三次补滚
      settleToBottom(3000)
    }
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
    // 群聊：拉取投票 / 接龙列表（卡片数据源；后续靠 PollUpdated / SolitaireUpdated 实时刷新）
    if (isGroup.value) {
      usePollsStore().load(convId.value)
      useSolitairesStore().load(convId.value)
    }
    try {
      await store.openConversation(convId.value)
    } finally {
      initializing.value = false
    }
    // 首次挂载（从会话列表点进来，组件刚创建）同样要走「持续贴底」，
    // 否则只在渲染后滚一次，后续图片撑高就停在中间/顶部。
    settleToBottom(3000)
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
    if (scrollRaf) {
      cancelAnimationFrame(scrollRaf)
      scrollRaf = 0
    }
    if (settleRaf) {
      cancelAnimationFrame(settleRaf)
      settleRaf = 0
    }
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
  gap: 4px;
  padding: 4px 8px;
  background: rgb(var(--v-theme-surface));
  border-bottom: 1px solid rgb(var(--v-theme-wechat-divider));
}

/* 窄屏放得下：按钮压掉默认最小宽度，文字按钮只留必要内边距 */
.wx-multi-bar :deep(.v-btn) {
  min-width: 0;
  padding: 0 8px;
  flex: none;
}
.wx-multi-bar .text-body-medium {
  white-space: nowrap;
  flex: none;
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
  background: var(--dm-primary, #07C160);
  animation: wx-typing-blink 1s infinite;
}

@keyframes wx-typing-blink {
  50% { opacity: 0.2; }
}

/* 微信式新消息浮标：输入框上方、贴右下角的「↓ N条新消息」胶囊（翻历史期间来新消息才出现） */
.wx-new-tip {
  position: sticky;
  bottom: 12px;
  z-index: 6;
  display: flex;
  align-items: center;
  gap: 2px;
  width: max-content;
  margin-left: auto;   /* 推到右侧：微信同款右下角 */
  margin-right: 14px;
  padding: 5px 12px 5px 8px;
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
    transform: translateY(6px) scale(0.92);
  }

  to {
    opacity: 1;
    transform: translateY(0) scale(1);
  }
}

.wx-msg-area {
  background: rgb(var(--v-theme-wechat-chat-bg));
  /* 顶部/底部留白：避免第一条消息被顶栏压住、最后一条贴着输入框 */
  padding: 12px 0 16px;
  display: flex;
  flex-direction: column;
  /* overflow-y:auto 会把 overflow-x 计算成 auto —— 只要有一个子元素宽 1px，
     整块消息区就能左右滑动（群聊里「右边多出一点空白，可以往右划」就是这个）。
     这里显式钉死横向不可滚，并禁止横向 overscroll 传给外层。 */
  overflow-x: hidden;
  overscroll-behavior-x: contain;
}

/* 首屏加载骨架：宽窄交替、左右错落，暗示"正在载入一批对话" */
/* 打开会话时的消息加载态：三个横排呼吸点。
   显式写 row + nowrap，圆点用 display:block 固定尺寸、颜色写死，
   不依赖主题变量（旧浏览器 rgb(var(--x), alpha) 解析失败会让点变形）。 */
.wx-msg-loading {
  display: flex;
  flex-direction: row;
  flex-wrap: nowrap;
  align-items: center;
  justify-content: center;
  gap: 8px;
  width: 100%;
  padding: 26px 0;
}

.wx-msg-loading-dot {
  display: block;
  flex: 0 0 auto;
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: #9ba1a6;
  opacity: 0.35;
  animation: wx-msg-dot 1.1s ease-in-out infinite;
}

/* 本文件是纯 <style scoped>（无 lang="scss"），这里必须平铺写选择器：
   用 & 嵌套会被编译成原生 CSS 嵌套语法，旧浏览器整条丢弃 → 圆点直接不显示。 */
.wx-msg-loading-dot:nth-child(2) { animation-delay: 0.18s; }

.wx-msg-loading-dot:nth-child(3) { animation-delay: 0.36s; }

@keyframes wx-msg-dot {
  0%, 100% { opacity: 0.3; }
  50% { opacity: 1; }
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

  /* 被引用文本可能是一长串无空格 URL：不折行会把回复条撑出屏幕 */
  .wx-quote-preview-text {
    word-break: break-word;
    overflow-wrap: anywhere;
    min-width: 0;
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
