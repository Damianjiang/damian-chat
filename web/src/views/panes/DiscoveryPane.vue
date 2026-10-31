<template>
  <v-sheet class="wx-panes" flat>
    <div class="wx-pane-header pa-2"><span class="wx-pane-title">{{ t('nav.discovery') }}</span></div>

    <v-list class="pa-2 wx-stagger" lines="two">
      <v-list-item @click="router.push({ name: 'moments' })">
        <template #prepend><v-avatar :color="avatarColor('moments')" size="36"><v-icon color="white" icon="mdi-camera-burst" size="22" /></v-avatar></template>
        <v-list-item-title>{{ t('discovery.moments') }}</v-list-item-title>
        <template #append><v-icon icon="mdi-chevron-right" /></template>
      </v-list-item>

      <v-list-item @click="router.push({ name: 'search' })">
        <template #prepend><v-avatar :color="avatarColor('search')" size="36"><v-icon color="white" icon="mdi-magnify" size="22" /></v-avatar></template>
        <v-list-item-title>{{ t('discovery.search') }}</v-list-item-title>
        <template #append><v-icon icon="mdi-chevron-right" /></template>
      </v-list-item>

      <v-list-item @click="scanDialog = true">
        <template #prepend><v-avatar :color="avatarColor('scan')" size="36"><v-icon color="white" icon="mdi-qrcode-scan" size="22" /></v-avatar></template>
        <v-list-item-title>{{ t('discovery.scan') }}</v-list-item-title>
        <v-list-item-subtitle>{{ t('discovery.scan.sub') }}</v-list-item-subtitle>
        <template #append><v-icon icon="mdi-chevron-right" /></template>
      </v-list-item>
    </v-list>

    <!-- 扫一扫：真实摄像头 + jsQR 解析 -->
    <qr-scanner v-model="scanDialog" />
  </v-sheet>
</template>

<script setup lang="ts">
  import { ref } from 'vue'
  import { useRouter } from 'vue-router'
  import QrScanner from '@/components/QrScanner.vue'
  import { avatarColor } from '@/lib/utils'
  import { t } from '@/lib/i18n'

  const router = useRouter()
  const scanDialog = ref(false)
</script>
