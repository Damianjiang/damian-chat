<template>
  <v-sheet class="wx-panes" flat>
    <v-app-bar class="wx-chat-bar" color="surface" density="compact" flat>
      <v-btn v-if="isMobile" icon="mdi-arrow-left" variant="text" @click="goBack" />
      <!-- 连点「设置」5 次 = 解锁开发者模式（见 onTitleClick） -->
      <v-toolbar-title class="text-body-large font-weight-medium" @click="onTitleClick">{{ t('settings.title') }}</v-toolbar-title>
    </v-app-bar>

    <div class="wx-settings-bg flex-grow-1 overflow-y-auto wx-scroll pa-2">
      <!-- 账号 -->
      <v-list bg-color="surface" class="wx-settings-group">
        <v-list-item @click="router.push({ name: 'profile' })">
          <v-list-item-title>{{ t('settings.profile') }}</v-list-item-title>
          <template #append><v-icon icon="mdi-chevron-right" /></template>
        </v-list-item>

        <v-list-item @click="pwdDialog = true">
          <v-list-item-title>{{ t('settings.account') }}</v-list-item-title>
          <template #append><v-icon icon="mdi-chevron-right" /></template>
        </v-list-item>

        <v-list-item @click="privacyDialog = true">
          <v-list-item-title>{{ t('settings.privacy') }}</v-list-item-title>
          <template #append><v-icon icon="mdi-chevron-right" /></template>
        </v-list-item>
      </v-list>

      <!-- 通用 -->
      <v-list bg-color="surface" class="wx-settings-group mt-2">
        <v-list-item @click="router.push({ name: 'blocked' })">
          <v-list-item-title>{{ t('settings.blocked') }}</v-list-item-title>

          <template #append>
            <span v-if="blockedCount > 0" class="text-body-medium text-medium-emphasis mr-1">{{ blockedCount }}</span>
            <v-icon icon="mdi-chevron-right" size="small" />
          </template>
        </v-list-item>

        <!-- 界面语言：自动 / 简体中文 / English（写 localStorage wx_lang，全站响应式切换） -->
        <v-list-item>
          <v-list-item-title>{{ t('settings.lang') }}</v-list-item-title>
          <template #append>
            <v-menu :close-on-content-click="false" location="end">
              <template #activator="{ props }">
                <v-btn v-bind="props" class="wx-lang-btn" density="comfortable" size="small" variant="tonal">
                  <span class="text-body-2">{{ langValue === 'auto' ? t('settings.lang.auto') : langValue === 'zh' ? t('settings.lang.zh') : t('settings.lang.en') }}</span>
                </v-btn>
              </template>
              <v-card width="180">
                <v-list density="compact" lines="one" nav>
                  <v-list-item
                    v-for="opt in langOptions"
                    :key="opt.value"
                    :active="langValue === opt.value"
                    :active-color="'primary'"
                    @click="langStore.setLang(opt.value as 'auto' | 'zh' | 'en')"
                  >
                    <v-list-item-title>{{ opt.label }}</v-list-item-title>
                  </v-list-item>
                </v-list>
              </v-card>
            </v-menu>
          </template>
        </v-list-item>

        <v-list-item @click="notifyDialog = true">
          <v-list-item-title>{{ t('settings.notify') }}</v-list-item-title>
          <template #append><v-icon icon="mdi-chevron-right" /></template>
        </v-list-item>

        <!-- 个性化设置：主题配色 / 深色模式 / 字体大小 / 组件半透明度 / 特效 / 背景图 全部移入子页 -->
        <v-list-item @click="router.push({ name: 'personalize' })">
          <v-list-item-title>{{ t('settings.personalize') }}</v-list-item-title>
          <template #append><v-icon icon="mdi-palette-swatch" /><v-icon icon="mdi-chevron-right" /></template>
        </v-list-item>

        <!-- 问题反馈：提交后 C# 后端可见并可复制（带客户端快照，开发者模式解锁时附带最近日志） -->
        <v-list-item @click="openFeedback">
          <v-list-item-title>{{ t('settings.feedback') }}</v-list-item-title>
          <template #append><v-icon icon="mdi-message-text-outline" /><v-icon icon="mdi-chevron-right" /></template>
        </v-list-item>

        <v-list-item @click="cacheDialog = true">
          <v-list-item-title>{{ t('settings.cache') }}</v-list-item-title>

          <template #append>
            <span class="text-body-medium text-medium-emphasis mr-1">{{ cacheSize }}</span>
            <v-icon icon="mdi-chevron-right" size="small" />
          </template>
        </v-list-item>

        <!-- 更新 App：拉取最新 UI 资源（Service Worker），进度条 + 完成自动刷新，失败给手动刷新 -->
        <v-list-item @click="openUpdate">
          <v-list-item-title>{{ t('settings.update') }}</v-list-item-title>

          <template #append>
            <v-icon v-if="updateAvailable" color="secondary" icon="mdi-update" size="20" />
            <v-icon v-else icon="mdi-chevron-right" size="small" />
          </template>
        </v-list-item>
      </v-list>

      <!-- 关于 -->
      <v-list bg-color="surface" class="wx-settings-group mt-2">
        <v-list-item @click="aboutDialog = true">
          <v-list-item-title>{{ t('settings.about') }}</v-list-item-title>

          <template #append>
            <span class="text-body-medium text-medium-emphasis mr-1">{{ t('settings.version') }} 1.3.0</span>
            <v-icon icon="mdi-chevron-right" size="small" />
          </template>
        </v-list-item>
      </v-list>

      <!-- 开发者模式（连点 设置 标题 5 次解锁后才显示） -->
      <template v-if="devUnlocked">
        <v-list bg-color="surface" class="wx-settings-group mt-2">
          <v-list-item @click="openDevLogs">
            <v-list-item-title>{{ t('settings.devlogs') }}</v-list-item-title>
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
          <div class="text-body-large text-center" style="width: 100%; color: #fa5151">{{ t('settings.logout') }}</div>
        </v-list-item>
      </v-list>
    </div>

    <!-- 修改密码 -->
    <v-dialog v-model="pwdDialog" max-width="360">
      <v-card>
        <v-card-title>{{ t('settings.pwd.title') }}</v-card-title>

        <v-card-text>
          <v-text-field v-model="oldPwd" autocomplete="current-password" :label="t('settings.pwd.old')" type="password" />
          <v-text-field v-model="newPwd" autocomplete="new-password" :label="t('settings.pwd.new')" type="password" />
          <v-text-field v-model="newPwd2" autocomplete="new-password" :label="t('settings.pwd.confirm')" type="password" />
        </v-card-text>

        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="pwdDialog = false">{{ t('common.cancel') }}</v-btn>
          <v-btn color="primary" :disabled="!pwdValid" :loading="pwdLoading" @click="changePassword">{{ t('settings.pwd.submit') }}</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 通知 -->
    <v-dialog v-model="notifyDialog" max-width="360">
      <v-card>
        <v-card-title>{{ t('settings.notify') }}</v-card-title>

        <v-card-text>
          <v-switch
            v-model="notify.sound"
            color="primary"
            density="compact"
            hide-details
            :label="t('settings.notify.sound')"
          />

          <v-switch
            v-model="notify.desktop"
            color="primary"
            density="compact"
            hide-details
            :label="t('settings.notify.desktop')"
            @update:model-value="onDesktopToggle"
          />

          <v-switch
            v-model="notify.vibrate"
            color="primary"
            density="compact"
            hide-details
            :label="t('settings.notify.vibrate')"
          />
        </v-card-text>

        <v-card-actions>
          <v-spacer />
          <v-btn color="primary" variant="text" @click="saveNotify">{{ t('common.confirm') }}</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 隐私 -->
    <v-dialog v-model="privacyDialog" max-width="360">
      <v-card>
        <v-card-title>{{ t('settings.privacy') }}</v-card-title>

        <v-card-text>
          <v-switch
            v-model="privacy.searchable"
            color="primary"
            density="compact"
            hide-details
            :label="t('settings.privacy.searchable')"
          />

          <v-switch
            v-model="privacy.strangerMoments"
            color="primary"
            density="compact"
            hide-details
            :label="t('settings.privacy.strangerMoments')"
          />

          <v-switch
            v-model="privacy.signatureVisible"
            color="primary"
            density="compact"
            hide-details
            :label="t('settings.privacy.signatureVisible')"
          />
        </v-card-text>

        <v-card-actions>
          <v-spacer />
          <v-btn color="primary" :loading="privacyLoading" variant="text" @click="savePrivacy">{{ t('common.save') }}</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 清除缓存（勾选制：聊天记录 / 图片视频语音 / 其它本地数据） -->
    <v-dialog v-model="cacheDialog" max-width="380">
      <v-card>
        <v-card-title>{{ t('settings.cache') }}</v-card-title>

        <v-card-text>
          <v-checkbox
            v-model="cacheOpts.chat"
            color="primary"
            density="compact"
            hide-details
            :label="t('settings.cache.chat')"
          />

          <v-checkbox
            v-model="cacheOpts.media"
            color="primary"
            density="compact"
            hide-details
            :label="t('settings.cache.media')"
          />

          <v-checkbox
            v-model="cacheOpts.local"
            color="primary"
            density="compact"
            hide-details
            :label="t('settings.cache.local')"
          />

          <div class="text-body-small text-medium-emphasis mt-2">{{ t('settings.cache.current', { n: cacheSize }) }}</div>
        </v-card-text>

        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="cacheDialog = false">{{ t('common.cancel') }}</v-btn>
          <v-btn color="primary" :disabled="!anyCacheChecked" :loading="cacheClearing" @click="doClearCache">{{ t('settings.cache.clear') }}</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 更新 App（真·检查更新：版本比对 + Service Worker 拉新资源） -->
    <v-dialog v-model="updateDialog" max-width="380">
      <v-card>
        <v-card-title class="text-body-large">{{ t('settings.update') }}</v-card-title>

        <v-card-text>
          <div class="text-body-small text-medium-emphasis mb-2">
            {{ t('settings.update.hint') }}
          </div>

          <v-sheet class="rounded pa-2 mb-2" color="wechat-chat-bg" variant="tonal">
            <div class="text-body-small d-flex justify-space-between">
              <span class="text-medium-emphasis">{{ t('settings.update.running') }}</span>
              <span class="text-truncate" style="max-width: 60%">{{ runningVersion }}</span>
            </div>
            <div class="text-body-small d-flex justify-space-between">
              <span class="text-medium-emphasis">{{ t('settings.update.server') }}</span>
              <span class="text-truncate" style="max-width: 60%">{{ latestVersion }}</span>
            </div>
            <div v-if="hasUpdate" class="text-body-small text-primary mt-1 d-flex align-center gap-1">
              <v-icon icon="mdi-update" size="16" />
              <span>{{ t('settings.update.found', { n: latestVersion }) }}</span>
            </div>
          </v-sheet>

          <v-progress-linear
            v-if="updating"
            class="my-4"
            color="primary"
            :indeterminate="updateProgress === null"
            :model-value="updateProgress ?? 0"
          />

          <div class="text-body-small" :class="updateMsg ? 'text-medium-emphasis' : 'text-primary'">{{ updateMsg || (hasUpdate ? t('settings.update.found.short') : t('settings.update.btn.check')) }}</div>
        </v-card-text>

        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="closeUpdate">{{ t('common.close') }}</v-btn>
          <v-btn v-if="!updating" color="primary" @click="runUpdate">{{ hasUpdate ? t('settings.update.btn.do') : t('settings.check.update') }}</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 关于 -->
    <v-dialog v-model="aboutDialog" max-width="340">
      <v-card>
        <v-card-title>{{ t('settings.about') }}</v-card-title>

        <v-card-text>
          <div class="text-body-medium mb-1">{{ t('settings.version') }}：1.3.0</div>
          <div class="text-body-medium mb-1">{{ t('settings.about.tech') }}</div>
          <div class="text-body-medium mb-1">{{ t('settings.about.device', { n: deviceLabel(deviceKind), m: deviceKind }) }}</div>
          <div class="text-body-small text-medium-emphasis" style="word-break: break-all">{{ uaShort }}</div>
        </v-card-text>

        <v-card-actions>
          <v-spacer />
          <v-btn color="primary" variant="text" @click="aboutDialog = false">{{ t('common.close') }}</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 退出确认 -->
    <v-dialog v-model="logoutDialog" max-width="300">
      <v-card>
        <v-card-title class="text-body-large">{{ t('settings.logout.confirm') }}</v-card-title>

        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="logoutDialog = false">{{ t('common.cancel') }}</v-btn>
          <v-btn color="red" :loading="logoutLoading" @click="doLogout">{{ t('settings.logout') }}</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 问题反馈（提交后 C# 后端可见；开发者模式解锁时自动附带最近日志快照） -->
    <v-dialog v-model="feedbackDialog" max-width="440">
      <v-card>
        <v-card-title>{{ t('settings.feedback') }}</v-card-title>
        <v-card-text>
          <div class="text-body-small text-medium-emphasis mb-2">
            {{ t('settings.feedback.hint') }}
          </div>
          <v-textarea
            v-model="feedbackText"
            :counter="2000"
            :rows="5"
            autocomplete="off"
            class="feedback-ta"
            density="comfortable"
            :label="t('settings.feedback.label')"
            variant="outlined"
          />
          <div class="text-caption text-medium-emphasis">
            <span v-if="devUnlocked">{{ t('settings.feedback.attach.dev', { n: LOG_ATTACH }) }}</span>
            <span v-else>{{ t('settings.feedback.attach.normal') }}</span>
          </div>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="closeFeedback">{{ t('common.cancel') }}</v-btn>
          <v-btn color="primary" :disabled="!feedbackText.trim() || feedbackSending" :loading="feedbackSending" @click="sendFeedback">{{ t('settings.feedback.submit') }}</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <!-- 开发者模式 · 全部客户端日志（连点标题「设置」5 次解锁） -->
    <v-dialog v-model="devLogDialog" max-width="720" persistent>
      <v-card class="devlog-card">
        <v-card-title class="d-flex align-center">
          <v-icon icon="mdi-console" />
          <span>{{ t('settings.devlogs') }}</span>
          <v-spacer />
          <span class="text-caption text-medium-emphasis">{{ t('settings.devlogs.count', { n: devLogLines.length }) }}</span>
        </v-card-title>
        <v-card-text>
          <div class="d-flex ga-2 mb-3 flex-wrap">
            <v-btn size="small" variant="tonal" @click="refreshDevLogs">{{ t('settings.devlogs.refresh') }}</v-btn>
            <v-btn size="small" variant="tonal" @click="clearDevLogs">{{ t('settings.devlogs.clear') }}</v-btn>
            <v-btn size="small" variant="tonal" @click="copyDevLogs">{{ t('settings.devlogs.copy') }}</v-btn>
            <v-btn size="small" variant="tonal" @click="downloadDevLogs">{{ t('settings.devlogs.download') }}</v-btn>
            <v-spacer />
            <v-checkbox v-model="devLogLive" class="devlog-live" density="compact" hide-details :label="t('settings.devlogs.live')" />
          </div>
          <pre class="devlog-viewer">{{ devLogText || t('settings.devlogs.empty') }}</pre>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn color="primary" variant="text" @click="devLogDialog = false">{{ t('common.close') }}</v-btn>
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
  import { t } from '@/lib/i18n'
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
  import { useLanguageStore } from '@/stores/language'
  import { clearConversationCache, conversationCacheSize } from '@/stores/conversations'

  const router = useRouter()
  const auth = useAuthStore()
  const contacts = useContactsStore()
  const langStore = useLanguageStore()

  const isMobile = isPageMode

  /* ---------- 界面语言切换（设置→通用：自动 / 中文 / English，与登录页同一份 wx_lang） ---------- */
  const langOptions = computed(() => [
    { value: 'auto', label: t('settings.lang.auto') },
    { value: 'zh', label: t('settings.lang.zh') },
    { value: 'en', label: t('settings.lang.en') },
  ])
  const langValue = computed(() => langStore.mode)
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
      toast(t('settings.pwd.ok'))
      pwdDialog.value = false
      await auth.logout()
      stopRealtime()
      router.push({ name: 'login' })
    } catch (error: any) {
      toast(error?.response?.data?.error ?? t('settings.pwd.fail'))
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
      toast(t('settings.notify.deny'))
    }
  }

  function saveNotify () {
    localStorage.setItem('wx_notify', JSON.stringify(notify))
    notifyDialog.value = false
    toast(t('settings.notify.saved'))
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
      toast(t('settings.privacy.saved'))
    } catch {
      toast(t('common.save.fail'))
    } finally {
      privacyLoading.value = false
    }
  }

  /* ---------- 清除缓存（勾选制） ---------- */
  const cacheSize = ref(t('common.loading'))
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
    if (chat > 0) parts.push(t('settings.cache.part.chat', { n: chat }))
    if (media > 0) parts.push(t('settings.cache.part.media', { n: media }))
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
      toast(cleared > 0 ? t('settings.cache.cleared', { n: cleared }) : t('settings.cache.empty'))
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
    latestVersion.value = serverVer || t('settings.update.noManifest')
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
    updateMsg.value = t('settings.update.checking')
    // 阶段 0：真·版本比对。没有新版本就到此为止，不白折腾 SW。
    const needUpdate = await checkVersion()
    if (!needUpdate) {
      updateProgress.value = 100
      updateMsg.value = t('settings.update.latest', { n: latestVersion.value })
      updating.value = false
      setTimeout(() => {
        updateMsg.value = ''
        updateProgress.value = null
      }, 1500)
      return
    }

    // 有新版 → 拉新 SW 并接管刷新
    updateMsg.value = t('settings.update.found', { n: latestVersion.value })
    let reg: ServiceWorkerRegistration
    try {
      reg = await navigator.serviceWorker.ready
    } catch {
      updateMsg.value = t('settings.update.sw.notReg')
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
        updateMsg.value = t('settings.update.downloading')
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
        updateMsg.value = t('settings.update.applying')
        target.postMessage({ type: 'SKIP_WAITING' })
        await new Promise<void>(resolve => {
          target.addEventListener('statechange', () => {
            if (target.state === 'activated') resolve()
          }, { once: true })
          setTimeout(() => resolve(), 3000)
        })
        updateProgress.value = 100
        updateMsg.value = t('settings.update.reloading')
        updating.value = false
        updateAvailable.value = false
        setTimeout(() => location.reload(), 400)
        return
      }

      updateProgress.value = 100
      updateMsg.value = t('settings.update.upToDate')
      updating.value = false
      setTimeout(() => {
        updateMsg.value = ''
        updateProgress.value = null
      }, 1500)
    } catch {
      updateMsg.value = t('settings.update.fetchFail')
      toast(t('settings.update.fail'))
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
    updateMsg.value = t('settings.update.checking')
    const need = await checkVersion()
    updateAvailable.value = need
    if (updating.value) return
    updateMsg.value = need ? t('settings.update.found') : t('settings.update.latest', { n: latestVersion.value })
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
    // 2.5s 窗口：桌面鼠标连点没问题，手机/iPad 手指点按节奏慢（还有双击缩放干扰），
    // 旧的 1.2s 基本点不出来
    devClickTimer = setTimeout(() => { devClicks = 0 }, 2500)
    if (devClicks >= 5) {
      devClicks = 0
      devUnlocked.value = true
      sessionStorage.setItem('wx_dev_mode', '1')
      toast(t('settings.dev.unlocked'))
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
      toast(t('settings.feedback.ok'))
    } catch (error: any) {
      toast(error?.response?.data?.error ?? t('settings.feedback.fail'))
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
      await navigator.clipboard.writeText(devLogText.value || t('settings.devlogs.empty'))
      toast(t('common.copied'))
    } catch {
      toast(t('common.copy.fail'))
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

/* ★ 界面语言按钮：tonal 默认底色 = currentColor 低透明度铺底，在 surface 列表上几乎不可见
   （.text-surface 等 vuetify 颜色工具类带 !important，故按钮上不写 color，由这里上色）。
   类名是 .v-btn--variant-tonal（v-btn--tonal 是旧类名，v4 里不存在）。 */
.wx-lang-btn {
  background: rgba(0, 0, 0, 0.05);
  border: 1px solid rgba(0, 0, 0, 0.12);
  color: #1a1a1a;
}
.wx-lang-btn:hover { background: rgba(0, 0, 0, 0.09); }
html[data-dm-mode='dark'] .wx-lang-btn {
  background: rgba(255, 255, 255, 0.12);
  border-color: rgba(255, 255, 255, 0.18);
  color: #f5f5f5;
}
html[data-dm-mode='dark'] .wx-lang-btn:hover { background: rgba(255, 255, 255, 0.18); }
</style>
