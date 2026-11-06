/**
 * lib/idb-crypto.ts
 *
 * 本机缓存加密（AES-GCM，WebCrypto）：
 *  - 密钥来自服务端按用户签发的 CacheKey（登录/注册/刷新响应下发），存 sessionStorage
 *    ——关标签页即清、不落盘明文；新标签页由 refresh 响应补上。
 *  - IndexedDB 里的聊天记录/媒体以密文落盘：裸提取 IDB 文件只能拿到密文与
 *    会话 id 等结构性字段，读不到任何聊天内容/图片。
 *  - 大媒体分块（8MB）加密，块 IV = 12B 随机根 IV + 4B 块序号（GCM 16B IV），
 *    避免同一密钥下不同块复用同一 IV 的两字掩码泄露；整文件仍一次随机 IV。
 *
 * 明确的能力边界（不吹牛）：攻击者若能同时拿到「浏览器完整会话」（含 sessionStorage
 * 或登录后的内存），密钥与密文同在，加密不提供额外保护 —— 它防的是「只拿到磁盘
 * IDB 文件 / 另一个浏览器 profile」的提取路径，以及跨账号互读。
 */

const KEY_STORAGE = 'wx_ck'
const CHUNK = 8 * 1024 * 1024
const IV_LEN = 12

let memKeyBase: string | null = null
let cryptoKey: CryptoKey | null = null

/** 服务端下发 CacheKey（登录/注册/刷新响应）时调用。 */
export function setCacheKey (base64: string): void {
  try { sessionStorage.setItem(KEY_STORAGE, base64) } catch { /* 隐私模式忽略 */ }
  if (memKeyBase !== base64) {
    memKeyBase = base64
    cryptoKey = null
  }
}

/** 换号/登出：清掉内存与 sessionStorage 的密钥（缓存数据由换号护栏另行整库清空）。 */
export function clearCacheKey (): void {
  try { sessionStorage.removeItem(KEY_STORAGE) } catch { /* 忽略 */ }
  memKeyBase = null
  cryptoKey = null
}

function keyBase64 (): string | null {
  if (memKeyBase) return memKeyBase
  try {
    const raw = sessionStorage.getItem(KEY_STORAGE)
    if (raw) memKeyBase = raw
    return raw
  } catch {
    return null
  }
}

async function getKey (): Promise<CryptoKey | null> {
  const b64 = keyBase64()
  if (!b64) return null
  if (!cryptoKey) {
    const bin = atob(b64)
    const raw = new Uint8Array(bin.length)
    for (let i = 0; i < bin.length; i++) raw[i] = bin.charCodeAt(i)
    cryptoKey = await crypto.subtle.importKey('raw', raw, 'AES-GCM', false, ['encrypt', 'decrypt'])
  }
  return cryptoKey
}

function toB64 (buf: ArrayBuffer | Uint8Array): string {
  const bytes = buf instanceof Uint8Array ? buf : new Uint8Array(buf)
  let s = ''
  for (let i = 0; i < bytes.length; i++) s += String.fromCharCode(bytes[i]!)
  return btoa(s)
}

function fromB64 (b64: string): Uint8Array {
  const bin = atob(b64)
  const out = new Uint8Array(bin.length)
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i)
  return out
}

/** 块 IV = 根 IV(12B) + 4B 大端块序号（GCM 16B IV，块间不复用 IV）。 */
function chunkIv (base: Uint8Array, index: number): Uint8Array {
  const iv = new Uint8Array(16)
  iv.set(base, 0)
  new DataView(iv.buffer).setUint32(12, index)
  return iv
}

/** 是否配置了加密（无密钥 = 不加密落盘，调用方按明文路径处理）。 */
export function hasCacheKey (): boolean {
  return !!keyBase64()
}

/**
 * 加密任意二进制：随机 12B 根 IV + 8MB 分块 GCM。
 * 返回 { iv: base64, data: ArrayBuffer }（data = 块密文依序拼接）。
 */
export async function encryptBytes (plain: ArrayBuffer): Promise<{ iv: string, data: ArrayBuffer }> {
  const key = await getKey()
  if (!key) throw new Error('cache-key-missing')
  const base = crypto.getRandomValues(new Uint8Array(IV_LEN))
  const parts: ArrayBuffer[] = []
  for (let off = 0, idx = 0; off < plain.byteLength || (off === 0 && plain.byteLength === 0); off += CHUNK, idx++) {
    const end = Math.min(plain.byteLength, off + CHUNK)
    const slice = plain.slice(off, end)
    parts.push(await crypto.subtle.encrypt({ name: 'AES-GCM', iv: chunkIv(base, idx) as BufferSource }, key, slice))
    if (end >= plain.byteLength) break
  }
  return { iv: toB64(base), data: join(parts) }
}

/** 解密 encryptBytes 的产物。密钥不匹配/数据损坏 → 抛错（调用方按缓存未命中处理）。 */
export async function decryptBytes (ivB64: string, data: ArrayBuffer): Promise<ArrayBuffer> {
  const key = await getKey()
  if (!key) throw new Error('cache-key-missing')
  const base = fromB64(ivB64)
  const total = data.byteLength
  // GCM 密文比明文多 16B tag → 反推块数
  const plainCap = CHUNK + 16
  const chunkCount = Math.max(1, Math.ceil(total / plainCap))
  const out = new Uint8Array(total - chunkCount * 16)
  let inOff = 0
  let outOff = 0
  for (let idx = 0; idx < chunkCount; idx++) {
    const remaining = total - inOff
    const plainLen = Math.min(CHUNK, remaining - 16)
    const slice = data.slice(inOff, inOff + plainLen + 16)
    const part = await crypto.subtle.decrypt({ name: 'AES-GCM', iv: chunkIv(base, idx) as BufferSource }, key, slice)
    new Uint8Array(out).set(new Uint8Array(part), outOff)
    outOff += part.byteLength
    inOff += plainLen + 16
  }
  return out.buffer.slice(0, outOff)
}

/** 加密 UTF-8 文本（聊天记录 JSON）。 */
export async function encryptText (text: string): Promise<{ iv: string, data: ArrayBuffer }> {
  return encryptBytes(new TextEncoder().encode(text) as unknown as ArrayBuffer)
}

/** 解密文本。 */
export async function decryptText (iv: string, data: ArrayBuffer): Promise<string> {
  const plain = await decryptBytes(iv, data)
  return new TextDecoder().decode(plain)
}

function join (parts: ArrayBuffer[]): ArrayBuffer {
  const total = parts.reduce((s, p) => s + p.byteLength, 0)
  const out = new Uint8Array(total)
  let off = 0
  for (const p of parts) {
    out.set(new Uint8Array(p), off)
    off += p.byteLength
  }
  return out.buffer
}
