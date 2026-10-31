/**
 * lib/confirm.ts
 *
 * 全局「危险操作确认」单例。App.vue 里挂一个全局对话框绑定本模块的状态，
 * 各组件调用 confirm() 即可弹出统一风格的确认框，避免每个组件各写一套弹窗。
 * 危险操作（拉黑、删除好友、清空聊天记录、删除消息等）应先 await confirm()，
 * 返回 false 时中止操作。
 */
import { reactive } from 'vue'
import { t } from '@/lib/i18n'

/**
 * 全局确认框宿主状态（单例）。
 *
 * ⚠️ 必须用 reactive() 而不是「普通对象 + ref 字段」：
 * 普通对象里的 ref 在模板里不会自动解包，`:model-value="confirmState.show"`
 * 会拿到 Ref 对象本身（恒为真）→ 对话框永远可见、且 text 渲染为空（曾经的 bug）。
 * reactive 的字段在模板与 JS 里都会自动解包，绑定才正确。
 */
export const confirmState = reactive({
  show: false,
  // 默认文案在 confirm() 调用时再取（t 依赖 Pinia，模块加载期 store 未就绪）
  title: '',
  text: '',
  confirmText: '',
  cancelText: '',
  danger: true,
})

let resolver: ((ok: boolean) => void) | null = null

/**
 * 弹出一个全局确认框（单例）。
 * @returns Promise<boolean> 点「确定」=true；点「取消」/关闭/重复触发=false。
 * 同一时间只允许一个确认框（IM 危险操作串行，避免叠加/串线）。
 */
export function confirm (opts: {
  title?: string
  text: string
  confirmText?: string
  cancelText?: string
  danger?: boolean
}): Promise<boolean> {
  // 已有确认框时直接拒绝，防止连续点击把多个解析器堆叠
  if (confirmState.show && resolver) return Promise.resolve(false)
  confirmState.title = opts.title ?? t('common.confirm.title')
  confirmState.text = opts.text
  confirmState.confirmText = opts.confirmText ?? t('common.confirm')
  confirmState.cancelText = opts.cancelText ?? t('common.cancel')
  confirmState.danger = opts.danger ?? true
  confirmState.show = true
  return new Promise<boolean>(res => { resolver = res })
}

/** App.vue 对话框回调：结束等待并复位（「确定」/「取消」都走这里）。 */
export function resolveConfirm (ok: boolean): void {
  const r = resolver
  resolver = null
  confirmState.show = false
  if (r) r(ok)
}
