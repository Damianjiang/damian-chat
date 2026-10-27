/**
 * lib/utils.ts
 */

export function formatFileSize (bytes?: number | null): string {
  if (!bytes) {
    return ''
  }
  if (bytes < 1024) {
    return `${bytes}B`
  }
  if (bytes < 1024 * 1024) {
    return `${(bytes / 1024).toFixed(1)}KB`
  }
  if (bytes < 1024 * 1024 * 1024) {
    return `${(bytes / 1024 / 1024).toFixed(1)}MB`
  }
  return `${(bytes / 1024 / 1024 / 1024).toFixed(1)}GB`
}

/** 聊天时间分组标签 */
export function timeLabel (iso: string): string {
  const d = new Date(iso)
  const now = new Date()
  const startToday = new Date(now.getFullYear(), now.getMonth(), now.getDate())
  const startYesterday = new Date(startToday.getTime() - 86_400_000)
  const hm = `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
  if (d >= startToday) {
    return hm
  }
  if (d >= startYesterday) {
    return `昨天 ${hm}`
  }
  if (d.getFullYear() === now.getFullYear()) {
    return `${d.getMonth() + 1}月${d.getDate()}日 ${hm}`
  }
  return `${d.getFullYear()}年${d.getMonth() + 1}月${d.getDate()}日`
}

export function sessionTimeLabel (iso?: string): string {
  if (!iso) {
    return ''
  }
  const d = new Date(iso)
  const now = new Date()
  if (d.toDateString() === now.toDateString()) {
    return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
  }
  const yest = new Date(now.getTime() - 86_400_000)
  if (d.toDateString() === yest.toDateString()) {
    return '昨天'
  }
  return `${d.getMonth() + 1}月${d.getDate()}日`
}

/** 头像背景墙配色（柔和的马卡龙色系，不再固定绿） */
export const AVATAR_COLORS = [
  '#5B8FF9', '#61DDAA', '#65789B', '#F6BD16', '#7262FD',
  '#78D3F8', '#9661BC', '#F6903D', '#008685', '#E86452',
]

/**
 * 头像背景色：按「昵称」哈希取色（同一昵称永远同色）。
 * 传昵称而不是 id —— 用户看到的是名字，首字母背景墙才稳定不随数据变化。
 */
export function avatarColor (nameOrId: string): string {
  const s = (nameOrId || '').trim() || '?'
  let hash = 0
  for (const c of s) {
    hash = Math.trunc(hash * 31 + (c.codePointAt(0) ?? 0))
  }
  return AVATAR_COLORS[Math.abs(hash) % AVATAR_COLORS.length] ?? '#5B8FF9'
}

/** 头像占位文字：取昵称首字符（中文取第一个字，英文取首字母大写） */
export function avatarInitial (nickname?: string | null): string {
  const s = (nickname ?? '').trim()
  if (!s) {
    return '?'
  }
  const first = [...s][0] ?? '?'
  return /[a-z]/i.test(first) ? first.toUpperCase() : first
}

/** 头像背景渐变（背景墙效果） */
export function avatarGradient (nickname?: string | null): string {
  const c = avatarColor(nickname ?? '?')
  return `linear-gradient(135deg, ${c} 0%, ${shade(c, -22)} 100%)`
}

/** 颜色明暗调整 */
function shade (hex: string, percent: number): string {
  const n = Number.parseInt(hex.slice(1), 16)
  const amt = Math.round(2.55 * percent)
  const r = Math.min(255, Math.max(0, (n >> 16) + amt))
  const g = Math.min(255, Math.max(0, ((n >> 8) & 0xff) + amt))
  const b = Math.min(255, Math.max(0, (n & 0xff) + amt))
  return `#${((1 << 24) + (r << 16) + (g << 8) + b).toString(16).slice(1)}`
}

export function messageSummary (m: { type: string, content: string, fileName?: string, stickerEmoji?: string, voiceSeconds?: number, recalled?: boolean } | undefined): string {
  if (!m) {
    return '暂无消息'
  }
  if (m.recalled) {
    return '撤回了一条消息'
  }
  switch (m.type) {
    case 'image': { return '[图片]'
    }
    case 'voice': { return `[语音] ${m.voiceSeconds ?? 0}"`
    }
    case 'file': { return `[文件] ${m.fileName ?? ''}`
    }
    case 'sticker': { return m.stickerEmoji ?? '[表情]'
    }
    case 'pat': { return '[拍一拍]'
    }
    case 'quote': { return m.content
    }
    default: { return m.content
    }
  }
}
