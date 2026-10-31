/**
 * stores/language.ts —— 全站语言管理。
 *
 * 默认行为：
 *   1. 如果用户手动选过（localStorage 'wx_lang' 有值）→ 用用户选的；
 *   2. 否则自动识别：navigator.languages 含中文 → 'zh'，否则 → 'en'。
 *
 * 切换时：
 *   - 写 localStorage 'wx_lang'；
 *   - 更新 <html lang> 属性（利于屏幕阅读器 / 翻译插件）；
 *   - 所有组件通过 useLanguageStore().lang 响应式取语言，无需刷新。
 *   - mode 记录「auto / zh / en」用户意图本身，供选择器高亮当前项（localStorage 非响应式，
 *     组件不能直接读它做选中态，否则切换后界面不刷新）。
 */
import { defineStore } from 'pinia'
import type { Lang } from '@/lib/i18n'

const LANG_KEY = 'wx_lang'

type LangMode = Lang | 'auto'

function readSavedMode (): LangMode {
  try {
    const saved = localStorage.getItem(LANG_KEY)
    if (saved === 'zh' || saved === 'en') return saved
  } catch { /* privacy mode */ }
  return 'auto'
}

function detect (): Lang {
  const saved = readSavedMode()
  if (saved !== 'auto') return saved
  // auto: 跟随浏览器
  const nav = navigator.languages ?? [navigator.language ?? '']
  for (const code of nav) {
    if (code.toLowerCase().startsWith('zh')) return 'zh'
  }
  return 'en'
}

export const useLanguageStore = defineStore('language', {
  state: () => ({
    lang: detect() as Lang,
    mode: readSavedMode() as LangMode,
  }),
  getters: {
    isZh: (s) => s.lang === 'zh',
    isEn: (s) => s.lang === 'en',
  },
  actions: {
    /** 设置语言（'auto' 表示恢复自动识别）。 */
    setLang (val: LangMode) {
      this.mode = val
      if (val === 'auto') {
        try { localStorage.removeItem(LANG_KEY) } catch { /* ignore */ }
        this.lang = detect()
      } else {
        this.lang = val
        try { localStorage.setItem(LANG_KEY, val) } catch { /* ignore */ }
      }
      document.documentElement.lang = this.lang === 'zh' ? 'zh-CN' : 'en'
    },
  },
})

// 启动时设置 <html lang>
document.documentElement.lang = detect() === 'zh' ? 'zh-CN' : 'en'
