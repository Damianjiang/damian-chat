/**
 * lib/emoji.ts
 *
 * 表情表与「我的表情」记录的唯一数据源：
 * 聊天输入框的表情面板与「我 → 表情」页共用同一份表，保证两处能发的表情完全一致。
 * 用过的表情记在 localStorage，「我 → 表情」里可以再发到任意会话。
 */
export const ALL_EMOJIS: string[] = [
  '😀', '😃', '😄', '😁', '😆', '😅', '🤣', '😂', '🙂', '😉',
  '😊', '😍', '😘', '😎', '🤔', '🙄', '😳', '🥺', '😇', '🤗',
  '👍', '🙏', '💪', '🤝', '👏', '🎉', '❤️', '🔥', '🚀', '☕',
  '🍺', '⚽', '🏀', '🎮', '📚', '✈️', '🌹', '⭐', '🌈', '🍀',
]

const USED_KEY = 'wx_used_emojis'

/** 读「我的表情」（最近使用的在前，最多 40 个） */
export function usedEmojis (): string[] {
  try {
    const raw = JSON.parse(localStorage.getItem(USED_KEY) ?? '[]')
    if (!Array.isArray(raw)) {
      return []
    }
    return raw.filter(e => typeof e === 'string' && ALL_EMOJIS.includes(e)).slice(0, 40)
  } catch {
    return []
  }
}

/** 记录一次使用（去重、最新在前） */
export function markEmojiUsed (emoji: string) {
  if (!ALL_EMOJIS.includes(emoji)) {
    return
  }
  const list = usedEmojis().filter(e => e !== emoji)
  list.unshift(emoji)
  try {
    localStorage.setItem(USED_KEY, JSON.stringify(list.slice(0, 40)))
  } catch { /* 隐私模式下写不了，忽略 */ }
}

/** 从「我的表情」移除 */
export function removeUsedEmoji (emoji: string) {
  const list = usedEmojis().filter(e => e !== emoji)
  try {
    localStorage.setItem(USED_KEY, JSON.stringify(list))
  } catch { /* 忽略 */ }
}
