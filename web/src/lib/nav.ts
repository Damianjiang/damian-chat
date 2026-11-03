/**
 * lib/nav.ts —— 详情页安全返回。
 *
 * router.back() 在「直达 URL / 刷新后」场景下没有可回退的历史，
 * 用户点返回会毫无反应（或停在原地）。这里统一封装：
 * 有历史 → back()；无历史 → 落到调用方指定的父级路由。
 */
import { useRouter } from 'vue-router'

export function useSmartBack () {
  const router = useRouter()
  return function smartBack (fallback: { name?: string, path?: string }) {
    if (window.history.length > 1 && window.history.state?.back) {
      router.back()
      return
    }
    void router.replace(fallback)
  }
}
