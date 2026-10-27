/*
 * Service Worker —— 仅缓存「App UI 静态资源」，让离线/弱网能进登录页、二次打开不再全量拉取。
 *
 * 安全边界（重要，勿放宽）：
 *   ① 只拦截「同源 + GET」请求；跨源一律放行（不放行 = 不缓存）。
 *   ② 绝不缓存 /api/*（鉴权数据、凭证、会话）、/files/*（聊天媒体 blob）、
 *      /hubs/*（SignalR 实时）、/swagger（接口结构）—— 这些必须实时走网络，
 *      缓存会把敏感数据/旧凭证留在磁盘、离线还继续用过期 token。
 *   ③ 缓存名带版本前缀 wxui-，升级时 activate 清理旧版，避免串版。
 *   ④ 更新只走本 SW 通道（skipWaiting/claim + 客户端 reload），不碰任何 token。
 *
 * 媒体（图片/语音/视频）不在这里 —— 它们由前端的 IndexedDB blob 缓存负责，
 * 走带鉴权的 objectURL，职责分离，互不重叠。
 */
'use strict'

const CACHE = 'wxui-v46'
const PRECACHE = ['/', '/favicon.ico', '/robots.txt']

// 绝不拦截缓存的路径前缀：鉴权数据 / 媒体 / 实时 / 接口文档
const EXCLUDE = [
  /^\/api\//,
  /^\/files\//,
  /^\/hubs\//,
  /^\/swagger/,
]

function excluded (path) {
  return EXCLUDE.some(re => re.test(path))
}

function isUiAsset (url) {
  const p = url.pathname
  // 内容哈希资源 / 字体 / 图标 / 静态文件：长期不可变，cache-first
  return p.startsWith('/assets/') || p.startsWith('/favicon') || p.includes('.woff') || p.includes('.ttf') || p.includes('.eot')
}

/** 离线兜底页：顶层导航无网且无缓存时返回（避免 Response.error() 在控制台报 network error） */
function offlinePage () {
  return new Response(
    '<!doctype html><meta charset="utf-8"><title>大面聊天</title>'
    + '<body style="margin:0;font-family:system-ui;background:#ededed;color:#181818">'
    + '<div style="min-height:100vh;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:12px;padding:24px;text-align:center">'
    + '<h1 style="font-size:18px;margin:0">网络连接不可用</h1>'
    + '<p style="font-size:14px;color:#888;margin:0">没有可用的本地缓存，且当前处于离线状态。请检查网络后刷新。</p>'
    + '<button style="margin-top:10px;padding:9px 22px;font-size:14px;border-radius:8px;border:none;background:#07c160;color:#fff;cursor:pointer" onclick="location.reload()">重新加载</button>'
    + '</div></body>',
    { status: 503, headers: { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' } },
  )
}

self.addEventListener('install', event => {
  event.waitUntil(
    caches.open(CACHE).then(cache => cache.addAll(PRECACHE)).then(() => self.skipWaiting()),
  )
})

self.addEventListener('activate', event => {
  event.waitUntil(
    caches.keys()
      .then(keys => Promise.all(keys.filter(k => k.startsWith('wxui-') && k !== CACHE).map(k => caches.delete(k))))
      .then(() => self.clients.claim()),
  )
})

// 客户端（设置页「更新 App」按钮）请求跳过等待，立即接管
self.addEventListener('message', event => {
  if (event.data && event.data.type === 'SKIP_WAITING') {
    self.skipWaiting()
  }
})

self.addEventListener('fetch', event => {
  const req = event.request
  if (req.method !== 'GET') {
    return
  } // 只拦 GET，写请求一律走网络

  const url = new URL(req.url)
  if (url.origin !== self.location.origin) {
    return
  } // 只拦同源（跨源不缓存）
  if (excluded(url.pathname)) {
    return
  } // 鉴权/媒体/实时/接口：绝不缓存

  const isNavigate = req.mode === 'navigate'

  if (isNavigate) {
    // 顶层导航（含 SPA 路由直达）：index.html network-first，拿不到用缓存里的 / 兜底 → 离线也能进登录页。
    // 兜底顺序：缓存的 index.html → 友好 503 离线页（不用 Response.error()，避免控制台 network error）
    event.respondWith(
      fetch(req)
        .then(res => {
          if (res && res.ok) {
            const copy = res.clone()
            caches.open(CACHE).then(c => c.put('/', copy)) // 始终用 / 作为 SPA 入口缓存 key
          }
          return res
        })
        .catch(() => caches.match('/', { cacheName: CACHE }).then(hit => hit || offlinePage())),
    )
    return
  }

  if (isUiAsset(url)) {
    // 静态资源：cache-first（不可变，命中即返回，省流量省带宽）
    event.respondWith(
      caches.match(req, { cacheName: CACHE }).then(hit => {
        if (hit) {
          return hit
        }
        return fetch(req).then(res => {
          if (res && res.ok) {
            const copy = res.clone()
            caches.open(CACHE).then(c => c.put(req, copy))
          }
          return res
        })
      }),
    )
    return
  }

  // 其它同源 GET：network-first + 回退缓存
  event.respondWith(
    fetch(req).then(res => {
      if (res && res.ok) {
        const copy = res.clone()
        caches.open(CACHE).then(c => c.put(req, copy))
      }
      return res
    }).catch(() => caches.match(req, { cacheName: CACHE }).then(hit => hit || offlinePage())),
  )
})
