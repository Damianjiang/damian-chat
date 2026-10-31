<template>
  <v-sheet class="wx-chat-surface wx-panes" flat>
    <v-app-bar class="wx-chat-bar" color="surface" density="compact" flat>
      <v-btn v-if="isMobile" icon="mdi-arrow-left" variant="text" @click="goBack" />
      <v-toolbar-title class="text-body-large font-weight-medium">{{ t('me.emojis') }}</v-toolbar-title>
    </v-app-bar>

    <div class="wx-emoji-bg flex-grow-1 overflow-y-auto wx-scroll pa-2">
      <div class="wx-emoji-panel">
        <div class="d-flex align-center justify-space-between px-3 py-2">
          <span class="text-body-medium">{{ t('me.emojis.mine') }}</span>

          <v-btn
            v-if="recent.length > 0"
            :color="deleting ? 'error' : undefined"
            :icon="deleting ? 'mdi-delete' : 'mdi-delete-outline'"
            size="x-small"
            variant="text"
            @click="deleting = !deleting"
          />
        </div>

        <div v-if="recent.length === 0" class="pa-4 text-body-small text-medium-emphasis">
          {{ t('me.emojis.empty') }}
        </div>

        <div v-else class="wx-emoji-grid">
          <button
            v-for="e in recent"
            :key="'r-' + e"
            class="wx-emoji-cell"
            type="button"
            @click="deleting ? remove(e) : send(e)"
          >
            {{ e }}
          </button>
        </div>
      </div>

      <div class="wx-emoji-panel mt-2">
        <div class="d-flex align-center justify-space-between px-3 py-2">
          <span class="text-body-medium">{{ t('me.emojis.all') }}</span>
          <span class="text-body-small text-medium-emphasis">{{ t('me.emojis.tapToSend') }}</span>
        </div>

        <div class="wx-emoji-grid">
          <button
            v-for="e in ALL_EMOJIS"
            :key="'a-' + e"
            class="wx-emoji-cell"
            type="button"
            @click="send(e)"
          >
            {{ e }}
          </button>
        </div>
      </div>
    </div>

    <v-snackbar v-model="snack" :timeout="1500">{{ snackMsg }}</v-snackbar>
  </v-sheet>
</template>

<script setup lang="ts">
  import { ref } from 'vue'
  import { useRouter } from 'vue-router'
  import { isPageMode } from '@/lib/device'
  import { ALL_EMOJIS, markEmojiUsed, removeUsedEmoji, usedEmojis } from '@/lib/emoji'
  import { t } from '@/lib/i18n'
  import { useConversationsStore } from '@/stores/conversations'

  const router = useRouter()
  const store = useConversationsStore()
  const isMobile = isPageMode

  const recent = ref<string[]>(usedEmojis())
  const deleting = ref(false)

  const snack = ref(false)
  const snackMsg = ref('')

  function send (emoji: string) {
    const s = store.activeSession
    if (!s) {
      snackMsg.value = t('me.emojis.noSession')
      snack.value = true
      return
    }
    markEmojiUsed(emoji)
    recent.value = usedEmojis()
    store.send(s, { type: 'sticker', content: '', stickerEmoji: emoji })
    snackMsg.value = t('me.emojis.sent', { name: s.title })
    snack.value = true
  }

  function remove (emoji: string) {
    removeUsedEmoji(emoji)
    recent.value = usedEmojis()
  }

  function goBack () {
    router.back()
  }
</script>

<style scoped>
.wx-emoji-bg {
  background: #ededed;
}

.wx-emoji-panel {
  background: rgb(var(--v-theme-surface));
  border-radius: 10px;
  overflow: hidden;
}

.wx-emoji-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(56px, 1fr));
  gap: 4px;
  padding: 8px 12px 16px;
}

.wx-emoji-cell {
  aspect-ratio: 1;
  border: none;
  background: transparent;
  font-size: 28px;
  line-height: 1;
  cursor: pointer;
  border-radius: 8px;
}

.wx-emoji-cell:hover {
  background: rgba(0, 0, 0, 0.06);
}

.v-theme--dark .wx-emoji-bg {
  background: #121212;
}
</style>
