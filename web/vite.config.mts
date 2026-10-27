import type { Plugin } from 'vite'
import { existsSync, readFileSync, writeFileSync } from 'node:fs'
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

/** closeBundle 后处理插件（显式 Plugin 类型，避免内联对象字面量让 defineConfig 报 TS2769）。 */
const stripPlugin: Plugin = {
  name: 'strip-unsupported-font-preload',
  enforce: 'post',
  closeBundle () {
    stripUnsupportedFontPreload()
    writeVersionManifest()
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
          if (id.includes('captcha')) return 'captcha'
          if (id.includes('vue-sliding-puzzle')) return 'slider'
          return 'libs'
        },
      },
    },
  },
  define: { 'process.env': {}, __BUILD_VERSION__: JSON.stringify(BUILD_VERSION) },
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('src', import.meta.url)),
      // 该包的 exports 未声明 CSS 子路径，这里直接指向 dist 里的样式文件
      '@puzzle-style': fileURLToPath(new URL('node_modules/vue-sliding-puzzle/dist/main.css', import.meta.url)),
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
