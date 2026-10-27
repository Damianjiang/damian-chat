/**
 * plugins/vuetify.ts
 *
 * WeChat-style theme overrides on top of the official scaffold.
 * Framework documentation: https://vuetifyjs.com
 */

// Composables
import { createVuetify } from 'vuetify'
import { aliases } from 'vuetify/iconsets/mdi'

import { currentPreset, getThemeId, getThemeMode, prefersDark, resolveDark } from '@/lib/theme'
// 图标：改用内联 SVG（@mdi/js 矢量路径，fill: currentColor）。
// 不再引入 @mdi/font webfont（3.6MB 字体在旧设备未应用时 PUA 字形回退成黑豆腐块）。
import { MdiSvgIcon } from '@/lib/mdi-svg'
// Styles
import 'vuetify/styles'

/** 按当前选中的预设生成 Vuetify 主题（主色随主题切换） */
function buildTheme (dark: boolean) {
  const p = currentPreset()
  const adapt = (hex: string, amt: number) => {
    const n = Number.parseInt(hex.slice(1), 16)
    const clamp = (v: number) => Math.min(255, Math.max(0, v))
    const r = clamp((n >> 16) + amt)
    const g = clamp(((n >> 8) & 0xff) + amt)
    const b = clamp((n & 0xff) + amt)
    return '#' + ((1 << 24) + (r << 16) + (g << 8) + b).toString(16).slice(1)
  }
  // 暗色下主色提亮，保证对比度
  const primary = dark ? adapt(p.primary, 30) : p.primary

  return dark
    ? {
        dark: true,
        colors: {
          primary,
          'on-primary': '#FFFFFF',
          'secondary': adapt(p.secondary, 24),
          'on-secondary': '#FFFFFF',
          'success': '#51CF66',
          'warning': '#FFD43B',
          'error': '#FF6B6B',
          'on-error': '#1A1A1A',
          'info': '#4DABF7',
          'on-info': '#1A1A1A',
          'on-success': '#1A1A1A',
          'on-warning': '#1A1A1A',
          'background': '#111214',
          'surface': '#1C1D20',
          'on-background': '#E9ECEF',
          'on-surface': '#E9ECEF',
          'wechat-chat-bg': '#0E0F11',
          'wechat-bubble-me': '#2B5E3F',
          'wechat-bubble-other': '#26282C',
          'wechat-divider': '#303238',
          'wechat-unread': '#FF6B6B',
          'wechat-text-primary': '#E9ECEF',
          'wechat-text-secondary': '#9BA1A6',
        },
        variables: { 'border-radius': '10px' },
      }
    : {
        colors: {
          primary,
          'on-primary': '#FFFFFF',
          'secondary': p.secondary,
          'on-secondary': '#FFFFFF',
          'success': p.primary,
          'warning': p.accent,
          'error': '#FA5151',
          'on-error': '#FFFFFF',
          'info': '#10AEFF',
          'on-info': '#FFFFFF',
          'on-success': '#FFFFFF',
          'on-warning': '#FFFFFF',
          'background': p.surfaceTint,
          'surface': '#FFFFFF',
          'on-background': '#181818',
          'on-surface': '#181818',
          'wechat-chat-bg': p.surfaceTint,
          'wechat-bubble-me': '#95EC69',
          'wechat-bubble-other': '#FFFFFF',
          'wechat-divider': '#E5E5E5',
          'wechat-unread': '#FA5151',
          'wechat-text-primary': '#181818',
          'wechat-text-secondary': '#888888',
        },
        variables: { 'border-radius': '10px' },
      }
}

const darkNow = resolveDark()

/** 供主题 store 在运行时重建两套主题（切主色/切明暗时调用） */
export function buildThemes () {
  return { light: buildTheme(false), dark: buildTheme(true) }
}

const light = buildTheme(false)
const dark = buildTheme(true)

export default createVuetify({
  display: {
    thresholds: {
      xs: 0,
      sm: 640,
      md: 768,
      lg: 1024,
      xl: 1440,
      xxl: 1920,
    },
    mobileBreakpoint: 768,
  },
  icons: {
    defaultSet: 'mdi',
    aliases,
    // component 换成自研的 SVG 渲染器（@mdi/js 矢量路径 + fill:currentColor），
    // aliases 仍复用 vuetify/iconsets/mdi 的 $close/$chevron-right 等内部别名。
    // MdiSvgIcon 的 props（tag 带默认值）与 Vuetify 内部 IconComponent（tag 必填）结构不完全一致，
    // 做一次类型断言对齐；运行期渲染行为不变（Vuetify 传 tag/icon/class/style）。
    sets: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      mdi: { component: MdiSvgIcon as any },
    },
  },
  theme: {
    // 亮/暗两套都按当前主色生成，切换主色时整体换肤
    defaultTheme: darkNow ? 'dark' : 'light',
    themes: {
      light,
      dark,
    },
  },
})
