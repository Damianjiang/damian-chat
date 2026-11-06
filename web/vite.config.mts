import type { Plugin } from 'vite'
import { existsSync, readdirSync, readFileSync, writeFileSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath, URL } from 'node:url'
import Vue from '@vitejs/plugin-vue'
import Fonts from 'unplugin-fonts/vite'
// Plugins
import Components from 'unplugin-vue-components/vite'
// Utilities
import { defineConfig } from 'vite'
import Vuetify, { transformAssetUrls } from 'vite-plugin-vuetify'

// 产物 index.html 的字体 preload 清理（用 closeBundle 直接改产物：unplugin-fonts 在自己的
// transformIndexHtml(post) 里注入这些 link，跑在普通插件之后，只有 closeBundle（所有构建插件
// 完成后）能稳定覆盖）。清两类噪音：
//   ① font/eot、font/ttf —— 浏览器不认，控制台报 "unsupported type value"。
//   ② font/woff —— 它只是 woff2 的兜底格式，woff2 加载成功就永远用不到，
//      控制台报 "preloaded using link preload but not used within a few seconds"。
//      （正则用结尾引号锚定，只匹配 font/woff，不会误伤 font/woff2。）
// 保留 font/woff2：现代浏览器真正用的格式，preload 它能让首屏图标/西文不闪。
const outDirAbs = fileURLToPath(new URL('../server/WeChatWeb/wwwroot', import.meta.url))

// 每次构建生成一个唯一版本号（注入进打包代码，运行中的 App 用它与服务器 version.json 比对，
// 判断「服务端是否有更新版本」——「检查更新」的真正数据源）。
const BUILD_VERSION = `${new Date().toISOString()}-${Date.now().toString(36)}`

function stripUnsupportedFontPreload () {
  const indexPath = path.join(outDirAbs, 'index.html')
  if (!existsSync(indexPath)) {
    return
  }
  const html = readFileSync(indexPath, 'utf8')
  const next = html.replace(/<link\b[^>]*>/g, m => {
    if (!/rel=["']preload["']/.test(m)) {
      return m
    }
    // 结尾引号锚定：只命中 font/woff，不会误伤 font/woff2
    if (/type=["']font\/(eot|ttf|woff)["']/.test(m)) {
      return ''
    }
    // MDI 图标字体 403KB，而首屏是加载层、一个图标字形都用不到它：
    // 预加载既抢首屏带宽，又必然触发 "preloaded using link preload but not used" 警告。
    // 交给 CSS 在图标真正渲染时按需加载。
    if (/href=["'][^"']*materialdesignicons-webfont[^"']*["']/.test(m)) {
      return ''
    }
    return m
  })
  if (next !== html) {
    writeFileSync(indexPath, next, 'utf8')
  }
}

/**
 * 构建后生成带「全量资源清单」的 sw.js：首访（SW 安装）即把整站 UI 资源
 * 全部装进浏览器（预缓存 ~几 MB，用户要求的首访整包下载），之后二次打开/弱网/离线
 * 全部走本地，不再逐个请求；「检查更新」因此真正有意义——新部署 = sw.js 字节必变
 * （CACHE 版本随 BUILD_VERSION）→ 浏览器重装 SW → 预缓存新清单 → activate 清空旧缓存。
 * 资源清单来自构建产物（dist/assets 内容哈希文件名），绝不含 /api、/files、/hubs。
 */
function generateSw () {
  const files: string[] = []
  const scan = (rel: string) => {
    const abs = path.join(outDirAbs, rel)
    let entries
    try {
      entries = readdirSync(abs, { withFileTypes: true })
    } catch {
      return
    }
    for (const e of entries) {
      const r = rel ? `${rel}/${e.name}` : e.name
      if (e.isDirectory()) {
        if (e.name === '.mimosa') continue   // 工具产物，不入缓存
        scan(r)
      } else if (/\.(js|css|woff2?|png|svg|ico|webp)$/.test(e.name)) {
        files.push(`/${r}`)
      }
    }
  }
  scan('assets')
  const roots = ['/manifest.json', '/favicon.ico', '/icon-192.png', '/icon-48.png', '/icon-512.png', '/apple-touch-icon.png', '/boot.js', '/robots.txt']
    .filter(f => existsSync(path.join(outDirAbs, f)))
  const precache = ['/', ...roots, ...new Set(files)]

  // fetch/activate 逻辑与手写版一致：只拦同源 GET；/api、/files、/hubs、/swagger 永不缓存
  const sw = `/*\n * Service Worker —— 由构建自动生成（模板见 web/vite.config.mts generateSw），请勿手改。\n * 预缓存清单：本构建的全部 UI 资源（${precache.length} 项，首访整包安装）。\n *\n * 安全边界（勿放宽）：\n *   ① 只拦同源 GET；跨源放行。\n *   ② 绝不缓存 /api/*、/files/*、/hubs/*、/swagger —— 实时数据/凭证/媒体必须走网络。\n *   ③ CACHE 带 BUILD_VERSION，新部署字节必变 → 重装 → activate 清空旧缓存。\n */\n'use strict'\n\nconst CACHE = 'wxui-${BUILD_VERSION}'\nconst PRECACHE = ${JSON.stringify(precache, null, 2)}\n\nconst EXCLUDE = ['/api/', '/files/', '/hubs/', '/swagger']\n\nfunction excluded (p) {\n  return EXCLUDE.some(pre => p.startsWith(pre))\n}\n\nfunction isUiAsset (p) {\n  return p.startsWith('/assets/') || p.startsWith('/favicon') || p.includes('.woff') || p.includes('.ttf') || p.includes('.eot')\n}\n\nfunction offlinePage () {\n  return new Response(\n    '<!doctype html><meta charset="utf-8"><title>大面聊天</title>'\n    + '<body style="margin:0;font-family:system-ui;background:#ededed;color:#181818">'\n    + '<div style="min-height:100vh;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:12px;padding:24px;text-align:center">'\n    + '<h1 style="font-size:18px;margin:0">网络连接不可用</h1>'\n    + '<p style="font-size:14px;color:#888;margin:0">没有可用的本地缓存，且当前处于离线状态。请检查网络后刷新。</p>'\n    + '<button style="margin-top:10px;padding:9px 22px;font-size:14px;border-radius:8px;border:none;background:#07c160;color:#fff;cursor:pointer" onclick="location.reload()">重新加载</button>'\n    + '</div></body>',\n    { status: 503, headers: { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' } },\n  )\n}\n\nself.addEventListener('install', event => {\n  // 全量预缓存：逐项 add，单项失败不拖垮整个安装（根资源失败才算失败）\n  event.waitUntil(\n    caches.open(CACHE).then(async cache => {\n      for (const url of PRECACHE) {\n        try {\n          const res = await fetch(url, { cache: 'no-store' })\n          if (res && res.ok) await cache.put(url, res)\n        } catch { /* 单项失败：用到时再走网络 */ }\n      }\n      /* 不在这里 skipWaiting：新 SW 停在 waiting，等用户点「立即更新」再接管。
         旧版 install 里直接抢班 → 旧页面跑旧 JS、资源却来自新缓存，版本错配/白屏。 */\n    }),\n  )\n})\n\nself.addEventListener('activate', event => {\n  event.waitUntil(\n    caches.keys()\n      .then(keys => Promise.all(keys.filter(k => k.startsWith('wxui-') && k !== CACHE).map(k => caches.delete(k))))\n      .then(() => self.clients.claim()),\n  )\n})\n\nself.addEventListener('message', event => {\n  if (event.data && event.data.type === 'SKIP_WAITING') {\n    self.skipWaiting()\n  }\n})\n\nself.addEventListener('fetch', event => {\n  const req = event.request\n  if (req.method !== 'GET') {\n    return\n  }\n\n  const url = new URL(req.url)\n  if (url.origin !== self.location.origin) {\n    return\n  }\n  if (excluded(url.pathname)) {\n    return\n  }\n\n  const isNavigate = req.mode === 'navigate'\n\n  if (isNavigate) {\n    // 顶层导航：index.html network-first，离线用缓存的 / 兜底 → 离线也能进登录页\n    event.respondWith(\n      fetch(req)\n        .then(res => {\n          if (res && res.ok) {\n            const copy = res.clone()\n            caches.open(CACHE).then(c => c.put('/', copy))\n          }\n          return res\n        })\n        .catch(() => caches.match('/', { cacheName: CACHE }).then(hit => hit || offlinePage())),\n    )\n    return\n  }\n\n  if (isUiAsset(url.pathname)) {\n    // 静态资源：cache-first（内容哈希不可变）\n    event.respondWith(\n      caches.match(req, { cacheName: CACHE }).then(hit => {\n        if (hit) {\n          return hit\n        }\n        return fetch(req).then(res => {\n          if (res && res.ok) {\n            const copy = res.clone()\n            caches.open(CACHE).then(c => c.put(req, copy))\n          }\n          return res\n        })\n      }),\n    )\n    return\n  }\n\n  // 其它同源 GET：network-first + 回退缓存\n  event.respondWith(\n    fetch(req).then(res => {\n      if (res && res.ok) {\n        const copy = res.clone()\n        caches.open(CACHE).then(c => c.put(req, copy))\n      }\n      return res\n    }).catch(() => caches.match(req, { cacheName: CACHE }).then(hit => hit || offlinePage())),\n  )\n})\n`
  writeFileSync(path.join(outDirAbs, 'sw.js'), sw, 'utf8')
  console.log(`[sw] 全量预缓存清单 -> ${precache.length} 项（assets ${files.length}）`)
}

/** closeBundle 后处理插件（显式 Plugin 类型，避免内联对象字面量让 defineConfig 报 TS2769）。 */
const stripPlugin: Plugin = {
  name: 'strip-unsupported-font-preload',
  enforce: 'post',
  closeBundle () {
    stripUnsupportedFontPreload()
    writeVersionManifest()
    generateSw()
  },
}

/**
 * 写出 version.json（「检查更新」的权威数据源）：
 * 服务端每次部署 = 一次新构建 → version.json 里的 version 变。
 * 运行中的 App 拿「自己打包进代码的 __BUILD_VERSION__」与服务端 version.json 比对，
 * 不同 = 有新版本 → 走 Service Worker 拉新资源并刷新。
 */
function writeVersionManifest () {
  const outPath = path.join(outDirAbs, 'version.json')
  const payload = JSON.stringify({
    version: BUILD_VERSION,
    ts: new Date().toISOString(),
    channels: ['assets', 'sw.js', 'index.html'],
  }, null, 2)
  writeFileSync(outPath, payload, 'utf8')
  console.log(`[version] ${outPath}  -> ${BUILD_VERSION}`)
}

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [
    Vue({
      template: { transformAssetUrls },
    }),
    Vuetify(),
    Components(),
    stripPlugin,
    Fonts({
      fontsource: {
        families: [
          {
            name: 'Roboto',
            // 限定 latin 子集：中文界面用系统字体（PingFang/雅黑），Roboto 只兜西文/数字。
            // 默认会拉全部 7 子集 × eot/woff/woff2/ttf（~67 条 preload，且 eot/ttf 浏览器不认
            // 报 "unsupported type"、未用子集报 "preloaded but not used"）。限定后只剩
            // latin-300/400/500/700 四条 woff2/woff，噪音清零。
            subset: 'latin',
            weights: [300, 400, 500, 700],
            styles: ['normal'],
          },
        ],
      },
    }),
  ],
  optimizeDeps: {
    exclude: ['vuetify'],
  },
  // 编译产物直接输出到后端 wwwroot：部署/运行时只需要后端，不需要 Node
  build: {
    outDir: fileURLToPath(new URL('../server/WeChatWeb/wwwroot', import.meta.url)),
    emptyOutDir: true,
    // 兼容旧手机内核：默认 target（baseline-widely-available ≈ Chrome 107）在
    // 低版本 WebView 上会因内置方法缺失整页白屏（首页能开、通讯录空白 —— toSorted）。
    // es2022：转译更新的语法，且支持 top-level await（main.ts 在用）；
    // 代码里已不再使用 toSorted / .at() 等 ES2022+ 内置方法。
    target: 'es2022',
    // 大库独立分桶，避免全部打进 index-*.js 触发 "chunk 过大" 警告：
    //   - vuetify  组件库最大头
    //   - vendor   vue + vue-router + pinia（前端框架内核）
    //   - mdi      @mdi/font 图标
    //   - signalr / qrcode / jsqr / captcha 各自独立（按需、可并行缓存）
    //   - libs     axios 等其余第三方
    //   - 其余仍走 Vite 默认分包（业务代码）
    rollupOptions: {
      output: {
        manualChunks (id) {
          if (!id.includes('node_modules')) return
          if (id.includes('vuetify') || id.includes('web-vue-notification')) return 'vuetify'
          if (id.includes('vue') || id.includes('@vue') || id.includes('pinia')) return 'vendor'
          if (id.includes('@mdi')) return 'mdi'
          if (id.includes('signalr')) return 'signalr'
          if (id.includes('qrcode') || id.includes('jsqr')) return 'qrcode'
          return 'libs'
        },
      },
    },
  },
  define: { 'process.env': {}, __BUILD_VERSION__: JSON.stringify(BUILD_VERSION) },
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('src', import.meta.url)),
    },
    extensions: [
      '.js',
      '.json',
      '.jsx',
      '.mjs',
      '.ts',
      '.tsx',
      '.vue',
    ],
  },
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:5000' },
      '/files': { target: 'http://localhost:5000' },
      '/hubs': { target: 'http://localhost:5000', ws: true },
    },
  },
})
