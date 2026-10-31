<template>
  <!-- 系统消息：居中灰字，无头像无气泡 -->
  <div v-if="message.type === 'system'" class="wx-system-row">
    <span class="wx-system-text">{{ message.content }}</span>
  </div>

  <!-- 拍一拍：微信式居中灰字「xx 拍了拍 yy」（双击头像触发，全群/对方可见） -->
  <div v-else-if="message.type === 'pat'" class="wx-system-row">
    <span class="wx-system-text">{{ patText }}</span>
  </div>

  <!-- 普通消息 -->
  <div
    v-else
    :class="[
      'wx-bubble-row',
      isMe ? 'wx-bubble-row--me' : 'wx-bubble-row--other',
    ]"
    @contextmenu.prevent="onContextMenu"
    @pointerdown="onDown"
    @pointerleave="onUp"
    @pointermove="onMove"
    @pointerup="onUp"
  >
    <!-- 对方头像：单击看资料卡，双击（群聊）拍一拍。
         触屏同样走 pointerdown 双击窗口判定；touch-action:manipulation 关掉双击缩放。 -->
    <div
      v-if="!isMe"
      class="wx-avatar-zone"
      @click="onAvatarClick"
    >
      <v-avatar class="wx-bubble-avatar" :color="avatarColor(message.senderId)" :size="display.lgAndUp.value ? 44 : 40">
        <img v-if="avatarSrc" :alt="senderName" :src="avatarSrc">
        <span v-else>{{ (senderName || '?').slice(0, 1) }}</span>
      </v-avatar>
    </div>
    <v-avatar v-else class="wx-bubble-avatar" :color="avatarColor(message.senderId)" :size="display.lgAndUp.value ? 44 : 40">
      <img v-if="avatarSrc" :alt="senderName" :src="avatarSrc">
      <span v-else>{{ (senderName || '?').slice(0, 1) }}</span>
    </v-avatar>

    <div class="wx-bubble-col">
      <div v-if="isGroup && !isMe" class="wx-sender-name">{{ senderName }}</div>
      <!-- 撤回文案 -->
      <div v-if="message.recalled" class="wx-recalled">{{ recallText }}</div>

      <!-- 文本（群聊里 @提及 会被高亮） -->
      <div v-else-if="message.type === 'text'" :class="bubbleClass">
        <div v-if="quote" class="wx-quote">
          <strong>{{ quote.senderName }}：{{ quote.text }}</strong>
        </div>

        <span class="wx-text">
          <template v-if="isGroup">
            <template v-for="(seg, i) in mentionSegments" :key="i">
              <span v-if="seg.isMention" class="wx-mention">{{ seg.text }}</span>
              <span v-else>{{ seg.text }}</span>
            </template>
          </template>
          <template v-else>{{ message.content }}</template>
        </span>
      </div>

      <!-- 图片：微信式裸图直出（不套气泡内衬，避免大图四周出现色块）。
           用原生 <img> 直接可点（@click 打开预览），不套 v-btn：v-btn 是带 min-height 的
           flex 按钮盒，把图片塞进去会被按钮行高压扁（竖图尤其明显、比例错乱）。
           原生 img + .wx-img 的 width/height:auto + object-fit:contain 才能真正按原图比例渲染。
           普通 img 给 min 尺寸 + object-fit:contain，加载前是占位灰框，加载完淡入，绝不塌陷。 -->
      <div v-else-if="message.type === 'image'" class="wx-media">
        <img
          v-if="src"
          :alt="message.content"
          :class="{ 'is-loaded': imgLoaded }"
          class="rounded-lg wx-img wx-img-fade"
          :src="src"
          @click="$emit('preview', message)"
          @error="imgLoaded = true"
          @load="imgLoaded = true"
        >
      </div>

      <!-- 语音 -->
      <div v-else-if="message.type === 'voice'" :class="bubbleClass">
        <div class="d-flex align-center gap-2">
          <v-btn
            :color="isMe ? 'white' : 'primary'"
            :icon="playing ? 'mdi-pause' : 'mdi-play'"
            size="small"
            variant="text"
            @click="togglePlay"
          />

          <span class="wx-voice-wave">{{ playing ? '▮▮▮' : message.voiceSeconds ?? 0 }}</span>
          <span class="text-body-small">{{ message.voiceSeconds ?? 0 }}″</span>
        </div>
      </div>

      <!-- 文件：可点。视频/音频可预览播放，其余走下载卡片 -->
      <div
        v-else-if="message.type === 'file'"
        class="wx-file-card"
        :class="[isMe ? '' : 'wx-file-card--other', mediaFile ? 'is-clickable' : '']"
        @click="mediaFile ? $emit('preview', message) : download()"
      >
        <v-icon color="primary" :icon="mediaIcon" size="34" />

        <div class="d-flex flex-column" :class="{ 'flex-grow-1': true }">
          <span class="text-body-medium text-truncate" style="max-width: 200px">{{ message.fileName }}</span>
          <span class="text-body-small text-medium-emphasis">{{ fmt(message.fileSize) }}</span>
        </div>

        <v-btn v-if="mediaFile" icon="mdi-play-circle" size="small" variant="text" @click.stop="$emit('preview', message)" />
        <v-btn v-else icon="mdi-download" size="small" variant="text" @click.stop="download" />
      </div>

      <!-- 表情：大表情裸出（同微信） -->
      <div v-else-if="message.type === 'sticker'" class="wx-media">
        <span class="wx-sticker">{{ message.stickerEmoji }}</span>
      </div>

      <!-- 群投票卡片：content 为 pollId，数据在 polls store（PollUpdated 实时事件驱动全员刷新） -->
      <div v-else-if="message.type === 'poll'" class="wx-media">
        <poll-card :conversation-id="message.conversationId" :poll-id="message.content" />
      </div>

      <!-- 其它 -->
      <div v-else :class="bubbleClass">{{ message.content }}</div>

      <div v-if="showTime" class="wx-bubble-time">{{ timeLabel(message.createdAt) }}</div>
    </div>

    <!-- 乐观发送状态：贴在气泡外侧（自己的消息在左侧，因为行是 row-reverse） -->
    <div v-if="message.pending || message.failed" class="wx-send-state">
      <span v-if="message.pending" class="wx-send-spin" :title="t('msg.send.pending')" />

      <span
        v-else
        class="wx-send-fail"
        :title="t('msg.send.failed')"
        @click.stop="$emit('retry', message)"
      >
        <v-icon color="error" icon="mdi-alert-circle" size="18" />
      </span>
    </div>

    <!-- 右键 / 长按菜单：回复 / 撤回(2分钟内) / 复制 / 转发 -->
    <ContextMenu v-model="menuOpen" :x="lastPoint.x" :y="lastPoint.y" :width="180">
      <v-card width="180">
        <v-list density="compact">
          <v-list-item v-if="!isMe && message.type === 'text'" @click="$emit('quote', message)">
            <v-list-item-title>{{ t('msg.menu.reply') }}</v-list-item-title>
            <template #append><v-icon icon="mdi-reply" /></template>
          </v-list-item>

          <!-- 还在发送中的乐观气泡没有服务端 id，撤回/删除都会 404 —— 先不给入口 -->
          <v-list-item v-if="isMe && canRecall && !message.pending" @click="$emit('recall', message)">
            <v-list-item-title>{{ t('msg.menu.recall') }}</v-list-item-title>
            <template #append><v-icon icon="mdi-undo" /></template>
          </v-list-item>

          <v-list-item v-if="message.type === 'text'" @click="copy">
            <v-list-item-title>{{ t('msg.menu.copy') }}</v-list-item-title>
            <template #append><v-icon icon="mdi-content-copy" /></template>
          </v-list-item>

          <v-list-item v-if="message.type === 'text'" @click="favorite">
            <v-list-item-title>{{ t('msg.menu.fav') }}</v-list-item-title>
            <template #append><v-icon icon="mdi-star-outline" /></template>
          </v-list-item>

          <v-list-item @click="$emit('forward', message)">
            <v-list-item-title>{{ t('msg.menu.forward') }}</v-list-item-title>
            <template #append><v-icon icon="mdi-forward" /></template>
          </v-list-item>

          <v-list-item @click="toggleSelect">
            <v-list-item-title>{{ props.selected ? t('msg.menu.multi.off') : t('msg.menu.multi') }}</v-list-item-title>
            <template #append><v-icon icon="mdi-checkbox-marked-circle-outline" /></template>
          </v-list-item>

          <v-divider class="my-1" />

          <!-- 仅「有附件/媒体」的消息才提供下载（纯文本没有 imageId，不给下载项 —— 避免文字消息右键出现"下载"） -->
          <v-list-item v-if="message.imageId" :title="t('msg.menu.download')" @click="downloadFile" />

          <!-- 发送失败的消息要能删掉（否则永远卡在界面上），发送中的不给入口 -->
          <v-list-item v-if="isMe && (!message.pending || message.failed)" class="text-error" @click="$emit('delete', message)">
            <v-list-item-title>{{ t('msg.menu.delete') }}</v-list-item-title>
            <template #append><v-icon icon="mdi-delete-outline" /></template>
          </v-list-item>
        </v-list>
      </v-card>
    </ContextMenu>
  </div>
</template>

<script setup lang="ts">
  import type { Message } from '@/types'
  import { computed, onBeforeUnmount, ref, watch, watchEffect } from 'vue'
  import { useDisplay } from 'vuetify'
  import { api } from '@/lib/api'
  import { downloadFile as downloadFileById, fileUrl } from '@/lib/files'
  import { t } from '@/lib/i18n'
  import { avatarColor, formatFileSize, timeLabel } from '@/lib/utils'
  import ContextMenu from '@/components/ContextMenu.vue'
  import PollCard from '@/components/PollCard.vue'

  const props = defineProps<{
    message: Message
    senderName: string
    avatar?: string
    meId: string
    isGroup: boolean
    selected?: boolean
    /** 与上一条间隔较大时才显示时间戳 */
    showTime?: boolean
    /** 拍一拍目标显示名（message.type==='pat' 时由父级传入被拍者昵称） */
    patTargetName?: string
  }>()

  const emit = defineEmits<{
    preview: [m: Message]
    recall: [m: Message]
    quote: [m: Message]
    forward: [m: Message]
    favorite: [m: Message, result: 'added' | 'removed' | 'failed', errorText?: string]
    delete: [m: Message]
    select: [m: Message]
    copied: [text: string]
    retry: [m: Message]
    /** 单击对方头像（看资料卡） */
    avatarTap: [m: Message]
    /** 双击对方头像（群聊拍一拍） */
    pat: [m: Message]
  }>()

  const display = useDisplay()
  const menuOpen = ref(false)
  let pressTimer: ReturnType<typeof setTimeout> | null = null

  const isMe = computed(() => props.message.senderId === props.meId)
  /** 是否在气泡下显示时间：与上一条间隔超过 5 分钟才显示（避免刷屏） */

  /** 发送中的乐观气泡半透明，确认后自然恢复（不做额外动画，避免"闪一下"） */
  const bubbleClass = computed(() => [
    'wx-bubble',
    isMe.value ? 'wx-bubble--me' : 'wx-bubble--other',
    { 'wx-bubble--pending': !!props.message.pending },
  ])

  /**
   * 群聊 @ 高亮：把文本里的「@昵称」拆成片段并标记，让被 @ 的人一眼可见。
   * 昵称由 @ 后到空白/标点/换行之间的连续字符构成（与 ChatInputToolbar 插入的 `@昵称 ` 一致）。
   * 纯文本安全（{{ }} 插值，无 v-html），无 XSS 面。
   */
  const mentionSegments = computed<{ text: string, isMention: boolean }[]>(() => {
    const text = props.message.content ?? ''
    if (!text) return []
    // 匹配 @ 后紧跟的连续非空白、非 @ 字符（昵称）
    const re = /@([^\s@]+)/g
    const segs: { text: string, isMention: boolean }[] = []
    let last = 0
    let m: RegExpExecArray | null
    while ((m = re.exec(text))) {
      if (m.index > last) segs.push({ text: text.slice(last, m.index), isMention: false })
      segs.push({ text: m[0], isMention: true })
      last = m.index + m[0].length
    }
    if (last < text.length) segs.push({ text: text.slice(last), isMention: false })
    return segs
  })

  const quote = computed(() => {
    if (!props.message.quoteJson) return null
    try {
      return JSON.parse(props.message.quoteJson)
    } catch {
      return null
    }
  })

  /** 附件扩展名（判断 video/audio 类可预览媒体，视频存的是 type:'file'） */
  const extName = computed(() => {
    const n = (props.message.fileName ?? '').toLowerCase()
    const i = n.lastIndexOf('.')
    return i > 0 ? n.slice(i + 1) : ''
  })
  const mediaFile = computed(() => {
    if (props.message.type !== 'file') return false
    const video = ['mp4', 'webm', 'mov', 'm4v', 'mkv', 'avi', '3gp', 'flv', 'ogv']
    const audio = ['mp3', 'm4a', 'webm', 'ogg', 'wav', 'aac', 'flac']
    const f = extName.value
    if (video.includes(f)) {
      return true
    }
    if (audio.includes(f) && f !== 'webm') {
      return true
    }
    return false
  })
  const mediaIcon = computed(() => {
    if (!mediaFile.value) return 'mdi-file-document-outline'
    if (['mp4', 'webm', 'mov', 'm4v', 'mkv', 'avi', '3gp', 'flv', 'ogv'].includes(extName.value)) return 'mdi-video'
    return 'mdi-music'
  })

  const src = ref('')
  /** 头像：props.avatar 是 32 位 hex fileId（非 URL），需经 fileUrl 解析成带鉴权 blob 再渲染 */
  const avatarSrc = ref('')

  watch(
    () => props.avatar,
    async v => {
      avatarSrc.value = ''
      if (!v) return
      // 已是可渲染地址则直接用（兼容历史 URL），否则按 fileId 解析
      if (/^(https?:|data:|blob:|\/)/i.test(v)) {
        avatarSrc.value = v
        return
      }
      avatarSrc.value = await fileUrl(v).catch(() => '')
    },
    { immediate: true },
  )

  /** 图片缩略图是否解码完成（未完成前保持透明，完成后淡入，避免"啪"地蹦出来） */
  const imgLoaded = ref(false)
  let imgFallback: ReturnType<typeof setTimeout> | null = null

  watchEffect(() => {
    fileUrl(props.message.imageId).then(u => {
      src.value = u
    })
  })

  // 兜底：极端情况下 load 事件没来（代理返回异常体等）也不能让图片永久隐形
  watch(src, () => {
    imgLoaded.value = false
    if (imgFallback) clearTimeout(imgFallback)
    imgFallback = setTimeout(() => {
      imgLoaded.value = true
    }, 1500)
  })

  /** 2 分钟内的自己的消息可撤回 */
  const canRecall = computed(() =>
    isMe.value
    && !props.message.recalled
    && Date.now() - new Date(props.message.createdAt).getTime() < 2 * 60 * 1000,
  )

  /** 撤回文案：单聊「你/对方」，群聊「昵称」 */
  const recallText = computed(() => {
    if (isMe.value) return t('msg.recall.self')
    return props.isGroup ? t('msg.recall.group', { who: props.senderName }) : t('msg.recall.other')
  })

  /** 拍一拍文案（自己发的显示「我」，否则显示昵称；群聊/单聊都是「A 拍了拍 B」） */
  const patText = computed(() => {
    const who = isMe.value ? t('msg.pat.me') : props.senderName
    const target = props.patTargetName || (props.isGroup ? t('msg.pat.group') : t('msg.pat.other'))
    return t('msg.pat', { who, target })
  })

  /* ---- 头像单/双击判定（触屏与鼠标统一走 click 事件；300ms 内两次点击 = 双击） ----
     单击：看资料卡（对方）；双击：群聊里拍一拍。
     自己的头像没有交互区（只读自己资料无意义，且自己拍自己不合法）。 */
  let avatarClickTimer: ReturnType<typeof setTimeout> | null = null
  let lastAvatarClick = 0

  function onAvatarClick () {
    const now = Date.now()
    const isDouble = now - lastAvatarClick < 300
    lastAvatarClick = now
    if (isDouble) {
      if (avatarClickTimer) { clearTimeout(avatarClickTimer); avatarClickTimer = null }
      if (props.isGroup && !isMe.value) {
        emit('pat', props.message)   // 双击 → 拍一拍（仅群聊、仅对方）
      }
      return
    }
    // 第一次点击：等 300ms 看有没有第二次，没有才执行单击（看资料卡）
    if (avatarClickTimer) clearTimeout(avatarClickTimer)
    avatarClickTimer = setTimeout(() => {
      avatarClickTimer = null
      if (!isMe.value) emit('avatarTap', props.message)
    }, 300)
  }

  onBeforeUnmount(() => {
    if (avatarClickTimer) { clearTimeout(avatarClickTimer); avatarClickTimer = null }
  })

  /** PC 右键：立即弹菜单 */
  function onContextMenu (e: MouseEvent) {
    if (pressTimer) {
      clearTimeout(pressTimer)
      pressTimer = null
    }
    menuOpen.value = true
    // 把菜单锚定到鼠标位置（v-menu 默认跟随 activator，这里用事件坐标兜底）
    lastPoint.value = { x: e.clientX, y: e.clientY }
  }

  /** 手机长按：按住 500ms 弹菜单；期间移动视为滚动则取消 */
  function onDown (e: PointerEvent) {
    startPoint.value = { x: e.clientX, y: e.clientY }
    lastPoint.value = { x: e.clientX, y: e.clientY }
    pressTimer = setTimeout(() => {
      menuOpen.value = true
      pressTimer = null
    }, 500)
  }

  function onUp () {
    if (pressTimer) {
      clearTimeout(pressTimer)
      pressTimer = null
    }
  }

  const lastPoint = ref({ x: 0, y: 0 })
  const startPoint = ref({ x: 0, y: 0 })

  /** 长按过程中移动超过 10px 视为滚动，取消菜单 */
  function onMove (e: PointerEvent) {
    if (!pressTimer) return
    const dx = Math.abs(e.clientX - startPoint.value.x)
    const dy = Math.abs(e.clientY - startPoint.value.y)
    if (dx > 10 || dy > 10) onUp()
  }

  onBeforeUnmount(() => {
    if (pressTimer) clearTimeout(pressTimer)
    if (imgFallback) clearTimeout(imgFallback)
    if (audio) {
      audio.pause()
      audio = null
    }
  })

  function toggleSelect () {
    emit('select', props.message)
    menuOpen.value = false
  }

  /** 下载附件（file / image / voice 都可下载原文件） */
  async function downloadFile () {
    const id = props.message.imageId
    if (!id) {
      menuOpen.value = false
      return
    }
    try {
      await downloadFileById(id, props.message.fileName)
    } catch { /* 失败由调用方忽略 */ }
    menuOpen.value = false
  }

  /** 收藏/取消收藏（服务端 toggle：重复收藏同一条即取消） */
  async function favorite () {
    menuOpen.value = false
    try {
      const res = await api.post('/favorites', {
        targetType: 'message',
        targetId: props.message.id,
        title: props.senderName,
        preview: (props.message.content ?? '').slice(0, 60),
      })
      emit('favorite', props.message, res.data?.toggled ? 'added' : 'removed')
    } catch (error: any) {
      emit('favorite', props.message, 'failed', error?.response?.data?.error ?? t('msg.fav.fail'))
    }
  }

  async function copy () {
    try {
      await navigator.clipboard.writeText(props.message.content ?? '')
      emit('copied', props.message.content ?? '')
      menuOpen.value = false
    } catch {
      menuOpen.value = false
    }
  }

  function fmt (bytes?: number | null) {
    return formatFileSize(bytes)
  }

  async function download () {
    if (props.message.imageId) {
      await downloadFileById(props.message.imageId, props.message.fileName)
    }
  }

  /** 语音音频地址：优先 imageId（上传回执），否则 content 中的 URL */
  const voiceUrl = ref('')

  watchEffect(() => {
    if (props.message.imageId) {
      fileUrl(props.message.imageId).then(u => {
        voiceUrl.value = u
      })
    } else {
      const c = props.message.content ?? ''
      voiceUrl.value = /^https?:\/\//.test(c) ? c : ''
    }
  })

  const playing = ref(false)
  let audio: HTMLAudioElement | null = null

  // onended 用属性赋值而非 addEventListener：属性天然覆盖，不会随播放次数累积监听器
  function togglePlay () {
    if (!voiceUrl.value) return
    if (playing.value && audio) {
      audio.pause()
      playing.value = false
      return
    }
    audio ??= Object.assign(new Audio(), {
      onended: () => {
        playing.value = false
      },
    })
    audio.src = voiceUrl.value
    audio.play().then(() => {
      playing.value = true
    }).catch(() => {
      playing.value = false
    })
  }

  onBeforeUnmount(() => {
    audio?.pause()
    audio = null
  })
</script>

<style scoped>
.wx-system-row {
  padding: 4px 12px;
  text-align: center;
}

.wx-system-text {
  font-size: 12px;
  color: rgb(var(--v-theme-wechat-text-secondary));
  background: rgba(128, 128, 128, 0.08);
  border-radius: 4px;
  padding: 2px 8px;
  display: inline-block;
}

.wx-bubble-row {
  display: flex;
  gap: 8px;
  padding: 3px 18px;
  position: relative;
  align-items: flex-start;
}

.wx-bubble-row--me {
  flex-direction: row-reverse;
}

/* 气泡列：宽度按内容自适应但不超过可用空间 */
.wx-bubble-col {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
  max-width: min(72%, 560px);
}

.wx-bubble-row--me .wx-bubble-col {
  align-items: flex-end;
}

/* 头像：小圆角方块，和气泡顶部对齐 */
.wx-bubble-avatar {
  flex: 0 0 auto;
  border-radius: 4px;
}

/* 对方头像的可交互区：单击看资料卡 / 双击（群聊）拍一拍。
   touch-action: manipulation 关掉触屏「双击缩放」，让快速两次点按被读成双击而不是缩放；
   300ms 双击窗口在 script 里判定（触屏/鼠标统一走 click）。 */
.wx-avatar-zone {
  flex: 0 0 auto;
  cursor: pointer;
  touch-action: manipulation;
}

/* ★ 头像图片自适应填满头像边框：裁切 cover，不再按原图尺寸溢出或留白；
     无照片时的首字母也按头像尺寸放合适的字号（此前 img 无尺寸约束 → 头像框里图片不贴合） */
.wx-bubble-avatar :deep(img) {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
}
.wx-bubble-avatar :deep(span) {
  font-size: 20px;
  font-weight: 500;
  line-height: 1;
}

/* ★ 真正缺失的气泡样式：这里补上带背景、圆角、尖角的完整气泡 */
.wx-bubble {
  position: relative;
  /* 多行时首行缩进两格、后续顶格 —— 视觉上和主流 IM 一致 */
  text-indent: 0;
  padding: 7px 11px;
  border-radius: 5px;
  font-size: 14px;
  line-height: 1.55;
  word-break: break-word;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  max-width: 100%;
  box-shadow: 0 1px 1px rgba(0, 0, 0, 0.04);
}

/* 对方气泡：背景 + 文字全走 Vuetify 主题变量，暗色自动翻转（不依赖 .v-theme--dark 类层叠——
   scoped 基础规则带 [data-v] 属性、特异性压过全局类覆盖，此前写死 #fff 底 + #181818 字，
   导致暗色仍是白底黑字 / 深绿底黑字，看不清）。
   背景 --v-theme-wechat-bubble-other（亮 #FFF / 暗 #26282C）、文字 --v-theme-wechat-text-primary（亮 #181818 / 暗 #E9ECEF）。 */
.wx-bubble--other {
  background: rgb(var(--v-theme-wechat-bubble-other));
  color: rgb(var(--v-theme-wechat-text-primary));
}

.wx-bubble--other::before {
  content: '';
  position: absolute;
  top: 12px;
  left: -5px;
  width: 0;
  height: 0;
  border-top: 5px solid transparent;
  border-bottom: 5px solid transparent;
  /* 尖角颜色随气泡背景变量翻转（暗色下气泡是深色，尖角不能再写死 #fff） */
  border-right: 5px solid rgb(var(--v-theme-wechat-bubble-other));
}

/* 自己：主色浅气泡（跟随主题）。文字色同样走 --v-theme-wechat-text-primary，
   亮色=深绿浅底 #95EC69 上黑字，暗色=深绿底 #2B5E3F 上浅色字，自动翻转不变形。 */
.wx-bubble--me {
  background: rgb(var(--v-theme-wechat-bubble-me));
  color: rgb(var(--v-theme-wechat-text-primary));
}

.wx-bubble--me::before {
  content: '';
  position: absolute;
  top: 12px;
  right: -5px;
  width: 0;
  height: 0;
  border-top: 5px solid transparent;
  border-bottom: 5px solid transparent;
  border-left: 5px solid rgb(var(--v-theme-wechat-bubble-me));
}

/* 时间戳：跟着气泡一起显示在下方，弱化 */
.wx-bubble-time {
  font-size: 11px;
  line-height: 1;
  color: rgb(var(--v-theme-wechat-text-secondary));
  padding: 0 2px;
  opacity: 0.75;
}

/* ★ 暗色不靠 .v-theme--dark 类（本项目暗色是 CSS 变量驱动，该类未挂在气泡祖先链上、
   此前三条 :global(.v-theme--dark) 覆盖全部是死代码）：
   气泡背景/文字已全走 --v-theme-wechat-* 变量，暗色 token 自动翻转。
   尖角颜色同样随背景变量翻转（border 用 currentColor 会跟文字色走、不对 → 直接引用背景 token）。 */

/* 乐观发送状态：气泡外侧的小指示器，不占气泡内部空间 */
.wx-send-state {
  flex: 0 0 auto;
  display: flex;
  align-items: center;
  align-self: center;
  min-width: 18px;
}

.wx-send-fail {
  display: flex;
  cursor: pointer;
  line-height: 1;
}

/* 发送中：16px 细圈自转（纯 border + transform，零依赖） */
.wx-send-spin {
  width: 14px;
  height: 14px;
  border: 2px solid rgb(var(--v-theme-on-surface), 0.18);
  border-top-color: rgb(var(--v-theme-primary));
  border-radius: 50%;
  animation: wx-spin 0.7s linear infinite;
}

@keyframes wx-spin {
  to { transform: rotate(360deg); }
}

@media (prefers-reduced-motion: reduce) {
  .wx-send-spin {
    animation: none;
  }
}

/* 未确认的气泡压淡一点，确认后瞬间恢复 */
.wx-bubble--pending {
  opacity: 0.62;
}

.wx-sender-name {
  font-size: 12px;
  color: rgb(var(--v-theme-wechat-text-secondary));
}

.wx-text {
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}

/* 群聊 @ 提及高亮：被 @ 的昵称用主色加粗，一眼可见 */
.wx-mention {
  color: rgb(var(--v-theme-primary));
  font-weight: 600;
}

/* 图片/表情：无气泡内衬，裸媒体直出（对齐微信）。
   ★ 宽度 100%（收满所在气泡列 .wx-bubble-col 的可用宽，即会话区 72%）——
   此前写 min(72%,560px) 是「列的 72% 再取 72%」双重嵌套，手机上容器只有 ~170px，
   而 .wx-img 最大 240px → 图片溢出容器向右压到同行的自己的头像上（用户反馈「图片盖头像」）。 */
.wx-media {
  position: relative;
  max-width: 100%;
}

.wx-img-btn {
  padding: 2px;
}

.wx-img {
  display: block;
  /* 等比自适应：宽列（桌面）大图钳 240px（与「气泡不过宽」原设计一致）；手机窄列收满容器
     （min(240px,100%)），绝不溢出 .wx-media 压到同行头像（此前写死 240px，容器更窄时图片溢出盖住头像）。
     高度钳 240，竖/横大图都等比缩小不变形。解码前（未 is-loaded）给占位灰框，
     占位也收在容器内（min(120px,100%)），避免塌 0 高或溢出。 */
  width: auto;
  height: auto;
  max-width: min(240px, 100%);
  max-height: 240px;
  object-fit: contain;
  background: rgba(128, 128, 128, 0.08);
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.08);
}

.wx-img.wx-img-fade:not(.is-loaded) {
  min-width: min(120px, 100%);
  min-height: min(120px, 100%);
}

.wx-img-fade {
  opacity: 0;
  transform: scale(0.98);
  transition:
    opacity var(--wx-dur-md) var(--wx-ease-out),
    transform var(--wx-dur-md) var(--wx-ease-out);
}

.wx-img-fade.is-loaded {
  opacity: 1;
  transform: scale(1);
}

@media (prefers-reduced-motion: reduce) {
  .wx-img-fade {
    transition: none;
  }
}

.wx-voice-wave {
  font-size: 13px;
}

.wx-sticker {
  font-size: 40px;
}

.wx-quote {
  border-left: 3px solid rgb(var(--v-theme-wechat-divider));
  padding: 4px 8px;
  margin-bottom: 4px;
  font-size: 13px;
  color: rgb(var(--v-theme-wechat-text-secondary));
  background: rgba(128, 128, 128, 0.08);
  border-radius: 4px;
}

.wx-file-card {
  /* 手机窄列下 220px 固定值会把文件卡撑爆 → 限到「220px 与 80% 里更小的那个」 */
  min-width: min(220px, 80%);
  max-width: 100%;
}
</style>
