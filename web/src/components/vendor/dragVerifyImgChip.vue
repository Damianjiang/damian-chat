<template>
  <div class="drag-verify-container">
    <div :style="dragVerifyImgStyle">
      <img
        ref="checkImg"
        alt=""
        crossOrigin="anonymous"
        :src="imgsrc"
        style="width:100%"
        @load="checkimgLoaded"
      >

      <canvas
        ref="maincanvas"
        class="main-canvas"
      />

      <canvas
        ref="movecanvas"
        class="move-canvas"
        :class="{goFirst:isOk, goKeep:isKeep}"
      />

      <div
        v-if="showRefresh && !isPassing"
        class="refresh"
      >
        <i
          :class="refreshIcon"
          @click="refreshimg"
        />
      </div>

      <div
        v-if="showTips && isPassing"
        class="tips success"
      >{{ successTip }}</div>

      <div
        v-if="showTips && !isPassing && showErrorTip"
        class="tips danger"
      >{{ failTip }}</div>
    </div>

    <div
      ref="dragVerify"
      class="drag_verify"
      :style="dragVerifyStyle"
      @mouseleave="dragFinish"
      @mousemove="dragMoving"
      @mouseup="dragFinish"
      @touchend="dragFinish"
      @touchmove="dragMoving"
    >

      <div
        ref="progressBar"
        class="dv_progress_bar"
        :class="{goFirst2:isOk}"
        :style="progressBarStyle"
      >
        {{ successMessage }}
      </div>

      <div
        ref="message"
        class="dv_text"
        :style="textStyle"
      >
        {{ message }}
      </div>

      <div
        ref="handler"
        class="dv_handler dv_handler_bg"
        :class="{goFirst:isOk}"
        :style="handlerStyle"
        @mousedown="dragStart"
        @touchstart="dragStart"
      >
        <i :class="handlerIcon" />
      </div>
    </div>
  </div>
</template>
<script>
  import { t } from '@/lib/i18n'

  export default {
    name: 'DragVerifyImgChip',
    props: {
      isPassing: {
        type: Boolean,
        default: false,
      },
      width: {
        type: Number,
        default: 250,
      },
      height: {
        type: Number,
        default: 40,
      },
      text: {
        type: String,
        default: 'swiping to the right side',
      },
      successText: {
        type: String,
        default: 'success',
      },
      background: {
        type: String,
        default: '#eee',
      },
      progressBarBg: {
        type: String,
        default: '#76c61d',
      },
      completedBg: {
        type: String,
        default: '#76c61d',
      },
      circle: {
        type: Boolean,
        default: false,
      },
      radius: {
        type: String,
        default: '4px',
      },
      handlerIcon: {
        type: String,
      },
      successIcon: {
        type: String,
      },
      handlerBg: {
        type: String,
        default: '#fff',
      },
      textSize: {
        type: String,
        default: '14px',
      },
      textColor: {
        type: String,
        default: '#333',
      },
      imgsrc: {
        type: String,
      },
      barWidth: {
        type: Number,
        default: 40,
      },
      barRadius: {
        type: Number,
        default: 8,
      },
      showRefresh: {
        type: Boolean,
        default: false,
      },
      refreshIcon: {
        type: String,
      },
      showTips: {
        type: Boolean,
        default: true,
      },
      successTip: {
        type: String,
        default: () => t('slider.tip.success'),
      },
      failTip: {
        type: String,
        default: () => t('slider.tip.fail'),
      },
      diffWidth: {
        type: Number,
        default: 20,
      },
      // 服务端权威目标（高饱和色块）位置。传入时用它裁拼块，避免本地随机；
      // 为 null 时退回本地随机（旧后端未部署时向后兼容）。
      slotX: {
        type: Number,
        default: null,
      },
      slotY: {
        type: Number,
        default: null,
      },
    },
    data () {
      return {
        isMoving: false,
        x: 0,
        isOk: false,
        isKeep: false,
        clipBarx: 0,
        showErrorTip: false,
        // 拖动行为轨迹（供外层上报给服务端做反机器人判定）：真人拖动必产生多次移动+一定时长，
        // 脚本一次性 POST 没有 → 服务端按 (moveCount, dragMs) 拒绝。
        moveCount: 0,
        dragMs: 0,
        dragStartAt: 0,
      }
    },
    computed: {
      handlerStyle: function () {
        return {
          width: this.height + 'px',
          height: this.height + 'px',
          background: this.handlerBg,
        }
      },
      message: function () {
        return this.isPassing ? '' : this.text
      },
      successMessage: function () {
        return this.isPassing ? this.successText : ''
      },
      dragVerifyStyle: function () {
        return {
          width: this.width + 'px',
          height: this.height + 'px',
          lineHeight: this.height + 'px',
          background: this.background,
          borderRadius: this.circle ? this.height / 2 + 'px' : this.radius,
        }
      },
      dragVerifyImgStyle: function () {
        return {
          width: this.width + 'px',
          position: 'relative',
          overflow: 'hidden',
        }
      },
      progressBarStyle: function () {
        return {
          background: this.progressBarBg,
          height: this.height + 'px',
          borderRadius: this.circle
            ? this.height / 2 + 'px 0 0 ' + this.height / 2 + 'px'
            : this.radius,
        }
      },
      textStyle: function () {
        return {
          height: this.height + 'px',
          width: this.width + 'px',
          fontSize: this.textSize,
        }
      },
    },
    watch: {
      imgsrc: {
        immediate: false,
        handler: function () {
          this.reImg()
        },
      },
    },
    mounted: function () {
      // 防御：ref 未就绪（DOM 未挂载 / 早期渲染）时不能读 .style，否则抛
      // "Cannot read properties of null (reading 'style')" 崩掉整个登录页。
      const dragEl = this.$refs.dragVerify
      if (!dragEl || !dragEl.style) return
      dragEl.style.setProperty('--textColor', this.textColor)
      dragEl.style.setProperty('--width', Math.floor(this.width / 2) + 'px')
      dragEl.style.setProperty('--pwidth', -Math.floor(this.width / 2) + 'px')
    },
    methods: {
      draw: function (ctx, x, y, operation) {
        const l = this.barWidth
        const r = this.barRadius
        const PI = Math.PI
        ctx.beginPath()
        ctx.moveTo(x, y)
        ctx.arc(x + l / 2, y - r + 2, r, 0.72 * PI, 2.26 * PI)
        ctx.lineTo(x + l, y)
        ctx.arc(x + l + r - 2, y + l / 2, r, 1.21 * PI, 2.78 * PI)
        ctx.lineTo(x + l, y + l)
        ctx.lineTo(x, y + l)
        ctx.arc(x + r - 2, y + l / 2, r + 0.4, 2.76 * PI, 1.24 * PI, true)
        ctx.lineTo(x, y)
        ctx.lineWidth = 2
        ctx.fillStyle = 'rgba(255, 255, 255, 0.8)'
        ctx.strokeStyle = 'rgba(255, 255, 255, 0.8)'
        ctx.stroke()
        ctx[operation]()
        ctx.globalCompositeOperation = 'destination-over'
      },
      checkimgLoaded: function () {
        // 防御：img/canvas 尚未挂载完成时 ref 为 null，读 .width/.style 会崩
        const checkImg = this.$refs.checkImg
        const maincanvas = this.$refs.maincanvas
        const movecanvas = this.$refs.movecanvas
        if (!checkImg || !maincanvas || !movecanvas) {
          return
        }
        // 拼图缺口位置：
        //  - 提供 slotX/slotY（= 前端 CV 定位的服务端「高饱和目标块」）→ 用它裁拼块，
        //    与 /verify 的落点校验对齐（服务端据此拒绝落点不符的白拿 passToken）；
        //  - 未提供（旧后端无目标块）→ 退回本地随机，保持可用。
        const barWidth = this.barWidth
        const imgHeight = checkImg.height
        const imgWidth = checkImg.width
        const halfWidth = Math.floor(this.width / 2)
        const refreshHeigth = 25
        const tipHeight = 20
        let x
        let y
        if (this.slotX != null && this.slotY != null) {
          x = Math.round(this.slotX)
          y = Math.round(this.slotY)
        } else {
          x = halfWidth + Math.ceil(Math.random() * (halfWidth - barWidth - this.barRadius - 5))
          y = refreshHeigth + Math.floor(
            Math.random() * (imgHeight - barWidth - refreshHeigth - tipHeight),
          )
        }
        maincanvas.setAttribute('width', imgWidth)
        maincanvas.setAttribute('height', imgHeight)
        maincanvas.style.display = 'block'
        const canvasCtx = maincanvas.getContext('2d')
        this.draw(canvasCtx, x, y, 'fill')
        this.clipBarx = x

        const moveCanvas = movecanvas
        moveCanvas.setAttribute('width', imgWidth)
        moveCanvas.setAttribute('height', imgHeight)
        moveCanvas.style.display = 'block'
        const L = barWidth + this.barRadius * 2 + 3 // 实际宽度
        const moveCtx = moveCanvas.getContext('2d')
        moveCtx.clearRect(0, 0, imgWidth, imgHeight)
        this.draw(moveCtx, x, y, 'clip')
        moveCtx.drawImage(checkImg, 0, 0, imgWidth, imgHeight)
        y = y - this.barRadius * 2 - 1
        const ImageData = moveCtx.getImageData(x, y, L, L)
        moveCanvas.setAttribute('width', L)
        moveCanvas.setAttribute('height', imgHeight)
        moveCtx.putImageData(ImageData, 0, y)
      },
      dragStart: function (e) {
        if (!this.isPassing) {
          this.isMoving = true
          this.x
            = (e.pageX || e.touches[0].pageX)
          this.moveCount = 0
          this.dragMs = 0
          this.dragStartAt = Date.now()
        }
        this.showBar = true
        this.showErrorTip = false
        this.$emit('handlerMove')
      },
      dragMoving: function (e) {
        if (this.isMoving && !this.isPassing) {
          const _x = (e.pageX || e.touches[0].pageX) - this.x
          const handler = this.$refs.handler
          const progressBar = this.$refs.progressBar
          const movecanvas = this.$refs.movecanvas
          if (!handler || !progressBar || !movecanvas) {
            return
          }
          handler.style.left = _x + 'px'
          progressBar.style.width = _x + this.height / 2 + 'px'
          movecanvas.style.left = _x + 'px'
          this.moveCount++
        }
      },
      dragFinish: function (e) {
        if (this.isMoving && !this.isPassing) {
          const _x = (e.pageX || (e.changedTouches && e.changedTouches[0] && e.changedTouches[0].pageX) || 0) - this.x
          this.dragMs = this.dragStartAt > 0 ? Date.now() - this.dragStartAt : 0
          if (Math.abs(_x - this.clipBarx) > this.diffWidth) {
            this.isOk = true
            const that = this
            setTimeout(function () {
              if (that.$refs.handler) that.$refs.handler.style.left = '0'
              if (that.$refs.progressBar) that.$refs.progressBar.style.width = '0'
              if (that.$refs.movecanvas) that.$refs.movecanvas.style.left = '0'
              that.isOk = false
            }, 500)
            this.$emit('passfail')
            this.showErrorTip = true
          } else {
            this.passVerify()
          }
          this.isMoving = false
        }
      },
      passVerify: function () {
        this.$emit('update:isPassing', true)
        this.isMoving = false
        const handler = this.$refs.handler
        const progressBar = this.$refs.progressBar
        const message = this.$refs.message
        // 防御：ref 未就绪则跳过样式设置（避免 null.style 崩溃），但照常上报通过
        if (handler && handler.children && handler.children[0]) {
          handler.children[0].className = this.successIcon
        }
        if (progressBar) {
          progressBar.style.background = this.completedBg
          progressBar.style.color = '#fff'
          progressBar.style.fontSize = this.textSize
        }
        if (message) {
          message.style['-webkit-text-fill-color'] = 'unset'
          message.style.animation = 'slidetounlock2 3s infinite'
        }
        this.isKeep = true
        setTimeout(() => {
          if (this.$refs.movecanvas) this.$refs.movecanvas.style.left = this.clipBarx + 'px'
          setTimeout(() => {
            this.isKeep = false
            if (this.$refs.maincanvas) this.$refs.maincanvas.style.display = 'none'
            if (this.$refs.movecanvas) this.$refs.movecanvas.style.display = 'none'
          }, 200)
        }, 100)
        this.$emit('passcallback')
      },
      reset: function () {
        this.reImg()
        this.checkimgLoaded()
      },
      reImg: function () {
        this.$emit('update:isPassing', false)
        const oriData = this.$options.data()
        for (const key in oriData) {
          if (Object.prototype.hasOwnProperty.call(oriData, key)) {
            this[key] = oriData[key]
          }
        }
        const handler = this.$refs.handler
        const message = this.$refs.message
        const progressBar = this.$refs.progressBar
        const movecanvas = this.$refs.movecanvas
        // 防御：ref 未就绪（组件未挂载 / 早期 watch）时跳过样式重置，避免 null.style 崩
        if (!handler || !message || !progressBar || !movecanvas) {
          return
        }
        handler.style.left = '0'
        progressBar.style.width = '0'
        if (handler.children && handler.children[0]) {
          handler.children[0].className = this.handlerIcon
        }
        message.style['-webkit-text-fill-color'] = 'transparent'
        message.style.animation = 'slidetounlock 3s infinite'
        message.style.color = this.background
        movecanvas.style.left = '0px'
      },
      refreshimg: function () {
        this.$emit('refresh')
      },
    },
  }
</script>
<style scoped>
.drag_verify {
  position: relative;
  background-color: #e8e8e8;
  text-align: center;
  overflow: hidden;
}
.drag_verify .dv_handler {
  position: absolute;
  top: 0px;
  left: 0px;
  cursor: move;
}
.drag_verify .dv_handler i {
  color: #666;
  padding-left: 0;
  font-size: 16px;
}
.drag_verify .dv_handler .el-icon-circle-check {
  color: #6c6;
  margin-top: 9px;
}
.drag_verify .dv_progress_bar {
  position: absolute;
  height: 34px;
  width: 0px;
}
.drag_verify .dv_text {
  position: absolute;
  top: 0px;
  color: transparent;
  -moz-user-select: none;
  -webkit-user-select: none;
  user-select: none;
  -o-user-select: none;
  -ms-user-select: none;
  background: -webkit-gradient(
    linear,
    left top,
    right top,
    color-stop(0, var(--textColor)),
    color-stop(0.4, var(--textColor)),
    color-stop(0.5, #fff),
    color-stop(0.6, var(--textColor)),
    color-stop(1, var(--textColor))
  );
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  -webkit-text-size-adjust: none;
  animation: slidetounlock 3s infinite;
}
.drag_verify .dv_text * {
  -webkit-text-fill-color: var(--textColor);
}
.goFirst {
  left: 0px !important;
  transition: left 0.5s;
}
.goKeep {
  transition: left 0.2s;
}
.goFirst2 {
  width: 0px !important;
  transition: width 0.5s;
}
.drag-verify-container {
  position: relative;
  line-height: 0;
}
.refresh {
  position: absolute;
  right: 5px;
  top: 5px;
  cursor: pointer;
  font-size: 20px;
  z-index: 200;
}
.tips {
  position: absolute;
  bottom: 0;
  height: 20px;
  line-height: 20px;
  text-align: center;
  width: 100%;
  font-size: 12px;
  z-index: 200;
}
.tips.success {
  background: rgba(255, 255, 255, 0.6);
  color: green;
}
.tips.danger {
  background: rgba(0, 0, 0, 0.6);
  color: yellow;
}
.main-canvas {
  width: 100%;
  height: 100%;
  position: absolute;
  top: 0;
  left: 0;
}
.move-canvas {
  position: absolute;
  top: 0;
  left: 0;
}
</style>
<style>
@-webkit-keyframes slidetounlock {
  0% {
    background-position: var(--pwidth) 0;
  }
  100% {
    background-position: var(--width) 0;
  }
}
@-webkit-keyframes slidetounlock2 {
  0% {
    background-position: var(--pwidth) 0;
  }
  100% {
    background-position: var(--pwidth) 0;
  }
}
</style>
