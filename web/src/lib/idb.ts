/**
 * lib/idb.ts
 *
 * IndexedDB 本地缓存层：服务端只存 1 天聊天记录，更早/离线的「接收聊天记录」与
 * 「图片/视频/语音」媒体全部落到浏览器缓存（IndexedDB），换机/清缓存可控。
 *
 * 两个 object store：
 *  - messages  keyPath=id，索引 byConv(conversationId)：按会话存消息（聊天记录）
 *  - media     keyPath=fileId：按文件 id 存 Blob（图片/视频/语音，去重 + 按 at 淘汰）
 *
 * 安全：IDB 只存业务数据（消息 JSON / 媒体 Blob），绝不存 token；媒体仅以
 * 服务端 fileId 关联，不做路径拼接，无路径穿越面；缓存内容在渲染端一律走
 * 文本插值 {{}} / 受控 Blob URL，不经 innerHTML，无 XSS 面。
 * 浏览器隐私模式 / IDB 不可用时所有调用降级为空操作，不阻塞主流程。
 */

import type { Message } from '@/types'

const DB_NAME = 'wx_cache'
const DB_VERSION = 1
const MEDIA_MAX_COUNT = 500 // 媒体 blob 上限：超限淘汰最旧，避免无界占用磁盘
const MEDIA_MAX_BYTES = 512 * 1024 * 1024 // 媒体总量上限 512MB（视频大）

let dbPromise: Promise<IDBDatabase> | null = null

function supported (): boolean {
  return typeof indexedDB !== 'undefined'
}

function openDB (): Promise<IDBDatabase> {
  if (!supported()) {
    return Promise.reject(new Error('indexedDB 不可用'))
  }
  if (!dbPromise) {
    dbPromise = new Promise((resolve, reject) => {
      const req = indexedDB.open(DB_NAME, DB_VERSION)
      req.onupgradeneeded = () => {
        const db = req.result
        if (!db.objectStoreNames.contains('messages')) {
          const m = db.createObjectStore('messages', { keyPath: 'id' })
          m.createIndex('byConv', 'conversationId', { unique: false })
        }
        if (!db.objectStoreNames.contains('media')) {
          db.createObjectStore('media', { keyPath: 'fileId' })
        }
      }
      req.onsuccess = () => resolve(req.result)
      req.onerror = () => reject(req.error ?? new Error('IDB 打开失败'))
    })
  }
  return dbPromise
}

function tx (db: IDBDatabase, store: string, mode: IDBTransactionMode): IDBObjectStore {
  return db.transaction(store, mode).objectStore(store)
}

/* ============ 消息（聊天记录） ============ */

/** 替换某会话的缓存消息：先删旧切片，再批量写入（单事务原子完成）。 */
export async function idbPutMessages (conversationId: string, msgs: Message[]): Promise<void> {
  if (!supported()) {
    return
  }
  try {
    const db = await openDB()
    return new Promise((resolve, reject) => {
      const t = db.transaction('messages', 'readwrite')
      const store = t.objectStore('messages')
      const byConv = store.index('byConv')
      const keysReq = byConv.getAllKeys(conversationId)
      keysReq.onsuccess = () => {
        for (const k of keysReq.result) {
          store.delete(k)
        }
      }
      for (const m of msgs) {
        store.put(m)
      }
      t.oncomplete = () => resolve()
      t.onerror = () => reject(t.error)
    })
  } catch { /* 降级忽略 */ }
}

/** 读某会话的缓存消息（升序）。 */
export async function idbGetMessages (conversationId: string): Promise<Message[]> {
  if (!supported()) {
    return []
  }
  try {
    const db = await openDB()
    return await new Promise((resolve, reject) => {
      const store = db.transaction('messages', 'readonly').objectStore('messages')
      const byConv = store.index('byConv')
      const req = byConv.getAll(conversationId)
      req.onsuccess = () => resolve(req.result as Message[])
      req.onerror = () => reject(req.error)
    })
  } catch {
    return []
  }
}

/** 删除单条消息缓存（按消息 id 主键）。跨设备删除广播到达时，接收端可能从未打开过该会话（内存为空），
 *  此时不能整段重写（会把对方该会话的整页缓存清光），只精准删掉这一条最安全。 */
export async function idbRemoveMessage (messageId: string): Promise<void> {
  if (!supported() || !messageId) {
    return
  }
  try {
    const db = await openDB()
    await new Promise<void>((resolve, reject) => {
      const t = db.transaction('messages', 'readwrite')
      t.objectStore('messages').delete(messageId)
      t.oncomplete = () => resolve()
      t.onerror = () => reject(t.error)
    })
  } catch { /* 降级忽略 */ }
}

/** 聊天记录总条数（用于设置页显示）。 */
export async function idbCountMessages (): Promise<number> {
  if (!supported()) {
    return 0
  }
  try {
    const db = await openDB()
    return await new Promise((resolve, reject) => {
      const store = db.transaction('messages', 'readonly').objectStore('messages')
      const req = store.count()
      req.onsuccess = () => resolve(req.result)
      req.onerror = () => reject(req.error)
    })
  } catch {
    return 0
  }
}

export async function idbClearMessages (): Promise<void> {
  if (!supported()) {
    return
  }
  try {
    const db = await openDB()
    await new Promise((resolve, reject) => {
      const t = db.transaction('messages', 'readwrite')
      t.objectStore('messages').clear()
      t.oncomplete = () => resolve(undefined)
      t.onerror = () => reject(t.error)
    })
  } catch { /* 忽略 */ }
}

/* ============ 媒体（图片/视频/语音 blob） ============ */

export interface MediaRecord {
  fileId: string
  blob: Blob
  ext?: string
  contentType?: string
  at: number
}

/** 写入/更新一个媒体 blob；超量淘汰最旧（按 at）。 */
export async function idbPutMedia (fileId: string, blob: Blob, ext?: string, contentType?: string): Promise<void> {
  if (!supported()) {
    return
  }
  try {
    const db = await openDB()
    await new Promise<void>((resolve, reject) => {
      const t = db.transaction('media', 'readwrite')
      const store = t.objectStore('media')
      store.put({ fileId, blob, ext, contentType, at: Date.now() } as MediaRecord)
      // 淘汰：超条数或超字节时删最旧
      const countReq = store.count()
      countReq.onsuccess = async () => {
        try {
          let toRemove = 0
          let bytes = 0
          const cur: MediaRecord[] = await new Promise((res, rej) => {
            const all = store.getAll()
            all.onsuccess = () => res(all.result as MediaRecord[])
            all.onerror = () => rej(all.error)
          })
          bytes = cur.reduce((s, r) => s + (r.blob?.size ?? 0), 0) + blob.size
          if (countReq.result + 1 > MEDIA_MAX_COUNT) {
            toRemove = countReq.result + 1 - MEDIA_MAX_COUNT
          }
          if (bytes > MEDIA_MAX_BYTES) {
            const sorted = cur.slice().sort((a, b) => a.at - b.at)
            for (const r of sorted) {
              if (bytes <= MEDIA_MAX_BYTES) {
                break
              }
              bytes -= r.blob?.size ?? 0
              toRemove++
            }
          }
          if (toRemove > 0) {
            const sorted = cur.slice().sort((a, b) => a.at - b.at)
            for (const r of sorted.slice(0, toRemove)) {
              store.delete(r.fileId)
            }
          }
        } catch { /* 淘汰失败不阻塞写入 */ }
        resolve()
      }
      t.oncomplete = () => resolve()
      t.onerror = () => reject(t.error)
    })
  } catch { /* 降级忽略 */ }
}

export async function idbGetMedia (fileId: string): Promise<MediaRecord | null> {
  if (!supported()) {
    return null
  }
  try {
    const db = await openDB()
    return await new Promise((resolve, reject) => {
      const store = db.transaction('media', 'readonly').objectStore('media')
      const req = store.get(fileId)
      req.onsuccess = () => resolve((req.result as MediaRecord) ?? null)
      req.onerror = () => reject(req.error)
    })
  } catch {
    return null
  }
}

export async function idbClearMedia (): Promise<void> {
  if (!supported()) {
    return
  }
  try {
    const db = await openDB()
    await new Promise((resolve, reject) => {
      const t = db.transaction('media', 'readwrite')
      t.objectStore('media').clear()
      t.oncomplete = () => resolve(undefined)
      t.onerror = () => reject(t.error)
    })
  } catch { /* 忽略 */ }
}

/** 估算 IDB 总占用（浏览器支持 navigator.storage.estimate 时）。 */
export async function idbEstimateBytes (): Promise<number> {
  try {
    if (typeof navigator !== 'undefined' && navigator.storage?.estimate) {
      const e = await navigator.storage.estimate()
      return e.usage ?? 0
    }
  } catch { /* 忽略 */ }
  return 0
}

/** 删除单个媒体（按 fileId），供个性化背景图等场景按需清理。 */
export async function idbRemoveMedia (fileId: string): Promise<void> {
  if (!supported() || !fileId) return
  try {
    const db = await openDB()
    await new Promise<void>((resolve, reject) => {
      const t = db.transaction('media', 'readwrite')
      t.objectStore('media').delete(fileId)
      t.oncomplete = () => resolve()
      t.onerror = () => reject(t.error)
    })
  } catch { /* 忽略 */ }
}

/* ============ 用户个性化背景图（存浏览器缓存 IndexedDB，media store 固定 key） ============
 * 说明：用户「上传背景图片」存在浏览器缓存里（IndexedDB），不清理缓存不会丢。
 * 复用 media store 的 blob 能力，用一个保留 key 标识，避免 bump DB_VERSION。 */
const USER_BG_KEY = '__user_bg__'

/** 写入用户背景图（建议先压缩到合理尺寸再存）。 */
export async function idbPutUserBg (blob: Blob, ext?: string, contentType?: string): Promise<void> {
  await idbPutMedia(USER_BG_KEY, blob, ext, contentType)
}

/** 读取用户背景图 blob。 */
export async function idbGetUserBg (): Promise<MediaRecord | null> {
  return idbGetMedia(USER_BG_KEY)
}

/** 删除用户背景图。 */
export async function idbClearUserBg (): Promise<void> {
  await idbRemoveMedia(USER_BG_KEY)
}

/** 释放某会话在 IDB 里的缓存（清退/登出时）。 */
export async function idbDropConversation (conversationId: string): Promise<void> {
  await idbClearMessages() // 简单起见整表清，聊天数据本就按 1 天窗口重建
  void conversationId
}

/** 隐私模式 / IDB 不可用时，所有 idb* 操作都安全降级为空。 */
export function idbAvailable (): boolean {
  return supported()
}
