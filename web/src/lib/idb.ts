/**
 * lib/idb.ts
 *
 * IndexedDB 本地缓存层：服务端只存最近 7 天聊天记录（与文件保留期对齐），更早/离线的「接收聊天记录」与
 * 「图片/视频/语音」媒体全部落到浏览器缓存（IndexedDB），换机/清缓存可控。
 *
 * 两个 object store：
 *  - messages  keyPath=id，索引 byConv(conversationId)：按会话存消息（聊天记录）
 *  - media     keyPath=fileId：按文件 id 存 Blob（图片/视频/语音，去重 + 按 at 淘汰）
 *
 * 安全：
 *  - IDB 不存 token；媒体仅以服务端 fileId 关联，无路径穿越面；渲染端走受控 Blob URL，无 XSS 面。
 *  - ★ 内容全程 AES-GCM 加密落盘（lib/idb-crypto.ts，密钥=服务端按用户签发的 CacheKey，
 *    存 sessionStorage）：裸提取 IndexedDB 文件只能拿到密文与会话 id 等结构字段，
 *    读不到聊天内容/图片。缓存明文从不落盘 —— 无密钥时写入直接跳过（按缓存未命中处理）。
 *  - DB v2：v1 是明文缓存，升级时整库清空重建（缓存可重建，不做双格式兼容）。
 * 浏览器隐私模式 / IDB 不可用时所有调用降级为空操作，不阻塞主流程。
 */

import type { Message } from '@/types'
import { decryptBytes, decryptText, encryptBytes, encryptText, hasCacheKey } from '@/lib/idb-crypto'

const DB_NAME = 'wx_cache'
const DB_VERSION = 2
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
      req.onupgradeneeded = ev => {
        const db = req.result
        // v1 → v2：明文缓存整库清空（不做明文/密文双格式兼容，缓存可从服务端重建）
        const oldVersion = (ev as IDBVersionChangeEvent).oldVersion
        if (oldVersion < 2) {
          if (db.objectStoreNames.contains('messages')) db.deleteObjectStore('messages')
          if (db.objectStoreNames.contains('media')) db.deleteObjectStore('media')
        }
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

/* ============ 消息（聊天记录，AES-GCM 加密落盘） ============ */

/** 密文记录形态（明文只存在于加密前的内存里）。 */
interface EncMessage {
  id: string
  conversationId: string
  iv: string
  data: ArrayBuffer
}

/** 替换某会话的缓存消息：先删旧切片，再批量写入（单事务原子完成）。全部密文落盘。 */
export async function idbPutMessages (conversationId: string, msgs: Message[]): Promise<void> {
  if (!supported() || !hasCacheKey()) {
    return   // 无密钥绝不落明文：按缓存未命中处理，应用侧会从服务端重拉
  }
  try {
    // 事务外逐条加密（crypto 是异步的，不能进 IDB 事务内 await）
    const enc: EncMessage[] = []
    for (const m of msgs) {
      const { iv, data } = await encryptText(JSON.stringify(m))
      enc.push({ id: m.id, conversationId, iv, data })
    }
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
      for (const r of enc) {
        store.put(r)
      }
      t.oncomplete = () => resolve()
      t.onerror = () => reject(t.error)
    })
  } catch { /* 降级忽略 */ }
}

/** 读某会话的缓存消息（升序）。解密失败的条目跳过（按缓存未命中处理）。 */
export async function idbGetMessages (conversationId: string): Promise<Message[]> {
  if (!supported() || !hasCacheKey()) {
    return []
  }
  try {
    const db = await openDB()
    const rows = await new Promise<EncMessage[]>((resolve, reject) => {
      const store = db.transaction('messages', 'readonly').objectStore('messages')
      const byConv = store.index('byConv')
      const req = byConv.getAll(conversationId)
      req.onsuccess = () => resolve(req.result as EncMessage[])
      req.onerror = () => reject(req.error)
    })
    const out: Message[] = []
    for (const r of rows) {
      try {
        out.push(JSON.parse(await decryptText(r.iv, r.data)) as Message)
      } catch { /* 解密失败：该条按不存在处理 */ }
    }
    return out
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

/* ============ 媒体（图片/视频/语音 blob，AES-GCM 8MB 分块加密落盘） ============ */

export interface MediaRecord {
  fileId: string
  blob: Blob
  ext?: string
  contentType?: string
  at: number
}

/** 磁盘记录形态（密文 + 明文元数据：原始大小/时间戳用于淘汰，不泄露内容）。 */
interface EncMedia {
  fileId: string
  at: number
  ext?: string
  contentType?: string
  size: number
  iv: string
  data: ArrayBuffer
}

/** 写入/更新一个媒体 blob；超量淘汰最旧（按 at）。 */
export async function idbPutMedia (fileId: string, blob: Blob, ext?: string, contentType?: string): Promise<void> {
  if (!supported() || !hasCacheKey()) {
    return   // 无密钥绝不落明文
  }
  try {
    const plain = await blob.arrayBuffer()
    const { iv, data } = await encryptBytes(plain)
    const rec: EncMedia = { fileId, at: Date.now(), ext, contentType, size: blob.size, iv, data }
    const db = await openDB()
    await new Promise<void>((resolve, reject) => {
      const t = db.transaction('media', 'readwrite')
      const store = t.objectStore('media')
      store.put(rec)
      // 淘汰：超条数或超字节时删最旧
      const countReq = store.count()
      countReq.onsuccess = async () => {
        try {
          let toRemove = 0
          let bytes = 0
          const cur: EncMedia[] = await new Promise((res, rej) => {
            const all = store.getAll()
            all.onsuccess = () => res(all.result as EncMedia[])
            all.onerror = () => rej(all.error)
          })
          // cur 是同事务 put 之后取的 getAll，已包含刚写入的记录：
          // 计数/字节都不能再 +1/+size（双计会每次插入多淘汰 1 条）
          bytes = cur.reduce((s, r) => s + (r.size ?? 0), 0)
          if (countReq.result > MEDIA_MAX_COUNT) {
            toRemove = countReq.result - MEDIA_MAX_COUNT
          }
          if (bytes > MEDIA_MAX_BYTES) {
            const sorted = cur.slice().sort((a, b) => a.at - b.at)
            for (const r of sorted) {
              if (bytes <= MEDIA_MAX_BYTES) {
                break
              }
              bytes -= r.size ?? 0
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
  if (!supported() || !hasCacheKey()) {
    return null
  }
  try {
    const db = await openDB()
    const rec = await new Promise<EncMedia | null>((resolve, reject) => {
      const store = db.transaction('media', 'readonly').objectStore('media')
      const req = store.get(fileId)
      req.onsuccess = () => resolve((req.result as EncMedia) ?? null)
      req.onerror = () => reject(req.error)
    })
    if (!rec) return null
    const plain = await decryptBytes(rec.iv, rec.data)
    return {
      fileId: rec.fileId,
      blob: new Blob([plain], { type: rec.contentType || 'application/octet-stream' }),
      ext: rec.ext,
      contentType: rec.contentType,
      at: rec.at,
    }
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

/** 估算 IDB 总占用。三级兜底，保证 iPhone/iPad（旧版 Safari 没有 storage.estimate）也能显示 MB：
 *  1) navigator.storage.estimate（Chrome/Android、Safari 17+）
 *  2) webkitTemporaryStorage.queryUsageAndQuota（旧版 Safari/iOS 一直有）
 *  3) 遍历 messages/media 两个 store 手动求和（消息按 UTF-16 每字符 2 字节估，媒体按 blob.size） */
export async function idbEstimateBytes (): Promise<number> {
  // 注意：不能用 navigator.storage.estimate / webkitTemporaryStorage ——
  // 那两个统计的是整个源站（含 Service Worker 的静态资源缓存），而「清除缓存」
  // 只清 IndexedDB，用户会看到"清完还有几 MB 清不掉"的假象。这里始终按
  // messages/media 两个 store 实际求和，与可清除的范围严格一致。
  // 旧版 Safari / iOS：webkit 前缀的配额 API 已弃用，一并走手动求和。
  if (!supported()) return 0
  try {
    const db = await openDB()
    let bytes = 0
    await new Promise<void>((resolve, reject) => {
      const t = db.transaction(['messages', 'media'], 'readonly')
      const walk = (storeName: 'messages' | 'media', sizeOf: (v: unknown) => number) => {
        const req = t.objectStore(storeName).openCursor()
        req.onsuccess = () => {
          const c = req.result
          if (!c) return
          bytes += sizeOf(c.value)
          c.continue()
        }
      }
      // v2 记录是密文（data: ArrayBuffer + 结构字段），按密文字节计 —— 与磁盘占用一致
      walk('messages', (v: any) => (v?.data?.byteLength ?? 0) + ((v?.id?.length ?? 0) + (v?.conversationId?.length ?? 0) + (v?.iv?.length ?? 0)) * 2)
      const mediaSize = (v: any) => (v?.data?.byteLength ?? 0)
        + ((v?.fileId?.length ?? 0) + (v?.ext?.length ?? 0) + (v?.contentType?.length ?? 0) + (v?.iv?.length ?? 0)) * 2 + 8
      walk('media', mediaSize)
      t.oncomplete = () => resolve()
      t.onerror = () => reject(t.error)
      t.onabort = () => reject(t.error ?? new Error('idb transaction aborted'))
    })
    return bytes
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
