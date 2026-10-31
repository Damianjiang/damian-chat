<template>
  <!--
    滑块拼图压块验证（拼图压块外观来自 vue-drag-verify 的 dragVerifyImgChip，见 vendor/）。
    外层保持原契约，LoginView 无需改动：
      - defineExpose({ open })         父组件 sliderRef.value?.open() 调起
      - emit('success', passToken)     验证通过，带回服务端签发的一次性 passToken
      - emit('close')                  关闭（失败/取消），父组件可重新调 open()

    说明：拼图压块的缺口位置由前端组件本地判定；通过后向后端 /captcha/verify
    换取「一次性、绑定 IP、HMAC 签名、90 秒过期」的 passToken（服务端仍负责防伪造/重放/限流）。
  -->
  <div v-if="visible" class="sc-mask" @click.self="onClose">
    <div :aria-label="t('slider.title')" class="sc-panel" role="dialog">
      <div class="sc-head">
        <span class="sc-title">{{ t('slider.title') }}</span>
        <button :aria-label="t('common.close')" class="sc-x" type="button" @click="onClose">×</button>
      </div>

      <!-- user-select:none 是 vue-drag-verify 的硬性要求（README） -->
      <div class="sc-body" style="user-select: none; -webkit-user-select: none" @click.stop>
        <drag-verify-img-chip
          v-if="image"
          ref="chipRef"
          v-model:is-passing="chipPassing"
          :bar-width="40"
          :diff-width="15"
          handler-icon="mdi mdi-menu-right"
          :imgsrc="image"
          :slot-x="slotX"
          :slot-y="slotY"
          refresh-icon="mdi mdi-refresh"
          :show-refresh="true"
          :show-tips="true"
          success-icon="mdi mdi-check"
          :success-text="t('slider.ok')"
          :text="t('slider.text')"
          width="300"
          @passcallback="onPass"
          @passfail="onPassFail"
          @refresh="onRefresh"
        />

        <div v-else class="sc-loading">{{ t('slider.loading') }}</div>
      </div>

      <div v-if="err" class="sc-err">{{ err }}</div>
    </div>
  </div>
</template>

<script setup lang="ts">
  import { onBeforeUnmount, ref } from 'vue'
  import dragVerifyImgChip from '@/components/vendor/dragVerifyImgChip.vue'
  import { api } from '@/lib/api'
  import { t } from '@/lib/i18n'

  const emit = defineEmits<{
    (e: 'success', passToken: string): void
    (e: 'close'): void
  }>()

  /** 弹窗显示状态：由父组件点击「登录 / 注册」触发 open() */
  const visible = ref(false)

  /** 服务端纯背景图（不含缺口，拼图压块由前端组件裁切生成） */
  const image = ref('')

  /** 当前挑战 key（一次性） */
  const key = ref('')

  /**
   * 服务端目标块位置（由前端 CV 从图片像素中定位）。
   * 服务端在背景图上画一块高饱和品红色（纯品红 R=255 G=0 B=255，50% 不透明），
   * 前端用「找最饱和像素群质心」稳健定位 → 传给 chip 的 slotX/slotY 裁拼块。
   * 坐标不在 JSON 明文下发（机器人无法直接读走）；CV 定位误差 ~2-3px，服务端容差 ±14px 兜底。
   * 旧后端（无目标块）CV 找不到 → 退回本地随机，保持向后兼容。
   */
  const slotX = ref<number | null>(null)
  const slotY = ref<number | null>(null)

  /** 前端组件的通过态（v-model:isPassing） */
  const chipPassing = ref(false)
  const chipRef = ref<any>(null)

  /** 服务端拒绝（429/网络）时的错误提示 */
  const err = ref('')
  const busy = ref(false)

  /** 从错误响应里取提示文案（429 失败锁定 / 限流 / 网络异常） */
  function serverMsg (e: any): string {
    return e?.response?.data?.ret?.msg ?? e?.response?.data?.error ?? ''
  }

  /** 取一张服务端挑战（key + 背景图，图上含一块高饱和目标色块） */
  async function issue (): Promise<void> {
    try {
      const res = await api.post('/captcha/init')
      const d = res.data?.data
      if (d?.key && d?.image) {
        key.value = d.key
        image.value = ''
        // 前端 CV：在图片像素里定位服务端画的「高饱和目标块」，把拼块裁到同一位置。
        // 坐标不在 JSON 明文下发，机器人无法直接读走；CV 误差 ~2px，服务端 ±14px 容差兜底。
        const found = await locateTarget(d.image)
        slotX.value = found ? found.x : null
        slotY.value = found ? found.y : null
        // 切图：组件 watch imgsrc → reImg，画新的拼图压块（按 slotX/slotY 裁）
        image.value = d.image
        chipPassing.value = false
        err.value = ''
      }
    } catch (error: any) {
      err.value = serverMsg(error) || t('slider.net')
    }
  }

  /**
   * 在挑战图里定位服务端目标块（纯品红 255,0,255 50% 不透明），返回其左上角。
   * 判据：R-G 与 B-G 都很大（品红特征，真实纹理几乎不可能）。
   * 目标块是 40x40 实心：其列/行命中数 ~40，零散误报 ≤ 少数像素 → 用「命中数阈值」取
   * 实心区左缘/顶缘，比简单取 min(x,y) 更稳。找不到（旧后端无目标块）→ 返回 null，退回本地随机。
   */
  function locateTarget (dataUrl: string): Promise<{ x: number, y: number } | null> {
    return new Promise(resolve => {
      const img = new Image()
      img.onload = () => {
        try {
          const w = img.naturalWidth || 300
          const h = img.naturalHeight || 150
          const c = document.createElement('canvas')
          c.width = w
          c.height = h
          const ctx = c.getContext('2d', { willReadFrequently: true })
          if (!ctx) return resolve(null)
          ctx.drawImage(img, 0, 0)
          const { data } = ctx.getImageData(0, 0, w, h)
          const thr = 45
          const colCount: number[] = Array.from({ length: w }, () => 0)
          const rowCount: number[] = Array.from({ length: h }, () => 0)
          let any = false
          for (let py = 0; py < h; py++) {
            for (let px = 0; px < w; px++) {
              const i = (py * w + px) * 4
              const r = data[i] ?? 0, g = data[i + 1] ?? 0, b = data[i + 2] ?? 0
              const score = Math.min(r - g, b - g)   // 品红：R、B 都显著高于 G
              if (score > thr) {
                any = true
                colCount[px] = (colCount[px] ?? 0) + 1
                rowCount[py] = (rowCount[py] ?? 0) + 1
              }
            }
          }
          if (!any) return resolve(null)
          // 实心块的最大列/行命中数 ~40；零散误报 ≤ 个位数。取 ≥ 50% 最大值的左缘/顶缘。
          let maxC = 0, maxR = 0
          for (let x = 0; x < w; x++) { const c = colCount[x] ?? 0; if (c > maxC) maxC = c }
          for (let y = 0; y < h; y++) { const c = rowCount[y] ?? 0; if (c > maxR) maxR = c }
          const cThr = Math.max(8, Math.round(maxC * 0.5))
          const rThr = Math.max(8, Math.round(maxR * 0.5))
          let sx = -1, sy = -1
          for (let x = 0; x < w; x++) if ((colCount[x] ?? 0) >= cThr) { sx = x; break }
          for (let y = 0; y < h; y++) if ((rowCount[y] ?? 0) >= rThr) { sy = y; break }
          if (sx < 0 || sy < 0) return resolve(null)
          resolve({ x: sx, y: sy })
        } catch {
          resolve(null)
        }
      }
      img.onerror = () => resolve(null)
      img.src = dataUrl
    })
  }

  /** 弹出滑块：换一张新挑战，再显示 */
  async function open () {
    visible.value = true
    chipPassing.value = false
    await issue()
    document.documentElement.style.overflow = 'hidden'
    document.body.style.overflow = 'hidden'
  }

  function unlockScroll () {
    document.documentElement.style.overflow = ''
    document.body.style.overflow = ''
  }

  function resetAndClose () {
    visible.value = false
    unlockScroll()
    emit('close')
  }

  function onClose () {
    resetAndClose()
  }

  /** 点右上刷新：换一张新图 */
  function onRefresh () {
    void issue()
  }

  /** 前端拖到缺口（组件本地判定通过）→ 向后端换一次性 passToken（带拖动行为轨迹） */
  async function onPass () {
    if (busy.value) return
    busy.value = true
    try {
      const chip = chipRef.value
      const dragX = Math.round(chip?.clipBarx ?? 0)
      // 行为轨迹：真实拖动的指针移动次数 + 拖动耗时（脚本一次性 POST 没有 → 服务端按此拒绝）
      const moveCount = Number(chip?.moveCount ?? 0)
      const dragMs = Number(chip?.dragMs ?? 0)
      const res = await api.post('/captcha/verify', { key: key.value, dragX, moveCount, dragMs })
      const body = res.data
      if (body?.ret?.code === 200 && body?.data?.passToken) {
        unlockScroll()
        visible.value = false
        emit('success', body.data.passToken)
        return
      }
      // 行为门槛拒绝 / key 失效 / 过期 / 重放：换一张再试
      err.value = body?.ret?.msg || t('slider.reshuffle')
      chipPassing.value = false
      await issue()
    } catch (error: any) {
      err.value = serverMsg(error) || t('slider.fail')
      chipPassing.value = false
      await issue()
    } finally {
      busy.value = false
    }
  }

  /** 拖错位置（组件本地判定失败）：提示并换图 */
  function onPassFail () {
    err.value = t('slider.bad')
    chipPassing.value = false
    void issue()
  }

  onBeforeUnmount(() => {
    unlockScroll()
  })

  defineExpose({ open })
</script>

<style scoped>
.sc-mask {
  position: fixed;
  inset: 0;
  z-index: 99999;
  background: rgba(0, 0, 0, 0.45);
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 16px;
  box-sizing: border-box;
}

.sc-panel {
  width: 336px;
  max-width: 100%;
  background: #fff;
  border-radius: 12px;
  overflow: hidden;
  box-shadow: 0 12px 40px rgba(0, 0, 0, 0.25);
  font-family: inherit;
}

.sc-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 14px;
  border-bottom: 1px solid #eee;
}

.sc-title {
  font-size: 14px;
  font-weight: 500;
  color: #333;
}

.sc-x {
  border: none;
  background: transparent;
  font-size: 20px;
  line-height: 1;
  color: #999;
  cursor: pointer;
  padding: 0 4px;
}
.sc-x:hover { color: #666; }

/* 给组件里 element-ui 风格的 <i> 图标留出 mdi 字体渲染空间 */
.sc-body {
  padding: 12px 16px;
}

/* 拼图压块组件自带的 .dv_handler i / .refresh i 里是 mdi 字体图标 */
.sc-body :deep(.dv_handler i),
.sc-body :deep(.refresh i) {
  font-size: 18px;
  line-height: 1;
  color: #555;
}

.sc-loading {
  width: 300px;
  height: 150px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #999;
  font-size: 13px;
  background: linear-gradient(135deg, #9fb0c0, #cdd7e0);
  border-radius: 8px;
}

.sc-err {
  margin: 0 16px 14px;
  color: #fa5151;
  font-size: 13px;
  text-align: center;
}

/* 暗色：面板保持白底可读（组件内部配色为浅色） */
.v-theme--dark .sc-panel {
  background: #1f1f1f;
  color: #eee;
}
.v-theme--dark .sc-title { color: #eee; }
.v-theme--dark .sc-head { border-bottom-color: #333; }
</style>
