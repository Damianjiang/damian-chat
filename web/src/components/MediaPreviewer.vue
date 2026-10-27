<template>
  <v-dialog :max-width="860" :model-value="open" persistent @update:model-value="o => (open = o)">
    <v-card v-if="src" class="wx-media-preview" color="black">
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
    </v-card>
  </v-dialog>
</template>

<script setup lang="ts">
  import { computed, ref, watch, watchEffect } from 'vue'
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
