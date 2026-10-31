<template>
  <v-container class="wx-login-page" :class="{ 'wx-perf': perfMode }" fluid>
    <!-- 背景层：微软 Fluent 风格全幅柔和渐变（默认）；后台配置为波纹时叠加 canvas 动波纹（个性化开启才渲染）。 -->
    <div class="wx-login-bg">
      <!-- 微软 Fluent 风格：全幅柔和蓝色渐变底（纯 CSS，绑定 bgStyle）；
           后台配置为波纹且个性化开启时，canvas 动波纹叠加在渐变之上。 -->
      <div class="wx-ms-bg" :style="bgStyle" aria-hidden="true"></div>
      <login-ripple-bg v-if="bgMode === 'ripple' && fx.ripple" :color="bgRipple" />
    </div>

    <!-- 右上角：语言 + 版本号 + 性能模式 + 个性化（主题配色/明暗/字号，本地持久化） -->
    <div class="wx-login-head">
      <v-menu :close-on-content-click="false" location="bottom end">
        <template #activator="{ props }">
          <v-btn v-bind="props" density="comfortable" size="small" variant="tonal">
            <v-icon icon="mdi-web" size="18" class="mr-1" />
            <span class="text-body-2">{{ langLabel }}</span>
          </v-btn>
        </template>
        <v-card width="160">
          <v-list density="compact" lines="one" nav>
            <v-list-item
              v-for="opt in langOptions"
              :key="opt.value"
              :active="langValue === opt.value"
              :active-color="'primary'"
              @click="setLang(opt.value)"
            >
              <v-list-item-title>{{ opt.label }}</v-list-item-title>
            </v-list-item>
          </v-list>
        </v-card>
      </v-menu>
      <v-chip v-if="appVersion" class="wx-login-version" density="comfortable" size="small" variant="tonal">v{{ appVersion }}</v-chip>
      <v-btn
        class="wx-perf-btn-head"
        :color="perfMode ? 'primary' : undefined"
        :variant="perfMode ? 'flat' : 'tonal'"
        density="comfortable"
        size="small"
        :icon="perfMode ? 'mdi-bolt' : 'mdi-gauge'"
        :title="perfMode ? t('login.perf.on.title') : t('login.perf.off.title')"
        @click="togglePerfMode"
      >{{ perfMode ? t('login.perf.on') : t('login.perf.off') }}</v-btn>
      <v-menu :close-on-content-click="false" location="bottom end">
        <template #activator="{ props }">
          <v-btn v-bind="props" density="comfortable" icon="mdi-tune" size="small" :title="t('login.personalize')" variant="tonal" />
        </template>
        <v-card width="264">
          <v-card-title class="text-body-medium pb-0">{{ t('login.personalize') }}</v-card-title>
          <v-card-text>
            <div class="text-body-small text-medium-emphasis mb-1">{{ t('login.personalize.theme') }}</div>
            <div class="wx-swatches">
              <button
                v-for="p in theme.presets"
                :key="p.id"
                :style="{ background: p.primary }"
                :title="p.name"
                class="wx-swatch"
                :class="{ 'wx-swatch-active': p.id === theme.themeId }"
                type="button"
                @click="theme.setTheme(p.id)"
              />
            </div>

            <div class="text-body-small text-medium-emphasis my-2">{{ t('login.personalize.dark') }}</div>
            <v-btn-toggle
              :model-value="theme.mode"
              class="wx-mode-toggle"
              density="compact"
              mandatory
              @update:model-value="(v: any) => theme.setMode(v)"
            >
              <v-btn value="light">{{ t('login.mode.light') }}</v-btn>
              <v-btn value="dark">{{ t('login.mode.dark') }}</v-btn>
              <v-btn value="system">{{ t('login.mode.system') }}</v-btn>
            </v-btn-toggle>

            <div class="text-body-small text-medium-emphasis my-2">{{ t('login.personalize.font') }}</div>
            <v-btn-group class="wx-font-group" density="compact">
              <v-btn
                v-for="f in fontOpts"
                :key="f.level"
                :value="f.level"
                size="x-small"
                :class="{ 'wx-font-active': f.level === fontLevel }"
                @click="applyFont(f.level)"
              >{{ f.label }}</v-btn>
            </v-btn-group>
          </v-card-text>
        </v-card>
      </v-menu>
    </div>

    <!--
      3D 卡片容器：卡片本体做 3D 倾斜（卡片"浮起来"）。
      ★ 关键修复：3D 不能放在父容器上（父容器挂 perspective + preserve-3d 会把卡片内
        所有子元素一起拖进 3D 场景，Chrome/Chromium 下子元素命中测试失效 → 输入框/按钮点不动）。
        正确做法：perspective() 写在卡片自身的 transform 里（见 cardStyle），卡片是 3D 物体、
        子元素保持平面（flat），命中测试正常。容器只做居中定位。
      个性化「3D 倾斜」开关 + reduced-motion 关闭时，卡片退化为纯平面（transform: none），
      玻璃质感与阴影保留。
    -->
    <div class="wx-3d" @pointerleave="resetTilt" @pointermove="onTilt">
      <v-sheet class="wx-login-card mx-auto" :style="cardStyle">
      <!-- 微软 Fluent 风格：白卡片 8px 圆角 + 分层柔和阴影，内部品牌区 + 表单区 -->
      <div class="wx-ms-card">
        <!-- 品牌行：logo + 产品名（左上，Segoe 风格） -->
        <div class="wx-ms-brand">
          <img class="wx-ms-brand-logo" src="/icon-192.png" alt="Damian Chat" />
          <div class="wx-ms-brand-text">
            <div class="wx-ms-brand-name">{{ t('login.brand') }}</div>
            <div class="wx-ms-brand-sub">{{ t('login.brand.sub') }}</div>
          </div>
        </div>

        <div class="wx-ms-panel">
          <h1 class="wx-ms-title">
            {{ tab === 'login' ? t('login.title') : t('login.title.register') }}
          </h1>

          <div class="wx-login-form">
          <v-window v-model="tab" class="wx-ms-window">
          <!-- 登录：账号 + 密码 + 图形验证码 -->
          <v-window-item value="login">
            <v-text-field
              v-model="account"
              autocomplete="username"
              :label="t('login.account')"
              :placeholder="t('login.account.ph')"
              prepend-inner-icon="mdi-account-outline"
            />

            <v-text-field
              v-model="password"
              autocomplete="current-password"
              :label="t('login.password')"
              :placeholder="t('login.password.ph')"
              prepend-inner-icon="mdi-lock-outline"
              type="password"
              @keyup.enter="submit"
            />

            <!-- 双重人机验证 ①：图形验证码 -->
            <div v-if="captchaRequired" class="d-flex align-center gap-2 mb-2">
              <v-text-field
                v-model="captchaInput"
                class="flex-grow-1"
                density="compact"
                hide-details
                :label="t('login.captcha')"
                :placeholder="t('login.captcha.ph')"
                @keyup.enter="submit"
              />

              <div class="wx-captcha" :title="t('login.captcha.refresh')" @click="refreshCaptcha">
                <img v-if="captchaImage" :alt="t('login.captcha')" :src="captchaImage">
                <v-icon v-else icon="mdi-reload" />
              </div>
            </div>

          </v-window-item>

          <!-- 注册：账号 + 密码 + 管理员注册密钥 -->
          <v-window-item value="register">
            <v-alert
              v-if="policyLoaded && !registerOpen"
              class="mb-3"
              :text="t('login.reg.closed')"
              type="info"
              variant="tonal"
            />

            <v-text-field
              v-model="regAccount"
              autocomplete="username"
              :label="t('login.account')"
              :placeholder="t('login.reg.account.ph')"
              prepend-inner-icon="mdi-account-plus-outline"
            />

            <v-text-field
              v-model="regPassword"
              :error-messages="regPassword ? (regPassword.length < 8 ? [t('login.reg.password.tooShort')] : []) : []"
              :error="regPassword.length > 0 && regPassword.length < 8"
              :hint="t('login.reg.password.hint')"
              :persistent-hint="!regPassword"
              autocomplete="new-password"
              :label="t('login.password')"
              :placeholder="t('login.reg.password.ph')"
              prepend-inner-icon="mdi-lock-outline"
              type="password"
            >
              <template #append-inner>
                <span class="wx-pw-strength" :class="'wx-pw-strength--' + passwordStrength.level" :title="passwordStrength.hint">
                  <span class="wx-pw-strength__bar">
                    <span
                      v-for="i in 4" :key="i"
                      class="wx-pw-strength__seg"
                      :class="{ active: i <= passwordStrength.score }"
                      :style="{ background: 'var(--wx-pw-color, transparent)' }"
                    />
                  </span>
                  <span class="wx-pw-strength__label">{{ passwordStrength.hint }}</span>
                </span>
              </template>
            </v-text-field>

            <v-text-field
              v-model="regPassword2"
              :error="pwMismatch"
              :error-messages="pwMismatch ? [t('login.reg.mismatch')] : []"
              :success="regPassword2.length > 0 && !pwMismatch"
              :success-messages="regPassword2.length > 0 && !pwMismatch ? [t('login.reg.same')] : []"
              autocomplete="new-password"
              :label="t('login.reg.confirm')"
              :placeholder="t('login.reg.confirm.ph')"
              prepend-inner-icon="mdi-lock-check-outline"
              type="password"
            />

            <v-text-field
              v-model="regKey"
              :label="t('login.reg.key')"
              :placeholder="t('login.reg.key.ph')"
              prepend-inner-icon="mdi-key-outline"
              @keyup.enter="submit"
            />

            <!-- 双重人机验证 ①：图形验证码（注册同样需要） -->
            <div v-if="captchaRequired" class="d-flex align-center gap-2 mt-2">
              <v-text-field
                v-model="captchaInput"
                class="flex-grow-1"
                density="compact"
                hide-details
                :label="t('login.captcha')"
                :placeholder="t('login.captcha.ph')"
              />

              <div class="wx-captcha" :title="t('login.captcha.refresh')" @click="refreshCaptcha">
                <img v-if="captchaImage" :alt="t('login.captcha')" :src="captchaImage">
                <v-icon v-else icon="mdi-reload" />
              </div>
            </div>
          </v-window-item>
        </v-window>

          <div class="wx-login-cta">
            <v-btn
              block
              class="wx-login-btn"
              color="primary"
              :disabled="tab === 'login' ? !validLogin : (!validRegister || (policyLoaded && !registerOpen))"
              :loading="loading"
              @click="submit"
            >
              {{ tab === 'login' ? t('login.btn') : t('login.btn.register') }}
            </v-btn>
          </div>

          <!-- 底部：登录 / 注册 切换链接（微软式） + 帮助文案 -->
          <div class="wx-ms-switch">
            <button
              v-if="tab === 'login'"
              type="button"
              class="wx-ms-switch-link"
              :disabled="!(policyLoaded && registerOpen)"
              @click="switchTab('register')"
>{{ t('login.no.account') }}</button>
            <button v-else type="button" class="wx-ms-switch-link" @click="switchTab('login')">{{ t('login.has.account') }}</button>
          </div>
          <p class="wx-ms-help">{{ t('login.help') }}</p>
        </div>
        </div>
        </div>
      </v-sheet>
    </div>

    <!-- 双重人机验证 ②：点击「登录 / 注册」时弹出的滑块拼图，服务端签发一次性 passToken -->
    <slider-captcha ref="sliderRef" @success="onSliderPass" />

    <v-snackbar v-model="snack" color="error" :timeout="2500">{{ snackMsg }}</v-snackbar>
    <v-snackbar v-model="snackOk" color="success" :timeout="1800">{{ snackOkMsg }}</v-snackbar>
  </v-container>
</template>

<script setup lang="ts">
  import { computed, nextTick, onMounted, reactive, ref } from 'vue'
  import { useRoute, useRouter } from 'vue-router'
  import SliderCaptcha from '@/components/SliderCaptcha.vue'
  import LoginRippleBg from '@/components/LoginRippleBg.vue'
  import { api } from '@/lib/api'
  import { t } from '@/lib/i18n'
  import { initRealtime } from '@/lib/realtime'
  import { resolveDark } from '@/lib/theme'
  import { getFx } from '@/lib/personalize'
  import { isLowPerf } from '@/lib/perf'
  import { useAuthStore } from '@/stores/auth'
  import { useContactsStore } from '@/stores/contacts'
  import { useConversationsStore } from '@/stores/conversations'
  import { useLanguageStore } from '@/stores/language'
  import { useThemeStore } from '@/stores/theme'

  const route = useRoute()
  const router = useRouter()
  const auth = useAuthStore()
  const theme = useThemeStore()
  const conversations = useConversationsStore()
  const contacts = useContactsStore()
  const langStore = useLanguageStore()

  /* ---------- 语言切换器（右上角：自动 / 中文 / English） ---------- */
  const langOptions = computed(() => [
    { value: 'auto', label: t('login.lang.auto') },
    { value: 'zh', label: t('login.lang.zh') },
    { value: 'en', label: t('login.lang.en') },
  ])
  const langValue = computed(() => langStore.mode)
  const langLabel = computed(() =>
    langValue.value === 'auto' ? t('login.lang.auto')
      : langValue.value === 'zh' ? t('login.lang.zh')
        : t('login.lang.en'))
  function setLang (val: string) {
    langStore.setLang(val as 'auto' | 'zh' | 'en')
  }

  /* ---------- 个性化特效：波纹 / 3D 倾斜 / 动效（lib/personalize，设置→个性化可开关） ---------- */
  const fx = reactive({ ...getFx() })
  // 记住打开本页时用户原本的波纹 / 3D 设置，关闭性能模式时还原（避免误改个性化设置）
  const fxSaved = { ...fx }

  /**
   * 性能模式：仅供卡顿用户在登录界面一键「关闭动波纹背景 + 关闭 3D 倾斜 + 关闭卡片磨砂模糊」。
   *  - 纯登录页临时状态，不写 localStorage、不影响个性化设置；关闭时还原 fxSaved。
   *  - 卡片内部所有控件（输入框/验证码/登录/注册按钮/滑块）完全不受影响，仍可正常操作。
   */
  // 低端设备（手机/平板/旧电脑，见 lib/perf.ts）默认进入性能模式：
  // 关掉 canvas 动波纹 + 3D 倾斜，叠加 CSS 关掉磨砂；用户仍可点「恢复特效」手动开回来。
  const perfMode = ref(isLowPerf())
  if (perfMode.value) {
    fx.ripple = false
    fx.tilt = false
  }

  function togglePerfMode () {
    perfMode.value = !perfMode.value
    if (perfMode.value) {
      fx.ripple = false   // 停掉 canvas 动波纹 → 静态纯色
      fx.tilt = false     // 卡片退化为纯平面
    } else {
      fx.ripple = fxSaved.ripple   // 还原用户原设置
      fx.tilt = fxSaved.tilt
      tilt.value = { rx: 0, ry: 0 }
    }
  }

  /* ---------- 右上角：版本号 + 个性化（主题/明暗/字号，本地持久化） ---------- */
  /** 产品版本号（顶栏展示，与「关于」一致）；构建指纹由 __BUILD_VERSION__ 单独注入，用于「检查更新」比对。 */
  const appVersion = '1.3.0'

  /** #RRGGBB → [r,g,b]。 */
  function hexToRgb3 (h: string): [number, number, number] {
    const m = /^#?([0-9a-f]{6})$/i.exec((h ?? '').trim())
    if (!m || !m[1]) return [47, 111, 237]
    const v = parseInt(m[1], 16)
    return [(v >> 16) & 255, (v >> 8) & 255, v & 255]
  }
  /** 颜色加深(负)/提亮(正)：k∈[-1,1]，向黑/白混合。 */
  function shade (hex: string, k: number): string {
    const [r, g, b] = hexToRgb3(hex)
    const t = k < 0 ? 0 : 255
    const p = Math.abs(k)
    const f = (c: number) => Math.round(c + (t - c) * p)
    return `rgb(${f(r)}, ${f(g)}, ${f(b)})`
  }
  /** 页面底色：波纹模式的浅色垫底（亮色偏白微蓝，暗色偏深灰蓝）。 */
  function bgToPage (hex: string): string {
    const [r, g, b] = hexToRgb3(hex)
    return bgDark.value
      ? `rgb(${Math.round(14 + r * 0.04)}, ${Math.round(16 + g * 0.04)}, ${Math.round(30 + b * 0.05)})`
      : `rgb(${Math.round(236 + (255 - r) * 0.25)}, ${Math.round(243 + (255 - g) * 0.25)}, 250)`
  }

  const bgMode = ref<'ripple' | 'color' | 'image'>('ripple')
  const bgRipple = ref('#2f6fed')
  const bgColor = ref('#2f6fed')
  const bgImage = ref('')
  const bgDark = computed(() => resolveDark(theme.mode))
  const bgStyle = computed(() => {
    if (bgMode.value === 'image' && bgImage.value) {
      return {
        background: `url('${bgImage.value}') center/cover no-repeat`,
      }
    }
    if (bgMode.value === 'color') {
      return { background: bgDark.value ? shade(bgColor.value, -0.55) : bgToPage(bgColor.value) }
    }
    // 波纹模式（默认）：背景交给 .wx-ms-bg 的微软式柔和蓝色渐变（纯 CSS，零 canvas 也好看）；
    // 个性化「登录页波纹」开启时 canvas 动波纹叠加其上。这里返回空，让 CSS 渐变透出。
    return {}
  })

  async function loadLoginTheme () {
    try {
      const res = await api.get('/auth/login-theme')
      bgMode.value = res.data.mode || 'ripple'
      bgRipple.value = res.data.rippleColor || bgRipple.value
      bgColor.value = res.data.color || bgColor.value
      bgImage.value = res.data.image || ''
    } catch { /* 旧后端/未部署 → 保持默认蓝色波纹 */ }
  }

  /* ---------- 3D 卡片：指针倾斜（★ 3D 只写在卡片自身 transform，不在父容器开 3D 场景） ---------- */
  const tilt = ref({ rx: 0, ry: 0 })
  const tiltEnabled = computed(() => fx.tilt
    && !(typeof window !== 'undefined' && !!window.matchMedia?.('(prefers-reduced-motion: reduce)').matches))
  const cardStyle = computed(() => {
    // 关键：perspective() 内联在卡片自己的 transform 上，卡片是 3D 物体、子元素 flat，
    // 控件命中测试正常。关闭 3D 或 reduced-motion → transform: none（纯平面，玻璃/阴影保留）。
    if (!tiltEnabled.value || (tilt.value.rx === 0 && tilt.value.ry === 0)) {
      return { transform: 'none', transition: 'transform .4s ease' }
    }
    return {
      transform: `perspective(1200px) rotateX(${tilt.value.rx}deg) rotateY(${tilt.value.ry}deg)`,
      transition: 'transform .12s ease-out',
    }
  })
  function onTilt (e: PointerEvent) {
    if (!tiltEnabled.value) return
    const el = (e.currentTarget as HTMLElement).getBoundingClientRect()
    const px = (e.clientX - el.left) / el.width - 0.5
    const py = (e.clientY - el.top) / el.height - 0.5
    const MAX = 7
    tilt.value = { rx: -py * MAX, ry: px * MAX }
  }
  function resetTilt () {
    tilt.value = { rx: 0, ry: 0 }
  }

  /* ---------- 字号（个性化，存 localStorage 与设置页/个性化子页共用 wx_font_level） ---------- */
  const fontOpts = computed(() => [
    { label: t('font.s'), level: 0 },
    { label: t('font.m'), level: 1 },
    { label: t('font.l'), level: 2 },
    { label: t('font.xl'), level: 3 },
  ])
  const fontLevel = ref(Number(localStorage.getItem('wx_font_level')) || 1)
  function applyFont (level: number) {
    fontLevel.value = level
    const scales = [0.9, 1, 1.15, 1.3]
    document.documentElement.style.fontSize = `${(16 * (scales[level] ?? 1)).toFixed(1)}px`
    localStorage.setItem('wx_font_level', String(level))
  }

  /* ---------- 登录 ---------- */
  const tab = ref<'login' | 'register'>('login')
  const loading = ref(false)
  const snack = ref(false)
  const snackMsg = ref('')
  const snackOk = ref(false)
  const snackOkMsg = ref('')
  const account = ref('')
  const password = ref('')
  const sliderRef = ref<{ open: () => void } | null>(null)

  /* ---------- 双重人机验证 ①：图形验证码 ---------- */
  const captchaId = ref('')
  const captchaImage = ref('')
  const captchaInput = ref('')

  async function refreshCaptcha () {
    try {
      const res = await api.get('/auth/captcha')
      captchaId.value = res.data.captchaId
      captchaImage.value = res.data.image
      captchaInput.value = ''
    } catch {
      snackMsg.value = t('login.captcha.fail')
      snack.value = true
    }
  }
  /** 滑块通过后拿到的一次性 passToken，等下随登录/注册请求提交 */
  const passToken = ref('')
  /** 记住用户点的哪个 tab，滑块通过后继续原操作 */
  const pendingTab = ref<'login' | 'register'>('login')

  /* 登录策略（服务端：验证码开关 / 注册开关） */
  const policyLoaded = ref(false)
  const captchaRequired = ref(true)
  const registerOpen = ref(true)

  async function loadPolicy () {
    try {
      const res = await api.get('/auth/policy')
      captchaRequired.value = res.data.captcha !== false
      registerOpen.value = !!res.data.registerOpen
      policyLoaded.value = true
      if (captchaRequired.value) await refreshCaptcha()
    } catch { /* 拿不到就按默认（需要验证码） */ }
  }

  const validLogin = computed(() =>
    account.value.trim().length >= 3
    && password.value.length > 0
    && (!captchaRequired.value || captchaInput.value.trim().length > 0),
  )

  /** 按钮点击 → 弹出滑块；通过后才提交 */
  async function switchTab (next: 'login' | 'register') {
    tab.value = next
    captchaInput.value = ''
    passToken.value = ''
    if (captchaRequired.value) await refreshCaptcha()
  }

  function submit () {
    if (tab.value === 'login') {
      if (!validLogin.value) return
    } else if (!validRegister.value) {
      return
    }
    pendingTab.value = tab.value
    passToken.value = ''
    sliderRef.value?.open()
  }

  /** 滑块通过：拿到一次性 passToken，立即提交原本的操作 */
  function onSliderPass (token: string) {
    passToken.value = token
    if (pendingTab.value === 'login') doLogin()
    else doRegister()
  }

  async function doLogin () {
    loading.value = true
    try {
      await auth.login(account.value.trim(), password.value, captchaId.value, captchaInput.value.trim(), passToken.value)
      await enterApp()
      snackOkMsg.value = t('login.ok')
      snackOk.value = true
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? t('login.fail')
      snack.value = true
      if (captchaRequired.value) await refreshCaptcha()
      passToken.value = ''
    } finally {
      loading.value = false
    }
  }

  /* ---------- 注册 ---------- */
  const regAccount = ref('')
  const regPassword = ref('')
  const regPassword2 = ref('')
  const regKey = ref('')

  /** 两次密码是否不一致（用于注册页实时红字提示，不再只在提交时才弹） */
  const pwMismatch = computed(() => regPassword2.value.length > 0 && regPassword.value !== regPassword2.value)

  /** 密码强度：4 段条（长度≥8 / 大小写混合 / 数字 / 符号），供注册页实时展示 */
  const passwordStrength = computed(() => {
    const p = regPassword.value
    if (!p) return { score: 0, level: 0, hint: t('login.pw.empty') }
    let s = 0
    if (p.length >= 8) s++
    if (p.length >= 12) s++
    if (/[a-z]/.test(p) && /[A-Z]/.test(p)) s++
    if (/\d/.test(p)) s++
    if (/[^A-Za-z0-9]/.test(p)) s++
    s = Math.min(4, s)
    const level = p.length < 8 ? 0 : s <= 1 ? 1 : s <= 2 ? 2 : s === 3 ? 3 : 4
    const hint = [t('login.pw.weak'), t('login.pw.fair'), t('login.pw.good'), t('login.pw.strong'), t('login.pv.vstrong')][level]
    return { score: s, level, hint }
  })

  const validRegister = computed(() =>
    regAccount.value.trim().length >= 3
    && regPassword.value.length >= 8
    && regPassword.value === regPassword2.value
    && regKey.value.trim().length > 0
    && (!captchaRequired.value || captchaInput.value.trim().length > 0),
  )

  async function doRegister () {
    if (regPassword.value !== regPassword2.value) {
      snackMsg.value = t('login.reg.mismatch')
      snack.value = true
      return
    }
    loading.value = true
    try {
      await auth.register(regAccount.value.trim(), regPassword.value, regKey.value.trim(), captchaId.value, captchaInput.value.trim(), passToken.value)
      await enterApp()
      snackOkMsg.value = t('login.reg.ok')
      snackOk.value = true
    } catch (error: any) {
      snackMsg.value = error?.response?.data?.error ?? t('login.reg.fail')
      snack.value = true
      if (captchaRequired.value) await refreshCaptcha()
      passToken.value = ''
    } finally {
      loading.value = false
    }
  }

  /* ---------- 进入应用 ---------- */
  /**
   * 登录/注册成功后进入主界面。
   *
   * ★ 关键：某些浏览器（尤其旧版 / 个别 WebView）里，SPA 软导航 router.push
   * 不一定会把视图从登录页切走——用户看到"登录成功"提示却仍停在登录页，
   * 必须手动 F5（走整页 App.vue bootstrap）才进得去。
   * 这里在软导航后检测：若下一帧仍停在登录页，就自动整页跳转到目标路由，
   * 把"手动刷新"这一步自动化掉。整页跳转后 App.vue bootstrap 会恢复会话，
   * 安全底线不变（refresh / 直达仍走 verifySession 网络校验）。
   */
  async function enterApp () {
    await Promise.allSettled([conversations.loadSessions(), contacts.load(), contacts.loadRequests()])
    void conversations.pullRecentHistoryIntoCache().catch(() => {})
    initRealtime()
    const redirect = route.query.redirect
    const target = typeof redirect === 'string' && redirect.startsWith('/') && !redirect.startsWith('//')
      ? redirect
      : '/chat'
    try { await router.push(target) } catch { /* 导航被拒则走下方整页兜底 */ }
    // 下一帧后检查：若 URL 已离开 /login（SPA 软导航成功）就不必再整页跳
    await nextTick()
    if (window.location.pathname === '/login') {
      // 仍停在登录页 → 某些浏览器软导航没切走视图，自动整页跳转进入应用
      window.location.replace(target)
    }
  }

  onMounted(async () => {
    loadLoginTheme()
    loadPolicy()
  })
</script>

<style scoped>
/* ============================================================
   微软 Fluent 风格登录页（高仿 login.live.com 新版）
   白卡片 8px 圆角 + 分层柔和阴影；全幅柔和蓝色渐变背景；
   Segoe UI 字体；主按钮=主题主色；字段 1px 灰边框聚焦变主色；
   卡片淡入上移动画；深色模式 #242424 卡片。
   ============================================================ */
.wx-login-page {
  min-height: 100vh;
  min-height: 100dvh;
  display: flex;
  align-items: safe center;
  justify-content: center;
  padding: 32px 16px;
  box-sizing: border-box;
  position: relative;
  overflow-y: auto;
  /* 微软风格字体栈：Segoe UI 优先（Windows 原生），回退系统无衬线 */
  font-family: 'Segoe UI Variable', 'Segoe UI', system-ui, -apple-system,
    'PingFang SC', 'Microsoft YaHei', sans-serif;
}

/* 背景层容器（canvas 波纹 / 渐变 铺满垫底） */
.wx-login-bg {
  position: fixed;
  inset: 0;
  z-index: 0;
  overflow: hidden;
}

/* 微软式全幅柔和蓝色渐变（纯 CSS，绑定 bgStyle；亮/暗两套） */
.wx-ms-bg {
  position: absolute;
  inset: 0;
  background:
    radial-gradient(120% 90% at 22% 0%, rgba(0, 120, 212, 0.10) 0%, rgba(0, 120, 212, 0) 46%),
    linear-gradient(180deg, #f6f9fe 0%, #eaf1fc 48%, #d9e5fb 100%);
}
html[data-dm-mode='dark'] .wx-ms-bg,
.v-theme--dark .wx-ms-bg {
  background:
    radial-gradient(120% 90% at 80% 0%, rgba(0, 120, 212, 0.16) 0%, rgba(0, 0, 0, 0) 52%),
    linear-gradient(180deg, #0d1424 0%, #0a0a0a 100%);
}

/* 卡片外层（v-sheet）：透明定位壳，3D 倾斜 transform 挂在这 */
.wx-3d {
  position: relative;
  z-index: 1;
  width: 100%;
  display: flex;
  justify-content: center;
}

/* 右上角：版本号 + 性能 + 个性化（窄窗口自动换行，不向左溢出裁切） */
.wx-login-head {
  position: fixed;
  top: 14px;
  right: 16px;
  z-index: 5;
  display: flex;
  align-items: center;
  gap: 8px;
  max-width: calc(100vw - 32px);
  flex-wrap: wrap;
  justify-content: flex-end;
}

/* ---- 卡片壳（v-sheet）：透明，仅承担 3D transform ---- */
.wx-login-card {
  width: 100%;
  max-width: 440px;
  background: transparent;
  border-radius: 8px;
}

/* ---- 微软 Fluent 白卡片 ---- */
.wx-ms-card {
  background: #ffffff;
  border: 1px solid rgba(0, 0, 0, 0.04);
  border-radius: 8px;
  padding: 30px 30px 26px;
  box-shadow:
    0 14px 40px rgba(0, 28, 60, 0.14),
    0 4px 10px rgba(0, 28, 60, 0.06);
  /* 入场：淡入 + 轻微上移（Fluent Motion） */
  animation: wx-ms-card-in var(--wx-dur-lg, 320ms) var(--wx-ease-out, cubic-bezier(0.33, 0, 0.67, 1)) both;
}
@keyframes wx-ms-card-in {
  from { opacity: 0; transform: translateY(12px); }
  to   { opacity: 1; transform: translateY(0); }
}
html[data-dm-mode='dark'] .wx-ms-card,
.v-theme--dark .wx-ms-card {
  background: #242424;
  border: 1px solid rgba(255, 255, 255, 0.08);
  box-shadow:
    0 16px 44px rgba(0, 0, 0, 0.55),
    0 4px 10px rgba(0, 0, 0, 0.4);
}

/* 品牌行：logo + 产品名 */
.wx-ms-brand {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 24px;
}
.wx-ms-brand-logo {
  width: 40px;
  height: 40px;
  border-radius: 8px;
  object-fit: cover;
  box-shadow: 0 2px 8px rgba(0, 28, 60, 0.18);
}
.wx-ms-brand-text { display: flex; flex-direction: column; line-height: 1.25; }
.wx-ms-brand-name { font-size: 16px; font-weight: 600; letter-spacing: .2px; }
.wx-ms-brand-sub { font-size: 12px; color: rgba(0, 0, 0, 0.55); }
html[data-dm-mode='dark'] .wx-ms-brand-sub,
.v-theme--dark .wx-ms-brand-sub { color: rgba(255, 255, 255, 0.55); }

/* 表单面板 + 动态标题 */
.wx-ms-panel { display: flex; flex-direction: column; }
.wx-ms-title {
  font-size: 22px;
  font-weight: 600;
  letter-spacing: -.01em;
  margin: 0 0 20px;
}

.wx-login-form { width: 100%; }

/* 登录/注册窗口切换：淡入 */
.wx-ms-window { transition: opacity var(--wx-dur-sm, 160ms) ease; }
.wx-ms-window .v-window-item { padding: 0; }

/* ---- 微软 Fluent 字段：白底 + 1px 灰边框 + 4px 圆角，聚焦 1px 主色边框 + 柔和光晕 ---- */
.wx-login-card :deep(.v-field) {
  background-color: #ffffff;
  border: 1px solid rgba(0, 0, 0, 0.18);
  border-radius: 4px;
  transition: border-color var(--wx-dur-sm, 160ms) ease, box-shadow var(--wx-dur-sm, 160ms) ease;
}
.wx-login-card :deep(.v-field .v-field__border) { opacity: 0; }
.wx-login-card :deep(.v-field .v-field__field) { padding: 8px 10px; }
.wx-login-card :deep(.v-field .v-label) { font-size: 12px; color: rgba(0, 0, 0, 0.6); }
.wx-login-card :deep(.v-field--focused) {
  border-color: rgb(var(--v-theme-primary));
  box-shadow: 0 0 0 2px color-mix(in srgb, rgb(var(--v-theme-primary)) 20%, transparent);
}
html[data-dm-mode='dark'] .wx-login-card :deep(.v-field),
.v-theme--dark .wx-login-card :deep(.v-field) {
  background-color: #2b2b2b;
  border-color: rgba(255, 255, 255, 0.16);
}
html[data-dm-mode='dark'] .wx-login-card :deep(.v-field .v-label),
.v-theme--dark .wx-login-card :deep(.v-field .v-label) { color: rgba(255, 255, 255, 0.6); }

/* ---- 微软 Fluent 主按钮：全宽、主题主色、4px 圆角、hover 提亮 ---- */
.wx-login-cta { margin-top: 20px; width: 100%; }
.wx-login-cta :deep(.v-btn) {
  height: 40px;
  border-radius: 4px;
  font-weight: 600;
  font-size: 14px;
  letter-spacing: .2px;
  text-transform: none;
}
.wx-login-cta :deep(.v-btn:not(:disabled):hover) { filter: brightness(1.05); }

/* 底部登录/注册切换链接（微软式文字链接，主色，hover 下划线） */
.wx-ms-switch { margin-top: 16px; text-align: center; }
.wx-ms-switch-link {
  background: none;
  border: 0;
  padding: 0;
  cursor: pointer;
  font-family: inherit;
  font-size: 13.5px;
  color: rgb(var(--v-theme-primary));
  text-decoration: none;
}
.wx-ms-switch-link:hover:not(:disabled) { text-decoration: underline; }
.wx-ms-switch-link:disabled { color: rgba(128, 128, 128, 0.6); cursor: not-allowed; }

.wx-ms-help {
  margin: 14px 0 0;
  font-size: 12.5px;
  color: rgba(0, 0, 0, 0.5);
  text-align: center;
}
html[data-dm-mode='dark'] .wx-ms-help,
.v-theme--dark .wx-ms-help { color: rgba(255, 255, 255, 0.45); }

/* 右上角版本徽标 */
.wx-login-version {
  border: 1px solid rgba(0, 0, 0, 0.08);
  background: rgba(255, 255, 255, 0.7);
}
html[data-dm-mode='dark'] .wx-login-version {
  border-color: rgba(255, 255, 255, 0.12);
  background: rgba(24, 28, 38, 0.7);
}

/* 右上角性能按钮 */
.wx-perf-btn-head { max-width: 150px; }

/* ★ 右上角「语言 / 关闭特效 / 个性化」中性按钮：登录页是浅蓝全幅背景，
   Vuetify 4 的 tonal 底色 = currentColor 的低透明度铺底，接近全透明 → 按钮看不见。
   注意 .text-surface 等 vuetify 颜色工具类带 !important，会盖掉这里的 color，
   所以按钮上不能写 color="surface"，统一由这里上色。
   类名必须是 .v-btn--variant-tonal（v-btn--tonal 是旧类名，v4 里不存在）；
   激活态的 perf 按钮是 .v-btn--flat（绿色）不受影响。 */
.wx-login-head :deep(.v-btn--variant-tonal) {
  background: rgba(255, 255, 255, 0.72);
  border: 1px solid rgba(0, 0, 0, 0.09);
  color: #181818;
}
.wx-login-head :deep(.v-btn--variant-tonal:hover) {
  background: rgba(255, 255, 255, 0.92);
}
html[data-dm-mode='dark'] .wx-login-head :deep(.v-btn--variant-tonal) {
  background: rgba(24, 28, 38, 0.72);
  border-color: rgba(255, 255, 255, 0.14);
  color: #f5f5f5;
}
html[data-dm-mode='dark'] .wx-login-head :deep(.v-btn--variant-tonal:hover) {
  background: rgba(24, 28, 38, 0.92);
}

/* 图形验证码框 */
.wx-captcha {
  flex: 0 0 auto;
  width: 110px;
  height: 40px;
  border: 1px solid rgba(0, 0, 0, 0.14);
  border-radius: 4px;
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
  cursor: pointer;
  background: #ffffff;
}
html[data-dm-mode='dark'] .wx-captcha {
  background: rgba(255, 255, 255, 0.06);
  border-color: rgba(255, 255, 255, 0.16);
}
.wx-captcha img { width: 100%; height: 100%; object-fit: contain; }

/* 个性化下拉里的色板 / 字号 / 明暗 */
.wx-swatches { display: flex; gap: 6px; flex-wrap: wrap; margin-bottom: 4px; }
.wx-swatch {
  width: 22px; height: 22px;
  border: 2px solid transparent;
  border-radius: 50%;
  cursor: pointer; padding: 0;
  outline-offset: 2px;
  transition: transform .12s ease;
}
.wx-swatch:hover { transform: scale(1.12); }
.wx-swatch-active { border-color: rgb(var(--v-theme-on-surface)); transform: scale(1.12); }
.wx-mode-toggle { width: 100%; justify-content: space-between; }
.wx-mode-toggle :deep(.v-btn) { flex: 1; margin: 0 2px; height: 30px; }
.wx-font-group { width: 100%; display: flex; justify-content: space-between; }
.wx-font-group :deep(.v-btn) { flex: 1; margin: 0 2px; height: 30px; border-radius: 4px; }
.wx-font-active { background: rgb(var(--v-theme-primary)) !important; color: rgb(var(--v-theme-on-primary)) !important; }

/* 性能模式：关入场动画 + 降阴影（弱端流畅；卡片控件不受影响） */
.wx-perf .wx-ms-card {
  animation: none !important;
  box-shadow: 0 6px 20px rgba(0, 28, 60, 0.16) !important;
  transition: none !important;
}

/* reduced-motion：停入场动画 */
@media (prefers-reduced-motion: reduce) {
  .wx-ms-card { animation: none; }
}

/* 注册页密码强度指示器：4 段条 + 文案 */
.wx-pw-strength {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  margin-top: 4px;
  --wx-pw-color: #b0b0b0;
}
.wx-pw-strength--1 { --wx-pw-color: #e53935; }
.wx-pw-strength--2 { --wx-pw-color: #fb8c00; }
.wx-pw-strength--3 { --wx-pw-color: #43a047; }
.wx-pw-strength--4 { --wx-pw-color: #00897b; }
.wx-pw-strength__bar { display: inline-flex; gap: 3px; }
.wx-pw-strength__seg {
  width: 14px; height: 4px; border-radius: 2px;
  background: rgba(128, 128, 128, 0.25);
  transition: background var(--wx-dur-md) var(--wx-ease-out);
}
.wx-pw-strength__seg.active { background: var(--wx-pw-color); }
.wx-pw-strength__label { font-size: 12px; color: var(--wx-pw-color); min-width: 2em; white-space: nowrap; }
.wx-pw-strength--0 .wx-pw-strength__label { color: #9e9e9e; }
</style>
