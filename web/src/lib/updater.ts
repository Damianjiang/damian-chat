/**
 * lib/updater.ts —— 统一的前端版本检测 / 更新执行器。
 *
 * 目标（对应线上真实故障）：
 *   ① 打开 webapp 就检测服务端是否有新版本 → 弹「发现新版本：立即更新 / 稍后 / 忽略」，
 *      不再像旧版那样「悄悄自动 reload」或「弹了但点了没用」；
 *   ② 「立即更新」必须真的换成新代码：
 *      - 有新 SW 在 waiting → postMessage(SKIP_WAITING) → 等 controllerchange → 硬刷新；
 *      - 没有 waiting（浏览器没拉到新 SW / 旧 SW 卡死）→ 注销全部 SW + 清 wxui- 缓存 → 硬刷新；
 *      - 全程 8 秒兜底：超时同样走「注销 + 清缓存 + 刷新」，绝不停在“正在更新”不动；
 *   ③ 忽略只忽略**这一个版本**（记 localStorage），下次服务端再发新版仍会提示。
 *
 * 为什么旧流程会失败：
 *   • sw.js 在 install 里直接 skipWaiting() → 新 SW 抢在页面刷新前接管，
 *     页面还在跑旧 JS，却从新缓存拿资源 → 版本错配、白屏/点不动；
 *   • 只在 updatefound 里判断 `installed` 就 location.reload()，
 *     此时新 SW 往往还没 activate，刷新后仍是旧缓存 → “更新了还是旧版”；
 *   • 没有兜底：SW 状态卡在 waiting/installing 时按钮永远转圈，用户只能手动清缓存。
 */
import { computed, ref } from 'vue'

/** 构建期注入的版本指纹（vite define）。 */
export const RUNNING_VERSION: string = typeof __BUILD_VERSION__ !== 'undefined' ? __BUILD_VERSION__ : 'local'

export interface UpdateState {
  /** 有可安装的新版本（且未被忽略） */
  available: boolean
  /** 正在执行更新（按钮 loading / 进度条） */
  applying: boolean
  /** 0~100 */
  progress: number
  /** 服务端最新版本（version.json） */
  latest: string
  /** 当前运行版本 */
  running: string
  /** 用户忽略的版本（仅该版本不再弹窗） */
  ignoredVersion: string
  /** 最近一次检测时间（毫秒） */
  checkedAt: number
}

export const updateState = ref<UpdateState>({
  available: false,
  applying: false,
  progress: 0,
  latest: '',
  running: RUNNING_VERSION,
  ignoredVersion: '',
  checkedAt: 0,
})

/** 有更新且没被忽略 —— 弹窗显隐直接用它。 */
export const showUpdatePrompt = computed(() =>
  updateState.value.available
  && !updateState.value.applying
  && updateState.value.latest !== updateState.value.ignoredVersion)

const IGNORE_KEY = 'wx_update_ignored'
const CHECK_INTERVAL_MS = 10 * 60 * 1000

let started = false
let registration: ServiceWorkerRegistration | null = null
let applying = false
let reloading = false

function readIgnored (): string {
  try { return localStorage.getItem(IGNORE_KEY) ?? '' } catch { return '' }
}

function canUseSw (): boolean {
  return 'serviceWorker' in navigator && window.isSecureContext
}

function sleep (ms: number) {
  return new Promise<void>(resolve => setTimeout(resolve, ms))
}

/** 服务端构建指纹（version.json 由 vite 构建时写出，no-store 拉取）。 */
export async function fetchServerVersion (): Promise<string> {
  try {
    const res = await fetch(`/version.json?t=${Date.now()}`, { cache: 'no-store' })
    if (!res.ok) return ''
    const data = await res.json() as { version?: string }
    return data?.version ?? ''
  } catch {
    return ''
  }
}

/** 等待 registration 上出现 waiting worker（新 SW 下载并 install 完成）。 */
async function waitForWaiting (reg: ServiceWorkerRegistration | null, timeoutMs: number): Promise<ServiceWorker | null> {
  if (!reg) return null
  if (reg.waiting) return reg.waiting
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    await sleep(200)
    if (reg.waiting) return reg.waiting
    // 安装失败 / 被丢弃：不再空等
    if (!reg.installing && !reg.waiting) {
      // 再给一次机会（update() 可能还没派发 updatefound）
      await sleep(200)
      if (reg.waiting) return reg.waiting
    }
  }
  return reg.waiting ?? null
}

function markAvailable (latest: string) {
  updateState.value.latest = latest
  updateState.value.running = RUNNING_VERSION
  updateState.value.checkedAt = Date.now()
  updateState.value.ignoredVersion = readIgnored()
  // 首次安装（没有 controller）不弹“更新”：那只是第一次装 SW，不是新版本
  const hasController = !!navigator.serviceWorker?.controller
  updateState.value.available = hasController && !!latest && latest !== RUNNING_VERSION && latest !== updateState.value.ignoredVersion
}

/** 监听 SW 更新（不自动刷新，只点亮提示）。 */
function watchRegistration (reg: ServiceWorkerRegistration) {
  registration = reg
  const onUpdateFound = () => {
    const installing = reg.installing
    if (!installing) return
    installing.addEventListener('statechange', () => {
      if (installing.state !== 'installed') return
      if (!navigator.serviceWorker.controller) return // 首装
      // 新 SW 已就绪但**故意不 skipWaiting**：等用户点“立即更新”
      updateState.value.ignoredVersion = readIgnored()
      void fetchServerVersion().then(v => {
        updateState.value.latest = v || updateState.value.latest
        updateState.value.available = true
      })
    })
  }
  reg.addEventListener('updatefound', onUpdateFound)
  if (reg.waiting && navigator.serviceWorker.controller) {
    void fetchServerVersion().then(v => { if (v) markAvailable(v) })
  }
}

/**
 * 检测更新。
 * @param fetchSw 是否顺带让浏览器去拉一次 sw.js（设置页手动检查时 true）
 * @returns true = 有可用更新
 */
export async function checkForUpdate (fetchSw = false): Promise<boolean> {
  updateState.value.running = RUNNING_VERSION
  const latest = await fetchServerVersion()
  if (latest) {
    updateState.value.latest = latest
    updateState.value.checkedAt = Date.now()
    updateState.value.ignoredVersion = readIgnored()
  }

  if (canUseSw()) {
    try {
      if (!registration) registration = await navigator.serviceWorker.getRegistration('/') ?? null
      if (registration && fetchSw) await registration.update()
    } catch { /* 拉取失败：仍按 version.json 判断 */ }
  }

  const hasNew = !!latest && latest !== RUNNING_VERSION
  // 版本不同但浏览器还没拿到新 SW：仍然提示（用户点击后走兜底强制刷新）
  updateState.value.available = hasNew && latest !== readIgnored() && !!navigator.serviceWorker?.controller
  return updateState.value.available
}

/** 立即更新：确保换成新代码，失败也一定给出结果（绝不停在加载中）。 */
export async function applyUpdate (): Promise<void> {
  if (applying) return
  applying = true
  updateState.value.applying = true
  updateState.value.progress = 15

  // 总兜底：8 秒还没刷新 → 无脑硬重置（注销 SW + 清 UI 缓存 + 刷新）
  const watchdog = window.setTimeout(() => { void hardReset() }, 8000)

  try {
    let reg = registration
    if (!reg && canUseSw()) reg = await navigator.serviceWorker.getRegistration('/') ?? null

    if (reg) {
      try { await reg.update() } catch { /* 网络差：继续走 waiting/兜底 */ }
      updateState.value.progress = 35
      const waiting = await waitForWaiting(reg, 4000)
      if (waiting) {
        const controllerChanged = new Promise<void>(resolve => {
          const onChange = () => {
            navigator.serviceWorker.removeEventListener('controllerchange', onChange)
            resolve()
          }
          navigator.serviceWorker.addEventListener('controllerchange', onChange)
          setTimeout(() => {
            navigator.serviceWorker.removeEventListener('controllerchange', onChange)
            resolve()
          }, 4000)
        })
        waiting.postMessage({ type: 'SKIP_WAITING' })
        updateState.value.progress = 70
        await controllerChanged
        updateState.value.progress = 100
        window.clearTimeout(watchdog)
        hardReload()
        return
      }
    }

    // 没有 waiting：说明浏览器没拿到新 SW（缓存/注册损坏）→ 直接强制换血
    updateState.value.progress = 70
    window.clearTimeout(watchdog)
    await hardReset()
  } catch {
    window.clearTimeout(watchdog)
    await hardReset()
  }
}

/** 稍后再更新（仅本次隐藏，不写忽略）。 */
export function snoozeUpdate () {
  updateState.value.available = false
}

/** 忽略这个版本（下次服务端再发新版仍提示）。 */
export function ignoreUpdate () {
  const v = updateState.value.latest
  if (v) {
    try { localStorage.setItem(IGNORE_KEY, v) } catch { /* 隐私模式 */ }
  }
  updateState.value.ignoredVersion = v
  updateState.value.available = false
}

function hardReload () {
  if (reloading) return
  reloading = true
  // 用 replace 避免用户点返回又回到旧页面；加时间戳参数绕过任何中间层缓存
  try {
    const url = new URL(location.href)
    url.searchParams.set('_v', String(Date.now()))
    location.replace(url.toString())
  } catch {
    location.reload()
  }
}

/** 注销所有 SW + 清掉本应用的 UI 缓存 → 刷新（终极兜底）。 */
async function hardReset () {
  if (reloading) return
  try {
    if ('serviceWorker' in navigator) {
      const regs = await navigator.serviceWorker.getRegistrations()
      await Promise.all(regs.map(r => r.unregister()))
    }
  } catch { /* 忽略 */ }
  try {
    if ('caches' in window) {
      const keys = await caches.keys()
      await Promise.all(keys.filter(k => k.startsWith('wxui-')).map(k => caches.delete(k)))
    }
  } catch { /* 忽略 */ }
  updateState.value.progress = 100
  hardReload()
}

/** 构建指纹（ISO 时间戳 + 随机串）→ 人读的本地时间。 */
export function formatBuild (v: string): string {
  if (!v) return '--'
  const m = /^(\d{4}-\d{2}-\d{2})T(\d{2}:\d{2})/.exec(v)
  if (!m) return v.length > 18 ? v.slice(0, 18) + '…' : v
  const d = new Date(`${m[1]}T${m[2]}:00Z`)
  if (Number.isNaN(d.getTime())) return `${m[1]} ${m[2]}`
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`
}

/** 应用启动时调用一次：注册 SW（不自动跳过等待）+ 首次检测 + 周期检测。 */
export function initUpdater () {
  if (started) return
  started = true

  if (!canUseSw()) return

  navigator.serviceWorker.register('/sw.js', { updateViaCache: 'none' })
    .then(reg => {
      watchRegistration(reg)
      try {
        ;(window as unknown as Record<string, unknown>).__swVersion = reg.active?.scriptURL ?? ''
      } catch { /* 忽略 */ }
      // 首访：服务端已是新版也不用弹（没有 controller，本来就会加载最新资源）
      void checkForUpdate(false)
    })
    .catch(() => { /* 隐私模式 / 非安全上下文：不注册，更新按钮会给出提示 */ })

  // 回到前台 / 定时检测：用户长时间挂着页面也能收到新版本提示
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'visible') void checkForUpdate(true)
  })
  window.setInterval(() => { void checkForUpdate(true) }, CHECK_INTERVAL_MS)
}
