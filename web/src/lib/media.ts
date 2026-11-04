/**
 * lib/media.ts
 *
 * 媒体文件类型判定（唯一的扩展名清单）。
 * 此前 video/audio 扩展名清单在 MessageBubble / ChatDetailPane 各抄一份且已经漂移
 * （webm 音频一边算音频一边走图片预览），统一到这里，新增扩展名只改一处。
 */

export const VIDEO_EXTS = ['mp4', 'webm', 'mov', 'm4v', 'mkv', 'avi', '3gp', 'flv', 'ogv']

// webm 可兼音频容器（语音消息 webm 录音），一并归入音频
export const AUDIO_EXTS = ['mp3', 'm4a', 'ogg', 'wav', 'aac', 'flac', 'webm']

export function extOfName (fileName?: string | null): string {
  const n = (fileName ?? '').toLowerCase()
  const i = n.lastIndexOf('.')
  return i > 0 ? n.slice(i + 1) : ''
}

/** 文件消息的预览形态：video / audio / image（其余当图片态占位，走下载卡片）。 */
export function mediaKindOf (fileName?: string | null): 'video' | 'audio' | 'image' {
  const ext = extOfName(fileName)
  if (VIDEO_EXTS.includes(ext)) return 'video'
  if (AUDIO_EXTS.includes(ext)) return 'audio'
  return 'image'
}
