/**
 * lib/personalize.ts
 *
 * 「个性化设置」子页的运行时通道：
 *  - 特效开关（波纹/3D 倾斜/动效）持久化到 localStorage，并写到 documentElement 的
 *    data 属性 + CSS 变量，CSS/组件按需读取，无需刷新。
 *  - 用户背景图（存浏览器缓存 IndexedDB，见 idb.ts 的 idb*UserBg）→ 生成 objectURL
 *    挂到 body 的壁纸层。
 *
 * 与 theme store（主色/明暗）互不干扰：theme 管「配色 + 明暗」，这里管「组件质感 + 动效 + 壁纸」。
 */

import { t } from '@/lib/i18n'
import { idbGetUserBg, idbClearUserBg, idbPutUserBg } from '@/lib/idb'

/** 用户可开关的特效集合。3D 倾斜默认关闭：卡片上的 3D 场（perspective/rotateX/rotateY）
 * 会干扰浏览器命中测试，导致卡片内输入框/按钮点不准；默认关保证可操作，
 * 用户可在「设置 → 个性化设置 → 组件与特效」里手动开启倾斜特效。 */
export interface FxFlags {
  /** 登录页动波纹背景（canvas 正弦波纹；关则显示静态纯色） */
  ripple: boolean
  /** 3D 指针倾斜（登录卡片；关则无倾斜，但保留玻璃质感） */
  tilt: boolean
  /** 通用动效（进场动画 / 呼吸 logo / 消息弹出等；关则尊重并强化 reduced-motion） */
  motion: boolean
}

const FX_KEY = 'wx_fx_flags'

export const FX_DEFAULTS: FxFlags = { ripple: true, tilt: true, motion: true }

function safeGet (): FxFlags {
  try {
    const raw = localStorage.getItem(FX_KEY)
    if (!raw) return { ...FX_DEFAULTS }
    const p = JSON.parse(raw) as Partial<FxFlags>
    return {
      ripple: p.ripple !== false,
      tilt: p.tilt !== false,
      motion: p.motion !== false,
    }
  } catch {
    return { ...FX_DEFAULTS }
  }
}

export function getFx (): FxFlags {
  return safeGet()
}

/**
 * 应用特效到 documentElement：
 *  - data-fx-motion / data-fx-tilt 供 CSS 与组件读取
 *  - 关「动效」时额外挂 prefers-reduced-motion 覆盖类，停掉所有非必需动画
 * 返回取消函数。
 */
export function applyFx (fx: FxFlags, root: HTMLElement = document.documentElement): void {
  root.dataset.fxMotion = fx.motion ? 'on' : 'off'
  root.dataset.fxTilt = fx.tilt ? 'on' : 'off'
  // motion 关 → 用 :root 上的类停掉全站 transition/animation（CSS 里配合）
  if (fx.motion) root.classList.remove('wx-no-motion')
  else root.classList.add('wx-no-motion')
}

export function saveFx (fx: FxFlags): void {
  applyFx(fx)
  try {
    localStorage.setItem(FX_KEY, JSON.stringify(fx))
  } catch { /* 隐私模式忽略 */ }
  dispatchFxChanged()
}

/** 通知登录页等组件特效开关已变（波纹/3D 按需启用）。 */
function dispatchFxChanged (): void {
  try {
    window.dispatchEvent(new CustomEvent('wx:fx-changed'))
  } catch { /* 忽略 */ }
}

/* ---------------- 组件半透明度（0..100，100=默认不透明） ---------------- */

const ALPHA_KEY = 'wx_alpha'

export function getAlpha (): number {
  try {
    const n = Number(localStorage.getItem(ALPHA_KEY))
    if (Number.isFinite(n)) return Math.min(100, Math.max(0, Math.round(n)))
  } catch { /* 忽略 */ }
  return 100
}

/**
 * 应用半透明度到 documentElement：
 *  - 写 --wx-alpha（CSS 用它对主要表面层做 color-mix 透明化）
 *  - <100 时挂 .wx-glass（CSS 里对顶栏/导航等加 backdrop-filter 磨砂）
 */
export function applyAlpha (n: number, root: HTMLElement = document.documentElement): void {
  const v = Math.min(100, Math.max(0, Math.round(n)))
  root.style.setProperty('--wx-alpha', String(v))
  if (v < 100) root.classList.add('wx-glass')
  else root.classList.remove('wx-glass')
}

export function saveAlpha (n: number): void {
  applyAlpha(n)
  try {
    localStorage.setItem(ALPHA_KEY, String(Math.min(100, Math.max(0, Math.round(n)))))
  } catch { /* 隐私模式忽略 */ }
}

/** 启动时应用：读 fx + alpha + 字号 + 挂用户壁纸（不刷新即时生效，幂等）。
 *  字号必须在这里恢复——此前只在个性化页 onMounted 里恢复，刷新后没进那页就丢了。 */
export function initPersonalize (): void {
  try {
    applyFx(getFx())
    applyAlpha(getAlpha())
    applyFontLevel(getFontLevel())
  } catch { /* 忽略 */ }
  void applyUserBg()
}

/* ---------------- 用户背景图（存浏览器缓存） ---------------- */

let bgObjectUrl = ''

/** 读取并返回用户背景图的 objectURL（无则空串）。幂等：已挂起则不重建。 */
export async function ensureUserBgUrl (): Promise<string> {
  if (bgObjectUrl) return bgObjectUrl
  const rec = await idbGetUserBg().catch(() => null)
  if (!rec || !rec.blob) return ''
  bgObjectUrl = URL.createObjectURL(rec.blob)
  return bgObjectUrl
}

/** 把用户背景图挂到 body 壁纸层（无图则移除）。返回是否有图。 */
export async function applyUserBg (): Promise<boolean> {
  const url = await ensureUserBgUrl()
  const body = document.body
  if (url) {
    body.style.setProperty('--wx-user-bg', `url("${url}")`)
    body.classList.add('wx-has-user-bg')
  } else {
    body.style.removeProperty('--wx-user-bg')
    body.classList.remove('wx-has-user-bg')
  }
  return !!url
}

/** 校验并压缩图片为 WebP（≤1600px 宽，质量 0.82）后存 IDB。非法类型/超量拒绝。 */
export async function setUserBg (file: File): Promise<{ ok: boolean, error?: string }> {
  const okType = file.type.startsWith('image/')
  if (!okType) return { ok: false, error: t('personalize.bg.notImage') }
  if (file.size > 10 * 1024 * 1024) return { ok: false, error: t('personalize.bg.tooLarge') }
  try {
    const dataUrl = await readAsDataURL(file)
    const img = await loadImage(dataUrl)
    const out = downscale(img, 1600)
    const blob = await toBlob(out) ?? file
    if (!blob) return { ok: false, error: t('personalize.bg.processFail') }
    const ext = blob.type.includes('webp') ? 'webp' : 'png'
    await idbPutUserBg(blob, ext, blob.type || 'image/webp')
    // 之前有旧 objectURL 就释放，再挂新的
    if (bgObjectUrl) { URL.revokeObjectURL(bgObjectUrl); bgObjectUrl = '' }
    await applyUserBg()
    return { ok: true }
  } catch {
    return { ok: false, error: t('personalize.bg.processFail') }
  }
}

export async function removeUserBg (): Promise<void> {
  await idbClearUserBg().catch(() => {})
  if (bgObjectUrl) { URL.revokeObjectURL(bgObjectUrl); bgObjectUrl = '' }
  document.body.classList.remove('wx-has-user-bg')
  document.body.style.removeProperty('--wx-user-bg')
}

/* ---------------- 字号（0..3 级，刷新/重开自动恢复） ---------------- */

export const FONT_KEY = 'wx_font_level'
export const FONT_SCALES = [0.9, 1, 1.15, 1.3]
/** 字号文案（i18n key，展示时需经 t() 翻译） */
export const FONT_LABELS = ['font.s', 'font.m', 'font.l', 'font.xl']

/** 读持久化字号级别（非法值回落到 1=标准）。 */
export function getFontLevel (): number {
  try {
    const n = Number(localStorage.getItem(FONT_KEY))
    if (Number.isInteger(n) && n >= 0 && n < FONT_SCALES.length) return n
  } catch { /* 隐私模式 */ }
  return 1
}

/** 按级别写根字号（内联 style 覆盖 wechat.scss 的 --wx-font-size 基础值）。 */
export function applyFontLevel (level: number, root: HTMLElement = document.documentElement): void {
  const lv = Number.isInteger(level) && level >= 0 && level < FONT_SCALES.length ? level : 1
  root.style.fontSize = (16 * FONT_SCALES[lv]!).toFixed(1) + 'px'
}

export function saveFontLevel (level: number): void {
  applyFontLevel(level)
  try {
    localStorage.setItem(FONT_KEY, String(Math.min(FONT_SCALES.length - 1, Math.max(0, Math.round(level)))))
  } catch { /* 隐私模式 */ }
}

/* ---------------- 内部工具 ---------------- */

function readAsDataURL (f: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const fr = new FileReader()
    fr.onload = () => resolve(String(fr.result))
    fr.onerror = () => reject(fr.error)
    fr.readAsDataURL(f)
  })
}

function loadImage (src: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const img = new Image()
    img.onload = () => resolve(img)
    img.onerror = () => reject(new Error('图片加载失败'))
    img.src = src
  })
}

interface CanvasWithBlob extends HTMLCanvasElement { toBlob(cb: (b: Blob | null) => void, type?: string, q?: number): void }

function downscale (img: HTMLImageElement, maxW: number): CanvasWithBlob {
  const scale = Math.min(1, maxW / img.width)
  const w = Math.max(1, Math.round(img.width * scale))
  const h = Math.max(1, Math.round(img.height * scale))
  const c = document.createElement('canvas') as CanvasWithBlob
  c.width = w
  c.height = h
  const ctx = c.getContext('2d')
  if (ctx) ctx.drawImage(img, 0, 0, w, h)
  return c
}

/** 把 canvas 转 webp blob（浏览器不支持 webp 时回退 png）。 */
async function toBlob (c: HTMLCanvasElement): Promise<Blob | null> {
  return new Promise(resolve => {
    const cv = c as CanvasWithBlob
    cv.toBlob(b => resolve(b), 'image/webp', 0.82)
  })
}
