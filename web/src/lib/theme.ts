/**
 * 主题系统：多套配色 + 亮/暗/跟随系统，切换后持久化到 localStorage，刷新不丢。
 *
 * 之所以做成这样：原来全局只有一套绿色（#07C160），所有界面看起来都一个样。
 * 这里把「主色」抽象成可切换的预设，用户选择存本地，登录前后一致。
 */

/** 主题预设：name 用于展示，primary 为主色，其余为辅助强调色。 */
export interface ThemePreset {
  id: string
  name: string
  primary: string
  secondary: string
  accent: string
  /** 浅色背景下用于卡片/悬浮的底色 */
  surfaceTint: string
}

export const THEME_PRESETS: ThemePreset[] = [
  { id: 'classic', name: '经典绿', primary: '#07C160', secondary: '#576B95', accent: '#FA9D3C', surfaceTint: '#F7F7F7' },
  { id: 'ocean', name: '深海蓝', primary: '#2F6FED', secondary: '#00A6A6', accent: '#F2994A', surfaceTint: '#F5F8FF' },
  { id: 'purple', name: '幽紫', primary: '#6C5CE7', secondary: '#00CEC9', accent: '#FD79A8', surfaceTint: '#F7F5FF' },
  { id: 'sunset', name: '暖橘', primary: '#E8590C', secondary: '#D6336C', accent: '#F59F00', surfaceTint: '#FFF8F2' },
  { id: 'forest', name: '松墨绿', primary: '#2F9E44', secondary: '#5C940D', accent: '#E67700', surfaceTint: '#F4FAF4' },
  { id: 'graphite', name: '石墨灰', primary: '#495057', secondary: '#868E96', accent: '#F03E3E', surfaceTint: '#F8F9FA' },
]

export type ThemeMode = 'light' | 'dark' | 'system'

const THEME_KEY = 'dm_theme_id'
const MODE_KEY = 'dm_theme_mode'

function read (key: string, fallback: string): string {
  try {
    return localStorage.getItem(key) || fallback
  } catch {
    return fallback
  }
}

function write (key: string, value: string): void {
  try {
    localStorage.setItem(key, value)
  } catch { /* 隐私模式下忽略 */ }
}

export function getThemeId (): string {
  const fallback = THEME_PRESETS[0]?.id ?? 'classic'
  const id = read(THEME_KEY, fallback)
  return THEME_PRESETS.some(t => t.id === id) ? id : fallback
}

export function setThemeId (id: string): void {
  if (THEME_PRESETS.some(t => t.id === id)) {
    write(THEME_KEY, id)
  }
}

export function getThemeMode (): ThemeMode {
  const m = read(MODE_KEY, 'system')
  return ['light', 'dark', 'system'].includes(m) ? (m as ThemeMode) : 'system'
}

export function setThemeMode (mode: ThemeMode): void {
  write(MODE_KEY, mode)
}

export function currentPreset (): ThemePreset {
  // THEME_PRESETS 保证非空，这里用 ?? 兜底给 TS
  return THEME_PRESETS.find(t => t.id === getThemeId()) ?? THEME_PRESETS[0]!
}

/** 系统是否处于暗色 */
export function prefersDark (): boolean {
  try {
    return window.matchMedia('(prefers-color-scheme: dark)').matches
  } catch {
    return false
  }
}

/** 实际生效的明暗：system 模式下跟随系统 */
export function resolveDark (mode: ThemeMode = getThemeMode()): boolean {
  return mode === 'dark' || (mode === 'system' && prefersDark())
}

/**
 * 「跟随系统」实时监听：系统明暗变化时立即重新应用主题。
 * 此前只在启动读一次 prefersDark()，用户在系统里切深色/浅色，App 不跟着变（需刷新）。
 * 返回取消监听函数。非 system 模式 / 不支持 matchMedia 时直接返回 no-op。
 * 安全：只读系统外观，不触碰任何数据；应用走已存在的重建通道（不碰 token）。
 */
export function watchSystemTheme (apply: (dark: boolean) => void): () => void {
  try {
    const mq = window.matchMedia('(prefers-color-scheme: dark)')
    const handler = () => {
      // 仅当当前处于「跟随系统」模式时才响应，避免覆盖用户手动选的亮/暗
      if (getThemeMode() === 'system') {
        apply(mq.matches)
      }
    }
    mq.addEventListener('change', handler)
    return () => mq.removeEventListener('change', handler)
  } catch {
    return () => {}
  }
}
