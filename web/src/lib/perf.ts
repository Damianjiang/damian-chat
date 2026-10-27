/**
 * lib/perf.ts
 *
 * 低端设备自适应：手机 / 平板 / 旧电脑（核心少、内存小）自动进入「低负载」渲染，
 * 关闭高开销的磨砂模糊（backdrop-filter）、全屏 canvas 动波纹、3D 倾斜，保证流畅。
 * 结果以 data 属性 + CSS 类挂在 <html> 上（见 wechat.scss 与 LoginView），CSS 与组件
 * 按需读取，无需刷新。纯运行时判定，不影响个性化设置（用户仍可在登录页手动开/关特效）。
 */
import { detectDevice } from '@/lib/device'

export type PerfLevel = 'full' | 'low'

let cached: PerfLevel | null = null

/** 核心数 + 内存（桌面旧电脑用）；触屏设备（手机/平板）在形态判定里已归为低负载。 */
function lowEndDesktop (): boolean {
  const cores = typeof navigator !== 'undefined' ? (navigator.hardwareConcurrency ?? 0) : 0
  const memGB = typeof navigator !== 'undefined' ? (navigator as unknown as { deviceMemory?: number }).deviceMemory : undefined
  if (cores > 0 && cores <= 2) return true      // 单核/双核（常见老机/低功耗核）
  if (memGB != null && memGB <= 4) return true  // deviceMemory ≤4GB（Chrome 系；其余浏览器 undefined 不误判）
  return false
}

/** 依据「设备形态 + 硬件规格」判定是否进入低负载渲染。 */
export function resolvePerfLevel (): PerfLevel {
  if (typeof window === 'undefined' || typeof navigator === 'undefined') return 'full'
  const kind = detectDevice(window.innerWidth)
  // 手机 / 平板：GPU 弱，backdrop-filter + 全屏 canvas 逐帧重绘是主要卡顿源 → 直接低负载
  if (kind === 'phone' || kind === 'pad') return 'low'
  // 桌面旧电脑（核心少 / 内存小）→ 低负载
  if (lowEndDesktop()) return 'low'
  return 'full'
}

/** 读取当前 perf 级别（进程内缓存一次；设备形态/硬件在会话内不变）。 */
export function getPerf (): PerfLevel {
  if (cached) return cached
  try {
    cached = resolvePerfLevel()
  } catch {
    cached = 'full'
  }
  return cached
}

export function isLowPerf (): boolean {
  return getPerf() === 'low'
}

/**
 * 应用 perf 级别到 documentElement：data-wx-perf + .wx-low-perf 供 CSS 读取。
 * 启动时调用，幂等（重复调用结果一致）。
 */
export function applyPerf (root: HTMLElement = document.documentElement): void {
  const level = getPerf()
  try {
    root.dataset.wxPerf = level
    root.classList.toggle('wx-low-perf', level === 'low')
  } catch { /* 非浏览器环境忽略 */ }
}
