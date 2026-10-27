import type { ThemeMode } from '@/lib/theme'
import { defineStore } from 'pinia'
import {
  currentPreset,
  getThemeId,
  getThemeMode,
  resolveDark,
  setThemeId,
  setThemeMode,
  THEME_PRESETS,
} from '@/lib/theme'

/**
 * 主题 store：主色 + 亮暗模式。
 * 切换即时生效（重建 Vuetify theme 并写 CSS 变量），并持久化到 localStorage。
 */
export const useThemeStore = defineStore('theme', {
  state: () => ({
    themeId: getThemeId(),
    mode: getThemeMode(),
    presets: THEME_PRESETS,
  }),
  getters: {
    preset: state => state.presets.find(p => p.id === state.themeId) ?? currentPreset(),
    isDark: state => resolveDark(state.mode),
  },
  actions: {
    /** 把主题色写进 CSS 变量，供非 Vuetify 组件（自定义样式）使用 */
    applyCssVars () {
      const p = this.preset
      const root = document.documentElement
      root.style.setProperty('--dm-primary', p.primary)
      root.style.setProperty('--dm-secondary', p.secondary)
      root.style.setProperty('--dm-accent', p.accent)
      root.style.setProperty('--dm-tint', p.surfaceTint)
      root.dataset.dmTheme = p.id
      root.dataset.dmMode = this.isDark ? 'dark' : 'light'
    },

    setTheme (id: string) {
      if (!this.presets.some(p => p.id === id)) {
        return
      }
      this.themeId = id
      setThemeId(id)
      this.applyCssVars()
      void this.rebuild()
    },

    setMode (mode: ThemeMode) {
      this.mode = mode
      setThemeMode(mode)
      this.applyCssVars()
      void this.rebuild()
    },

    /**
     * 系统明暗变化（matchMedia change）时调用：
     * 仅「跟随系统」模式生效。isDark 每次访问都会 live 读 prefersDark()，
     * 这里重新写 CSS 变量 + 重建 Vuetify 主题即可切到新的明暗。手动亮/暗不受系统影响。
     */
    applySystemDark () {
      if (this.mode !== 'system') {
        return
      }
      this.applyCssVars()
      void this.rebuild()
    },

    /** 通知 App 重建 Vuetify 主题实例（主色/明暗切换即时生效） */
    async rebuild () {
      try {
        const vuetifyMod = await import('@/plugins/vuetify')
        const vuetify = vuetifyMod.default
        const { buildThemes } = vuetifyMod
        const themes = buildThemes()
        vuetify.theme.themes.value = themes as any
        vuetify.theme.global.name.value = this.isDark ? 'dark' : 'light'
      } catch (error) {
        console.error('[theme] 重建 Vuetify 主题失败：', error)
        // 兜底：重建失败时至少把主色写到 CSS 变量，避免完全不动
        this.applyCssVars()
      }
    },
  },
})
