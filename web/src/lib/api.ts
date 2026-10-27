/**
 * lib/api.ts
 *
 * Axios instance with JWT auth + refresh interceptor.
 *
 * 令牌存储（防 XSS 窃取长效凭据）：
 *  - access token（2h 短期）→ localStorage（JS 可读，但短期、可被服务端吊销/轮转）。
 *  - refresh token（7 天长效，拿到即全权接管账号）→ 服务端下发的 httpOnly + SameSite=Strict
 *    cookie（JS 读不到；即使某处 XSS 命中，也读不走 refresh，XSS 无法持久劫持会话）。
 *  刷新/登出由浏览器自动携带该 cookie，前端不再把 refresh 落 localStorage。
 */
import axios from 'axios'

const TOKEN_KEY = 'wx_access_token'
/** 历史遗留 key：refresh 曾存这里。现在不再写入；clearTokens 顺带清理，杜绝旧数据残留。 */
const LEGACY_REFRESH_KEY = 'wx_refresh_token'

export function getAccessToken (): string {
  return localStorage.getItem(TOKEN_KEY) ?? ''
}

/**
 * 「是否存在可刷新的会话」信号。refresh 本体现在在 httpOnly cookie（JS 读不到），
 * 浏览器刷新/登出时自动携带；这里用 access token 是否存在作为等价判断。
 * （刷新请求本身不再需要把 refresh 放 body——cookie 已带。）
 */
export function getRefreshToken (): string {
  return getAccessToken()
}

export function saveTokens (access: string, _refresh?: string) {
  // refresh 不再落 localStorage：由服务端 Set-Cookie（httpOnly）管理。
  // 仅写 access；顺带清掉历史遗留的 refresh localStorage（旧版本残留，可能泄露长效凭据）。
  localStorage.setItem(TOKEN_KEY, access)
  localStorage.removeItem(LEGACY_REFRESH_KEY)
}

export function clearTokens () {
  localStorage.removeItem(TOKEN_KEY)
  localStorage.removeItem(LEGACY_REFRESH_KEY)
  // httpOnly cookie 清不掉（JS 不可写），由后端 /auth/logout 的 Set-Cookie 失效负责；
  // 登出端点被调用时服务端 ClearRefreshCookie 会下发过期 cookie。
}

export const api = axios.create({
  baseURL: '/api',
  timeout: 20_000,
})

api.interceptors.request.use(config => {
  const token = getAccessToken()
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

let refreshing: Promise<string> | null = null

api.interceptors.response.use(
  res => res,
  async error => {
    const original = error.config
    if (error.response?.status === 401 && original && !original._retry && getRefreshToken()) {
      original._retry = true
      try {
        // refresh 走 httpOnly cookie（同源自动携带，无需放 body）。
        // 必须带 timeout：默认 0（无限等）会让挂起的刷新请求把共享 promise 永久卡死，
        // 之后所有 401 的恢复链路全部停在 await refreshing 上（整个 API 层"半死"）。
        refreshing = refreshing ?? axios.post('/api/auth/refresh', undefined, { timeout: 20_000 })
          .then(res => {
            saveTokens(res.data.accessToken, res.data.refreshToken ?? res.data.accessToken)
            return res.data.accessToken as string
          })
          // 失败也要释放共享刷新 promise，finally 统一兜底
          .finally(() => {
            refreshing = null
          })
        const token = await refreshing
        original.headers.Authorization = `Bearer ${token}`
        return api(original)
      } catch (refreshError: any) {
        // 仅在刷新令牌确实被服务端拒绝（401/403）时才本地登出；
        // 网络抖动/超时不该把用户登出 —— 保留 access，下次请求再重试刷新
        if (refreshError?.response && [401, 403].includes(refreshError.response.status)) {
          clearTokens()
          if (window.location.pathname !== '/login') {
            window.location.href = '/login'
          }
        }
      }
    }
    throw error
  },
)
