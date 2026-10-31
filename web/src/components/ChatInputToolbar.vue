<template>
  <div class="wx-input">
    <div v-if="showToolbarAbove" class="d-flex align-center wx-input-tools">
      <v-btn
        v-for="t in tools"
        :key="t.icon"
        :icon="t.icon"
        size="small"
        :title="t.label"
        variant="text"
        @click="t.event"
      />

      <input
        ref="fileRef"
        class="d-none"
        type="file"
        @change="onFile"
      >

      <input
        ref="imageRef"
        accept="image/*"
        class="d-none"
        type="file"
        @change="onFile"
      >
    </div>

    <!-- 上传进度（图片/文件/语音） -->
    <div v-if="uploading || uploadError" class="wx-upload-bar">
      <template v-if="uploading">
        <span class="text-body-small">{{ t('input.uploading') }} {{ uploadPercent }}%</span>

        <v-progress-linear
          class="mt-1"
          color="primary"
          height="4"
          :model-value="uploadPercent"
          rounded
        />
      </template>

      <span v-else class="text-body-small" style="color: #fa5151">{{ uploadError }}</span>
    </div>

    <div v-if="recording" class="d-flex align-center justify-space-between wx-rec-bar">
      <div class="d-flex align-center">
        <span class="wx-rec-dot" />
        <span class="text-body-medium ml-2">{{ t('input.recording') }} {{ recLabel }}</span>
      </div>

      <div class="d-flex ga-1">
        <v-btn size="small" variant="text" @click="cancelRec">{{ t('input.cancel') }}</v-btn>
        <v-btn color="primary" :loading="recUploading" size="small" @click="finishRec">{{ t('input.send') }}</v-btn>
      </div>
    </div>

    <div v-else class="d-flex align-center gap-2 wx-input-main">
      <v-btn icon="mdi-emoticon-cool" size="small" variant="text" @click="emojiOpen = !emojiOpen" />

      <v-btn
        v-if="isGroup && members?.length"
        icon="mdi-at"
        size="small"
        title="@"
        variant="text"
        @click="mentionOpen = true"
      />

      <v-textarea
        ref="textareaRef"
        v-model="draft"
        auto-grow
        class="wx-textarea flex-grow-1"
        color="wechat-chat-bg"
        density="comfortable"
        :hide-details="true"
        max-rows="6"
        :maxlength="MAX_TEXT"
        :placeholder="placeholder"
        rows="1"
        variant="plain"
        @keydown="onKeydown"
      />

      <!-- 接近上限才显示字数计数（微信式），超限被 maxlength 截断 -->
      <span v-if="draft.length > MAX_TEXT - 200" class="wx-char-count">{{ draft.length }}/{{ MAX_TEXT }}</span>

      <v-btn
        v-if="draft.trim()"
        color="primary"
        icon="mdi-send"
        size="small"
        @click="submit"
      />
    </div>

    <emoji-panel v-if="emojiOpen" @close="emojiOpen = false" @pick="pickEmoji" />

    <!-- @ 成员选择 -->
    <v-bottom-sheet v-model="mentionOpen" max-width="360" scrollable>
      <v-card class="pa-2">
        <div class="text-title-small pa-2">{{ t('input.members') }}</div>

        <v-list density="compact">
          <!-- @全体成员：正文渲染端 MessageBubble 的 @ 高亮正则 /@([^\s@]+)/ 天然匹配「@全体成员」，
               服务端按普通文本存，无需改后端。群内始终可用（手动输入也等价）。 -->
          <v-list-item
            @click="append(t('input.all') + ' '); mentionOpen = false"
          >
            <template #prepend>
              <v-avatar color="primary" :size="32">
                <v-icon icon="mdi-account-multiple" />
              </v-avatar>
            </template>

            <v-list-item-title>{{ t('input.all') }}</v-list-item-title>
            <v-chip color="primary" class="ml-auto" size="x-small">{{ t('input.all.chip') }}</v-chip>
          </v-list-item>

          <v-list-item
            v-for="m in (members ?? [])"
            :key="m.userId"
            @click="append(`@${m.nickname} `); mentionOpen = false"
          >
            <template #prepend>
              <v-avatar :color="avatarColor(m.userId)" :size="32">
                <span>{{ m.nickname.slice(0, 1) }}</span>
              </v-avatar>
            </template>

            <v-list-item-title>{{ m.nickname }}</v-list-item-title>
            <v-chip v-if="m.role === 'owner'" class="ml-auto" size="x-small">{{ t('input.role.owner') }}</v-chip>
            <v-chip v-else-if="m.role === 'admin'" class="ml-auto" size="x-small">{{ t('input.role.admin') }}</v-chip>
          </v-list-item>
        </v-list>
      </v-card>
    </v-bottom-sheet>
  </div>
</template>

<script setup lang="ts">
  import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
  import EmojiPanel from '@/components/EmojiPanel.vue'
  import { api } from '@/lib/api'
  import { markEmojiUsed } from '@/lib/emoji'
  import { t } from '@/lib/i18n'
  import { avatarColor } from '@/lib/utils'

  const props = defineProps<{
    placeholder?: string
    showToolbarAbove?: boolean
    isGroup?: boolean
    /** 群成员列表，用于 @ 选择 */
    members?: { userId: string, nickname: string, role?: string }[]
  }>()

  const emit = defineEmits<{
    send: [payload: {
      type: string
      content?: string
      imageId?: string
      fileName?: string
      fileSize?: number
      stickerEmoji?: string
      quoteJson?: string
      voiceSeconds?: number
    }]
    /** 「对方正在输入」输入事件（组件内部按 3s 节流） */
    typing: []
    /** 打开发起投票对话框（仅群聊显示入口） */
    poll: []
  }>()

  const draft = ref('')
  const emojiOpen = ref(false)
  const mentionOpen = ref(false)
  const fileRef = ref<HTMLInputElement | null>(null)
  const imageRef = ref<HTMLInputElement | null>(null)
  const textareaRef = ref<{ focus: () => void } | null>(null)

  /* ---------- 「对方正在输入」信号（3s 节流） ---------- */
  let lastTypingAt = 0
  watch(draft, () => {
    if (!draft.value.trim()) return
    const now = Date.now()
    if (now - lastTypingAt >= 3000) {
      lastTypingAt = now
      emit('typing')
    }
  })

  /** 聚焦输入框（引用消息/公告插入后调用，让用户直接可打字）。 */
  function focus () {
    nextTick(() => {
      textareaRef.value?.focus()
    })
  }

  /** 单条文本上限：与服务端 Validate.MaxMessageText 一致，超出部分输入时即被截断 */
  const MAX_TEXT = 2000

  /**
   * 上传大小上限（与后端全局 1GB 上限对齐；头像/语音在后端按 kind 有 5MB/10MB 更严上限）。
   * 超过即前端直接提示，避免白耗流量。
   */
  const MAX_UPLOAD = 1024 * 1024 * 1024

  // 图片与文件各自独立选择器：此前共用一个按文档扩展名过滤的 input，
  // 导致「图片」按钮弹出的选择器里选不了 png/jpg 等常见图片。
  const tools = computed(() => [
    { icon: 'mdi-image-plus', label: t('input.img'), event: () => imageRef.value?.click() },
    { icon: 'mdi-folder-multiple-plus', label: t('input.file'), event: () => fileRef.value?.click() },
    { icon: 'mdi-microphone', label: t('input.voice'), event: startRecording },
    // 群投票（微信式）：仅群聊显示入口
    ...(props.isGroup ? [{ icon: 'mdi-poll', label: t('poll.create'), event: () => emit('poll') }] : []),
  ])

  /* ---------- 真实语音录音（MediaRecorder） ---------- */
  const recording = ref(false)
  const recSeconds = ref(0)
  const recUploading = ref(false)
  const uploading = ref(false)
  const uploadPercent = ref(0)
  const uploadError = ref('')
  let recorder: MediaRecorder | null = null
  let recStream: MediaStream | null = null
  let recTimer: ReturnType<typeof setInterval> | null = null
  let recChunks: BlobPart[] = []

  const recLabel = computed(() => {
    const s = recSeconds.value
    return `${String(Math.floor(s / 60)).padStart(2, '0')}:${String(s % 60).padStart(2, '0')}`
  })

  /** 上传/录音失败提示（复用上传错误条展示，2.5s 自动消失） */
  function showError (msg: string) {
    uploadError.value = msg
    setTimeout(() => {
      uploadError.value = ''
    }, 2500)
  }

  async function startRecording () {
    if (recording.value) return
    // 浏览器不支持录音 / 拒绝麦克风授权：如实提示，不再伪造一条假语音消息
    if (typeof MediaRecorder === 'undefined') {
      showError(t('input.no.rec'))
      return
    }
    try {
      recStream = await navigator.mediaDevices.getUserMedia({ audio: true })
      const mime = MediaRecorder.isTypeSupported('audio/webm;codecs=opus')
        ? 'audio/webm;codecs=opus'
        : (MediaRecorder.isTypeSupported('audio/mp4') ? 'audio/mp4' : '')
      recorder = new MediaRecorder(recStream, mime ? { mimeType: mime } : undefined)
      recChunks = []
      recorder.ondataavailable = e => {
        if (e.data.size > 0) recChunks.push(e.data)
      }
      recorder.onstop = () => {}
      recorder.start()
      recSeconds.value = 0
      recording.value = true
      recTimer = setInterval(() => {
        recSeconds.value += 1
        if (recSeconds.value >= 60) finishRec()
      }, 1000)
    } catch {
      showError(t('input.mic.deny'))
    }
  }

  function cleanupRec () {
    if (recTimer) clearInterval(recTimer)
    recTimer = null
    if (recStream) {
      for (const t of recStream.getTracks()) t.stop()
    }
    recStream = null
    recorder = null
    recChunks = []
    recording.value = false
  }

  function cancelRec () {
    try {
      recorder?.stop()
    } catch { /* ignore */ }
    cleanupRec()
  }

  async function finishRec () {
    if (!recording.value || recUploading.value) return
    const seconds = Math.max(1, recSeconds.value)
    const rec = recorder
    const recMime = rec?.mimeType || 'audio/webm'
    const container = recMime.includes('mp4') ? 'm4a' : 'webm'
    // stop() 之后 dataavailable/onstop 是异步入列：此前同步 new Blob(recChunks) 再清空 recChunks，
    // 最后一块（短录音甚至全部分片）还没进 recChunks 就被清掉 → <100 字节静默丢弃。
    // 改为等 onstop 再组 Blob，并带 1.5s 兜底（避免个别浏览器不触发 onstop 时永久挂起）。
    const type = recMime.split(';', 1)[0] || 'audio/webm'
    let settled = false
    const blob = await new Promise<Blob>(resolve => {
      const done = () => {
        if (settled) return
        settled = true
        resolve(new Blob(recChunks, { type }))
      }
      if (rec) {
        rec.onstop = done
        try { rec.stop() } catch { done() }
        setTimeout(done, 1500)
      } else {
        done()
      }
    })
    cleanupRec()

    if (blob.size < 100) return
    recUploading.value = true
    try {
      const form = new FormData()
      form.append('file', new File([blob], `voice-${Date.now()}.${container}`, { type }))
      form.append('kind', 'voice')
      uploading.value = true
      uploadPercent.value = 0
      const res = await api.post('/files/upload', form, {
        headers: { 'Content-Type': 'multipart/form-data' },
        // 语音大文件同样避免被 20s 全局超时误判失败
        timeout: 0,
        onUploadProgress: e => {
          if (e.total) uploadPercent.value = Math.min(100, Math.round((e.loaded * 100) / e.total))
        },
      })
      const meta = res.data
      emit('send', { type: 'voice', content: `/api/files/${meta.id}`, imageId: meta.id, voiceSeconds: seconds })
    } catch (error) {
      console.error(error)
    } finally {
      recUploading.value = false
      // ★ 语音路径也占用了共享的 uploading（进度条）。不在此复位，
      // 传完语音后上传条会永久常驻（看着像"卡在 100%"）。onFile 路径已在自身 finally 复位。
      uploading.value = false
      uploadPercent.value = 0
    }
  }

  function onKeydown (e: KeyboardEvent) {
    // 中文/日文输入法组词中的回车是"确认候选词"，绝不能当发送（P0:输入法用户每次选词都会误发）
    if (e.isComposing || (e as unknown as { keyCode?: number }).keyCode === 229) return
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      submit()
    }
  }

  function submit () {
    const text = draft.value.trim()
    if (!text) return
    emit('send', { type: 'text', content: text })
    draft.value = ''
    emojiOpen.value = false
  }

  function pickEmoji (emoji: string) {
    insertAtCursor(emoji)
    markEmojiUsed(emoji) // 记入「我 → 表情」
    emojiOpen.value = false
  }

  /**
   * 在光标处插入文本（不再恒追加到末尾）：取底层原生 <textarea> 的 selectionStart/End 做拼接。
   * 取不到元素时退回旧的「追加到末尾」行为。
   */
  function insertAtCursor (text: string) {
    const el = (textareaRef.value as unknown as { $el?: HTMLElement } | null)?.$el
      ?.querySelector?.('textarea') as HTMLTextAreaElement | null
    if (el && typeof el.selectionStart === 'number' && !el.disabled) {
      const start = el.selectionStart
      const end = el.selectionEnd ?? el.selectionStart
      draft.value = draft.value.slice(0, start) + text + draft.value.slice(end)
      nextTick(() => {
        el.focus()
        el.selectionStart = el.selectionEnd = start + text.length
      })
    } else {
      draft.value += text
    }
  }

  /** 由父组件触发插入文本（如群公告、@ 等） */
  function append (text: string) {
    insertAtCursor(text)
  }

  defineExpose({ append, focus, draft })

  // 组件卸载兜底：聊天中正在录音时直接返回/切换会话，会漏掉 cancelRec ——
  // （麦克风被持续占用 + interval 继续走）。卸载时统一停止录音、释放轨道、清掉计时器。
  onBeforeUnmount(() => {
    cancelRec()
  })

  async function onFile (e: Event) {
    const input = e.target as HTMLInputElement
    const file = input.files?.[0]
    if (!file) return
    // 云盘模式：任何文件都能传（不拦截）。这里只决定它进「图片消息」还是「文件消息」：
    // ・安全的位图图片 → 图片消息（聊天里 <img> 内联预览）；
    // ・其它一切（SVG/视频/文档/可执行/未知）→ 文件消息（下载卡片，浏览器强制下载，绝不内联渲染）。
    // 安全不靠"拒收文件"，而靠"不内联渲染不可信内容 + 服务端 nosniff/attachment"。
    const isSafeImage = SAFE_IMAGE_EXTS.includes(extOf(file.name))
    const kind = isSafeImage ? 'image' : 'file'
    // 大小预检（与后端 1GB 上限对齐），超限直接提示，不上传白耗流量
    if (file.size > MAX_UPLOAD) {
      const capLabel = MAX_UPLOAD >= 1024 * 1024 * 1024
        ? `${Math.floor(MAX_UPLOAD / 1024 / 1024 / 1024)}GB`
        : `${Math.floor(MAX_UPLOAD / 1024 / 1024)}MB`
      showError(t('input.file.tooBig', { n: capLabel }))
      input.value = ''
      return
    }
    const form = new FormData()
    form.append('file', file)
    form.append('kind', kind)
    uploading.value = true
    uploadPercent.value = 0
    try {
      // timeout:0 —— 大文件（1GB 内）上传耗时远超默认 20s，全局超时会把它误判失败
      const res = await api.post('/files/upload', form, {
        headers: { 'Content-Type': 'multipart/form-data' },
        timeout: 0,
        onUploadProgress: e => {
          if (e.total) uploadPercent.value = Math.min(100, Math.round((e.loaded * 100) / e.total))
        },
      })
      const meta = res.data
      if (isSafeImage) {
        emit('send', { type: 'image', content: '', imageId: meta.id })
      } else {
        emit('send', { type: 'file', content: '', fileName: file.name, fileSize: file.size, imageId: meta.id })
      }
    } catch (error: any) {
      uploadError.value = error?.response?.data?.error ?? t('input.upload.fail')
      setTimeout(() => {
        uploadError.value = ''
      }, 2500)
    } finally {
      uploading.value = false
      input.value = ''
    }
  }

  /** 安全位图图片扩展名（聊天里唯一允许 <img> 内联渲染的类型；SVG/视频等一律走下载卡片）。 */
  const SAFE_IMAGE_EXTS = ['.png', '.jpg', '.jpeg', '.gif', '.webp', '.bmp', '.heic', '.heif']

  /** 取文件名扩展名（小写，含前导点；无扩展名返回空串）。 */
  function extOf (name: string): string {
    const i = name.lastIndexOf('.')
    return i > 0 ? name.slice(i).toLowerCase() : ''
  }
</script>

<style scoped>
.wx-upload-bar {
  padding: 6px 12px 4px;
  border-bottom: 1px solid rgb(var(--v-theme-wechat-divider));
  background: rgb(var(--v-theme-surface));
}
</style>

<style scoped>
.wx-input {
  position: relative;
  border-top: 1px solid rgb(var(--v-theme-wechat-divider));
  background: rgb(var(--v-theme-wechat-chat-bg));
  padding: 8px;
}

.wx-input-main {
  gap: 8px;
}

.wx-textarea :deep(.v-field) {
  background: transparent;
}

/* 字数计数：接近上限才出现，贴右侧不抢焦点 */
.wx-char-count {
  align-self: flex-end;
  flex: 0 0 auto;
  font-size: 11px;
  line-height: 1;
  color: rgb(var(--v-theme-wechat-text-secondary));
  user-select: none;
}

.wx-rec-bar {
  padding: 4px 4px;
}

.wx-rec-dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  background: #fa5151;
  animation: wx-blink 1s infinite;
}

@keyframes wx-blink {
  50% { opacity: 0.2; }
}
</style>
