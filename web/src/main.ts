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
import '@puzzle-style'
import '@/styles/wechat.scss'

// 客户端日志采集：最早就 hook console + 全局错误（开发者模式面板 / 问题反馈都依赖它）
installHooks()

try {
  const app = createApp(App)
  registerPlugins(app)

  // 主题：先写入 CSS 变量，避免首屏闪烁成默认绿
  try {
    const { currentPreset, getThemeId, resolveDark } = await import('@/lib/theme')
    const p = currentPreset()
    const root = document.documentElement
    root.style.setProperty('--dm-primary', p.primary)
    root.style.setProperty('--dm-secondary', p.secondary)
    root.style.setProperty('--dm-accent', p.accent)
    root.style.setProperty('--dm-tint', p.surfaceTint)
    root.dataset.dmTheme = getThemeId()
    root.dataset.dmMode = resolveDark() ? 'dark' : 'light'
  } catch { /* 主题失败不影响启动 */ }

  // 低端设备自适应（手机 / 平板 / 旧电脑）：在挂载前写入 <html> 的 data-wx-perf + .wx-low-perf，
  // 让 CSS 一帧就接管（关 backdrop-filter 磨砂、登录页动波纹/3D），避免首屏闪烁。见 lib/perf.ts。
  try {
    const { applyPerf } = await import('@/lib/perf')
    applyPerf()
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

  // 注册 Service Worker：仅同源 + secure context（https 或 localhost/127.0.0.1）才注册。
  // 缓存的只是 UI 静态资源（见 public/sw.js 的安全边界），绝不碰 /api、/files、/hubs。
  //
  // 关键：注册后立即 update() + 监听 updatefound。
  // 一旦服务端的新 sw.js 字节与本机激活版不同（每次发版都会改 CACHE 版本号），
  // 新 SW 安装完成后自动刷新一次页面，让旧 bundle / 旧 JS（含旧版全局确认框等 bug）
  // 从所有端（PC/手机）被替换掉 —— 修掉"电脑版一直加载旧 UI 导致遮罩锁死、
  // 点不动任何按钮"的根源。只刷新 UI 静态资源，API/会话数据不受影响（无 token 风险）。
  try {
    const secureCtx
      = location.protocol === 'https:'
        || location.hostname === 'localhost'
        || location.hostname === '127.0.0.1'
    if ('serviceWorker' in navigator && secureCtx) {
      const reg = await navigator.serviceWorker.register('/sw.js')
      // 记录激活的 SW 脚本 URL，问题反馈快照里据此判断客户端跑的是哪版 UI
      try {
        ;(window as unknown as Record<string, unknown>).__swVersion = (reg as unknown as { scriptURL?: string }).scriptURL ?? ''
      } catch { /* 忽略 */ }
      reg.update().catch(error => {
        console.warn('[sw] update() 检查失败', error)
      })
      logMark('Service Worker 已注册')
      navigator.serviceWorker.addEventListener('updatefound', () => {
        const nw = reg.installing
        if (!nw) return
        nw.addEventListener('statechange', () => {
          // 有激活中的旧 SW 且新 SW 已安装（sw.js 带 skipWaiting/claim）→ 刷新接管新版 UI
          if (nw.state === 'installed' && navigator.serviceWorker.controller) {
            location.reload()
          }
        })
      })
    }
  } catch { /* 隐私模式等场景下忽略 */ }
} catch (error) {
  // 不吞异常现场：控制台留痕；资源没下完 → 显示网络错误页（不再停在纯 HTML 门）
  console.error('[boot] Vue 挂载失败，使用纯 HTML 确认门 / 网络错误提示', error)
  try {
    ;(window as unknown as Record<string, ((...a: unknown[]) => void) | undefined>).__wxShowNeterr?.()
  } catch { /* 忽略 */ }
}
