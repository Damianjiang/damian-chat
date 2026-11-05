<template>
  <v-dialog
    :model-value="modelValue"
    fullscreen
    scrollable
    transition="wx-fade"
    @update:model-value="v => emit('update:modelValue', v)"
  >
    <v-card color="wechat-chat-bg">
      <!-- 顶栏：返回 + 标题 -->
      <div class="wx-fp-bar d-flex align-center px-2">
        <v-btn icon="mdi-arrow-left" variant="text" @click="close" />
        <div class="text-body-large font-weight-medium">{{ title }}</div>
      </div>

      <!-- 搜索 -->
      <div class="px-3 pb-1">
        <v-text-field
          v-model="kw"
          density="compact"
          hide-details
          prepend-inner-icon="mdi-magnify"
          :placeholder="t('chat.forward.search')"
          variant="solo"
          clearable
        />
      </div>

      <!-- 会话列表：真实头像 + 多选框 -->
      <div class="wx-fp-list">
        <button
          v-for="s in filteredSessions"
          :key="s.id"
          class="wx-fp-row"
          type="button"
          @click="pick(s)"
        >
          <user-avatar :nickname="s.title" :size="42" :src="avatarOf(s)" />
          <div class="flex-grow-1 text-left" style="min-width: 0">
            <div class="text-body-large text-truncate">{{ s.title }}</div>
            <div class="text-body-small text-medium-emphasis text-truncate">
              {{ s.kind === 'group' ? t('chat.forward.group') : t('chat.forward.friend') }}
            </div>
          </div>
          <v-checkbox
            v-if="mode === 'multi'"
            :model-value="picked.has(s.id)"
            class="flex-grow-0 mr-1"
            color="primary"
            density="compact"
            hide-details
            @click.stop
            @update:model-value="() => toggle(s.id)"
          />
        </button>
        <div v-if="filteredSessions.length === 0" class="pa-8 text-center text-body-small text-medium-emphasis">
          {{ t('chat.forward.none') }}
        </div>
      </div>

      <!-- 底部操作（多选模式）：已选计数 + 转发 -->
      <div v-if="mode === 'multi'" class="wx-fp-bar d-flex align-center px-4 py-2">
        <span class="text-body-small text-medium-emphasis">
          {{ t('chat.forward.picked', { n: picked.size }) }}
        </span>
        <v-spacer />
        <v-btn variant="text" @click="close">{{ t('common.cancel') }}</v-btn>
        <v-btn
          color="primary"
          :disabled="picked.size === 0 || sending"
          :loading="sending"
          rounded="sm"
          @click="confirm"
        >{{ t('chat.forward.send', { n: picked.size }) }}</v-btn>
      </div>
    </v-card>
  </v-dialog>
</template>

<script setup lang="ts">
  import { computed, ref, watch } from 'vue'
  import UserAvatar from '@/components/UserAvatar.vue'
  import { t } from '@/lib/i18n'
  import { useContactsStore } from '@/stores/contacts'
  import type { Conversation } from '@/types'

  /**
   * 全屏转发选择器（微信/QQ 同款形态）：真实头像 + 搜索 + 多选批量。
   * single 模式点一行立即回传一个会话；multi 模式勾选后统一确认回传集合。
   * 头像来源：联系人 / 我的群聊（会话 id 本身不含头像，memberId 才指向人/群）。
   */
  const props = defineProps<{
    modelValue: boolean
    mode: 'single' | 'multi'
    sessions: Conversation[]
    excludeConvId?: string
    sending?: boolean
  }>()

  const emit = defineEmits<{
    (e: 'update:modelValue', v: boolean): void
    (e: 'confirm', targets: Conversation[]): void
  }>()

  const contacts = useContactsStore()
  const kw = ref('')
  const picked = ref<Set<string>>(new Set())

  const title = computed(() =>
    props.mode === 'multi' ? t('chat.forward.pickMulti') : t('chat.forward.pick'))

  const candidates = computed(() =>
    props.excludeConvId ? props.sessions.filter(s => s.id !== props.excludeConvId) : props.sessions)

  const filteredSessions = computed(() => {
    const k = kw.value.trim().toLowerCase()
    if (!k) return candidates.value
    return candidates.value.filter(s => s.title.toLowerCase().includes(k))
  })

  function avatarOf (s: Conversation): string {
    if (s.kind === 'group') {
      return contacts.groups.find(g => g.id === s.memberId)?.avatar ?? ''
    }
    return contacts.items.find(c => c.user?.id === s.memberId)?.user?.avatar ?? ''
  }

  function close () {
    emit('update:modelValue', false)
  }

  function toggle (id: string) {
    const next = new Set(picked.value)
    if (next.has(id)) next.delete(id)
    else next.add(id)
    picked.value = next
  }

  function pick (s: Conversation) {
    if (props.mode !== 'multi') {
      emit('confirm', [s])
      return
    }
    toggle(s.id)
  }

  function confirm () {
    const targets = props.sessions.filter(c => picked.value.has(c.id))
    emit('confirm', targets)
  }

  watch(() => props.modelValue, v => {
    if (v) {
      kw.value = ''
      picked.value = new Set()
    }
  })
</script>
