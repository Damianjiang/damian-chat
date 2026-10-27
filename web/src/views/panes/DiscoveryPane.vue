<template>
  <v-sheet class="wx-panes" flat>
    <div class="wx-pane-header pa-2"><span class="wx-pane-title">发现</span></div>

    <v-list class="pa-2 wx-stagger" lines="two">
      <v-list-item @click="router.push({ name: 'moments' })">
        <template #prepend><v-avatar :color="avatarColor('moments')" size="36"><v-icon color="white" icon="mdi-camera-burst" size="22" /></v-avatar></template>
        <v-list-item-title>朋友圈</v-list-item-title>
        <template #append><v-icon icon="mdi-chevron-right" /></template>
      </v-list-item>

      <v-list-item @click="router.push({ name: 'search' })">
        <template #prepend><v-avatar :color="avatarColor('search')" size="36"><v-icon color="white" icon="mdi-magnify" size="22" /></v-avatar></template>
        <v-list-item-title>搜索</v-list-item-title>
        <template #append><v-icon icon="mdi-chevron-right" /></template>
      </v-list-item>

      <v-list-item @click="scanDialog = true">
        <template #prepend><v-avatar :color="avatarColor('scan')" size="36"><v-icon color="white" icon="mdi-qrcode-scan" size="22" /></v-avatar></template>
        <v-list-item-title>扫一扫</v-list-item-title>
        <v-list-item-subtitle>识别二维码：登录确认 / 加好友</v-list-item-subtitle>
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

  const router = useRouter()
  const scanDialog = ref(false)
</script>
