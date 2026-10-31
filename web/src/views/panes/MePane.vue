<template>
  <v-sheet class="wx-panes wx-me-bg" flat>
    <!-- 顶部资料卡（点进去是个人资料页） -->
    <div class="pa-3">
      <v-sheet
        class="d-flex align-center ga-4 pa-4 rounded-lg"
        color="surface"
        @click="router.push({ name: 'profile' })"
      >
        <user-avatar :nickname="auth.user?.nickname" :size="72" :src="auth.user?.avatar" />

        <div class="flex-grow-1">
          <div class="text-headline-small font-weight-medium text-truncate">{{ auth.user?.nickname }}</div>
          <div class="text-body-small text-medium-emphasis">{{ t('me.wxid') }}：{{ auth.user?.wxId || '-' }}</div>
        </div>

        <v-btn icon="mdi-qrcode" variant="text" @click.stop="qrDialog = true" />
        <v-icon icon="mdi-chevron-right" />
      </v-sheet>
    </div>

    <v-list bg-color="surface" class="mx-2 mb-2 wx-me-group">
      <v-list-item @click="router.push({ name: 'search' })">
        <template #prepend><v-icon color="primary" icon="mdi-magnify" /></template>
        <v-list-item-title>{{ t('common.search') }}</v-list-item-title>
        <template #append><v-icon icon="mdi-chevron-right" /></template>
      </v-list-item>
    </v-list>

    <v-list bg-color="surface" class="mx-2 mb-2 wx-me-group">
      <v-list-item @click="router.push({ name: 'favorites' })">
        <template #prepend><v-icon color="primary" icon="mdi-star-outline" /></template>
        <v-list-item-title>{{ t('me.favorites') }}</v-list-item-title>
        <template #append><v-icon icon="mdi-chevron-right" /></template>
      </v-list-item>

      <v-list-item @click="router.push({ name: 'moments' })">
        <template #prepend><v-icon color="primary" icon="mdi-camera-account" /></template>
        <v-list-item-title>{{ t('nav.moments') }}</v-list-item-title>
        <template #append><v-icon icon="mdi-chevron-right" /></template>
      </v-list-item>

      <v-list-item @click="router.push({ name: 'emojis' })">
        <template #prepend><v-icon color="primary" icon="mdi-emoticon-outline" /></template>
        <v-list-item-title>{{ t('me.emojis') }}</v-list-item-title>
        <template #append><v-icon icon="mdi-chevron-right" /></template>
      </v-list-item>
    </v-list>

    <v-list bg-color="surface" class="mx-2 mb-2 wx-me-group">
      <v-list-item @click="router.push({ name: 'settings' })">
        <template #prepend><v-icon color="primary" icon="mdi-cog-outline" /></template>
        <v-list-item-title>{{ t('settings.title') }}</v-list-item-title>
        <template #append><v-icon icon="mdi-chevron-right" /></template>
      </v-list-item>
    </v-list>

    <div class="flex-grow-1" />

    <v-list v-if="isPageMode" bg-color="surface" class="mx-2 mb-2 wx-me-group">
      <v-list-item @click="logoutDialog = true">
        <div class="text-body-large text-center" style="width: 100%; color: #fa5151">{{ t('settings.logout') }}</div>
      </v-list-item>
    </v-list>

    <!-- 我的二维码名片 -->
    <v-dialog v-model="qrDialog" max-width="340">
      <v-card class="text-center">
        <v-card-title>{{ t('me.qr.title') }}</v-card-title>

        <v-card-text>
          <user-avatar class="mb-3" :nickname="auth.user?.nickname" :size="64" :src="auth.user?.avatar" />

          <div class="font-weight-medium mb-3">{{ auth.user?.nickname }}</div>

          <div class="d-inline-block pa-3 rounded-lg" style="background: #fff">
            <img v-if="myQr" :alt="t('me.qr.alt')" :src="myQr" style="width: 200px; height: 200px">
            <v-progress-circular v-else color="primary" indeterminate />
          </div>

          <div class="text-body-small text-medium-emphasis mt-3">{{ t('me.qr.hint') }}</div>
        </v-card-text>

        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="qrDialog = false">{{ t('common.close') }}</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 退出确认 -->
    <v-dialog v-model="logoutDialog" max-width="300">
      <v-card>
        <v-card-title class="text-body-large">{{ t('me.logout.confirm') }}</v-card-title>

        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="logoutDialog = false">{{ t('common.cancel') }}</v-btn>
          <v-btn color="red" @click="logout">{{ t('me.logout.btn') }}</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>
  </v-sheet>
</template>

<script setup lang="ts">
  import QRCode from 'qrcode'
  import { ref, watch } from 'vue'
  import { useRouter } from 'vue-router'
  import UserAvatar from '@/components/UserAvatar.vue'
  import { isPageMode } from '@/lib/device'
  import { t } from '@/lib/i18n'
  import { stopRealtime } from '@/lib/realtime'
  import { useAuthStore } from '@/stores/auth'

  const router = useRouter()
  const auth = useAuthStore()

  /* 二维码名片：内容 wxapp://user/{id}，扫一扫页据此拉取资料并加好友 */
  const qrDialog = ref(false)
  const logoutDialog = ref(false)
  const myQr = ref('')

  watch(qrDialog, v => {
    if (!v) return
    myQr.value = ''
    QRCode.toDataURL(`wxapp://user/${auth.user?.id}`, { width: 200, margin: 1 })
      .then(url => {
        myQr.value = url
      })
      .catch(() => { /* 保持 loading */ })
  })

  async function logout () {
    logoutDialog.value = false
    stopRealtime()
    await auth.logout()
    router.replace({ name: 'login' })
  }
</script>

<style scoped>
.wx-me-bg {
  background: #ededed;
}

.wx-me-group {
  border-radius: 10px;
  overflow: hidden;
}

.v-theme--dark .wx-me-bg {
  background: #121212;
}
</style>
