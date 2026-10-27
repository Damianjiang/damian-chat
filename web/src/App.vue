<template>
  <v-app>
    <!--
      浏览器确认门（对标主流 IM 网页版）：不做自动检测。
      进入后先显示确认页：上面是 Vuetify 组件，下面两个纯 HTML 按钮
      （index.html 里还有同款纯 HTML 兜底门，Vue 挂不上时可用）。
      能看到组件 → 「能看到，进入应用」→ 进入应用；
      看不到 → 「看不到」→ 显示"您当前浏览器版本过低"页（四个浏览器下载）。
      确认过一次（localStorage/cookie）直接进入应用。
    -->
    <template v-if="confirmed">
      <transition name="wx-page" mode="out-in">
        <router-view />
      </transition>
    </template>
    <browser-gate v-else @confirmed="onConfirmed" />

    <!-- 全局「危险操作确认」对话框：lib/confirm.ts 的单例状态绑定在此，
         各组件调 confirm() 统一弹出，危险操作（拉黑/删除/清空）走此确认。
         v-if 按需挂载：只有 confirm() 激活时才渲染 DOM，彻底杜绝遮罩层/
         inert 属性残留在页面上把全站点"锁死"（此前一直挂载 + 状态失同步
         导致电脑版所有按钮/输入框全点不动）。允许 ESC / 点遮罩关闭（视为取消）。 -->
    <v-dialog v-if="confirmState.show" :model-value="confirmState.show" max-width="360" @update:model-value="onDialogClose">
      <v-card>
        <v-card-title class="text-body-large">{{ confirmState.title }}</v-card-title>
        <v-card-text class="text-body-medium" style="white-space: pre-wrap; word-break: break-word">{{ confirmState.text }}</v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="resolveConfirm(false)">{{ confirmState.cancelText }}</v-btn>
          <v-btn :color="confirmState.danger ? 'error' : 'primary'" variant="flat" @click="resolveConfirm(true)">{{ confirmState.confirmText }}</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>
  </v-app>
</template>

<script setup lang="ts">
  import { onMounted, ref } from 'vue'
  import { useRouter } from 'vue-router'
  import BrowserGate from '@/components/BrowserGate.vue'
  import { confirmState, resolveConfirm } from '@/lib/confirm'
  import { clearTokens, getAccessToken } from '@/lib/api'
  import { initRealtime } from '@/lib/realtime'
  import { watchSystemTheme } from '@/lib/theme'
  import { useAuthStore } from '@/stores/auth'
  import { useContactsStore } from '@/stores/contacts'
  import { useConversationsStore } from '@/stores/conversations'
  import { useThemeStore } from '@/stores/theme'

  const theme = useThemeStore()

  function readConfirmed (): boolean {
    try {
      if (localStorage.getItem('wx_browser_confirmed') === '1') return true
    } catch { /* localStorage 不可用时走 cookie */ }
    return /(?:^|;\s*)wx_browser_confirmed=1/.test(document.cookie)
  }

  const confirmed = ref(readConfirmed())
  const router = useRouter()
  const auth = useAuthStore()
  const conversations = useConversationsStore()
  const contacts = useContactsStore()

  /**
   * 会话恢复（F5/直达内页）：有令牌则拉取当前用户、预载会话与通讯录、接通实时通道，
   * 保证刷新后不丢登录态与消息。
   */
  async function bootstrap () {
    if (!getAccessToken()) return
    try {
      await auth.fetchMe()
    } catch {
      // 令牌过期且续期失败：清本地并回登录页
      clearTokens()
      auth.user = null
      if (router.currentRoute.value.name !== 'login') await router.push({ name: 'login' })
      return
    }
    await Promise.allSettled([conversations.loadSessions(), contacts.load(), contacts.loadRequests()])
    // 登录/刷新后自动拉取服务端「最近 1 周」聊天记录，灌入浏览器缓存（IDB），
    // 之后可离线查看 + 打开会话秒开；并发受 pullRecentHistoryIntoCache 内部限流。
    void conversations.pullRecentHistoryIntoCache().catch(() => {})
    initRealtime()
    if (router.currentRoute.value.name === 'login') await router.push({ name: 'chat' })
  }

  function onConfirmed () {
    confirmed.value = true
    void bootstrap()
  }

  /** 关闭全局确认框（ESC / 点击遮罩）→ 视为取消。 */
  function onDialogClose (v: boolean) {
    if (!v && confirmState.show) resolveConfirm(false)
  }

  onMounted(() => {
    if (confirmed.value) void bootstrap()
    // 「跟随系统」实时切换：系统在深浅色之间变，App 立即跟着变（此前要刷新才生效）
    watchSystemTheme(() => theme.applySystemDark())
  })
</script>
