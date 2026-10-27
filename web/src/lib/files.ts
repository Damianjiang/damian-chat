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
  return url
}

/** 清理媒体缓存（设清缓存勾选「图片视频语音」时调用）。 */
export async function clearMediaCache (): Promise<void> {
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
const MAX_HOT = 300 // 热层 objectURL 上限：长会话无界增长时按插入序淘汰并 revoke

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

  const task = (async () => {
    try {
      // timeout:0 —— 大文件（视频/文档，1GB 内）下载可能远超 20s，
      // 旧的全局 20s 超时会把"1GB 以内"的正常文件误判成失败（"上传/下载失败"）。
      const res = await api.get(`/files/${id}`, { responseType: 'blob', timeout: 0 })
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
      pending.delete(id)
    }
  })()

  pending.set(id, task)
  return task
}

/** 热层 objectURL 超上限时按插入序淘汰并 revoke，防长会话无界内存增长。 */
function evictHotIfNeeded () {
  if (blobCache.size <= MAX_HOT) {
    return
  }
  const overflow = blobCache.size - MAX_HOT
  const keys = Array.from(blobCache.keys()).slice(0, overflow)
  for (const k of keys) {
    try {
      URL.revokeObjectURL(blobCache.get(k)!)
    } catch { /* 已 revoke */ }
    blobCache.delete(k)
  }
}

/** 下载附件：命中本地 blob 缓存直接用，未命中走带鉴权下载（不走裸链接/票据）。
 *  文档类（大文件）只在有热层/IDB 时复用，不额外写入 IDB，避免白占磁盘。 */
export async function downloadFile (id: string, fileName?: string): Promise<void> {
  let blob: Blob | null = null
  const hot = blobCache.get(id)
  if (hot) {
    try {
      blob = await fetch(hot).then(r => r.blob()).catch(() => null)
    } catch {
      blob = null
    }
  }
  if (!blob) {
    const rec = await idbGetMedia(id).catch(() => null)
    blob = rec?.blob ?? null
  }
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
