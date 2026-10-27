/**
 * boot.js —— index.html 的同步引导脚本（外部文件，CSP script-src 'self' 合规）。
 *
 * 背景：服务端 SecurityHeadersMiddleware 下发 CSP `script-src 'self'`，
 * index.html 里任何形式的内联脚本（<script> 块 / onclick=）都会被浏览器拦截，
 * 导致 loader 控制函数从未定义、加载层无法移除 → 首屏卡死在"下载必要资源"。
 * 因此所有引导逻辑集中在这个同源外部文件里。
 *
 * 职责：
 * ① 主题色首帧（data-dm-mode），避免加载/错误页颜色闪烁
 * ② 浏览器确认门 & 网络错误页的按钮（替代被 CSP 拦掉的 onclick=）
 * ③ __wxHideLoader / __wxShowNeterr / __wxHideNeterr（main.ts 挂载后调用）
 * ④ PerformanceObserver 资源进度（百分比 + 进度条 + 正在下载的文件名）
 * ⑤ 30 秒兜底：资源没加载完 / Vue 没挂载 → 显示"网络错误"页（可重新加载）
 */

/* ---------- ① 主题首帧 ---------- */
(function () {
  try {
    const m = localStorage.getItem('dm_theme_mode') || 'system'
    const dark = m === 'dark' || (m === 'system' && matchMedia('(prefers-color-scheme: dark)').matches)
    document.documentElement.dataset.dmMode = dark ? 'dark' : 'light'
  } catch {
    document.documentElement.dataset.dmMode = 'light'
  }
})()

/* ---------- ①⁺ UI 版本自愈哨兵（关键！解决"电脑版点不动"的旧缓存死锁） ----------
 *
 * 背景：电脑版卡死是因为 Service Worker 装着旧版，一直喂「旧 JS」；旧 JS 里有一个
 * 永远打开、无法关闭的全屏弹窗 → Vuetify 给整页加了 inert → 所有按钮/输入框全点不动。
 * 而"自动刷新"代码只在新 JS 里，旧 JS 没有 → 旧页面永远不会自我更新（死锁）。
 *
 * boot.js 是每次加载都强制走网络的经典脚本（SW 的 fetch 对它走 network-first，
 * 不缓存命中），所以无论当前跑的是哪一版 JS，本页加载时执行的都是「最新」boot.js。
 * 因此把自愈放在这里：只要服务器发过新版（version.json 变了），就把旧 SW 注销、
 * 清掉所有 UI 缓存、强制整站重载一次，让所有端（PC/手机）一步到位换血。
 * 只做一次：换血后 localStorage 里记下最新 version，后续不再重复刷新。 */
;(function () {
  try {
    if (typeof fetch !== 'function' || !navigator.onLine) return // 离线时不折腾
    fetch('/version.json', { cache: 'no-store' })
      .then(function (r) { return r.ok ? r.json() : null })
      .then(function (v) {
        var ver = v && v.version
        if (!ver) return
        var stored = null
        try { stored = localStorage.getItem('wx_ui_gen') } catch (e) {}
        if (stored !== ver) {
          // 有新 UI 版本：彻底注销旧 SW + 清缓存，再强制重载拿到新 JS
          var doReset = function () {
            try { localStorage.setItem('wx_ui_gen', ver) } catch (e) {}
            location.replace('/')
          }
          var swReady = Promise.resolve()
          if ('serviceWorker' in navigator) {
            swReady = navigator.serviceWorker.getRegistrations().then(function (rs) {
              return Promise.all(rs.map(function (r) { return r.unregister() }))
            })
          }
          var cacheReady = 'caches' in window
            ? caches.keys().then(function (ks) {
                // 只清本应用自己命名的 UI 缓存（wxui- 前缀），绝不触碰其它数据。
                // （聊天记录/文件在 IndexedDB「wx_cache」，登录态在 localStorage，均不受影响）
                return Promise.all(ks.filter(function (k) { return k.indexOf('wxui-') === 0 }).map(function (k) { return caches.delete(k) }))
              })
            : Promise.resolve()
          Promise.all([swReady, cacheReady]).then(doReset)
        } else {
          // 已是最新：确保哨兵值在（首访时存下）
          try { if (stored === null) localStorage.setItem('wx_ui_gen', ver) } catch (e) {}
        }
      })
      .catch(function () { /* 拿不到 version.json（离线/旧后端）不干预，保持现状 */ })
  } catch (e) { /* 哨兵失败绝不影响正常加载 */ }
})()

/* ---------- ② 按钮（外部监听，替代被 CSP 拦截的 onclick=） ---------- */
function wxBrowserConfirmed () {
  try {
    localStorage.setItem('wx_browser_confirmed', '1')
  } catch {
    document.cookie = 'wx_browser_confirmed=1;path=/;max-age=31536000'
  }
  location.reload()
}

function wxBrowserUnsupported () {
  const el = document.querySelector('#sg-blocked')
  if (el) {
    el.style.display = 'block'
  }
}

function wxBrowserRetry () {
  location.reload()
}

document.addEventListener('click', function (ev) {
  const t = ev.target
  if (!t) {
    return
  }
  if (t.closest('[data-wx-confirm]')) {
    wxBrowserConfirmed()
  } else if (t.closest('[data-wx-unsupported]')) {
    wxBrowserUnsupported()
  } else if (t.closest('#wx-neterr-retry')) {
    wxBrowserRetry()
  }
})

/* ---------- ③ main.ts 调用的控制函数 ---------- */
// 显示网络错误页 + 移除加载层（此前 loader z-index 高于错误页且不被移除 → 卡死）
window.__wxShowNeterr = function () {
  const l = document.querySelector('#wx-loader')
  if (l) {
    l.remove()
  }
  const n = document.querySelector('#wx-neterr')
  if (n) {
    n.classList.add('on')
  }
}

window.__wxHideNeterr = function () {
  const n = document.querySelector('#wx-neterr')
  if (n) {
    n.classList.remove('on')
  }
}

// Vue 挂载成功：整层移除加载动画
window.__wxHideLoader = function () {
  const l = document.querySelector('#wx-loader')
  if (l) {
    l.remove()
  }
}

/* ---------- ④ 资源加载进度（百分比 + 进度条 + 文件名） ---------- */
;(function () {
  const pct = document.querySelector('#wx-loader-pct')
  const bar = document.querySelector('#wx-loader-bar')
  const file = document.querySelector('#wx-loader-file')
  if (!pct || !bar || !file) {
    return
  }

  let totalEntries = 0
  let done = 0

  function showProgress (p) {
    const clamped = Math.max(0, Math.min(100, p))
    pct.textContent = Math.round(clamped) + '%'
    bar.style.width = clamped + '%'
  }

  function showFile (name) {
    if (!name) {
      return
    }
    // 只显示路径部分（去掉协议/域名，去掉 .map），整洁且不泄露部署路径
    let short = name.replace(/^https?:\/\/[^/]+/, '').replace(/\.map$/i, '')
    if (short.length > 40) {
      short = short.slice(0, 38) + '…'
    }
    file.textContent = '正在下载 ' + short
  }

  const onEntries = function (list) {
    const entries = list.getEntries()
    for (const e of entries) {
      if (e.entryType !== 'resource') {
        continue
      }
      totalEntries++
      done++
      showFile(e.name)
      // 按已完成条目数 / 预估总数估进度（字节法受 SW 缓存影响，命中缓存 transferSize=0）
      const estimated = Math.min(Math.max(totalEntries, 3), 12)
      showProgress((done / estimated) * 100)
    }
  }

  /**
   * ★ 规范：buffered 只能和 type 搭配，不能和 entryTypes 搭配。
   * 混用会直接抛 "The PerformanceObserver does not support buffered flag with
   * the entryTypes argument." —— 控制台红字，而且抛在 try 里就意味着进度条永远停在 0%。
   * 所以：优先 { type, buffered }，失败再退 { entryTypes }，都失败才走下面的模拟进度。
   */
  let observed = false
  if (typeof PerformanceObserver === 'function') {
    try {
      new PerformanceObserver(onEntries).observe({ type: 'resource', buffered: true })
      observed = true
    } catch {
      try {
        new PerformanceObserver(onEntries).observe({ entryTypes: ['resource'] })
        observed = true
      } catch { observed = false }
    }
  }

  // 不支持 / observe 抛错 → 每 500ms 推进模拟进度，避免卡在第 0%
  if (!observed) {
    var simTimer = setInterval(function () {
      const layer = document.querySelector('#wx-loader')
      if (!layer) {
        clearInterval(simTimer); return
      }
      const cur = Number.parseInt(pct.textContent, 10) || 0
      if (cur < 95) {
        showProgress(cur + 5)
      }
    }, 500)
  }

  // 全部资源到达：100%
  window.addEventListener('load', function () {
    showProgress(100)
    file.textContent = '资源加载完成，正在启动…'
  })
})()

/* ---------- ⑤ 30 秒兜底：卡住 → 网络错误页（无条件，在线离线都触发） ---------- */
window.addEventListener('online', function () {
  window.__wxHideNeterr()
})

setTimeout(function () {
  const l = document.querySelector('#wx-loader')
  if (l && l.parentNode) {
    window.__wxShowNeterr()
  }
}, 30_000)
