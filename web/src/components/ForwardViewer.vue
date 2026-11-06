<template>
  <v-dialog
    :model-value="modelValue"
    fullscreen
    scrollable
    transition="wx-fade"
    @update:model-value="v => emit('update:modelValue', v)"
  >
    <v-card color="wechat-chat-bg">
      <div class="wx-fp-bar d-flex align-center px-2">
        <v-btn icon="mdi-arrow-left" variant="text" @click="goBack" />
        <div class="text-body-large font-weight-medium">{{ stack.length > 1 ? t('chat.forward.viewer') : t('chat.forward.title') }}</div>
        <v-spacer />
        <span v-if="current" class="text-body-small text-medium-emphasis mr-2">
          {{ t('chat.forward.count', { n: current.msgs.length }) }}
        </span>
      </div>

      <!-- QQ 式聊天记录阅读器：按时间顺序逐条展开，嵌套合并卡片可继续下钻 -->
      <div class="wx-fv-body">
        <div v-for="(m, i) in current?.msgs ?? []" :key="i" class="wx-fv-row">
          <v-avatar :color="avatarColor(m.s || '?')" size="36">
            <span class="text-body-medium">{{ (m.s || '?').slice(0, 1) }}</span>
          </v-avatar>
          <div class="flex-grow-1" style="min-width: 0">
            <div class="d-flex align-center gap-2">
              <span class="text-body-small font-weight-medium">{{ m.s || t('msg.pat.group') }}</span>
              <span class="text-caption text-medium-emphasis">{{ fmtTime(m.t) }}</span>
            </div>

            <div v-if="m.k === 'forward'" class="mt-1">
              <button class="wx-fv-card" type="button" @click="openNested(m)">
                <div class="text-body-medium font-weight-medium">{{ t('chat.forward.title') }}</div>
                <div class="text-body-small text-medium-emphasis text-truncate">
                  {{ nestedPreview(m) }}
                </div>
                <div class="text-caption text-medium-emphasis mt-1">{{ t('chat.forward.viewCard') }}</div>
              </button>
            </div>

            <div v-else-if="m.k === 'sticker'" class="text-h5 mt-1">{{ m.e || '🙂' }}</div>

            <div v-else-if="m.k === 'image'" class="mt-1">
              <img v-if="imgUrls[m.i ?? '']" :alt="t('chat.preview.image')" class="wx-fv-img" decoding="async" loading="lazy" :src="imgUrls[m.i ?? '']">
              <span v-else class="text-body-small text-medium-emphasis">{{ t('chat.preview.image') }}</span>
            </div>

            <div v-else-if="m.k === 'file'" class="mt-1 d-flex align-center gap-2 wx-fv-file">
              <v-icon icon="mdi-file-document-outline" />
              <span class="text-body-small text-truncate">{{ m.f || t('chat.preview.file', { name: '' }) }}</span>
            </div>

            <div v-else class="text-body-medium wx-fv-text mt-1">{{ unescape(m.c) }}</div>
          </div>
        </div>
      </div>
    </v-card>
  </v-dialog>
</template>

<script setup lang="ts">
  import { computed, ref, watch } from 'vue'
  import { fileUrl } from '@/lib/files'
  import { avatarColor, timeLabel, unescapeEntities as unescape } from '@/lib/utils'
  import { t } from '@/lib/i18n'

  /** 合并转发快照（服务端 Validate.ParseForwardJson 的输出结构） */
  interface ForwardEntry {
    s?: string
    t?: number
    k: string
    c?: string
    f?: string
    fs?: number
    i?: string
    e?: string
  }
  interface ForwardBundle {
    v?: number
    msgs: ForwardEntry[]
  }

  /**
   * 合并转发阅读器（QQ 式）：全屏按时间顺序展开每条原始消息；
   * 包内再套合并卡片时进入下一层（栈式导航，返回键回上一层）。
   */
  const props = defineProps<{ modelValue: boolean, bundle: ForwardBundle | null }>()
  const emit = defineEmits<{ (e: 'update:modelValue', v: boolean): void }>()

  /** 阅读栈：最底是当前打开的合并卡片，点嵌套卡片入栈，返回键出栈 */
  const stack = ref<ForwardBundle[]>([])
  const current = computed(() => stack.value[stack.value.length - 1] ?? props.bundle)

  const imgUrls = ref<Record<string, string>>({})

  function parse (m: { forwardJson?: string } | null | undefined): ForwardBundle | null {
    if (!m?.forwardJson) return null
    try {
      const b = JSON.parse(m.forwardJson) as ForwardBundle
      return Array.isArray(b?.msgs) ? b : null
    } catch {
      return null
    }
  }

  watch(() => [props.modelValue, props.bundle] as const, ([open]) => {
    if (open) {
      const b = props.bundle ?? null
      stack.value = b ? [b] : []
      void loadImages(b)
    } else {
      stack.value = []
    }
  })

  async function loadImages (b: ForwardBundle | null) {
    imgUrls.value = {}
    if (!b) return
    for (const m of b.msgs) {
      if (m.k === 'image' && m.i && !imgUrls.value[m.i]) {
        imgUrls.value[m.i] = await fileUrl(m.i).catch(() => '')
      }
    }
  }

  function openNested (m: ForwardEntry) {
    // 嵌套卡片：服务端不允许直接转发时包内不会有 forward 条目；
    // 保险起见这里仍做兜底（理论上到不了）
    try {
      const b = JSON.parse(m.c ?? '{}') as ForwardBundle
      if (Array.isArray(b?.msgs)) {
        stack.value = [...stack.value, b]
        void loadImages(b)
      }
    } catch { /* 嵌套内容损坏则忽略 */ }
  }

  function nestedPreview (m: ForwardEntry) {
    return unescape(m.c?.replace('[聊天记录]', '').trim()) || t('chat.forward.title')
  }

  function goBack () {
    if (stack.value.length > 1) {
      stack.value = stack.value.slice(0, -1)
      return
    }
    emit('update:modelValue', false)
  }

  function fmtTime (ms?: number) {
    if (!ms) return ''
    try {
      return timeLabel(new Date(ms).toISOString())
    } catch {
      return ''
    }
  }
</script>
