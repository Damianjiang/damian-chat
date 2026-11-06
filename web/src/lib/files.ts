import { api } from '@/lib/api'
import { idbClearMedia, idbGetMedia, idbPutMedia } from '@/lib/idb'

/**
 * 媒体本地缓存（IndexedDB）：
 *  ① 签名 URL 命中后，并发把 blob 拉一份存进 IDB（按 fileId 去重）；
 *  ② 之后同文件再渲染/预览/下载直接取本地 blob → 离线可用 + 不再反复打服务端取票；
 *  ③ 设「清除缓存」勾掉媒体时，清掉 blob 缓存与已 revoke 的 objectURL。
 * 下载附件（file 型）不缓存 blob：大文件按需下载，避免白占磁盘。
 * 安全：IDB 只存 Blob，不存 token；渲染走 ObjectURL，Content-Type 仍由服务端决定（已魔数校验），
 *      不拼用户路径，无路径穿越面；本地 blob 只影响当前用户自己的渲染。
 */
const blobCache = new Map<string, string>() // fileId → objectURL（内存热层）
/** 缓存代际：登出/清缓存时 +1，在途下载完成后发现代际变化即丢弃，防跨账号缓存回写 */
let cacheEpoch = 0

/** 从 IDB 取缓存 blob → objectURL；命中则返回，未命中返回空。 */
async function cachedMediaUrl (fileId: string): Promise<string> {
  const hot = blobCache.get(fileId)
  if (hot) {
    return hot
  }
  const rec = await idbGetMedia(fileId).catch(() => null)
  if (!rec || !rec.blob || rec.blob.size === 0) {
    return ''
  }
  const url = URL.createObjectURL(rec.blob)
  blobCache.set(fileId, url)
  // IDB 路径同样要驱逐热层：此前只有网络路径驱逐，长会话滚大聊天时
  // IDB 命中的每个 blob 都常驻内存（objectURL 钉住 blob，登出才释放）
  evictHotIfNeeded()
  return url
}

/** 清理媒体缓存（设清缓存勾选「图片视频语音」时调用）。 */
export async function clearMediaCache (): Promise<void> {
  cacheEpoch++   // 在途下载完成后发现代际变化即丢弃，不再回写
  // 先 revoke 热层 objectURL，防泄漏
  for (const url of blobCache.values()) {
    try {
      URL.revokeObjectURL(url)
    } catch { /* 已 revoke */ }
  }
  blobCache.clear()
  await idbClearMedia()
}

/** 当前媒体缓存条数（设置页大小显示用）。 */
export function mediaCacheCount (): number {
  return blobCache.size
}

/**
 * 文件访问地址：本地 blob 缓存优先（IndexedDB），未命中再向服务端取 blob。
 *
 * 之前用「5 分钟签名票据」给 <img>/<a> 造可直连 URL，有两个问题：
 *  ① <img>/<a download> 不带 Authorization 头 → 依赖票据，裸 id 外泄即可枚举下载；
 *  ② 票据 URL 仍可被转发/分享泄露。
 * 现在改为「带鉴权的 blob 下载 + 本地 objectURL + IndexedDB 持久化」：
 *  ① blob 下载走 JWT + 服务端归属校验（CanReadFile），无裸直链可枚举；
 *  ② 渲染用 objectURL（同源、不经票据字符串），票据串不再是外泄面；
 *  ③ 命中 IDB 直接离线可用；同文件只下载一次，长会话/反复渲染不再打服务端。
 */
const pending = new Map<string, Promise<string>>()
/**
 * 热层 objectURL 上限。
 *
 * 取值远大于 IDB 的 MEDIA_MAX_COUNT(500) —— 正常情况下**永远不该触发淘汰**。
 * 曾经的 300 是个严重 bug：第二次打开页面时 IDB 全量命中，会话列表 + 消息里的
 * 头像/图片会在几百毫秒内并发创建几百个 objectURL，淘汰一启动就把**此刻正挂在
 * <img src> 上**的那些 revoke 掉 → 整屏图片变成裂图（用户反馈的"第二次打开全破损"）。
 * 现在把上限抬高，并按下面的方式安全淘汰。
 */
const MAX_HOT = 2000

/**
 * 下载并发闸门。
 *
 * 进会话列表/打开聊天时，几十个头像与图片会在同一帧里全部调用 fileUrl。
 * 不加限制就是几十个请求一起冲，每个都要过服务端 JWT 校验 + 归属校验 + 磁盘读取，
 * 互相挤占之后「每一个都很慢」。限到 6 个（与浏览器同源并发上限同量级）后，
 * 排队反而让整体完成时间明显缩短，首屏头像也能更早陆续出现。
 */
const MAX_PARALLEL_FETCH = 6
let activeFetch = 0
const fetchWaiters: Array<() => void> = []

function acquireFetchSlot (): Promise<void> {
  if (activeFetch < MAX_PARALLEL_FETCH) {
    activeFetch++
    return Promise.resolve()
  }
  return new Promise<void>(resolve => fetchWaiters.push(resolve)).then(() => { activeFetch++ })
}

function releaseFetchSlot (): void {
  activeFetch--
  const next = fetchWaiters.shift()
  if (next) next()
}

/** 取可访问地址（objectURL）；未命中/失败返回空串，调用方按无图处理。 */
export async function fileUrl (id?: string | null): Promise<string> {
  if (!id) {
    return ''
  }

  // ① 热层命中
  const hot = blobCache.get(id)
  if (hot) {
    return hot
  }

  // ② IDB 命中（离线可用，不再请求服务端）
  const local = await cachedMediaUrl(id)
  if (local) {
    return local
  }

  // ③ 未命中 → 带鉴权下载 blob；并发同 id 合并为一次请求
  const inflight = pending.get(id)
  if (inflight) {
    return inflight
  }

  const myEpoch = cacheEpoch
  const task = (async () => {
    // 并发闸门：只有真正要发网络请求的才占位，命中 IDB/热层的不受影响
    await acquireFetchSlot()
    try {
      // timeout:0 —— 大文件（视频/文档，1GB 内）下载可能远超 20s，
      // 旧的全局 20s 超时会把"1GB 以内"的正常文件误判成失败（"上传/下载失败"）。
      const res = await api.get(`/files/${id}`, { responseType: 'blob', timeout: 0 })
      // 下载期间登出/清缓存（cacheEpoch 变化）→ 丢弃结果，不回写缓存（防跨账号媒体残留）
      if (myEpoch !== cacheEpoch) return ''
      const blob = res.data as Blob
      if (!blob || blob.size === 0) {
        return ''
      }
      const url = URL.createObjectURL(blob)
      blobCache.set(id, url)
      evictHotIfNeeded()
      void idbPutMedia(id, blob, undefined, blob.type).catch(() => {})
      return url
    } catch {
      // 失效 fileId / 网络失败：返回空，调用方按无媒体处理，不再反复打 404
      return ''
    } finally {
      releaseFetchSlot()
      pending.delete(id)
    }
  })()

  pending.set(id, task)
  return task
}

/**
 * 热层超上限时按插入序淘汰。
 *
 * ★ 只从 Map 移出，**绝不 revokeObjectURL**。
 *   调用方拿到 URL 后立刻塞进 <img src> / CSS url() / <video src>，这里根本无从得知
 *   它此刻是否还在渲染。一旦 revoke，那个元素当场变裂图，而且不会再恢复 —— 用户看到
 *   的就是"头像、图片、图标全破损"。
 *   真正需要释放的时机只有两个：登出 / 用户主动清缓存（clearMediaCache），
 *   以及页面销毁时浏览器自动回收。宁可多占一点内存，也不能让界面崩成一片裂图。
 */
function evictHotIfNeeded () {
  if (blobCache.size <= MAX_HOT) {
    return
  }
  const overflow = blobCache.size - MAX_HOT
  const keys = Array.from(blobCache.keys()).slice(0, overflow)
  for (const k of keys) {
    blobCache.delete(k)
  }
}

/** 下载附件：优先取本地 blob，未命中走带鉴权下载（不走裸链接/票据）。
 *  文档类（大文件）只在 IDB 有缓存时复用，不额外写入 IDB，避免白占磁盘。
 *
 *  ★ 不要用 `fetch(objectURL, { cache: 'only-if-cached' })` 回读 ——
 *    `only-if-cached` 是 HTTP 缓存指令，对 `blob:` URL 没有定义语义，
 *    实测拿到的可能是空/截断内容，用户下到的文件打不开。
 *    直接从 IndexedDB 取原始 Blob（存进去的就是服务端返回的字节），最可靠。 */
export async function downloadFile (id: string, fileName?: string): Promise<void> {
  let blob: Blob | null = null
  const rec = await idbGetMedia(id).catch(() => null)
  blob = rec?.blob ?? null
  if (!blob) {
    const res = await api.get(`/files/${id}`, { responseType: 'blob', timeout: 0 })
    blob = res.data as Blob
  }
  const blobUrl = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = blobUrl
  a.download = fileName || 'download'
  document.body.append(a)
  a.click()
  a.remove()
  setTimeout(() => URL.revokeObjectURL(blobUrl), 1000)
}
