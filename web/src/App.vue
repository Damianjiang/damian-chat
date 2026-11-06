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

    <!-- 全局「账号安全提醒」弹窗（防盗号）：新设备登录 / 换绑邮箱时服务端实时推送，
         被盗号的第一发现人是本人。提供了「去改密码」一键入口（改密即全设备下线）。 -->
    <v-dialog :model-value="securityAlertState.show" max-width="380" persistent>
      <v-card>
        <v-card-title class="text-body-large d-flex align-center">
          <v-icon class="mr-2" color="warning" icon="mdi-shield-alert" size="22" />
          {{ t('security.alert.title') }}
        </v-card-title>
        <v-card-text class="text-body-medium" style="word-break: break-word">
          <template v-if="securityAlertState.type === 'new-device-login'">
            {{ t('security.alert.newDevice', { time: formatAlertTime(securityAlertState.time) }) }}
          </template>
          <template v-else-if="securityAlertState.type === 'email-changed'">
            {{ t('security.alert.emailChanged', { email: securityAlertState.email }) }}
          </template>
          <template v-else>{{ t('security.alert.generic') }}</template>
          <div class="text-body-small text-medium-emphasis mt-2">{{ t('security.alert.footer') }}</div>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="dismissSecurityAlert">{{ t('security.alert.dismiss') }}</v-btn>
          <v-btn color="primary" variant="flat" @click="goSecurity">{{ t('security.alert.action') }}</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 全局「发现新版本」：打开 webapp 即检测服务端构建，更新/稍后/忽略 -->
    <update-notice />
  </v-app>
</template>

<script setup lang="ts">
  import { onMounted, ref } from 'vue'
  import { useRouter } from 'vue-router'
  import BrowserGate from '@/components/BrowserGate.vue'
  import UpdateNotice from '@/components/UpdateNotice.vue'
  import { confirmState, resolveConfirm } from '@/lib/confirm'
  import { clearTokens, getAccessToken } from '@/lib/api'
  import { dismissSecurityAlert, securityAlertState } from '@/lib/securityAlert'
  import { watchSystemTheme } from '@/lib/theme'
  import { t } from '@/lib/i18n'
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
    // 换号护栏：同浏览器换了账号（前一个没走登出/token 静默过期）→ 在拉取/回填任何
    // 会话数据之前先清掉上一个账号的内存 store 与 IDB 残留
    try { await auth.accountSwitchGuard(auth.user!.id) } catch { /* 忽略 */ }
    await Promise.allSettled([conversations.loadSessions(), contacts.load(), contacts.loadRequests()])
    // 注：不再启动时后台批量预下载聊天记录（首启流量大、低端机抢主线程，且打开会话
    // 时本来就会按需从服务器拉取）。历史记录改为「打开哪个会话拉哪个」，首启更轻。
    // 动态取实时通道模块：SignalR（55KB）不该在登录页就解析——刷新恢复场景走到这里才需要
    try {
      const { initRealtime } = await import('@/lib/realtime')
      initRealtime()
    } catch (e) { console.warn('[realtime] 初始化失败', e) }
    if (router.currentRoute.value.name === 'login') await router.push({ name: 'chat' })
  }

  function onConfirmed () {
    confirmed.value = true
    void bootstrap()
  }

  /** 安全提醒的时间展示（服务端 UTC → 本地「MM-DD HH:mm」）。 */
  function formatAlertTime (iso?: string) {
    try {
      const d = iso ? new Date(iso) : new Date()
      const p = (n: number) => String(n).padStart(2, '0')
      return `${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`
    } catch {
      return ''
    }
  }

  /** 安全提醒的「去处理」：进 设置 → 账号（改密码/看登录设备）。 */
  function goSecurity () {
    dismissSecurityAlert()
    void router.push({ name: 'settings' })
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
