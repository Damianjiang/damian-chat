<template>
  <v-dialog :max-width="860" :model-value="open" persistent @update:model-value="o => (open = o)">
    <v-card v-if="src" class="wx-media-preview" color="black" @contextmenu.prevent="onCtx">
      <template #append>
        <v-btn color="white" icon="mdi-close" variant="text" @click="open = false" />
      </template>

      <!-- 视频：原生 <video> 播放（此前只有 <v-img>，视频无法预览） -->
      <video
        v-if="kind === 'video'"
        :controls="true"
        :muted="false"
        class="wx-media-el"
        :src="src"
        @loadeddata="ready = true"
      />

      <!-- 音频：原生 <audio> 播放 -->
      <div v-else-if="kind === 'audio'" class="wx-audio-wrap">
        <audio :controls="true" :src="src" />
      </div>

      <!-- 图片：v-img -->
      <v-img v-else :alt="alt" class="fit-contain" max-height="70vh" :src="src" />

      <!-- 自定义右键菜单：压掉浏览器默认图片菜单（菜单里只有"查看图片信息"等无用项，
           还会暴露直链），换成 保存 / 复制链接 -->
      <ContextMenu v-model="ctxOpen" :x="ctxX" :y="ctxY" :width="150">
        <v-card width="150">
          <v-list density="compact">
            <v-list-item prepend-icon="mdi-download" :title="t('msg.menu.download')" @click="saveMedia" />
            <v-list-item prepend-icon="mdi-content-copy" :title="t('msg.menu.copy')" @click="copyLink" />
          </v-list>
        </v-card>
      </ContextMenu>
    </v-card>
  </v-dialog>
</template>

<script setup lang="ts">
  import { computed, ref, watch, watchEffect } from 'vue'
  import ContextMenu from '@/components/ContextMenu.vue'
  import { t } from '@/lib/i18n'
  import { fileUrl } from '@/lib/files'

  const props = defineProps<{
    imageId?: string
    url?: string
    alt?: string
    modelValue: boolean
    /** 媒体类型：image / video / audio（用于选择 <img> / <video> / <audio> 渲染） */
    kind?: 'image' | 'video' | 'audio'
  }>()

  const emit = defineEmits<{ 'update:modelValue': [v: boolean] }>()

  const open = ref(props.modelValue)
  const ready = ref(false)
  watch(open, v => emit('update:modelValue', v))
  watch(() => props.modelValue, v => {
    open.value = v
  })

  const kind = computed(() => props.kind ?? 'image')
  const src = ref('')

  /* ---- 右键菜单（压掉浏览器默认图片菜单） ---- */
  const ctxOpen = ref(false)
  const ctxX = ref(0)
  const ctxY = ref(0)

  function onCtx (e: MouseEvent) {
    ctxX.value = e.clientX
    ctxY.value = e.clientY
    ctxOpen.value = true
  }

  /** 保存：签名 URL 直接落 <a download>（fileUrl 生成的是带票据的临时地址，浏览器直接另存） */
  function saveMedia () {
    ctxOpen.value = false
    const a = document.createElement('a')
    a.href = src.value
    a.download = props.alt || 'media'
    a.rel = 'noopener'
    document.body.appendChild(a)
    a.click()
    a.remove()
  }

  async function copyLink () {
    ctxOpen.value = false
    try { await navigator.clipboard.writeText(src.value) } catch { /* 剪贴板权限拒绝：静默 */ }
  }

  watchEffect(() => {
    src.value = ''
    if (props.url) {
      src.value = props.url
    } else if (props.imageId) {
      fileUrl(props.imageId).then(u => {
        src.value = u
      })
    }
  })
</script>

<style scoped>
.wx-media-preview {
  padding: 0;
}
.wx-media-preview :deep(.v-card__append) {
  position: absolute;
  top: 8px;
  right: 8px;
}
.wx-media-el {
  width: 100%;
  max-height: 70vh;
  display: block;
  background: #000;
}
.wx-audio-wrap {
  padding: 40px;
  width: 100%;
}
.wx-audio-wrap audio {
  width: 100%;
}
</style>
