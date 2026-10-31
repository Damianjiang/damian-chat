<template>
  <v-sheet class="wx-panes" flat>
    <v-app-bar class="wx-chat-bar" color="surface" density="compact" flat>
      <v-btn v-if="isMobile" icon="mdi-arrow-left" variant="text" @click="goBack" />
      <v-toolbar-title class="text-body-large font-weight-medium">{{ t('settings.blocked') }}</v-toolbar-title>
    </v-app-bar>

    <div class="wx-blocked-bg flex-grow-1 overflow-y-auto wx-scroll pa-2">
      <div class="text-body-small text-medium-emphasis px-1 pb-1">
        {{ t('settings.blocked.hint') }}
      </div>

      <v-list bg-color="surface" class="wx-settings-group">
        <template v-if="blockedUsers.length > 0">
          <v-list-item v-for="b in blockedUsers" :key="b.id" class="wx-blocked-item">
            <template #prepend>
              <v-avatar color="secondary" :size="40">
                <span>{{ (b.user?.nickname ?? '?').slice(0, 1) }}</span>
              </v-avatar>
            </template>

            <template #title>
              <div class="d-flex align-center gap-2">
                <span class="text-body-medium">{{ b.user?.nickname }}</span>
                <span v-if="b.user?.phone" class="text-body-small text-medium-emphasis">{{ b.user.phone }}</span>
              </div>
            </template>
            <template #subtitle>
              <span class="text-body-small text-medium-emphasis">{{ b.isFriend ? t('settings.blocked.isFriend') : t('settings.blocked.notFriend') }} · {{ t('settings.blocked.badge') }}</span>
            </template>

            <template #append>
              <div class="d-flex align-center flex-wrap gap-1">
                <v-btn size="x-small" variant="flat" :loading="busy[b.id] === 'unblock'" @click="unblockUser(b)">{{ t('settings.blocked.unblock') }}</v-btn>
                <v-btn v-if="b.isFriend" color="error" size="x-small" variant="text" :loading="busy[b.id] === 'delete'" @click="deleteFriend(b)">{{ t('settings.blocked.deleteFriend') }}</v-btn>
              </div>
            </template>
          </v-list-item>
        </template>

        <v-list-item v-else-if="loaded" class="justify-center" disabled>
          <v-list-item-title class="text-medium-emphasis text-body-medium d-flex flex-column align-center py-4">
            <v-icon color="medium-emphasis" icon="mdi-shield-check-outline" size="44" class="mb-2" />
            <span>{{ t('settings.blocked.empty') }}</span>
          </v-list-item-title>
        </v-list-item>

        <v-list-item v-else class="justify-center" disabled>
          <v-list-item-title class="text-medium-emphasis">{{ t('common.loading') }}</v-list-item-title>
        </v-list-item>
      </v-list>
    </div>

    <v-snackbar v-model="snack" color="error" :timeout="2000">{{ snackMsg }}</v-snackbar>
    <v-snackbar v-model="snackOk" color="success" :timeout="1800">{{ snackOkMsg }}</v-snackbar>
  </v-sheet>
</template>

<script setup lang="ts">
  import { onMounted, ref } from 'vue'
  import { useRouter } from 'vue-router'
  import { isPageMode } from '@/lib/device'
  import { t } from '@/lib/i18n'
  import { useContactsStore } from '@/stores/contacts'
  import { confirm } from '@/lib/confirm'
  import type { User } from '@/types'

  const router = useRouter()
  const contacts = useContactsStore()
  const isMobile = isPageMode

  interface BlockedUser { id: string, blocked: boolean, isFriend: boolean, user: User | null }

  const blockedUsers = ref<BlockedUser[]>([])
  const busy = ref<Record<string, 'unblock' | 'delete'>>({})
  const loaded = ref(false)

  const snack = ref(false)
  const snackMsg = ref('')
  const snackOk = ref(false)
  const snackOkMsg = ref('')

  function goBack () {
    router.push({ name: 'settings' })
  }

  async function loadBlocked () {
    loaded.value = false
    try {
      const list = await contacts.loadBlocked()
      blockedUsers.value = list as unknown as BlockedUser[]
    } catch {
      // 加载失败保持空态
    } finally {
      loaded.value = true
    }
  }

  async function unblockUser (b: BlockedUser) {
    const name = b.user?.nickname ?? t('settings.blocked.thisUser')
    const ok = await confirm({
      title: t('settings.blocked.unblock.title'),
      text: t('settings.blocked.unblock.text', { name }),
      confirmText: t('settings.blocked.unblock'),
      danger: false,
    })
    if (!ok) return
    busy.value = { ...busy.value, [b.id]: 'unblock' }
    try {
      await contacts.unblock(b.id)
      snackOkMsg.value = t('settings.blocked.unblock.ok', { name })
      snackOk.value = true
      await loadBlocked()
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? t('settings.blocked.unblock.fail')
      snack.value = true
    } finally {
      busy.value = { ...busy.value }
      delete busy.value[b.id]
    }
  }

  async function deleteFriend (b: BlockedUser) {
    const name = b.user?.nickname ?? t('settings.blocked.thisUser')
    const ok = await confirm({
      title: t('settings.blocked.delete.title'),
      text: t('settings.blocked.delete.text', { name }),
      confirmText: t('common.delete'),
    })
    if (!ok) return
    busy.value = { ...busy.value, [b.id]: 'delete' }
    try {
      await contacts.removeFriend(b.id)
      snackOkMsg.value = t('settings.blocked.delete.ok', { name })
      snackOk.value = true
      await loadBlocked()
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? t('settings.blocked.delete.fail')
      snack.value = true
    } finally {
      delete busy.value[b.id]
      busy.value = { ...busy.value }
    }
  }

  onMounted(loadBlocked)
</script>

<style scoped>
.wx-chat-bar {
  border-bottom: 1px solid rgb(var(--v-theme-wechat-divider));
}

.wx-blocked-bg {
  background: #ededed;
}

.v-theme--dark .wx-blocked-bg {
  background: #121212;
}

.wx-settings-group {
  border-radius: 10px;
  overflow: hidden;
}

.wx-blocked-item {
  min-height: 64px;
}
</style>
