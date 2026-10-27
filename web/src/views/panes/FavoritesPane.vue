<template>
  <v-sheet class="wx-chat-surface wx-panes" flat>
    <v-app-bar class="wx-chat-bar" color="surface" density="compact" flat>
      <v-btn v-if="isMobile" icon="mdi-arrow-left" variant="text" @click="goBack" />
      <v-toolbar-title class="text-body-large font-weight-medium">收藏</v-toolbar-title>
    </v-app-bar>

    <div v-if="loading" class="d-flex justify-center py-8">
      <v-progress-circular color="primary" indeterminate />
    </div>

    <div v-else-if="items.length === 0" class="flex-grow-1 d-flex flex-column align-center justify-center text-medium-emphasis">
      <v-icon class="mb-3" icon="mdi-star-outline" size="56" />
      <div class="text-body-medium">还没有收藏</div>
      <div class="text-body-small mt-1">在聊天里长按消息或动态卡片即可收藏</div>
    </div>

    <div v-else class="wx-fav-bg flex-grow-1 overflow-y-auto wx-scroll pa-2">
      <div class="wx-fav-panel">
        <v-list bg-color="surface" class="wx-fav-list">
          <v-list-item v-for="f in items" :key="f.id" @click="open(f)">
            <template #prepend>
              <v-avatar :color="f.targetType === 'moment' ? 'primary' : 'secondary'" :size="40">
                <v-icon :icon="f.targetType === 'moment' ? 'mdi-image-multiple-outline' : 'mdi-message-text-outline'" />
              </v-avatar>
            </template>

            <v-list-item-title>{{ f.title || '(无标题)' }}</v-list-item-title>

            <v-list-item-subtitle v-if="f.preview" class="text-truncate">{{ f.preview }}</v-list-item-subtitle>

            <template #append>
              <v-btn
                icon="mdi-star"
                size="x-small"
                variant="text"
                @click.stop="remove(f)"
              />
            </template>
          </v-list-item>
        </v-list>
      </div>
    </div>

    <v-snackbar v-model="snack" :timeout="1500">{{ snackMsg }}</v-snackbar>
  </v-sheet>
</template>

<script setup lang="ts">
  import { onMounted, ref } from 'vue'
  import { useRouter } from 'vue-router'
  import { api } from '@/lib/api'
  import { isPageMode } from '@/lib/device'

  interface FavoriteItem {
    id: string
    targetType: 'message' | 'moment'
    targetId: string
    title: string
    preview: string
    imageUrl?: string | null
    createdAt: string
  }

  const router = useRouter()
  const isMobile = isPageMode

  const items = ref<FavoriteItem[]>([])
  const loading = ref(true)
  const snack = ref(false)
  const snackMsg = ref('')

  onMounted(load)

  async function load () {
    loading.value = true
    try {
      const res = await api.get('/favorites')
      items.value = res.data
    } catch {
      snackMsg.value = '加载收藏失败'
      snack.value = true
    } finally {
      loading.value = false
    }
  }

  async function remove (f: FavoriteItem) {
    try {
      await api.delete(`/favorites/${f.id}`)
      items.value = items.value.filter(x => x.id !== f.id)
      snackMsg.value = '已取消收藏'
      snack.value = true
    } catch {
      snackMsg.value = '操作失败'
      snack.value = true
    }
  }

  function open (f: FavoriteItem) {
    if (f.targetType === 'moment') {
      router.push({ name: 'moment-detail', params: { id: f.targetId } })
      return
    }
    snackMsg.value = '消息收藏仅作留存预览'
    snack.value = true
  }

  function goBack () {
    router.back()
  }
</script>

<style scoped>
.wx-fav-bg {
  background: #ededed;
}

.wx-fav-panel {
  border-radius: 10px;
  overflow: hidden;
}

.v-theme--dark .wx-fav-bg {
  background: #121212;
}
</style>
