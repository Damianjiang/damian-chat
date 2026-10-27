<template>
  <v-sheet class="wx-chat-surface wx-panes" flat>
    <div class="wx-pane-header d-flex align-center justify-space-between pa-2">
      <v-btn
        v-if="isMobile"
        class="mr-1"
        icon="mdi-arrow-left"
        variant="text"
        @click="goBack"
      />

      <span class="wx-pane-title">朋友圈</span>

      <v-btn
        class="ml-auto"
        icon="mdi-pencil"
        size="small"
        variant="text"
        @click="postSheet = true"
      />
    </div>

    <div ref="feedRef" class="flex-grow-1 overflow-y-auto wx-scroll pa-3" @scroll="onFeedScroll">
      <div class="wx-stagger">
        <v-card class="mb-3 pa-2" color="background" variant="tonal">
          <div class="d-flex align-center gap-2">
            <user-avatar alt="我的头像" :nickname="auth.user?.nickname" :size="40" :src="auth.user?.avatar" />
            <span class="text-body-large">我</span>
          </div>
        </v-card>

        <moment-card
          v-for="m in moments.items"
          :key="m.id"
          :me-id="meId"
          :moment="m"
          @comment="onComment"
          @delete="onDelete"
          @like="onLike"
          @preview="openPreview"
          @unlike="onUnlike"
        />
      </div>

      <div class="pa-4 text-center text-body-small text-medium-emphasis">
        {{ moments.loading ? '加载中…' : (moments.hasMore ? '' : '没有更多了') }}
      </div>

      <!-- 评论输入 bottom-sheet -->
      <v-bottom-sheet v-model="commentSheet" max-width="480" scrollable>
        <v-card v-if="commenting" class="pa-4">
          <div class="text-title-small mb-2">评论 {{ commenting.author?.nickname ?? '' }}</div>
          <v-text-field v-model="commentText" autofocus placeholder="写下你的评论…" @keydown.enter="submitComment" />

          <div class="d-flex justify-end gap-2">
            <v-btn variant="text" @click="commentSheet = false">取消</v-btn>
            <v-btn color="primary" :disabled="!commentText.trim()" @click="submitComment">发送</v-btn>
          </div>
        </v-card>
      </v-bottom-sheet>
    </div>

    <!-- 发动态 bottom-sheet -->
    <v-bottom-sheet v-model="postSheet" max-width="520" scrollable>
      <v-card class="pa-4">
        <v-textarea
          v-model="postText"
          hide-details
          label="这一刻的想法…"
          rows="4"
          variant="plain"
        />

        <div class="d-flex align-center gap-2">
          <v-btn
            :disabled="uploading"
            icon="mdi-image-plus"
            size="small"
            variant="tonal"
            @click="pickImages"
          />

          <v-progress-circular v-if="uploading" indeterminate size="20" width="2" />

          <div class="d-flex flex-wrap gap-1 flex-grow-1">
            <div v-for="(img, i) in postImages" :key="i" class="wx-img-pick">
              <v-img
                :alt="'图' + (i + 1)"
                class="rounded-lg"
                cover
                height="64"
                :src="img.url"
                width="64"
              />

              <v-btn
                class="wx-img-remove"
                color="error"
                icon="mdi-close-circle"
                size="x-small"
                @click="postImages.splice(i, 1)"
              />
            </div>
          </div>
        </div>

        <v-select
          v-model="postScope"
          density="compact"
          hide-details
          :items="scopeItems"
          label="谁可以看"
          variant="outlined"
        />

        <div class="d-flex justify-end gap-2 mt-4">
          <v-btn variant="text" @click="closePost">取消</v-btn>
          <v-btn color="primary" :disabled="!canPost" :loading="posting" @click="submitPost">发表</v-btn>
        </div>

        <input
          ref="imgInput"
          accept="image/*"
          class="d-none"
          multiple
          type="file"
          @change="onFilePicked"
        >
      </v-card>
    </v-bottom-sheet>

    <media-previewer v-model="previewOpen" :url="previewUrl" />

    <!-- 错误/提示 -->
    <v-snackbar v-model="noticeBar" location="top" :timeout="3000">{{ notice }}</v-snackbar>

    <!-- 删除确认 -->
    <v-dialog v-model="deleteConfirm" max-width="320">
      <v-card>
        <v-card-text class="pt-4">删除这条动态吗？删除后不可恢复。</v-card-text>

        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="deleteConfirm = false">取消</v-btn>
          <v-btn color="error" :loading="deleting" @click="confirmDelete">删除</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>
  </v-sheet>
</template>

<script setup lang="ts">
  import type { Moment } from '@/types'
  import { computed, onMounted, ref } from 'vue'
  import { useRouter } from 'vue-router'
  import MediaPreviewer from '@/components/MediaPreviewer.vue'
  import MomentCard from '@/components/MomentCard.vue'
  import UserAvatar from '@/components/UserAvatar.vue'
  import { api } from '@/lib/api'
  import { isPageMode } from '@/lib/device'
  import { fileUrl } from '@/lib/files'
  import { useAuthStore } from '@/stores/auth'
  import { useMomentsStore } from '@/stores/moments'

  const router = useRouter()
  const auth = useAuthStore()
  const moments = useMomentsStore()

  const isMobile = isPageMode
  const meId = computed(() => auth.user?.id ?? 'me')
  const feedRef = ref<HTMLElement | null>(null)

  function goBack () {
    if (window.history.length > 1) router.back()
    else router.push({ name: 'discovery' })
  }

  const postSheet = ref(false)
  const commentSheet = ref(false)
  const previewOpen = ref(false)
  const previewUrl = ref('')
  const postText = ref('')
  const postImages = ref<{ url: string, id?: string }[]>([])
  const postScope = ref<'all' | 'friends' | 'private'>('all')
  const posting = ref(false)
  const commenting = ref<Moment | null>(null)
  const commentText = ref('')
  const imgInput = ref<HTMLInputElement | null>(null)
  const deleteConfirm = ref(false)
  const deleting = ref(false)
  const deletingId = ref('')
  const notice = ref('')
  const uploading = ref(false)
  let noticeTimer: ReturnType<typeof setTimeout> | undefined

  const noticeBar = computed({
    get: () => notice.value.length > 0,
    set: (v: boolean) => {
      if (!v) notice.value = ''
    },
  })

  function showError (text: string) {
    notice.value = text
    clearTimeout(noticeTimer)
    noticeTimer = setTimeout(() => {
      notice.value = ''
    }, 3000)
  }

  const scopeItems = [
    { title: '所有人', value: 'all' },
    { title: '我的好友', value: 'friends' },
    { title: '仅自己', value: 'private' },
  ]

  const canPost = computed(() => postText.value.trim().length > 0 || postImages.value.length > 0)

  function onFeedScroll (e: Event) {
    const el = e.target as HTMLElement
    if (el.scrollTop + el.clientHeight >= el.scrollHeight - 80) {
      moments.load(true).catch(() => {})
    }
  }

  function openPreview (url: string) {
    previewUrl.value = url
    previewOpen.value = true
  }

  async function onLike (m: Moment) {
    try {
      await moments.like(m.id, true)
    } catch {
      showError('点赞失败，请重试')
    }
  }

  async function onUnlike (m: Moment) {
    try {
      await moments.like(m.id, false)
    } catch {
      showError('操作失败，请重试')
    }
  }

  function onDelete (m: Moment) {
    deletingId.value = m.id
    deleteConfirm.value = true
  }

  async function confirmDelete () {
    deleting.value = true
    try {
      await moments.remove(deletingId.value)
      deleteConfirm.value = false
    } catch {
      deleteConfirm.value = false
    } finally {
      deleting.value = false
    }
  }

  function onComment (m: Moment) {
    commenting.value = m
    commentText.value = ''
    commentSheet.value = true
  }

  async function submitComment () {
    if (!commenting.value || !commentText.value.trim()) return
    try {
      await moments.comment(commenting.value.id, commentText.value.trim())
      commentSheet.value = false
    } catch {
      showError('评论发送失败，请重试')
    }
  }

  function closePost () {
    postSheet.value = false
    postText.value = ''
    postImages.value = []
  }

  function pickImages () {
    imgInput.value?.click()
  }

  async function onFilePicked (e: Event) {
    const input = e.target as HTMLInputElement
    uploading.value = true
    try {
      for (const file of Array.from(input.files ?? [])) {
        if (file.size > 20 * 1024 * 1024) {
          showError(`图片「${file.name}」超过 20MB，已跳过`)
          continue
        }
        const form = new FormData()
        form.append('file', file)
        form.append('kind', 'image')
        const res = await api.post('/files/upload', form, { headers: { 'Content-Type': 'multipart/form-data' } })
        const signed = await fileUrl(res.data.id)
        postImages.value.push({ url: signed || `/api/files/${res.data.id}`, id: res.data.id })
      }
    } catch {
      showError('图片上传失败，请重试')
    } finally {
      uploading.value = false
      input.value = ''
    }
  }

  async function submitPost () {
    posting.value = true
    try {
      await moments.post(postText.value.trim(), postImages.value.map(i => i.id ?? ''), postScope.value)
      closePost()
    } catch {
      showError('发表失败，请重试')
    } finally {
      posting.value = false
    }
  }

  onMounted(() => {
    moments.load(false)
  })
</script>

<style scoped>
.wx-img-pick {
  position: relative;
}

.wx-img-remove {
  position: absolute;
  top: -6px;
  right: -6px;
}

.wx-infinite {
  min-height: 100%;
}
</style>
