/**
 * lib/clientlog.ts
 *
 * 前端客户端日志收集器（环形缓冲 + 实时订阅 + 客户端快照）。
 *
 * 用途：
 * 1) 「设置 → 连点标题“设置”5 次 → 开发者模式」里展示全部客户端日志；
 * 2) 「问题反馈」提交时附带最近若干条日志 + 客户端快照，便于 C# 后端定位。
 *
 * 采集来源（全部内存环形缓冲，不落地、不上传，除非用户主动提交反馈）：
 * - console.log / warn / error / info / debug（hook 包装，原方法保留）
 * - window 'error'（含资源加载失败）
 * - window 'unhandledrejection'（未捕获的 Promise 拒绝）
 * - 关键启动节点（挂载、SW 版本、主题、设备）通过 mark() 打点
 */

export type LogLevel = 'log' | 'info' | 'warn' | 'error' | 'debug'

export interface LogEntry {
  /** 毫秒时间戳 */
  ts: number
  level: LogLevel
  /** 人类可读首行（已序列化，多行截断到 300 字） */
  msg: string
  /** 完整 stack / 详情（可选，仅 error 保留，避免缓冲膨胀） */
  detail?: string
}

/** 环形缓冲容量（开发者面板展示上限，也是反馈可附带的最大行数基数）。 */
const CAPACITY = 600

let buffer: LogEntry[] = []
let seq = 0
const listeners = new Set<(e: LogEntry) => void>()

/** 是否已 hook（避免重复包装 console）。 */
let hooked = false

function formatArg (a: unknown): string {
  if (a instanceof Error) return a.stack ?? `${a.name}: ${a.message}`
  if (typeof a === 'object' && a !== null) {
    try {
      const s = JSON.stringify(a)
      return s ?? String(a)
    } catch {
      return String(a)
    }
  }
  return String(a)
}

/** 把一组 console 参数拼成首行（截断），并决定是否保留完整 detail。 */
function summarize (args: unknown[]): { msg: string, detail?: string } {
  const line = args.map(formatArg).join(' ')
  const msg = line.length > 300 ? line.slice(0, 300) + '…' : line
  // 仅当含 Error 对象或超长时保留完整 detail，避免普通日志把缓冲撑爆
  const hasError = args.some(a => a instanceof Error)
  const detail = (hasError || line.length > 300) ? line : undefined
  return { msg, detail }
}

function push (level: LogLevel, args: unknown[]) {
  const s = summarize(args)
  const entry: LogEntry = { ts: Date.now(), level, msg: s.msg, detail: s.detail }
  buffer.push(entry)
  if (buffer.length > CAPACITY) buffer = buffer.slice(buffer.length - CAPACITY)
  seq++
  for (const cb of listeners.values()) {
    try { cb(entry) } catch { /* 监听器异常不影响采集 */ }
  }
}

/** hook console + 全局错误事件。幂等。 */
export function installHooks () {
  if (hooked) return
  hooked = true

  const levels: LogLevel[] = ['log', 'info', 'warn', 'error', 'debug']
  for (const lvl of levels) {
    const orig = console[lvl].bind(console)
    console[lvl] = (...args: unknown[]) => {
      // error 级额外保留 stack；先原样打印，再记录
      try { push(lvl, args) } catch { /* 记录失败绝不阻断原日志 */ }
      orig(...args)
    }
  }

  window.addEventListener('error', (e: ErrorEvent) => {
    const target = e.target instanceof Element ? e.target : null
    if (target && 'tagName' in target) {
      // 资源加载失败（img/script/...）
      push('error', [`资源加载失败：${(target as HTMLElement).tagName} ${e.message || ''}`])
      return
    }
    if (!e.error) {
      // ErrorEvent 标准行号是 lineno（lineNumber 是 Chromium 私有名，新 TS lib 类型不再含它）
      push('error', [e.message, e.filename || '', e.lineno ?? 0])
      return
    }
    push('error', [`未捕获异常：`, e.error])
  })

  window.addEventListener('unhandledrejection', (e: PromiseRejectionEvent) => {
    const reason = e.reason
    if (reason instanceof Error) {
      push('error', [`未处理的 Promise 拒绝：`, reason])
    } else {
      push('error', ['未处理的 Promise 拒绝：', reason])
    }
  })
}

/** 主动打点（启动节点 / 主题 / 设备等），归一化到 clientlog。 */
export function mark (msg: string, detail?: string) {
  push('info', [msg, detail ?? ''])
}

/** 取当前环形缓冲（拷贝，时间正序）。 */
export function buffer_ (): LogEntry[] {
  return buffer.slice()
}

/** 取最近 n 条（时间正序），用于反馈附带。 */
export function recent (n: number): LogEntry[] {
  return buffer.slice(Math.max(0, buffer.length - n))
}

/** 清空缓冲。 */
export function clear () {
  buffer = []
  seq++
}

/** 格式化一条为「时间 级别 消息」可读行。 */
export function fmtEntry (e: LogEntry): string {
  const t = new Date(e.ts)
  const pad = (x: number) => (x < 10 ? '0' + x : '' + x)
  const time = `${pad(t.getHours())}:${pad(t.getMinutes())}:${pad(t.getSeconds())}.${String(t.getMilliseconds()).padStart(3, '0')}`
  const up = e.msg.replace(/\n/g, ' ')
  return `[${time}] ${e.level.toUpperCase()} ${up}`
}

/** 全部缓冲拼成可复制 / 下载的纯文本。 */
export function toText (entries: LogEntry[] = buffer_()): string {
  return entries.map(fmtEntry).join('\n')
}

/** 订阅实时日志（返回取消函数）。开发者面板开启时用它跟随刷新。 */
export function subscribe (cb: (e: LogEntry) => void): () => void {
  listeners.add(cb)
  return () => { listeners.delete(cb) }
}

/** 客户端快照（随反馈一并上报，便于后端定位版本 / 设备 / 运行态）。 */
export function clientSnapshot (withRecentLogs = false, maxLogs = 50): string {
  const w = window as unknown as Record<string, unknown>
  const buildVersion = (typeof __BUILD_VERSION__ !== 'undefined' ? __BUILD_VERSION__ : 'unknown')
  const lines: string[] = []
  lines.push(`版本=${buildVersion}`)
  lines.push(`UA=${navigator.userAgent}`)
  try {
    const swVersion = w.__swVersion ? String(w.__swVersion) : 'unknown'
    lines.push(`SW=${swVersion}`)
  } catch { lines.push('SW=n/a') }
  lines.push(`在线=${navigator.onLine ? 'yes' : 'no'}`)
  lines.push(`视口=${window.innerWidth}x${window.innerHeight} · 设备像素比=${window.devicePixelRatio}`)
  const theme = localStorage.getItem('dm_theme') ?? localStorage.getItem('wx_theme') ?? 'default'
  const mode = localStorage.getItem('dm_mode') ?? localStorage.getItem('wx_mode') ?? 'unknown'
  lines.push(`主题=${theme} 模式=${mode}`)

  if (withRecentLogs) {
    const logText = toText(recent(maxLogs))
    lines.push(`---- 最近 ${Math.min(maxLogs, buffer.length)} 条客户端日志 ----`)
    lines.push(logText || '（无日志）')
  }
  return lines.join('\n')
}

/** 下载当前日志为 .txt（Blob + a[download]）。 */
export function download (entries: LogEntry[] = buffer_()) {
  const text = toText(entries)
  const blob = new Blob([text], { type: 'text/plain;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `clientlog-${new Date().toISOString().slice(0, 19).replace(/[:T]/g, '')}.txt`
  document.body.appendChild(a)
  a.click()
  a.remove()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}
