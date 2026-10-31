<template>
  <v-card class="wx-moment-card" :color="'surface'" variant="flat">
    <div class="d-flex gap-3">
      <user-avatar
        :alt="m.author?.nickname"
        :nickname="m.author?.nickname"
        :size="display.lgAndUp.value ? 44 : 40"
        :src="m.author?.avatar"
      />

      <div class="flex-grow-1 d-flex flex-column gap-2" style="min-width: 0">
        <div class="d-flex justify-space-between align-center">
          <span class="text-body-large font-weight-medium">{{ m.author?.nickname }}</span>
          <span class="text-body-small text-medium-emphasis">{{ timeLabel(m.createdAt) }}</span>
        </div>

        <p v-if="m.text" class="wx-moment-text ma-0 text-body-medium">{{ m.text }}</p>

        <div v-if="resolvedImages.length > 0" :class="['wx-moment-images', `wx-moment-images--${cols}`]">
          <img
            v-for="(img, i) in resolvedImages"
            :key="i"
            :alt="t('moments.img', { n: i + 1 })"
            loading="lazy"
            :src="img"
            @click="emit('preview', img)"
          >
        </div>

        <!-- 点赞：有人赞时显示 -->
        <div v-if="likeNames" class="wx-moment-likes">
          <v-icon color="primary" icon="mdi-heart" size="16" />
          <span>{{ likeNames }}</span>
        </div>

        <!-- 评论 -->
        <div v-if="m.comments.length > 0" class="wx-moment-comments">
          <div v-for="c in m.comments" :key="c.id" class="wx-moment-comment">
            <strong>{{ c.nickname }}</strong>
            <span class="text-medium-emphasis">{{ c.text }}</span>
          </div>
        </div>

        <!-- 操作：与内容区分隔，按钮放大、间距放宽，不再挤成一坨 -->
        <div class="wx-moment-actions">
          <v-btn
            :prepend-icon="likedByMe ? 'mdi-heart' : 'mdi-heart-outline'"
            color="primary"
            density="comfortable"
            size="small"
            variant="text"
            @click="toggleLike"
          >
            {{ t('moments.like') }}
          </v-btn>

          <v-btn
            prepend-icon="mdi-comment-outline"
            density="comfortable"
            size="small"
            variant="text"
            @click="emit('comment', m)"
          >
            {{ t('moments.comment') }}
          </v-btn>

          <v-btn
            v-if="m.author?.id === meId"
            color="error"
            density="comfortable"
            prepend-icon="mdi-delete-outline"
            size="small"
            variant="text"
            @click="emit('delete', m)"
          >
            {{ t('moments.delete') }}
          </v-btn>
        </div>
      </div>
    </div>
  </v-card>
</template>

<script setup lang="ts">
  import type { Moment } from '@/types'
  import { computed, onMounted, ref, watch } from 'vue'
  import { useDisplay } from 'vuetify'
  import UserAvatar from '@/components/UserAvatar.vue'
  import { fileUrl } from '@/lib/files'
  import { t } from '@/lib/i18n'
  import { timeLabel } from '@/lib/utils'

  const props = defineProps<{
    moment: Moment
    meId: string
  }>()

  const emit = defineEmits<{
    preview: [url: string]
    comment: [m: Moment]
    like: [m: Moment]
    unlike: [m: Moment]
    delete: [m: Moment]
  }>()

  const display = useDisplay()

  /** 模板内访问当前动态的别名 */
  const m = computed(() => props.moment)

  /**
   * 朋友圈图片存的是「fileId」（32 位 hex），不是 URL。
   * 必须经 fileUrl 解析成带鉴权的 blob 再渲染，否则 <img> 直接拿 fileId 当 src → 全显示不出
   * （用户看到的"朋友圈图片/布局不对"根因）。预览也传解析后的 URL。
   */
  const resolvedImages = ref<string[]>([])

  async function resolveImages () {
    const ids = props.moment.images ?? []
    resolvedImages.value = await Promise.all(ids.map(id => {
      const s = (id ?? '').trim()
      // 已是可渲染地址则直接用（兼容历史数据）
      if (/^(https?:|data:|blob:|\/)/i.test(s)) return s
      return fileUrl(s).catch(() => '')
    }))
    resolvedImages.value = resolvedImages.value.filter(u => u.length > 0)
  }

  watch(() => props.moment, () => { void resolveImages() }, { deep: true })
  onMounted(resolveImages)

  /** 手机 3 列 / iPad 4 列 / 电脑 5 列 */
  const cols = computed(() => {
    if (display.lgAndUp.value) return 5
    if (display.mdAndUp.value) return 4
    return 3
  })

  const likeNames = computed(() =>
    props.moment.likes.map(l => l.nickname).join('、'),
  )

  const likedByMe = computed(() =>
    props.moment.likes.some(l => l.userId === props.meId),
  )

  function toggleLike () {
    if (likedByMe.value) emit('unlike', props.moment)
    else emit('like', props.moment)
  }
</script>

<style scoped>
.wx-moment-card {
  border-radius: 12px;
  margin-bottom: 12px;
}

.wx-moment-text {
  white-space: pre-wrap;
  word-break: break-word;
}

.wx-moment-likes {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
  color: rgb(var(--v-theme-wechat-text-secondary));
}

.wx-moment-comments {
  background: rgba(128, 128, 128, 0.08);
  border-radius: 8px;
  padding: 8px 10px;
  display: flex;
  flex-direction: column;
  gap: 5px;
}

.wx-moment-comment {
  display: flex;
  align-items: baseline;
  gap: 8px;
  font-size: 14px;
  line-height: 1.4;
}

.wx-moment-comment strong {
  flex-shrink: 0;
  color: rgb(var(--v-theme-wechat-text-primary));
  font-weight: 600;
}

.wx-moment-actions {
  display: flex;
  gap: 4px;
  padding: 6px 0 2px;
  margin-top: 4px;
  border-top: 1px solid rgba(128, 128, 128, 0.16);
  align-items: center;
  flex-wrap: wrap;
}
</style>
