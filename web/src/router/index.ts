/**
 * router/index.ts
 *
 * 单一路由树 + WeChatAppShell 按端渲染三套布局（手机 page / iPad two / 电脑 three）。
 * 子路由本身不渲染组件，shell 依据 meta.section / meta.detail 从 pane 映射表渲染。
 *
 * 鉴权：localStorage 里有 token 不等于已登录（可被手工伪造），必须向服务端校验一次
 * （GET /api/users/me）。校验失败/过期一律清令牌并回登录页，杜绝“改个 localStorage 就进去”。
 */
import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import Noop from '@/components/Noop.vue'
import { api, clearTokens, getAccessToken, getRefreshToken } from '@/lib/api'
import { useAuthStore } from '@/stores/auth'

declare module 'vue-router' {
  interface RouteMeta {
    public?: boolean
    section?: 'chat' | 'contacts' | 'moments' | 'discovery' | 'search' | 'me'
    detail?: 'chat' | 'group' | 'contact' | 'moments' | 'settings' | 'personalize' | 'profile' | 'emojis' | 'favorites' | 'blocked'
    hideBottom?: boolean
  }
}

const routes: RouteRecordRaw[] = [
  {
    path: '/login',
    name: 'login',
    component: () => import('@/views/LoginView.vue'),
    meta: { public: true },
  },
  {
    path: '/',
    component: () => import('@/components/WeChatAppShell.vue'),
    children: [
      { path: '', redirect: '/chat' },
      { path: 'chat', name: 'chat', component: Noop, meta: { section: 'chat' } },
      { path: 'chat/:id', name: 'chat-detail', component: Noop, meta: { section: 'chat', detail: 'chat', hideBottom: true } },
      { path: 'contacts', name: 'contacts', component: Noop, meta: { section: 'contacts' } },
      { path: 'contacts/:id', name: 'contact-profile', component: Noop, meta: { section: 'contacts', detail: 'contact', hideBottom: true } },
      { path: 'group/:id', name: 'group', component: Noop, meta: { section: 'chat', detail: 'group', hideBottom: true } },
      { path: 'moments', name: 'moments', component: Noop, meta: { section: 'moments', hideBottom: true } },
      { path: 'moments/:id', name: 'moment-detail', component: Noop, meta: { section: 'moments', detail: 'moments', hideBottom: true } },
      { path: 'discovery', name: 'discovery', component: Noop, meta: { section: 'discovery' } },
      { path: 'search', name: 'search', component: Noop, meta: { section: 'search' } },
      { path: 'me', name: 'me', component: Noop, meta: { section: 'me' } },
      { path: 'settings', name: 'settings', component: Noop, meta: { section: 'me', detail: 'settings', hideBottom: true } },
      { path: 'personalize', name: 'personalize', component: Noop, meta: { section: 'me', detail: 'personalize', hideBottom: true } },
      { path: 'blocked', name: 'blocked', component: Noop, meta: { section: 'me', detail: 'blocked', hideBottom: true } },
      { path: 'profile', name: 'profile', component: Noop, meta: { section: 'me', detail: 'profile', hideBottom: true } },
      { path: 'emojis', name: 'emojis', component: Noop, meta: { section: 'me', detail: 'emojis', hideBottom: true } },
      { path: 'favorites', name: 'favorites', component: Noop, meta: { section: 'me', detail: 'favorites', hideBottom: true } },
    ],
  },
  { path: '/:pathMatch(.*)*', redirect: '/chat' },
]

const router = createRouter({
  history: createWebHistory(),
  routes,
})

/**
 * 懒加载 chunk 失败自动整页刷新（发版自愈）。
 *
 * 场景：前端重新构建后服务器上的哈希产物已更换，还开着旧页面的用户点到一个
 * 没去过的路由（如通讯录）时，按旧哈希去拉 chunk → 404 → 路由挂掉，
 * 表现为「某个界面无法加载」。已访问过的页面在内存里所以看起来正常。
 * 处理：这类错误强制整页刷新一次（拿新 index.html + 新产物）；sessionStorage
 * 防死循环（刷新后仍失败就放行报错，多半是真断网）。
 */
router.onError((error, to) => {
  const msg = String((error as Error)?.message ?? '')
  if (!/Failed to fetch dynamically imported module|Importing a module script failed|error loading dynamically imported module/i.test(msg)) {
    return
  }
  if (sessionStorage.getItem('wx_chunk_reload')) return
  sessionStorage.setItem('wx_chunk_reload', '1')
  location.href = to?.fullPath ?? location.pathname
})

// Vite 预加载（依赖预取）失败同样处理
window.addEventListener('vite:preloadError', () => {
  if (sessionStorage.getItem('wx_chunk_reload')) return
  sessionStorage.setItem('wx_chunk_reload', '1')
  location.reload()
})

let sessionChecked: Promise<boolean> | null = null

/**
 * 一次性会话校验：拿本地 token 请求 /api/users/me。
 * 成功即认为已登录；401/异常（token 伪造、过期、被踢）都视为未登录并清空本地令牌。
 *
 * ★ 登录/注册成功后的"本会话内"短路：登录/注册接口本身已把权威 user 写进 pinia store
 * （纯内存态：刷新即丢、手改 localStorage 也改不动它）。此后同一会话里的路由导航
 * 直接信任 store.user，不再额外打一次 /users/me。
 * 否则会触发"登录后二次网络校验"：弱网 / 浏览器续期时序 / 401 抖动下这条校验偶发失败，
 * 守卫随即 clearTokens 并弹回登录页 —— 表现成"明明登录成功却进不了主界面"。
 * 安全底线不变：刷新 / 直达 / 手改 localStorage 时 store.user 为空（非本会话来源），
 * 仍必须走下面的真实 /users/me 网络校验。
 */
async function verifySession (): Promise<boolean> {
  if (!getAccessToken()) {
    return false
  }
  // 本会话内已登录（登录/注册刚成功）：信任内存 store，跳过重复网络校验
  try {
    if (useAuthStore().user) return true
  } catch { /* pinia 尚未就绪则继续走网络校验 */ }
  sessionChecked ??= (async () => {
    try {
      await api.get('/users/me')
      return true
    } catch (error: any) {
      // 401/403 = 令牌无效；刷新令牌也救不回来才算彻底失效
      if (error?.response?.status === 401 && getRefreshToken()) {
        try {
          // refresh 令牌走 httpOnly cookie（同源自动携带，不再放 body，防它经代理/日志/Referer 泄露）
          const res = await api.post('/auth/refresh')
          const { saveTokens } = await import('@/lib/api')
          saveTokens(res.data.accessToken, res.data.refreshToken ?? res.data.accessToken)
          await api.get('/users/me')
          return true
        } catch { /* 刷新失败 = 彻底未登录 */ }
      }
      clearTokens()
      return false
    } finally {
      sessionChecked = null
    }
  })()
  return sessionChecked
}

router.beforeEach(async to => {
  if (to.meta.public) {
    // 已登录用户再访问 /login 直接回聊天页
    if (to.name === 'login' && getAccessToken() && await verifySession()) {
      return { name: 'chat' }
    }
    return true
  }
  if (!await verifySession()) {
    return { name: 'login', query: { redirect: to.fullPath } }
  }
  return true
})

export default router
