/**
 * lib/device.ts
 *
 * 自动识别设备形态（电脑 / 平板 iPad / 手机），按「标识(UA+触点) + 屏幕宽度」双因子判定。
 * iPad 判定参考：iPadOS 13+ 的 Safari 伪装成 Macintosh UA，
 * 标准做法是 platform === 'MacIntel' && maxTouchPoints > 1（Mac 无触摸屏）；
 * 旧 iPad 直接匹配 UA 中的 iPad。
 */
import { computed, ref } from 'vue'

export type DeviceKind = 'phone' | 'pad' | 'pc'

const width = ref(typeof window === 'undefined' ? 1280 : window.innerWidth)

if (typeof window !== 'undefined') {
  let timer: ReturnType<typeof setTimeout> | undefined
  window.addEventListener('resize', () => {
    clearTimeout(timer)
    timer = setTimeout(() => {
      width.value = window.innerWidth
    }, 100)
  })
}

export function detectDevice (w: number): DeviceKind {
  const ua = navigator.userAgent
  const platform = navigator.platform
  const touch = navigator.maxTouchPoints ?? 0

  // 旧 iPad（UA 明示）
  if (/\biPad\b/.test(ua)) {
    return 'pad'
  }
  // iPadOS 13+ 桌面模式：伪装 Mac UA，但具备多点触控（真 Mac 触摸点数为 0）
  if (platform === 'MacIntel' && touch > 1) {
    return 'pad'
  }
  // 手机（含 iPhone、Android 手机、鸿蒙手机）
  if (/\b(?:iPhone|iPod)\b/.test(ua)) {
    return 'phone'
  }
  if (/Android/i.test(ua) && /Mobile/i.test(ua)) {
    return 'phone'
  }
  if (/Windows Phone|IEMobile|Opera Mini/i.test(ua)) {
    return 'phone'
  }
  if (/HarmonyOS/i.test(ua) && /Mobile/i.test(ua)) {
    return 'phone'
  }
  // Android 平板等无 Mobile 标记的触屏设备
  if (/Android/i.test(ua)) {
    return 'pad'
  }
  // 桌面系统（Windows / 桌面 macOS / Linux / ChromeOS）：窗口不窄时一律电脑排版。
  // 修：远程桌面等窄窗口曾被宽度兜底误判成手机（PC 上显示 iPhone 界面）
  if (w >= 480 && /\b(?:Windows NT|Mac OS X|X11; (?:Linux|CrOS))\b/.test(ua)) {
    return 'pc'
  }
  // 无明确标识：按宽度兜底
  if (w < 768) {
    return 'phone'
  }
  if (w < 1200) {
    return 'pad'
  }
  return 'pc'
}

/** 响应式设备形态（resize / UA 变化自动重算） */
export const deviceKind = computed<DeviceKind>(() => detectDevice(width.value))

export const deviceWidth = computed(() => width.value)

/**
 * 三端布局：手机 → page（单页 + 底部 tab）；iPad → two（顶栏 tab + 双列）；
 * 电脑 → three（左 rail，窗口 <1024 降为 two，<768 降为 page）。
 * shell 与各 pane 共用（返回按钮显隐以 page 模式为准，避免 600~767px 窗口没有返回键）。
 */
export const layoutMode = computed<'page' | 'two' | 'three'>(() => {
  const kind = deviceKind.value
  const w = width.value
  if (kind === 'phone') {
    return 'page'
  }
  if (kind === 'pad') {
    return w < 768 ? 'page' : 'two'
  }
  if (w >= 1024) {
    return 'three'
  }
  if (w >= 768) {
    return 'two'
  }
  return 'page'
})

/** 是否为手机式整页布局（单页 + 底部 tab / 详情整页） */
export const isPageMode = computed(() => layoutMode.value === 'page')

export function deviceLabel (kind: DeviceKind): string {
  return kind === 'phone' ? '手机' : (kind === 'pad' ? '平板（iPad）' : '电脑')
}
