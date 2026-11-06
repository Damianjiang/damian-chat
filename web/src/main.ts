/**
 * main.ts
 *
 * Bootstraps Vuetify and other plugins then mounts the App.
 * 挂载失败（浏览器过旧跑不动 Vue）时保留 index.html 里的纯 HTML 确认门。
 */

import { createApp } from 'vue'

import { registerPlugins } from '@/plugins'

import App from './App.vue'

import { installHooks, mark as logMark } from '@/lib/clientlog'
// 首屏即设置 <html lang>（language store 模块副作用，按 localStorage/navigator 自动识别），避免语言闪烁
import '@/stores/language'
import 'unfonts.css'
import '@/styles/wechat.scss'

// 客户端日志采集：最早就 hook console + 全局错误（开发者模式面板 / 问题反馈都依赖它）
installHooks()

try {
  const app = createApp(App)
  registerPlugins(app)

  // 主题 + 低端设备自适应：两个小模块并行取（串行动态 import 在高 RTT 手机上多一跳瀑布）。
  // 都在挂载前写入 <html> 属性，避免首屏闪烁（主题变量 / .wx-low-perf 关磨砂与动效）。
  const [themeMod, perfMod] = await Promise.all([
    import('@/lib/theme').catch(() => null),
    import('@/lib/perf').catch(() => null),
  ])
  try {
    const { currentPreset, getThemeId, resolveDark } = themeMod!
    const p = currentPreset()
    const root = document.documentElement
    root.style.setProperty('--dm-primary', p.primary)
    root.style.setProperty('--dm-secondary', p.secondary)
    root.style.setProperty('--dm-accent', p.accent)
    root.style.setProperty('--dm-tint', p.surfaceTint)
    root.dataset.dmTheme = getThemeId()
    root.dataset.dmMode = resolveDark() ? 'dark' : 'light'
  } catch { /* 主题失败不影响启动 */ }

  try {
    perfMod?.applyPerf()
  } catch { /* 忽略 */ }

  app.mount('#app')
  logMark('Vue 已挂载')

  // 个性化（特效开关 / 组件半透明 / 用户壁纸）：读 localStorage + IndexedDB 壁纸并即时应用，
  // 不刷新也能生效；失败静默降级为默认外观。
  try {
    const { initPersonalize } = await import('@/lib/personalize')
    initPersonalize()
  } catch { /* 忽略 */ }

  // Vue 挂载成功：隐藏资源加载动画；若当前离线（但靠 UI 缓存才进来），提示网络不可用
  const w = window as unknown as Record<string, ((...a: unknown[]) => void) | undefined>
  try {
    w.__wxHideLoader?.()
    if (!navigator.onLine) {
      w.__wxHideNeterr?.()
      // 有 UI 缓存才可能离线进来；此时仍提示"网络不可用"，接口不可达
      w.__wxShowNeterr?.()
    }
  } catch { /* 无这些全局函数时忽略 */ }

  // 版本检测 / Service Worker 更新统一交给 lib/updater：
  // 打开即检测新版本 → 弹「立即更新 / 稍后 / 忽略」，点击更新才切换 SW 并刷新。
  // 旧逻辑在 updatefound 里直接 location.reload()（新 SW 还没 activate）→ 刷新后仍是旧版，
  // 而且 install 里 skipWaiting 抢班会造成新旧资源混用，这就是"点了更新没反应"的根因。
  try {
    const { initUpdater } = await import('@/lib/updater')
    initUpdater()
    logMark('更新检测已启动')
  } catch { /* 隐私模式 / 非安全上下文：不注册 SW，设置页仍可手动检查 */ }
} catch (error) {
  // 不吞异常现场：控制台留痕；资源没下完 → 显示网络错误页（不再停在纯 HTML 门）
  console.error('[boot] Vue 挂载失败，使用纯 HTML 确认门 / 网络错误提示', error)
  try {
    ;(window as unknown as Record<string, ((...a: unknown[]) => void) | undefined>).__wxShowNeterr?.()
  } catch { /* 忽略 */ }
}
