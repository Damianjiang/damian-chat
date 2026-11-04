/**
 * imgCompress.ts
 *
 * 上传前的客户端图片压缩。老手机卡顿/流量的主要来源之一：相机原图 3-8MB，
 * 聊天气泡实际显示宽只有 240px、头像更小 —— 全尺寸上传、传输、解码（一张 12MP
 * 照片解码约 45MB 位图）纯属浪费。canvas 重绘到最长边 ≤ maxSide 再 JPEG 编码；
 * 压不出收益（截图/已很小的图）或任一环节失败时原样返回，绝不阻断上传。
 */

const MAX_SIDE_DEFAULT = 1600
const QUALITY_DEFAULT = 0.82

/** 安全位图类型（SVG 不在此列：矢量重编码无意义且不可信；GIF 会丢动画，单独跳过）。 */
export function isBitmapImage (file: File): boolean {
  return /^image\/(png|jpe?g|webp|bmp)$/i.test(file.type) || /\.(png|jpe?g|webp|bmp)$/i.test(file.name)
}

export async function compressImage (file: File, maxSide = MAX_SIDE_DEFAULT, quality = QUALITY_DEFAULT): Promise<File> {
  try {
    if (!isBitmapImage(file) || file.type === 'image/gif') return file
    const src = await decode(file)
    const w0 = 'width' in src ? src.width : 0
    const h0 = 'height' in src ? src.height : 0
    if (w0 <= 0 || h0 <= 0) return file
    const scale = Math.min(1, maxSide / Math.max(w0, h0))
    // 尺寸已在范围内且本身就是高压缩格式：不再重编码（白损画质）
    if (scale >= 1 && (file.type === 'image/jpeg' || file.type === 'image/webp')) return file
    const w = Math.max(1, Math.round(w0 * scale))
    const h = Math.max(1, Math.round(h0 * scale))
    const canvas = document.createElement('canvas')
    canvas.width = w
    canvas.height = h
    const ctx = canvas.getContext('2d')
    if (!ctx) return file
    // JPEG 无透明通道：透明区域铺白底，避免变黑
    ctx.fillStyle = '#ffffff'
    ctx.fillRect(0, 0, w, h)
    ctx.drawImage(src, 0, 0, w, h)
    const blob = await new Promise<Blob | null>(res => canvas.toBlob(res, 'image/jpeg', quality))
    if (!blob || blob.size >= file.size) return file // 没压小（文字截图等）→ 用原图
    const name = file.name.replace(/\.[^.]+$/, '') + '.jpg'
    return new File([blob], name, { type: 'image/jpeg', lastModified: Date.now() })
  } catch {
    return file // 解码失败（HEIC 等 WebView 不支持的格式）→ 原样上传，行为与从前一致
  }
}

async function decode (file: File): Promise<ImageBitmap | HTMLImageElement> {
  if (typeof createImageBitmap === 'function') {
    try {
      return await createImageBitmap(file)
    } catch { /* 某些 WebView 对个别格式失败 → 走 <img> 兜底 */ }
  }
  return new Promise((resolve, reject) => {
    const url = URL.createObjectURL(file)
    const img = new Image()
    img.onload = () => {
      URL.revokeObjectURL(url)
      resolve(img)
    }
    img.onerror = err => {
      URL.revokeObjectURL(url)
      reject(err)
    }
    img.src = url
  })
}
