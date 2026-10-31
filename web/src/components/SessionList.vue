<template>
  <div>
    <v-list class="wx-session-list" lines="two">
      <v-list-item
        v-for="s in sessions"
        :key="s.id"
        :active="activeId === s.id"
        class="wx-session-item"
        rounded="lg"
        @click="open(s.id)"
        @contextmenu.prevent="onContextMenu(s, $event)"
      >
        <template #prepend>
          <user-avatar
            :alt="s.title"
            :nickname="s.title"
            :size="avatarSize"
            :src="s.avatar"
          />
        </template>

        <template #title>
          <div class="d-flex align-center">
            <span class="text-truncate">{{ s.title }}</span>

            <v-icon
              v-if="s.muted"
              class="ml-1"
              color="secondary"
              icon="mdi-volume-off"
              size="small"
            />

            <v-icon
              v-if="s.pinned"
              class="ml-1"
              color="primary"
              icon="mdi-pin"
              size="small"
            />
          </div>
        </template>

        <template #subtitle>
          <span :class="{ 'wx-muted-last': s.muted }">{{ summaryOf(s) }}</span>
        </template>

        <template #append>
          <div class="d-flex flex-column align-end" style="min-width: 64px">
            <span class="text-body-small text-medium-emphasis">{{ timeOf(s) }}</span>

            <v-badge
              v-if="s.unread > 0"
              class="my-1"
              color="wechat-unread"
              :content="Math.min(s.unread, 99)"
              :model-value="true"
            />
          </div>
        </template>
      </v-list-item>

      <v-list-item v-if="sessions.length === 0" class="justify-center" disabled>
        <v-list-item-title class="text-medium-emphasis">{{ t('sess.empty') }}</v-list-item-title>
      </v-list-item>
    </v-list>

    <ContextMenu
      v-model="menuOpen"
      :x="menuPoint.x"
      :y="menuPoint.y"
      :width="190"
    >
      <v-card v-if="menuSession" width="190">
        <v-list density="compact">
          <v-list-item @click="pin(!menuSession.pinned)">
            <v-list-item-title>{{ menuSession.pinned ? t('sess.unpin') : t('sess.pin') }}</v-list-item-title>
            <template #append><v-icon :icon="menuSession.pinned ? 'mdi-pin-off' : 'mdi-pin'" /></template>
          </v-list-item>

          <v-list-item @click="mute(!menuSession.muted)">
            <v-list-item-title>{{ menuSession.muted ? t('sess.unmute') : t('sess.mute') }}</v-list-item-title>
            <template #append><v-icon :icon="menuSession.muted ? 'mdi-volume-off' : 'mdi-volume-high'" /></template>
          </v-list-item>

          <v-divider />

          <v-list-item @click="clearChat">
            <v-list-item-title>{{ t('sess.clear') }}</v-list-item-title>
            <template #append><v-icon icon="mdi-broom" /></template>
          </v-list-item>

          <template v-if="menuSession.kind === 'group'">
            <v-divider />

            <v-list-item @click="fold(!menuSession.folded)">
              <v-list-item-title>{{ menuSession.folded ? t('sess.unfold') : t('sess.fold') }}</v-list-item-title>
              <template #append><v-icon :icon="menuSession.folded ? 'mdi-folder-move' : 'mdi-folder-lock-open'" /></template>
            </v-list-item>
          </template>
        </v-list>
      </v-card>
    </ContextMenu>
  </div>
</template>

<script setup lang="ts">
  import type { Conversation } from '@/types'
  import { computed, ref } from 'vue'
  import { useDisplay } from 'vuetify'
  import { api } from '@/lib/api'
  import { confirm } from '@/lib/confirm'
  import { t } from '@/lib/i18n'
  import { messageSummary, sessionTimeLabel } from '@/lib/utils'
  import UserAvatar from '@/components/UserAvatar.vue'
  import ContextMenu from '@/components/ContextMenu.vue'
  import { useConversationsStore } from '@/stores/conversations'

  const props = defineProps<{
    sessions: Conversation[]
    activeId?: string | null
  }>()

  const emit = defineEmits<{ open: [id: string] }>()
  const store = useConversationsStore()
  const display = useDisplay()

  const avatarSize = computed(() => (display.lgAndUp.value ? 44 : 40))

  const menuOpen = ref(false)
  const menuFor = ref<string | null>(null)
  const menuPoint = ref({ x: 0, y: 0 })

  function onContextMenu (s: Conversation, e: MouseEvent) {
    menuFor.value = s.id
    menuPoint.value = { x: e.clientX, y: e.clientY }
    menuOpen.value = true
  }

  const menuSession = computed(() =>
    menuFor.value ? props.sessions.find(s => s.id === menuFor.value) ?? null : null,
  )

  function open (id: string) {
    emit('open', id)
  }

  function summaryOf (s: Conversation) {
    return messageSummary(s.lastMessage)
  }

  function timeOf (s: Conversation) {
    return sessionTimeLabel(s.lastAt ?? undefined)
  }

  async function pin (pinned: boolean) {
    if (menuSession.value) await store.togglePin(menuSession.value.id, pinned)
    menuOpen.value = false
  }

  async function mute (muted: boolean) {
    if (menuSession.value) await store.toggleMute(menuSession.value.id, muted)
    menuOpen.value = false
  }

  /** 清空该会话的聊天记录（服务端删除）。危险操作先弹确认。失败给菜单保持可重试，不阻塞。 */
  async function clearChat () {
    if (!menuSession.value) return
    const ok = await confirm({
      title: t('sess.clear.title'),
      text: t('sess.clear.text', { name: menuSession.value.title }),
      confirmText: t('sess.clear.btn'),
    })
    if (!ok) { menuOpen.value = false; return }
    try {
      await api.delete(`/conversations/${menuSession.value.id}/messages`)
      // 统一走 store.clearConversationLocal：清内存 + 会话预览 + IDB 缓存
      // （此前只清内存和会话预览，IDB 缓存还留着旧消息 → 重开会话被 idbGetMessages 回填「刷回来」）
      store.clearConversationLocal(menuSession.value.id)
    } catch { /* 失败保持菜单可重试 */ }
    menuOpen.value = false
  }

  /** 折叠 / 恢复群聊会话（隐藏到"折叠群聊"）。 */
  async function fold (folded: boolean) {
    if (!menuSession.value) return
    await store.toggleFold(menuSession.value.id, folded)
    menuOpen.value = false
  }
</script>

<style scoped>
.wx-session-list {
  padding: 8px;
  gap: 2px;
}

.wx-session-list :deep(.v-list-item) {
  margin-bottom: 2px;
}

.wx-muted-last {
  opacity: 0.5;
}

.wx-session-item {
  cursor: pointer;
}
</style>
