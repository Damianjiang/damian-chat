<template>
  <div
    class="user-avatar"
    :class="{ 'user-avatar--circle': circle, 'user-avatar--gradient': variant === 'gradient' }"
    :style="{ width: size + 'px', height: size + 'px', background: bgStyle, borderRadius: radius }"
  >
    <img v-if="resolvedSrc" :alt="alt || nickname || ''" :src="resolvedSrc">
    <span v-else :style="{ fontSize: Math.round(size * 0.42) + 'px' }">{{ initial }}</span>
  </div>
</template>

<script setup lang="ts">
  import { computed, ref, watch } from 'vue'
  import { fileUrl } from '@/lib/files'
  import { avatarColor, avatarGradient, avatarInitial } from '@/lib/utils'

  /**
   * 统一头像（通讯录样式：纯色圆角，有照片显照片）。
   * src 传的是「fileId」（服务端头像/群头像字段存的都是 32 位 hex fileId，不是 URL），
   * 这里统一走 fileUrl 换成带鉴权的 blob 地址再渲染；已是 http/data/blob URL 则直接用。
   * 没有照片时按昵称取纯色背景墙（与通讯录 v-avatar avatarColor 完全一致，不再是"立体"渐变）。
   */
  const props = withDefaults(defineProps<{
    nickname?: string | null
    src?: string | null | undefined
    size?: number
    alt?: string
    /** 兼容旧调用：圆形头像。现默认即圆形，保留无害。 */
    circle?: boolean
    /** solid=纯色圆（通讯录样式，默认）；gradient=旧渐变（个别场景才用） */
    variant?: 'solid' | 'gradient'
  }>(), { nickname: '', src: '', size: 40, alt: '', circle: true, variant: 'solid' })

  const size = computed(() => Number(props.size) || 40)
  const initial = computed(() => avatarInitial(props.nickname))
  /** 背景：纯色（通讯录样式）或可选渐变 */
  const bgStyle = computed(() =>
    (props.variant === 'gradient' ? avatarGradient : avatarColor)(props.nickname ?? ''))
  /** 默认圆形（与通讯录 v-avatar 一致） */
  const radius = computed(() => '50%')

  /** fileId → objectURL 解析结果；src 为空 / 非 fileId 且非 URL 时保持空。 */
  const resolvedSrc = ref('')

  async function resolve (raw: string | null) {
    const s = (raw ?? '').trim()
    if (!s) {
      resolvedSrc.value = ''
      return
    }
    // 已是可直接渲染的地址：原样用（兼容历史数据里存 URL 的情况）
    if (/^(https?:|data:|blob:|\/)/i.test(s)) {
      resolvedSrc.value = s
      return
    }
    // 其余一律当 fileId：走带鉴权 + 本地缓存的 blob 下载
    resolvedSrc.value = await fileUrl(s).catch(() => '')
  }

  watch(() => props.src, v => {
    void resolve(v ?? '')
  }, { immediate: true })
</script>

<style scoped>
.user-avatar {
  flex: 0 0 auto;
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
  border-radius: 50%;
  color: #fff;
  font-weight: 500;
  user-select: none;
  box-shadow: inset 0 0 0 1px rgba(0, 0, 0, 0.05);
}

.user-avatar img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  border-radius: 50%;
}
</style>
