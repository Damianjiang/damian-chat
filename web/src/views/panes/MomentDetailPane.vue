<template>
  <v-sheet class="wx-panes" flat>
    <v-app-bar class="wx-chat-bar" color="surface" density="compact" flat>
      <v-btn v-if="isMobile" icon="mdi-arrow-left" variant="text" @click="goBack" />
      <v-toolbar-title class="text-body-large font-weight-medium">动态详情</v-toolbar-title>
    </v-app-bar>

    <div class="flex-grow-1 overflow-y-auto wx-scroll pa-4">
      <moment-card
        v-if="moment"
        :me-id="meId"
        :moment="moment"
        @comment="openComments"
        @delete="onDelete"
        @like="onLike"
        @preview="openPreview"
        @unlike="onUnlike"
      />

      <v-empty-state v-else-if="notFound" icon="mdi-inbox-outline" title="动态不存在或已删除" />

      <div v-else class="pa-8 text-center">
        <v-progress-circular indeterminate />
      </div>

      <div v-if="moment" class="mt-6">
        <div class="text-title-small mb-2">评论</div>

        <div v-if="moment.comments.length > 0" class="d-flex flex-column gap-2">
          <div v-for="c in moment.comments" :key="c.id" class="d-flex gap-2">
            <v-avatar :color="avatarColor(c.userId)" :size="32">{{ c.nickname.slice(0, 1) }}</v-avatar>

            <div>
              <div class="text-body-small text-medium-emphasis">{{ c.nickname }}</div>
              <div class="text-body-medium">{{ c.text }}</div>
            </div>
          </div>
        </div>

        <div v-else class="text-body-small text-medium-emphasis">还没有评论</div>
      </div>
    </div>

    <!-- 评论输入 -->
    <div v-if="commenting" class="d-flex gap-2 pa-2" style="border-top: 1px solid rgb(var(--v-theme-wechat-divider))">
      <v-text-field
        v-model="commentText"
        autofocus
        hide-details
        placeholder="写下你的评论…"
        variant="plain"
        @keydown.enter="submitComment"
      />

      <v-btn color="primary" :disabled="!commentText.trim()" size="small" @click="submitComment">发送</v-btn>
    </div>

    <media-previewer v-model="previewOpen" :url="previewUrl" />
  </v-sheet>
</template>

<script setup lang="ts">
  import { computed, onMounted, ref, watch } from 'vue'
  import { useRoute, useRouter } from 'vue-router'
  import MediaPreviewer from '@/components/MediaPreviewer.vue'
  import MomentCard from '@/components/MomentCard.vue'
  import { isPageMode } from '@/lib/device'
  import { avatarColor } from '@/lib/utils'
  import { useAuthStore } from '@/stores/auth'
  import { useMomentsStore } from '@/stores/moments'

  const route = useRoute()
  const router = useRouter()
  const auth = useAuthStore()
  const moments = useMomentsStore()

  const isMobile = isPageMode
  const meId = computed(() => auth.user?.id ?? 'me')
  const momentId = computed(() => String(route.params.id ?? ''))

  const moment = computed(() => moments.items.find(m => m.id === momentId.value) ?? null)

  const commenting = ref(false)
  const commentText = ref('')
  const previewOpen = ref(false)
  const previewUrl = ref('')
  const notFound = ref(false)

  function openComments () {
    commenting.value = true
  }

  function openPreview (url: string) {
    previewUrl.value = url
    previewOpen.value = true
  }

  async function submitComment () {
    if (!moment.value || !commentText.value.trim()) return
    try {
      await moments.comment(moment.value.id, commentText.value.trim())
      commentText.value = ''
      commenting.value = false
    } catch {
      // 评论失败保持输入，用户可重试
    }
  }

  async function onLike () {
    if (moment.value) await moments.like(moment.value.id, true).catch(() => {})
  }

  async function onUnlike () {
    if (moment.value) await moments.like(moment.value.id, false).catch(() => {})
  }

  async function onDelete () {
    if (!moment.value) return
    const id = moment.value.id
    try {
      await moments.remove(id)
      goBack()
    } catch {
      // 删除失败留在原地
    }
  }

  function goBack () {
    router.push({ name: 'moments' })
  }

  /** 深链兜底：列表里找不到就单独拉取这一条（GET /moments/{id}） */
  async function ensureLoaded () {
    if (moment.value || notFound.value) return
    try {
      await moments.fetchOne(momentId.value)
    } catch {
      notFound.value = true
    }
  }

  watch(momentId, () => {
    notFound.value = false
    ensureLoaded()
  })
  onMounted(ensureLoaded)
</script>

<style scoped>
.wx-chat-bar {
  border-bottom: 1px solid rgb(var(--v-theme-wechat-divider));
}
</style>
