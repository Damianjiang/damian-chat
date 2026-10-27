<template>
  <!-- 登录页默认「蓝色动波纹」背景（暗色自动浅蓝）。后台可设为纯色/图片，此组件只在 ripple 模式渲染。 -->
  <canvas ref="cv" class="wx-ripple-canvas" aria-hidden="true" />
</template>

<script setup lang="ts">
  import { onBeforeUnmount, onMounted, ref } from 'vue'
  import { resolveDark } from '@/lib/theme'

  const props = defineProps<{ color?: string }>()
  const cv = ref<HTMLCanvasElement | null>(null)
  let raf = 0
  let dark = false
  let mqDark: MediaQueryList | null = null
  let resizeHandler: (() => void) | null = null
  let darkHandler: (() => void) | null = null
  let visHandler: (() => void) | null = null

  /** #RRGGBB → [r,g,b]；解析失败回退默认蓝 #2F6FED。 */
  function hexToRgb (h: string | undefined): [number, number, number] {
    const m = /^#?([0-9a-f]{6})$/i.exec((h ?? '').trim())
    if (!m || !m[1]) return [47, 111, 237]
    const v = parseInt(m[1], 16)
    return [(v >> 16) & 255, (v >> 8) & 255, v & 255]
  }

  function start () {
    const el = cv.value
    if (!el) return
    const reduce = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
    dark = resolveDark()
    mqDark = window.matchMedia?.('(prefers-color-scheme: dark)')
    const ctx = el.getContext('2d')
    if (!ctx || reduce) return // 减弱动态：静止背景（CSS 渐变兜底由页面 bg 承担）
    // 判空后捕获非 null 局部（嵌套闭包里 el/ctx 的收窄会丢失），供 size/paint 复用
    const canvas: HTMLCanvasElement = el
    const g2d: CanvasRenderingContext2D = ctx

    function size () {
      // ★ 性能：DPR 上限压到 1.25（4K 屏 3x 全分辨率逐帧绘多层正弦是主要卡顿源）；
      // 背景是模糊层，观感几乎无差别，GPU/CPU 负载大幅下降。
      const dpr = Math.min(1.25, window.devicePixelRatio || 1)
      canvas.width = Math.floor(window.innerWidth * dpr)
      canvas.height = Math.floor(window.innerHeight * dpr)
      canvas.style.width = window.innerWidth + 'px'
      canvas.style.height = window.innerHeight + 'px'
    }
    size()
    resizeHandler = size
    window.addEventListener('resize', resizeHandler)

    darkHandler = () => {
      dark = resolveDark()
    }
    mqDark?.addEventListener?.('change', darkHandler)

    let t = 0
    let last = performance.now()
    let running = true

    // ★ 性能：切后台（标签页隐藏）暂停 rAF，回来再跑，避免后台空转耗电卡顿
    visHandler = () => {
      if (document.hidden) {
        running = false
        if (raf) { cancelAnimationFrame(raf); raf = 0 }
      } else if (!running) {
        running = true
        last = performance.now()
        raf = requestAnimationFrame(loop)
      }
    }
    document.addEventListener('visibilitychange', visHandler)

    // ★ 性能：3 层（原 4 层），每层 ≤90 个采样点（原 220 → 采样量减半以上）
    const LAYERS = 3
    function paint (dt: number) {
      const w = canvas.width
      const h = canvas.height
      if (w === 0 || h === 0) return
      const [br, bg2, bb] = hexToRgb(props.color)
      // 暗色：波纹转「浅蓝」（向白混合提亮）；亮色：正常蓝
      const wave = dark
        ? [Math.round(br + (255 - br) * 0.62), Math.round(bg2 + (255 - bg2) * 0.5), Math.round(bb + (255 - bb) * 0.45)]
        : [br, bg2, bb]
      const top = dark
        ? `rgb(${Math.round(10 + br * 0.06)}, ${Math.round(14 + bg2 * 0.06)}, ${Math.round(26 + bb * 0.08)})`
        : `rgb(${Math.round(235 + (255 - br) * 0.2)}, ${Math.round(242 + (255 - bg2) * 0.2)}, 250)`

      const g = g2d.createLinearGradient(0, 0, 0, h)
      g.addColorStop(0, top)
      g.addColorStop(1, dark ? `rgb(${Math.round(br * 0.05)}, ${Math.round(bg2 * 0.05)}, ${Math.round(24 + bb * 0.05)})` : `rgb(${br}, ${bg2}, ${bb})`)
      g2d.fillStyle = g
      g2d.fillRect(0, 0, w, h)

      for (let i = 0; i < LAYERS; i++) {
        const baseY = h * (0.55 + i * 0.14)
        const amp = h * (0.032 + i * 0.013)
        const alpha = dark ? 0.10 + i * 0.055 : 0.10 + i * 0.045
        g2d.beginPath()
        g2d.moveTo(0, h)
        const step = Math.max(10, Math.floor(w / 90))   // 每层 ≤90 个采样点
        for (let x = 0; x <= w; x += step) {
          const ph = t * (0.5 + i * 0.28) + x * 0.006 * (1 + i * 0.2)
          const y = baseY + Math.sin(ph) * amp + Math.sin(ph * 0.5 + i) * amp * 0.6
          g2d.lineTo(x, y)
        }
        g2d.lineTo(w, h)
        g2d.closePath()
        g2d.fillStyle = `rgba(${wave[0]}, ${wave[1]}, ${wave[2]}, ${alpha})`
        g2d.fill()
      }
      t += dt
    }

    function loop (now: number) {
      if (!running) return
      const dt = Math.min(0.05, (now - last) / 1000)
      last = now
      paint(dt)
      raf = requestAnimationFrame(loop)
    }
    raf = requestAnimationFrame(loop)
  }
  onMounted(start)

  onBeforeUnmount(() => {
    if (raf) cancelAnimationFrame(raf)
    if (resizeHandler) window.removeEventListener('resize', resizeHandler)
    if (darkHandler && mqDark) mqDark.removeEventListener('change', darkHandler)
    if (visHandler) document.removeEventListener('visibilitychange', visHandler)
  })
</script>

<style scoped>
.wx-ripple-canvas {
  position: fixed;
  inset: 0;
  width: 100vw;
  height: 100vh;
  z-index: 0;
  pointer-events: none;
  display: block;
}
</style>
