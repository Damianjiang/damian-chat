<template>
  <v-sheet class="wx-panes" flat>
    <v-app-bar class="wx-chat-bar" color="surface" density="compact" flat>
      <v-btn v-if="isMobile" icon="mdi-arrow-left" variant="text" @click="goBack" />
      <!-- 连点「设置」5 次 = 解锁开发者模式（见 onTitleClick） -->
      <v-toolbar-title class="text-body-large font-weight-medium" @click="onTitleClick">设置</v-toolbar-title>
    </v-app-bar>

    <div class="wx-settings-bg flex-grow-1 overflow-y-auto wx-scroll pa-2">
      <!-- 账号 -->
      <v-list bg-color="surface" class="wx-settings-group">
        <v-list-item @click="router.push({ name: 'profile' })">
          <v-list-item-title>个人资料</v-list-item-title>
          <template #append><v-icon icon="mdi-chevron-right" /></template>
        </v-list-item>

        <v-list-item @click="pwdDialog = true">
          <v-list-item-title>账号与安全</v-list-item-title>
          <template #append><v-icon icon="mdi-chevron-right" /></template>
        </v-list-item>

        <v-list-item @click="privacyDialog = true">
          <v-list-item-title>个人信息与权限</v-list-item-title>
          <template #append><v-icon icon="mdi-chevron-right" /></template>
        </v-list-item>
      </v-list>

      <!-- 通用 -->
      <v-list bg-color="surface" class="wx-settings-group mt-2">
        <v-list-item @click="router.push({ name: 'blocked' })">
          <v-list-item-title>被拉黑的用户</v-list-item-title>

          <template #append>
            <span v-if="blockedCount > 0" class="text-body-medium text-medium-emphasis mr-1">{{ blockedCount }}</span>
            <v-icon icon="mdi-chevron-right" size="small" />
          </template>
        </v-list-item>

        <v-list-item @click="notifyDialog = true">
          <v-list-item-title>通知</v-list-item-title>
          <template #append><v-icon icon="mdi-chevron-right" /></template>
        </v-list-item>

        <!-- 个性化设置：主题配色 / 深色模式 / 字体大小 / 组件半透明度 / 特效 / 背景图 全部移入子页 -->
        <v-list-item @click="router.push({ name: 'personalize' })">
          <v-list-item-title>个性化设置</v-list-item-title>
          <template #append><v-icon icon="mdi-palette-swatch" /><v-icon icon="mdi-chevron-right" /></template>
        </v-list-item>

        <!-- 问题反馈：提交后 C# 后端可见并可复制（带客户端快照，开发者模式解锁时附带最近日志） -->
        <v-list-item @click="openFeedback">
          <v-list-item-title>问题反馈</v-list-item-title>
          <template #append><v-icon icon="mdi-message-text-outline" /><v-icon icon="mdi-chevron-right" /></template>
        </v-list-item>

        <v-list-item @click="cacheDialog = true">
          <v-list-item-title>清除缓存</v-list-item-title>

          <template #append>
            <span class="text-body-medium text-medium-emphasis mr-1">{{ cacheSize }}</span>
            <v-icon icon="mdi-chevron-right" size="small" />
          </template>
        </v-list-item>

        <!-- 更新 App：拉取最新 UI 资源（Service Worker），进度条 + 完成自动刷新，失败给手动刷新 -->
        <v-list-item @click="openUpdate">
          <v-list-item-title>更新系统</v-list-item-title>

          <template #append>
            <v-icon v-if="updateAvailable" color="secondary" icon="mdi-update" size="20" />
            <v-icon v-else icon="mdi-chevron-right" size="small" />
          </template>
        </v-list-item>
      </v-list>

      <!-- 关于 -->
      <v-list bg-color="surface" class="wx-settings-group mt-2">
        <v-list-item @click="aboutDialog = true">
          <v-list-item-title>关于大面聊天</v-list-item-title>

          <template #append>
            <span class="text-body-medium text-medium-emphasis mr-1">版本 1.3.0</span>
            <v-icon icon="mdi-chevron-right" size="small" />
          </template>
        </v-list-item>
      </v-list>

      <!-- 开发者模式（连点 设置 标题 5 次解锁后才显示） -->
      <template v-if="devUnlocked">
        <v-list bg-color="surface" class="wx-settings-group mt-2">
          <v-list-item @click="openDevLogs">
            <v-list-item-title>全部客户端日志</v-list-item-title>
            <template #append>
              <span v-if="logCount > 0" class="text-body-medium text-medium-emphasis mr-1">{{ logCount }}</span>
              <v-icon icon="mdi-console" /><v-icon icon="mdi-chevron-right" size="small" />
            </template>
          </v-list-item>
        </v-list>
      </template>

      <!-- 退出 -->
      <v-list bg-color="surface" class="wx-settings-group mt-2">
        <v-list-item @click="logoutDialog = true">
          <div class="text-body-large text-center" style="width: 100%; color: #fa5151">退出登录</div>
        </v-list-item>
      </v-list>
    </div>

    <!-- 修改密码 -->
    <v-dialog v-model="pwdDialog" max-width="360">
      <v-card>
        <v-card-title>修改密码</v-card-title>

        <v-card-text>
          <v-text-field v-model="oldPwd" autocomplete="current-password" label="当前密码" type="password" />
          <v-text-field v-model="newPwd" autocomplete="new-password" label="新密码（8-32 位）" type="password" />
          <v-text-field v-model="newPwd2" autocomplete="new-password" label="确认新密码" type="password" />
        </v-card-text>

        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="pwdDialog = false">取消</v-btn>
          <v-btn color="primary" :disabled="!pwdValid" :loading="pwdLoading" @click="changePassword">确认修改</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 通知 -->
    <v-dialog v-model="notifyDialog" max-width="360">
      <v-card>
        <v-card-title>通知</v-card-title>

        <v-card-text>
          <v-switch
            v-model="notify.sound"
            color="primary"
            density="compact"
            hide-details
            label="新消息提示音"
          />

          <v-switch
            v-model="notify.desktop"
            color="primary"
            density="compact"
            hide-details
            label="桌面通知"
            @update:model-value="onDesktopToggle"
          />

          <v-switch
            v-model="notify.vibrate"
            color="primary"
            density="compact"
            hide-details
            label="消息振动（支持的设备）"
          />
        </v-card-text>

        <v-card-actions>
          <v-spacer />
          <v-btn color="primary" variant="text" @click="saveNotify">完成</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 隐私 -->
    <v-dialog v-model="privacyDialog" max-width="360">
      <v-card>
        <v-card-title>隐私</v-card-title>

        <v-card-text>
          <v-switch
            v-model="privacy.searchable"
            color="primary"
            density="compact"
            hide-details
            label="允许通过手机号/账号搜索到我"
          />

          <v-switch
            v-model="privacy.strangerMoments"
            color="primary"
            density="compact"
            hide-details
            label="允许陌生人查看十条朋友圈"
          />

          <v-switch
            v-model="privacy.signatureVisible"
            color="primary"
            density="compact"
            hide-details
            label="个性签名对他人可见"
          />
        </v-card-text>

        <v-card-actions>
          <v-spacer />
          <v-btn color="primary" :loading="privacyLoading" variant="text" @click="savePrivacy">保存</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 清除缓存（勾选制：聊天记录 / 图片视频语音 / 其它本地数据） -->
    <v-dialog v-model="cacheDialog" max-width="380">
      <v-card>
        <v-card-title>清除缓存</v-card-title>

        <v-card-text>
          <v-checkbox
            v-model="cacheOpts.chat"
            color="primary"
            density="compact"
            hide-details
            label="聊天记录（本地缓存的会话消息）"
          />

          <v-checkbox
            v-model="cacheOpts.media"
            color="primary"
            density="compact"
            hide-details
            label="图片 / 视频 / 语音（媒体文件缓存）"
          />

          <v-checkbox
            v-model="cacheOpts.local"
            color="primary"
            density="compact"
            hide-details
            label="其它本地数据（主题、通知、字体等设置）"
          />

          <div class="text-body-small text-medium-emphasis mt-2">当前缓存：{{ cacheSize }}</div>
        </v-card-text>

        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="cacheDialog = false">取消</v-btn>
          <v-btn color="primary" :disabled="!anyCacheChecked" :loading="cacheClearing" @click="doClearCache">清除</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 更新 App（真·检查更新：版本比对 + Service Worker 拉新资源） -->
    <v-dialog v-model="updateDialog" max-width="380">
      <v-card>
        <v-card-title class="text-body-large">更新系统</v-card-title>

        <v-card-text>
          <div class="text-body-small text-medium-emphasis mb-2">
            从服务器拉取最新的界面资源以节省流量。更新完成后自动刷新页面。
          </div>

          <v-sheet class="rounded pa-2 mb-2" color="wechat-chat-bg" variant="tonal">
            <div class="text-body-small d-flex justify-space-between">
              <span class="text-medium-emphasis">当前版本</span>
              <span class="text-truncate" style="max-width: 60%">{{ runningVersion }}</span>
            </div>
            <div class="text-body-small d-flex justify-space-between">
              <span class="text-medium-emphasis">服务器版本</span>
              <span class="text-truncate" style="max-width: 60%">{{ latestVersion }}</span>
            </div>
            <div v-if="hasUpdate" class="text-body-small text-primary mt-1 d-flex align-center gap-1">
              <v-icon icon="mdi-update" size="16" />
              <span>检测到新版本，点击下方按钮更新</span>
            </div>
          </v-sheet>

          <v-progress-linear
            v-if="updating"
            class="my-4"
            color="primary"
            :indeterminate="updateProgress === null"
            :model-value="updateProgress ?? 0"
          />

          <div class="text-body-small" :class="updateMsg ? 'text-medium-emphasis' : 'text-primary'">{{ updateMsg || (hasUpdate ? '检测到新版本' : '点击下方按钮检测更新') }}</div>
        </v-card-text>

        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="closeUpdate">关闭</v-btn>
          <v-btn v-if="!updating" color="primary" @click="runUpdate">{{ hasUpdate ? '立即更新' : '检查更新' }}</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 关于 -->
    <v-dialog v-model="aboutDialog" max-width="340">
      <v-card>
        <v-card-title>关于大面聊天</v-card-title>

        <v-card-text>
          <div class="text-body-medium mb-1">版本：1.3.0</div>
          <div class="text-body-medium mb-1">技术栈：Vue 3 · Vuetify 4 · ASP.NET Core 8</div>
          <div class="text-body-medium mb-1">识别设备：{{ deviceLabel(deviceKind) }}（{{ deviceKind }}）</div>
          <div class="text-body-small text-medium-emphasis" style="word-break: break-all">{{ uaShort }}</div>
        </v-card-text>

        <v-card-actions>
          <v-spacer />
          <v-btn color="primary" variant="text" @click="aboutDialog = false">关闭</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 退出确认 -->
    <v-dialog v-model="logoutDialog" max-width="300">
      <v-card>
        <v-card-title class="text-body-large">退出登录？</v-card-title>

        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="logoutDialog = false">取消</v-btn>
          <v-btn color="red" :loading="logoutLoading" @click="doLogout">退出</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 问题反馈（提交后 C# 后端可见；开发者模式解锁时自动附带最近日志快照） -->
    <v-dialog v-model="feedbackDialog" max-width="440">
      <v-card>
        <v-card-title>问题反馈</v-card-title>
        <v-card-text>
          <div class="text-body-small text-medium-emphasis mb-2">
            描述遇到的问题（可附日志）。提交后管理员可在后端查看并复制，用于定位与修复。
          </div>
          <v-textarea
            v-model="feedbackText"
            :counter="2000"
            :rows="5"
            autocomplete="off"
            class="feedback-ta"
            density="comfortable"
            label="请尽量说明：操作路径、现象、期望结果"
            variant="outlined"
          />
          <div class="text-caption text-medium-emphasis">
            <span v-if="devUnlocked">将自动附带最近 {{ LOG_ATTACH }} 条客户端日志与设备/版本快照。</span>
            <span v-else>提交时自动附带当前版本 / 设备 / 在线状态等快照。</span>
          </div>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="closeFeedback">取消</v-btn>
          <v-btn color="primary" :disabled="!feedbackText.trim() || feedbackSending" :loading="feedbackSending" @click="sendFeedback">提交</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 开发者模式 · 全部客户端日志（连点标题「设置」5 次解锁） -->
    <v-dialog v-model="devLogDialog" max-width="720" persistent>
      <v-card class="devlog-card">
        <v-card-title class="d-flex align-center">
          <v-icon icon="mdi-console" />
          <span>全部客户端日志</span>
          <v-spacer />
          <span class="text-caption text-medium-emphasis">共 {{ devLogLines.length }} 条</span>
        </v-card-title>
        <v-card-text>
          <div class="d-flex ga-2 mb-3 flex-wrap">
            <v-btn size="small" variant="tonal" @click="refreshDevLogs">刷新</v-btn>
            <v-btn size="small" variant="tonal" @click="clearDevLogs">清空</v-btn>
            <v-btn size="small" variant="tonal" @click="copyDevLogs">复制到剪贴板</v-btn>
            <v-btn size="small" variant="tonal" @click="downloadDevLogs">下载 .txt</v-btn>
            <v-spacer />
            <v-checkbox v-model="devLogLive" class="devlog-live" density="compact" hide-details label="实时" />
          </div>
          <pre class="devlog-viewer">{{ devLogText || '（暂无日志）' }}</pre>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn color="primary" variant="text" @click="devLogDialog = false">关闭</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <v-snackbar v-model="snack" :timeout="1800">{{ snackMsg }}</v-snackbar>
  </v-sheet>
</template>

<script setup lang="ts">
  import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
  import { useRouter } from 'vue-router'

  import { api } from '@/lib/api'
  import {
    buffer_,
    clientSnapshot,
    clear as clearClientLog,
    download as downloadClientLog,
    subscribe,
    toText,
    type LogEntry,
  } from '@/lib/clientlog'
  import { deviceKind, deviceLabel, isPageMode } from '@/lib/device'
  import { clearMediaCache, mediaCacheCount } from '@/lib/files'
  import { idbEstimateBytes } from '@/lib/idb'
  import { stopRealtime } from '@/lib/realtime'
  import { useAuthStore } from '@/stores/auth'
  import { useContactsStore } from '@/stores/contacts'
  import { clearConversationCache, conversationCacheSize } from '@/stores/conversations'

  const router = useRouter()
  const auth = useAuthStore()
  const contacts = useContactsStore()

  const isMobile = isPageMode
  const snack = ref(false)
  const snackMsg = ref('')

  function toast (msg: string) {
    snackMsg.value = msg
    snack.value = true
  }

  function goBack () {
    router.push({ name: 'me' })
  }

  /* ---------- 修改密码 ---------- */
  const pwdDialog = ref(false)
  const oldPwd = ref('')
  const newPwd = ref('')
  const newPwd2 = ref('')
  const pwdLoading = ref(false)
  const pwdValid = computed(() => oldPwd.value.length > 0 && newPwd.value.length >= 8 && newPwd.value === newPwd2.value)

  async function changePassword () {
    pwdLoading.value = true
    try {
      await api.post('/account/password', { oldPassword: oldPwd.value, newPassword: newPwd.value })
      toast('密码已修改，请重新登录')
      pwdDialog.value = false
      await auth.logout()
      stopRealtime()
      router.push({ name: 'login' })
    } catch (error: any) {
      toast(error?.response?.data?.error ?? '修改失败')
    } finally {
      pwdLoading.value = false
    }
  }

  /* ---------- 通知 ---------- */
  const notifyDialog = ref(false)
  const notify = reactive({ sound: true, desktop: false, vibrate: true })

  function onDesktopToggle (v: boolean | null) {
    if (v && typeof Notification !== 'undefined' && Notification.permission === 'default') {
      Notification.requestPermission().then(p => {
        notify.desktop = p === 'granted'
      })
    } else if (v && typeof Notification !== 'undefined' && Notification.permission === 'denied') {
      notify.desktop = false
      toast('浏览器已拒绝通知权限')
    }
  }

  function saveNotify () {
    localStorage.setItem('wx_notify', JSON.stringify(notify))
    notifyDialog.value = false
    toast('通知设置已保存')
  }

  /* ---------- 隐私（存服务端） ---------- */
  const privacyDialog = ref(false)
  const privacy = reactive<Record<string, boolean>>({ searchable: true, strangerMoments: true, signatureVisible: true })
  const privacyLoading = ref(false)

  async function savePrivacy () {
    privacyLoading.value = true
    try {
      const res = await api.put('/users/me', { privacy: { ...privacy } })
      auth.user = res.data
      privacyDialog.value = false
      toast('隐私设置已保存')
    } catch {
      toast('保存失败')
    } finally {
      privacyLoading.value = false
    }
  }

  /* ---------- 清除缓存（勾选制） ---------- */
  const cacheSize = ref('计算中…')
  const cacheDialog = ref(false)
  const cacheClearing = ref(false)
  const cacheOpts = reactive({ chat: true, media: true, local: false })
  const anyCacheChecked = computed(() => cacheOpts.chat || cacheOpts.media || cacheOpts.local)

  /** 保留的 localStorage 键（登录态 / 设置类，勾选「其它本地数据」才清）。 */
  const KEEP_KEYS = new Set(['wx_access_token', 'wx_refresh_token', 'wx_browser_confirmed'])

  function localCacheBytes (): { size: number, keys: string[] } {
    // 未勾选「其它本地数据」时，keep 集合 = 除 token 外全部保留（token 属登录态，绝不清）
    let size = 0
    const keys: string[] = []
    for (let i = 0; i < localStorage.length; i++) {
      const k = localStorage.key(i)
      if (!k || KEEP_KEYS.has(k)) continue
      keys.push(k)
      size += (k.length + (localStorage.getItem(k)?.length ?? 0)) * 2
    }
    return { size, keys }
  }

  function humanSize (bytes: number): string {
    if (bytes < 1024) return `${bytes}B`
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)}KB`
    return `${(bytes / 1024 / 1024).toFixed(1)}MB`
  }

  async function refreshCacheSize () {
    const [chat, media, total] = await Promise.all([
      conversationCacheSize().catch(() => 0),
      Promise.resolve(mediaCacheCount()),
      idbEstimateBytes().catch(() => 0),
    ])
    const parts: string[] = []
    if (chat > 0) parts.push(`聊天记录 ${chat} 条`)
    if (media > 0) parts.push(`媒体 ${media} 项`)
    if (total > 0) parts.push(humanSize(total))
    cacheSize.value = parts.length > 0 ? parts.join(' · ') : '0B'
  }

  async function doClearCache () {
    cacheClearing.value = true
    try {
      let cleared = 0
      if (cacheOpts.chat) {
        const n = await conversationCacheSize().catch(() => 0)
        await clearConversationCache()
        cleared += n
      }
      if (cacheOpts.media) {
        await clearMediaCache()
      }
      if (cacheOpts.local) {
        const { keys } = localCacheBytes()
        for (const k of keys) localStorage.removeItem(k)
        cleared += keys.length
      }
      cacheOpts.chat = cacheOpts.media = cacheOpts.local = false
      toast(cleared > 0 ? `已清除 ${cleared} 项缓存` : '缓存已清空')
      await refreshCacheSize()
      cacheDialog.value = false
    } finally {
      cacheClearing.value = false
    }
  }

  /* ---------- 更新 App（真·检查更新：拉服务端 version.json 与当前运行版本比对） ---------- */
  const updateDialog = ref(false)
  const updating = ref(false)
  const updateProgress = ref<number | null>(null)
  const updateMsg = ref('')
  /** 是否有新版本待激活（SW updatefound 时置 true，列表行显示更新标记） */
  const updateAvailable = ref(false)
  /** 当前运行版本 / 服务端最新版本的展示（「检查更新」结果落在这） */
  const runningVersion = ref('--')
  const latestVersion = ref('--')
  const hasUpdate = ref(false)

  let updateHandlerBound = false

  /** 当前运行的构建版本（构建期注入）。 */
  const currentBuildVersion = typeof __BUILD_VERSION__ !== 'undefined' ? __BUILD_VERSION__ : 'local'

  /** 拉取服务端 version.json，判断是否有更新。返回 true = 有新版待更新。 */
  async function checkVersion (): Promise<boolean> {
    runningVersion.value = currentBuildVersion
    let serverVer = ''
    try {
      const res = await fetch('/version.json?t=' + Date.now(), { cache: 'no-store' })
      if (res.ok) {
        const v = await res.json()
        serverVer = (v && v.version) || ''
      }
    } catch { /* 拿不到 version.json（旧后端未部署）→ 视为无更新来源 */ }
    latestVersion.value = serverVer || '无清单'
    hasUpdate.value = !!serverVer && serverVer !== currentBuildVersion
    return hasUpdate.value
  }

  /**
   * 监听新 SW 下载完成 → 列表行亮「更新」标记。
   * 注意：updatefound 是在 **ServiceWorkerRegistration** 上派发的，
   * navigator.serviceWorker（ServiceWorkerContainer）上永远收不到 —— 早期挂在
   * container 上导致标记从不亮起。这里等 registration ready 后再绑。
   */
  function bindSwUpdateWatcher () {
    if (updateHandlerBound || !('serviceWorker' in navigator)) return
    updateHandlerBound = true
    navigator.serviceWorker.ready
      .then(reg => {
        reg.addEventListener('updatefound', () => {
          updateAvailable.value = true
        })
      })
      .catch(() => { /* SW 未就绪：用户手动点「检查更新」时仍会走 runUpdate 的等价逻辑 */ })
  }

  onMounted(() => {
    bindSwUpdateWatcher()
    void checkVersion().then(has => {
      if (has) updateAvailable.value = true
    })
  })

  async function runUpdate () {
    updating.value = true
    updateProgress.value = 0
    updateMsg.value = '正在检查最新版本…'
    // 阶段 0：真·版本比对。没有新版本就到此为止，不白折腾 SW。
    const needUpdate = await checkVersion()
    if (!needUpdate) {
      updateProgress.value = 100
      updateMsg.value = '已是最新版本（' + latestVersion.value + '）。'
      updating.value = false
      setTimeout(() => {
        updateMsg.value = ''
        updateProgress.value = null
      }, 1500)
      return
    }

    // 有新版 → 拉新 SW 并接管刷新
    updateMsg.value = `发现新版本 ${latestVersion.value}，正在更新…`
    let reg: ServiceWorkerRegistration
    try {
      reg = await navigator.serviceWorker.ready
    } catch {
      updateMsg.value = 'Service Worker 未注册，请手动刷新页面。'
      updating.value = false
      return
    }
    try {
      updateProgress.value = 30
      const regAny = reg as ServiceWorkerRegistration & { updateWithValidation?: () => Promise<void> }
      await (regAny.updateWithValidation ? regAny.updateWithValidation().catch(() => reg.update()) : reg.update())

      const target: ServiceWorker | null = reg.waiting ?? reg.installing
      if (target && reg.active !== target) {
        updateProgress.value = 60
        updateMsg.value = '正在下载新版本…'
        await new Promise<void>(resolve => {
          if (target.state === 'installed') {
            resolve()
            return
          }
          const onState = () => {
            if (target.state === 'installed' || target.state === 'activated') {
              target.removeEventListener('statechange', onState)
              resolve()
            }
          }
          target.addEventListener('statechange', onState)
          setTimeout(resolve, 5000)
        })
        updateProgress.value = 70
        updateMsg.value = '正在应用新版本…'
        target.postMessage({ type: 'SKIP_WAITING' })
        await new Promise<void>(resolve => {
          target.addEventListener('statechange', () => {
            if (target.state === 'activated') resolve()
          }, { once: true })
          setTimeout(() => resolve(), 3000)
        })
        updateProgress.value = 100
        updateMsg.value = '已更新，正在刷新…'
        updating.value = false
        updateAvailable.value = false
        setTimeout(() => location.reload(), 400)
        return
      }

      updateProgress.value = 100
      updateMsg.value = '已是最新版本，无需更新。'
      updating.value = false
      setTimeout(() => {
        updateMsg.value = ''
        updateProgress.value = null
      }, 1500)
    } catch {
      updateMsg.value = '更新拉取失败，请手动刷新页面重试。'
      toast('更新失败，可手动刷新')
      updating.value = false
    }
  }

  function closeUpdate () {
    if (updating.value) return // 正在刷新中不允许关闭
    updateDialog.value = false
    updateMsg.value = ''
    updateProgress.value = null
  }

  /* ---------- 关于 ---------- */
  /** 打开「更新系统」弹窗即重新比对版本（拉最新 version.json），让 hasUpdate / 按钮文案
   *  反映服务端最新情况：刚发版时打开就能看到「立即更新」按钮，不会停在上次过期的值。
   *  只做比对、不自动下载，实际更新仍由「立即更新」按钮触发（避免打开设置页就自动刷新）。 */
  async function openUpdate () {
    updateDialog.value = true
    updateMsg.value = '正在检测最新版本…'
    const need = await checkVersion()
    updateAvailable.value = need
    if (updating.value) return
    updateMsg.value = need ? '发现新版本，点击「立即更新」' : '已是最新版本（' + latestVersion.value + '）'
  }

  const aboutDialog = ref(false)
  const uaShort = navigator.userAgent

  /* ---------- 退出登录 ---------- */
  const logoutDialog = ref(false)
  const logoutLoading = ref(false)

  async function doLogout () {
    logoutLoading.value = true
    await auth.logout()
    stopRealtime()
    logoutLoading.value = false
    router.push({ name: 'login' })
  }

  /* ---------- 被拉黑的用户：数量徽标（详情在子页面 /blocked，恢复/删除逻辑也在那） ---------- */
  const blockedCount = ref(0)

  async function loadBlockedCount () {
    try {
      const list = await contacts.loadBlocked()
      blockedCount.value = list.length
    } catch { /* 忽略：徽标保持 0 */ }
  }

  onMounted(() => {
    void refreshCacheSize()
    void loadBlockedCount()
    try {
      const saved = JSON.parse(localStorage.getItem('wx_notify') ?? 'null')
      if (saved) Object.assign(notify, saved)
    } catch { /* 忽略坏数据 */ }
    const p = auth.user?.privacy
    if (p) for (const k of Object.keys(privacy)) {
      if (typeof p[k] === 'boolean') privacy[k] = p[k]
    }
  })

  watch(() => auth.user?.privacy, p => {
    if (p) for (const k of Object.keys(privacy)) {
      if (typeof p[k] === 'boolean') privacy[k] = p[k]
    }
  })
 /* ---------- 问题反馈（提交后端 /api/feedback，附客户端快照；开发者模式解锁时附带最近日志） ---------- */
  const LOG_ATTACH = 80
  const feedbackDialog = ref(false)
  const feedbackText = ref('')
  const feedbackSending = ref(false)

  /** 连点「设置」标题解锁开发者模式：5 次解锁（sessionStorage 记住，刷新不丢）。 */
  const devUnlocked = ref(false)
  let devClicks = 0
  let devClickTimer: ReturnType<typeof setTimeout> | null = null

  onMounted(() => {
    devUnlocked.value = sessionStorage.getItem('wx_dev_mode') === '1'
    // 初始化日志徽标条数（后续实时刷新在打开面板时处理）
    refreshDevLogs()
  })

  function onTitleClick () {
    devClicks++
    if (devClickTimer) clearTimeout(devClickTimer)
    devClickTimer = setTimeout(() => { devClicks = 0 }, 1200)
    if (devClicks >= 5) {
      devClicks = 0
      devUnlocked.value = true
      sessionStorage.setItem('wx_dev_mode', '1')
      toast('开发者模式已解锁')
    }
  }

  function openFeedback () {
    feedbackDialog.value = true
  }

  function closeFeedback () {
    feedbackDialog.value = false
  }

  async function sendFeedback () {
    const body = feedbackText.value.trim()
    if (!body || feedbackSending.value) return
    feedbackSending.value = true
    try {
      const client = clientSnapshot(devUnlocked.value, LOG_ATTACH)
      await api.post('/feedback', { body, client })
      feedbackText.value = ''
      feedbackDialog.value = false
      toast('反馈已提交，感谢')
    } catch (error: any) {
      toast(error?.response?.data?.error ?? '提交失败，请稍后再试')
    } finally {
      feedbackSending.value = false
    }
  }

  /* ---------- 开发者模式 · 全部客户端日志（实时订阅 + 刷新/清空/复制/下载） ---------- */
  const devLogDialog = ref(false)
  const devLogLines = ref<LogEntry[]>([])
  /** 徽标条数：跟随日志面板当前缓冲条数 */
  const logCount = computed(() => devLogLines.value.length)
  const devLogLive = ref(true)
  let devLogUnsub: (() => void) | null = null

  const devLogText = computed(() => toText(devLogLines.value))

  function refreshDevLogs () {
    devLogLines.value = buffer_()
  }

  function clearDevLogs () {
    clearClientLog()
    devLogLines.value = []
  }

  async function copyDevLogs () {
    try {
      await navigator.clipboard.writeText(devLogText.value || '（暂无日志）')
      toast('日志已复制到剪贴板')
    } catch {
      toast('复制失败')
    }
  }

  function downloadDevLogs () {
    downloadClientLog(devLogLines.value)
  }

  function openDevLogs () {
    devLogDialog.value = true
    refreshDevLogs()
    if (devLogLive.value && !devLogUnsub) {
      devLogUnsub = subscribe(() => {
        if (!devLogLive.value || !devLogDialog.value) return
        devLogLines.value = buffer_()
      })
    }
  }

  watch(devLogDialog, open => {
    if (!open && devLogUnsub) {
      devLogUnsub()
      devLogUnsub = null
    }
  })

  onBeforeUnmount(() => {
    if (devLogUnsub) { devLogUnsub(); devLogUnsub = null }
    if (devClickTimer) clearTimeout(devClickTimer)
  })

</script>

<style scoped>
.wx-chat-bar {
  border-bottom: 1px solid rgb(var(--v-theme-wechat-divider));
}

/* 设置页：灰底 + 白色分组卡片 */
.wx-settings-bg {
  background: #ededed;
}

.v-theme--dark .wx-settings-bg {
  background: #121212;
}

.wx-settings-group {
  border-radius: 10px;
  overflow: hidden;
}
.dm-theme-swatches {
  display: flex;
  gap: 6px;
  align-items: center;
}

.dm-swatch {
  width: 20px;
  height: 20px;
  border: 2px solid transparent;
  border-radius: 50%;
  cursor: pointer;
  padding: 0;
  transition: transform 0.12s ease;
}

.dm-swatch:hover {
  transform: scale(1.12);
}

.dm-swatch-active {
  border-color: rgb(var(--v-theme-on-surface));
  transform: scale(1.12);
}

/* 问题反馈：多行输入 */
.feedback-ta :deep(.v-field__field) { min-height: 110px; }
.feedback-ta :deep(textarea) { line-height: 1.5; }

/* 开发者模式 · 客户端日志查看器（等宽、可滚动、不撑爆弹窗） */
.devlog-viewer {
  margin: 0;
  padding: 10px;
  height: 360px;
  overflow: auto;
  white-space: pre-wrap;
  word-break: break-word;
  font-family: Consolas, "SFMono-Regular", Menlo, monospace;
  font-size: 12px;
  line-height: 1.55;
  background: #0f172a;
  color: #d7e0ef;
  border-radius: 8px;
}
.v-theme--dark .devlog-viewer { background: #000; }
.devlog-live { margin: 0 !important; }
</style>
